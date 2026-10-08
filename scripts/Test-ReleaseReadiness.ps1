#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$LedgerPath,
    [string]$ExpectedReleaseVersion,
    [string]$BaselinePath,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$BaselineRevision
)

# Offline evidence gate. A valid report may honestly say "not ready"; it must never invent readiness.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not $LedgerPath) { $LedgerPath = Join-Path $root 'docs/readiness/readiness.json' }
if (-not $ExpectedReleaseVersion) {
    [xml]$project = [IO.File]::ReadAllText((Join-Path $root 'Sati.csproj'))
    $ExpectedReleaseVersion = [string]@($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
}
$failures = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $failures.Add($Message) } }
function RequiredText($Value, [string]$Name, [int]$Maximum=6000) {
    Check ($Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value) -and $Value.Length -le $Maximum -and $Value -notmatch '[\x00-\x08\x0B\x0C\x0E-\x1F]') "$Name must contain bounded text."
}
function BoundedArray($Value, [int]$Minimum, [int]$Maximum, [string]$Name) {
    Check ($Value -is [Array] -and $Value.Count -ge $Minimum -and $Value.Count -le $Maximum) "$Name must be a bounded JSON array."
}
function HasFields($Value, [string[]]$Names, [string]$Name) {
    foreach ($field in $Names) { Check ($null -ne $Value -and $Value.PSObject.Properties.Name -ccontains $field) "$Name lacks $field." }
    foreach ($field in @($Value.PSObject.Properties.Name)) { Check ($Names -ccontains $field) "$Name contains unknown field $field." }
}
function Check-JsonKeys([System.Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            Check ($names.Add($property.Name)) "Duplicate JSON field $($property.Name)."
            Check-JsonKeys $property.Value
        }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Check-JsonKeys $item }
    }
}
function ReferenceExists([string]$Reference) {
    if ($Reference -cmatch '^https://[^\s]+$') { return $true }
    # Evidence is a version-controlled file, optionally with an anchor or source line.
    $relative = ($Reference -split '#', 2)[0] -replace ':\d+(?:[-,]\d+)*$', ''
    if (-not $relative -or [IO.Path]::IsPathRooted($relative)) { return $false }
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $path.StartsWith($root.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    return [IO.File]::Exists($path)
}
function Canonical($Value) { ConvertTo-Json -InputObject $Value -Depth 100 -Compress }
function CommitExists([string]$Revision) {
    $info=[Diagnostics.ProcessStartInfo]::new('git'); $info.WorkingDirectory=$root
    $info.UseShellExecute=$false; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    foreach($argument in @('cat-file','-e',($Revision+'^{commit}'))){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($info)
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    if(-not $process.WaitForExit(10000)){$process.Kill($true);throw 'Source-commit validation timed out.'}
    [void]$stdout.GetAwaiter().GetResult();[void]$stderr.GetAwaiter().GetResult()
    $exists=$process.ExitCode -eq 0;$process.Dispose();return $exists
}
try {
    if ((Get-Item -LiteralPath $LedgerPath).Length -gt 16777216) { throw 'Readiness report exceeds the 16-megabyte limit.' }
    $json = [IO.File]::ReadAllText($LedgerPath)
    $options = [System.Text.Json.JsonDocumentOptions]::new(); $options.MaxDepth = 32
    $document = [System.Text.Json.JsonDocument]::Parse($json, $options)
    try { Check-JsonKeys $document.RootElement } finally { $document.Dispose() }
    $data = $json | ConvertFrom-Json -ErrorAction Stop
}
catch { throw "Readiness report cannot be read: $($_.Exception.Message)" }
HasFields $data @('schemaVersion','rubrics','snapshots') 'Readiness report'
Check (($data.schemaVersion -is [int] -or $data.schemaVersion -is [long]) -and $data.schemaVersion -ceq 1) 'Unsupported readiness schema.'
Check ($data.rubrics -is [Array] -and $data.snapshots -is [Array]) 'Rubrics and snapshots must be JSON arrays.'
BoundedArray $data.rubrics 1 128 'Rubrics'
BoundedArray $data.snapshots 1 1024 'Snapshots'
$rubrics = @($data.rubrics)
$snapshots = @($data.snapshots)
Check ($rubrics.Count -gt 0) 'At least one versioned rubric is required.'
Check ($snapshots.Count -gt 0) 'At least one release snapshot is required.'
$rubricMap = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
foreach ($rubric in $rubrics) {
    HasFields $rubric @('version','criteria') 'Rubric'
    RequiredText $rubric.version 'Rubric version' 100
    Check (-not $rubricMap.ContainsKey([string]$rubric.version)) "Duplicate rubric $($rubric.version)."
    $rubricMap[[string]$rubric.version] = $rubric
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $criteria = @($rubric.criteria)
    Check ($rubric.criteria -is [Array]) 'Criteria must be a JSON array.'
    BoundedArray $rubric.criteria 1 512 'Criteria'
    Check ($criteria.Count -gt 0) "Rubric $($rubric.version) has no criteria."
    foreach ($dimension in @('multitenancy','idempotency','operations')) {
        Check (@($criteria | Where-Object dimension -CEQ $dimension).Count -gt 0) "Rubric $($rubric.version) lacks $dimension."
    }
    foreach ($criterion in $criteria) {
        HasFields $criterion @('id','dimension','title','weight','blocking','owner','requiredEvidence','failureModes','acceptance','references') 'Criterion'
        Check ($ids.Add([string]$criterion.id)) "Duplicate criterion $($criterion.id)."
        RequiredText $criterion.id 'Criterion ID' 120
        RequiredText $criterion.title 'Criterion title' 500
        RequiredText $criterion.owner 'Criterion owner' 200
        Check (@('multitenancy','idempotency','operations') -ccontains $criterion.dimension) "Unknown dimension $($criterion.dimension)."
        Check ($criterion.weight -is [ValueType] -and $criterion.weight -isnot [bool] -and [double]$criterion.weight -gt 0 -and [double]$criterion.weight -le 1000 -and [double]::IsFinite([double]$criterion.weight)) "Invalid weight for $($criterion.id)."
        Check ($criterion.blocking -is [bool]) "Blocking must be true or false for $($criterion.id)."
        Check (@($criterion.requiredEvidence).Count -gt 0) "Required evidence is empty for $($criterion.id)."
        Check ($criterion.requiredEvidence -is [Array]) 'Required evidence must be a JSON array.'
        BoundedArray $criterion.requiredEvidence 1 5 'Required evidence'
        Check (@($criterion.requiredEvidence | Select-Object -Unique).Count -eq @($criterion.requiredEvidence).Count) "Duplicate required evidence for $($criterion.id)."
        foreach ($kind in @($criterion.requiredEvidence)) { Check (@('source','synthetic','sql','live','independent') -ccontains $kind) "Unknown evidence kind $kind." }
        foreach ($field in @('failureModes','acceptance','references')) {
            Check ($criterion.$field -is [Array]) "$field must be a JSON array."
            BoundedArray $criterion.$field 1 64 $field
            Check (@($criterion.$field).Count -gt 0) "$field is empty for $($criterion.id)."
            $maximum = if ($field -eq 'references') { 2048 } else { 4000 }
            foreach ($entry in @($criterion.$field)) { RequiredText $entry "$($criterion.id) $field" $maximum }
        }
        foreach ($reference in @($criterion.references)) { Check (ReferenceExists $reference) "Missing criterion reference: $reference." }
    }
}
$previousVersion = $null
$previousDate = $null
$releaseIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$current = $null
$checkedCommits=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($snapshot in $snapshots) {
    HasFields $snapshot @('release','assessedAt','sourceRevision','rubricVersion','summary','nextSteps','assessments') 'Snapshot'
    Check ($snapshot.release -cmatch '^\d+\.\d+\.\d+$') "Invalid release $($snapshot.release)."
    Check ($releaseIds.Add([string]$snapshot.release)) "Duplicate release $($snapshot.release)."
    try {
        $version = [version]$snapshot.release
        Check ($version.ToString(3) -ceq $snapshot.release -and $version.Revision -lt 0) 'Release versions must be canonical x.y.z without leading zeroes.'
        if ($null -ne $previousVersion) { Check ($version -gt $previousVersion) 'Release snapshots must be in ascending version order.' }
        $previousVersion = $version
    } catch { $failures.Add('Snapshot release is not a valid version.') }
    try {
        $date = [DateTime]::ParseExact($snapshot.assessedAt, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
        Check ($date.Date -le [DateTime]::UtcNow.Date) 'Future-dated evidence is not allowed.'
        if ($null -ne $previousDate) { Check ($date -ge $previousDate) 'Snapshot assessment dates move backwards.' }
        $previousDate = $date
    } catch { $failures.Add('Snapshot assessedAt must be yyyy-MM-dd.') }
    Check ($snapshot.sourceRevision -cmatch '^[0-9a-f]{40}$') 'Snapshot must identify an audited source commit.'
    if((Test-Path -LiteralPath (Join-Path $root '.git')) -and $snapshot.sourceRevision -cmatch '^[0-9a-f]{40}$' -and $checkedCommits.Add($snapshot.sourceRevision)) {
        Check (CommitExists $snapshot.sourceRevision) 'An audited source commit is missing from accessible repository history.'
    }
    RequiredText $snapshot.summary 'Snapshot summary'
    RequiredText $snapshot.rubricVersion 'Snapshot rubric version' 100
    Check ($snapshot.nextSteps -is [Array] -and $snapshot.assessments -is [Array]) 'Next steps and assessments must be JSON arrays.'
    BoundedArray $snapshot.nextSteps 1 64 'Next steps'
    BoundedArray $snapshot.assessments 1 512 'Assessments'
    Check (@($snapshot.nextSteps).Count -gt 0) 'Snapshot must explain the next steps or ongoing monitoring.'
    foreach ($step in @($snapshot.nextSteps)) { RequiredText $step 'Next step' 4000 }
    Check ($rubricMap.ContainsKey([string]$snapshot.rubricVersion)) "Unknown rubric $($snapshot.rubricVersion)."
    if (-not $rubricMap.ContainsKey([string]$snapshot.rubricVersion)) { continue }
    $criteria = @($rubricMap[[string]$snapshot.rubricVersion].criteria)
    $assessmentIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($assessment in @($snapshot.assessments)) {
        HasFields $assessment @('criterionId','status','evidence','notes') 'Assessment'
        Check ($assessmentIds.Add([string]$assessment.criterionId)) "Duplicate assessment $($assessment.criterionId)."
        $criterion = @($criteria | Where-Object id -CEQ $assessment.criterionId)
        Check ($criterion.Count -eq 1) "Unknown assessment criterion $($assessment.criterionId)."
        Check (@('unknown','planned','implemented','tested','verified','blocked') -ccontains $assessment.status) "Unknown assessment status $($assessment.status)."
        RequiredText $assessment.notes 'Assessment explanation'
        Check ($assessment.evidence -is [Array]) 'Assessment evidence must be a JSON array.'
        BoundedArray $assessment.evidence 0 64 'Evidence'
        $kinds = @($assessment.evidence | ForEach-Object kind)
        foreach ($evidence in @($assessment.evidence)) {
            HasFields $evidence @('kind','reference') 'Evidence'
            Check (@('source','synthetic','sql','live','independent') -ccontains $evidence.kind) "Unknown evidence kind $($evidence.kind)."
            RequiredText $evidence.reference 'Evidence reference' 2048
            Check (ReferenceExists $evidence.reference) "Missing evidence reference: $($evidence.reference)."
            Check ($evidence.reference -cnotmatch '^https://') 'Sati implementation/execution evidence must reference a retained repository artifact, not a protocol website.'
        }
        if (@('implemented','tested','verified') -ccontains $assessment.status) { Check ($kinds -ccontains 'source') "$($assessment.status) $($assessment.criterionId) needs source evidence." }
        if ($assessment.status -ceq 'tested') { Check ($kinds -ccontains 'synthetic' -or $kinds -ccontains 'sql') "Tested $($assessment.criterionId) needs test evidence." }
        if ($assessment.status -ceq 'verified' -and $criterion.Count -eq 1) {
            foreach ($kind in @($criterion[0].requiredEvidence)) { Check ($kinds -ccontains $kind) "Verified $($assessment.criterionId) lacks $kind evidence." }
        }
    }
    Check ($assessmentIds.Count -eq $criteria.Count) "Release $($snapshot.release) must assess every criterion."
    foreach ($criterion in $criteria) { Check ($assessmentIds.Contains([string]$criterion.id)) "Missing assessment $($criterion.id)." }
    if ($snapshot.release -ceq $ExpectedReleaseVersion) { $current = $snapshot }
}
Check ($null -ne $current) "No readiness snapshot for source release $ExpectedReleaseVersion."
Check ($snapshots.Count -gt 0 -and $snapshots[-1].release -ceq $ExpectedReleaseVersion) 'The newest snapshot must match the source release.'

function Compare-History($Baseline, [string]$Name) {
    Check ($Baseline.schemaVersion -ceq $data.schemaVersion) "$Name schema changed without a reviewed migration."
    $oldSnapshots = @($Baseline.snapshots)
    Check ($oldSnapshots.Count -le $snapshots.Count) "$Name release history was truncated."
    for ($i = 0; $i -lt [Math]::Min($oldSnapshots.Count, $snapshots.Count); $i++) {
        Check ((Canonical $oldSnapshots[$i]) -ceq (Canonical $snapshots[$i])) "$Name snapshot $($oldSnapshots[$i].release) was rewritten. Append a correction in a new release."
    }
    foreach ($oldRubric in @($Baseline.rubrics)) {
        $matching = @($rubrics | Where-Object version -CEQ $oldRubric.version)
        Check ($matching.Count -eq 1 -and (Canonical $oldRubric) -ceq (Canonical $matching[0])) "$Name rubric $($oldRubric.version) was rewritten. Add a new rubric version."
    }
}
$historyChecked = $false
if ($BaselinePath) {
    Compare-History ([IO.File]::ReadAllText($BaselinePath) | ConvertFrom-Json) 'Baseline'
    $historyChecked = $true
} elseif (Test-Path -LiteralPath (Join-Path $root '.git')) {
    # Check both uncommitted edits and a committed change relative to its predecessor.
    $revisions = @('HEAD','HEAD^')
    if ($BaselineRevision -and $BaselineRevision -cne ('0'*40)) { $revisions += $BaselineRevision }
    foreach ($revision in @($revisions | Select-Object -Unique)) {
        $info = [Diagnostics.ProcessStartInfo]::new('git')
        $info.WorkingDirectory = $root
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($arg in @('show', ($revision + ':docs/readiness/readiness.json'))) { $info.ArgumentList.Add($arg) }
        $process = [Diagnostics.Process]::Start($info)
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) { $process.Kill($true); throw 'Readiness history check timed out.' }
        $output = $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -eq 0) { Compare-History ($output | ConvertFrom-Json) $revision; $historyChecked = $true }
        else {
            Check ($errorText -match "path .* does not exist in|path .* exists on disk, but not in|invalid object name 'HEAD\^'") "Unable to validate readiness history at ${revision}: $errorText"
        }
        $process.Dispose()
    }
} elseif ($BaselineRevision) { $failures.Add('A requested Git baseline requires an accessible repository.')
}
if ($failures.Count) { throw ("Readiness evidence gate failed:`n- " + ($failures -join "`n- ")) }
$criteria = @($rubricMap[[string]$current.rubricVersion].criteria)
$points = @{unknown=0;planned=0;blocked=0;implemented=25;tested=50;verified=100}
$scores = @{}
foreach ($dimension in @('multitenancy','idempotency','operations')) {
    $weight = 0.0; $score = 0.0
    foreach ($criterion in @($criteria | Where-Object dimension -CEQ $dimension)) {
        $assessment = @($current.assessments | Where-Object criterionId -CEQ $criterion.id)[0]
        $weight += [double]$criterion.weight
        $score += [double]$criterion.weight * $points[$assessment.status]
    }
    $scores[$dimension] = [Math]::Round($score / $weight, 1, [MidpointRounding]::AwayFromZero)
}
$totalWeight = 0.0; $totalScore = 0.0
foreach ($criterion in $criteria) {
    $assessment = @($current.assessments | Where-Object criterionId -CEQ $criterion.id)[0]
    $totalWeight += [double]$criterion.weight
    $totalScore += [double]$criterion.weight * $points[$assessment.status]
}
$overall = [Math]::Round($totalScore / $totalWeight, 1, [MidpointRounding]::AwayFromZero)
$blockers = @($criteria | Where-Object { $_.blocking -and @($current.assessments | Where-Object criterionId -CEQ $_.id)[0].status -cne 'verified' })
[pscustomobject]@{Gate='ReleaseReadinessEvidence';Passed=$true;Release=$current.release;Rubric=$current.rubricVersion;Multitenancy=$scores.multitenancy;Idempotency=$scores.idempotency;Operations=$scores.operations;Overall=$overall;HardBlockers=$blockers.Count;ReadyForMultitenantProduction=($blockers.Count -eq 0 -and @($current.assessments | Where-Object status -CNE 'verified').Count -eq 0);HistoryChecked=$historyChecked}
