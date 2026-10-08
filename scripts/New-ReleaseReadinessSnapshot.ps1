#requires -Version 7.0
[CmdletBinding(DefaultParameterSetName='Draft')]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Release,
    [Parameter(Mandatory,ParameterSetName='Draft')][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceRevision,
    [Parameter(Mandatory,ParameterSetName='Draft')][string]$DraftPath,
    [Parameter(ParameterSetName='Draft')][ValidatePattern('^\d{4}-\d{2}-\d{2}$')][string]$AssessedAt = [DateTime]::UtcNow.ToString('yyyy-MM-dd'),
    [Parameter(Mandatory,ParameterSetName='Append')][string]$SnapshotPath,
    [string]$RubricVersion,
    [string]$LedgerPath
)

# Creates a review template or appends reviewed evidence. Never measures readiness from a release number.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
function Read-StrictJson([string]$Path) {
    if ((Get-Item -LiteralPath $Path).Length -gt 16777216) { throw 'Readiness input exceeds the 16-megabyte limit.' }
    $text = [IO.File]::ReadAllText($Path)
    $options = [System.Text.Json.JsonDocumentOptions]::new(); $options.MaxDepth=32
    $document = [System.Text.Json.JsonDocument]::Parse($text, $options)
    function Check-Keys([System.Text.Json.JsonElement]$Element) {
        if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($property in $Element.EnumerateObject()) {
                if (-not $names.Add($property.Name)) { throw "Duplicate JSON field $($property.Name)." }
                Check-Keys $property.Value
            }
        } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
            foreach ($item in $Element.EnumerateArray()) { Check-Keys $item }
        }
    }
    try { Check-Keys $document.RootElement } finally { $document.Dispose() }
    return ($text | ConvertFrom-Json)
}
if (-not $LedgerPath) { $LedgerPath = Join-Path $root 'docs/readiness/readiness.json' }
$writeLock = $null
try {
if ($PSCmdlet.ParameterSetName -eq 'Append') {
    # Two release assessments may not race and replace each other's append.
    $writeLock = [IO.FileStream]::new(([IO.Path]::GetFullPath($LedgerPath) + '.readiness.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None,
        1, [IO.FileOptions]::DeleteOnClose)
}
$ledger = Read-StrictJson $LedgerPath
$inputHash = (Get-FileHash -LiteralPath $LedgerPath -Algorithm SHA256).Hash
if (-not $RubricVersion) { $RubricVersion = [string]$ledger.snapshots[-1].rubricVersion }
$rubric = @($ledger.rubrics | Where-Object version -CEQ $RubricVersion)
if ($rubric.Count -ne 1) { throw 'Select one existing versioned rubric.' }
function Canonical($Value) { ConvertTo-Json -InputObject $Value -Depth 100 -Compress }
if ($PSCmdlet.ParameterSetName -eq 'Draft') {
    $path = [IO.Path]::GetFullPath($DraftPath)
    if (Test-Path -LiteralPath $path) { throw 'Draft already exists; review it without overwriting it.' }
    $draft = [ordered]@{
        release=$Release; assessedAt=$AssessedAt; sourceRevision=$SourceRevision;
        rubricVersion=$RubricVersion;
        summary='REVIEW REQUIRED: Explain what is protected, what remains unproven, and what changed.';
        nextSteps=@('REVIEW REQUIRED: Explain the next priority in plain terms.');
        assessments=@($rubric[0].criteria | ForEach-Object {
            [ordered]@{criterionId=$_.id;status='unknown';evidence=@();notes='REVIEW REQUIRED: Reassess against acceptance requirements and record versioned evidence.'}
        })
    }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    $draftBytes = [Text.UTF8Encoding]::new($false).GetBytes((($draft | ConvertTo-Json -Depth 100) + [Environment]::NewLine))
    $draftStream = [IO.FileStream]::new($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $draftStream.Write($draftBytes, 0, $draftBytes.Length); $draftStream.Flush($true) } finally { $draftStream.Dispose() }
    [pscustomobject]@{Action='DraftCreated';Path=$path;Release=$Release;Instruction='Review every criterion; unchanged progress is valid. Do not add unperformed evidence.'}
    return
}
$snapshot = Read-StrictJson $SnapshotPath
if ($snapshot.release -cne $Release -or $snapshot.rubricVersion -cne $RubricVersion) { throw 'Reviewed snapshot release/rubric differs from the requested append.' }
if ((Canonical $snapshot).Contains('REVIEW REQUIRED')) { throw 'Complete the assessment before appending the review template.' }
$existing = @($ledger.snapshots | Where-Object release -CEQ $Release)
if ($existing.Count) {
    if ($existing.Count -eq 1 -and (Canonical $existing[0]) -ceq (Canonical $snapshot)) {
        & (Join-Path $PSScriptRoot 'Test-ReleaseReadiness.ps1') -RepositoryRoot $root -LedgerPath $LedgerPath -ExpectedReleaseVersion $Release | Out-Null
        [pscustomobject]@{Action='AlreadyRecorded';Release=$Release}
        return
    }
    throw 'A snapshot already exists for that release. History is immutable; use a new release for a correction.'
}
if ([version]$Release -le [version]$ledger.snapshots[-1].release) { throw 'New release must follow the last recorded release.' }
$ledger.snapshots = @($ledger.snapshots) + @($snapshot)
$absoluteLedger = [IO.Path]::GetFullPath($LedgerPath)
$temporary = $absoluteLedger + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, (($ledger | ConvertTo-Json -Depth 100) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'Test-ReleaseReadiness.ps1') -RepositoryRoot $root -LedgerPath $temporary -BaselinePath $absoluteLedger | Out-Null
    # Replace only this ledger after its complete schema, release, references, and append-only history pass.
    if ((Get-FileHash -LiteralPath $absoluteLedger -Algorithm SHA256).Hash -cne $inputHash) { throw 'Readiness ledger changed during review; refusing to overwrite new work. Retry from the current ledger.' }
    [IO.File]::Move($temporary, $absoluteLedger, $true)
    [pscustomobject]@{Action='SnapshotAppended';Release=$Release;Path=$absoluteLedger}
} finally {
    if ([IO.Path]::GetDirectoryName($temporary) -cne [IO.Path]::GetDirectoryName($absoluteLedger) -or -not $temporary.StartsWith($absoluteLedger + '.', [StringComparison]::Ordinal)) { throw 'Unexpected temporary readiness path.' }
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
} finally { if ($null -ne $writeLock) { $writeLock.Dispose() } }
