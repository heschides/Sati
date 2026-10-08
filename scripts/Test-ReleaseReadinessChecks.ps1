#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SatiReadiness_' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$checker = Join-Path $PSScriptRoot 'Test-ReleaseReadiness.ps1'
$helper = Join-Path $PSScriptRoot 'New-ReleaseReadinessSnapshot.ps1'
$original = [IO.File]::ReadAllText((Join-Path $repo 'docs/readiness/readiness.json'))
$baseline = Join-Path $fixture 'baseline.json'
$candidate = Join-Path $fixture 'candidate.json'
[IO.File]::WriteAllText($baseline, $original)
$version = [string]($original | ConvertFrom-Json).snapshots[-1].release
function Save($Value) { [IO.File]::WriteAllText($candidate, (($Value | ConvertTo-Json -Depth 100) + "`n")) }
function Reject([string]$Name, [scriptblock]$Mutation, [string]$Expected, [switch]$History) {
    $data = $original | ConvertFrom-Json
    & $Mutation $data
    Save $data
    $caught = $null
    try {
        $arguments = @{RepositoryRoot=$repo;LedgerPath=$candidate;ExpectedReleaseVersion=$version}
        if ($History) { $arguments.BaselinePath = $baseline }
        & $checker @arguments | Out-Null
    } catch { $caught = $_.Exception.Message }
    if (-not $caught -or $caught -notlike ('*' + $Expected + '*')) { throw "Readiness mutation $Name did not fail for the expected reason: $caught" }
    Write-Output "READINESS_CHECK_TEST_PASSED Case=$Name"
}
try {
    & $checker -RepositoryRoot $repo | Out-Null
    Reject 'MissingAssessment' {param($d) $d.snapshots[-1].assessments=@($d.snapshots[-1].assessments | Select-Object -Skip 1)} 'must assess every criterion'
    Reject 'DuplicateCriterion' {param($d) $d.rubrics[0].criteria[1].id=$d.rubrics[0].criteria[0].id} 'Duplicate criterion'
    Reject 'DuplicateAssessment' {param($d) $d.snapshots[-1].assessments[1].criterionId=$d.snapshots[-1].assessments[0].criterionId} 'Duplicate assessment'
    Reject 'UnknownAssessment' {param($d) $d.snapshots[-1].assessments[0].criterionId='not-a-criterion'} 'Unknown assessment'
    Reject 'UnknownStatus' {param($d) $d.snapshots[-1].assessments[0].status='approved'} 'Unknown assessment status'
    Reject 'UnknownEvidence' {param($d) $d.snapshots[-1].assessments[0].evidence=@([pscustomobject]@{kind='guess';reference='AGENDA.md'})} 'Unknown evidence kind'
    Reject 'ZeroWeight' {param($d) $d.rubrics[0].criteria[0].weight=0} 'Invalid weight'
    Reject 'NegativeWeight' {param($d) $d.rubrics[0].criteria[0].weight=-1} 'Invalid weight'
    Reject 'TextWeight' {param($d) $d.rubrics[0].criteria[0].weight='1'} 'Invalid weight'
    Reject 'UnknownDimension' {param($d) $d.rubrics[0].criteria[0].dimension='quality'} 'Unknown dimension'
    Reject 'MissingDimension' {param($d) foreach($c in $d.rubrics[0].criteria) {if($c.dimension -ceq 'operations'){$c.dimension='multitenancy'}}} 'lacks operations'
    Reject 'InvalidBlocking' {param($d) $d.rubrics[0].criteria[0].blocking='yes'} 'Blocking must'
    Reject 'VerifiedWithoutProof' {param($d) $d.snapshots[-1].assessments[0].status='verified';$d.snapshots[-1].assessments[0].evidence=@()} 'lacks'
    Reject 'ImplementedWithoutSource' {param($d) $d.snapshots[-1].assessments[0].status='implemented';$d.snapshots[-1].assessments[0].evidence=@()} 'needs source evidence'
    Reject 'TestedWithoutSource' {param($d) $d.snapshots[-1].assessments[0].status='tested';$d.snapshots[-1].assessments[0].evidence=@([pscustomobject]@{kind='synthetic';reference='AGENDA.md'})} 'needs source evidence'
    Reject 'TestedWithoutTests' {param($d) $d.snapshots[-1].assessments[0].status='tested';$d.snapshots[-1].assessments[0].evidence=@([pscustomobject]@{kind='source';reference='AGENDA.md'})} 'needs test evidence'
    Reject 'MissingVersion' {param($d) $d.snapshots[-1].release='0.0.0'} 'No readiness snapshot'
    Reject 'BadDate' {param($d) $d.snapshots[-1].assessedAt='October 8, 2026'} 'must be yyyy-MM-dd'
    Reject 'FutureDate' {param($d) $d.snapshots[-1].assessedAt=[DateTime]::UtcNow.AddDays(2).ToString('yyyy-MM-dd')} 'Future-dated'
    Reject 'BadSourceCommit' {param($d) $d.snapshots[-1].sourceRevision='a1af921'} 'audited source commit'
    Reject 'MissingSummary' {param($d) $d.snapshots[-1].summary=''} 'Snapshot summary'
    Reject 'MissingNextSteps' {param($d) $d.snapshots[-1].nextSteps=@()} 'next steps'
    Reject 'MissingRubric' {param($d) $d.snapshots[-1].rubricVersion='not-a-rubric'} 'Unknown rubric'
    Reject 'DuplicateRubric' {param($d) $d.rubrics=@($d.rubrics[0],$d.rubrics[0])} 'Duplicate rubric'
    Reject 'MissingAcceptance' {param($d) $d.rubrics[0].criteria[0].acceptance=@()} 'acceptance is empty'
    Reject 'MissingEvidenceReference' {param($d) $d.snapshots[-1].assessments[0].evidence=@([pscustomobject]@{kind='source';reference='does-not-exist.md'})} 'Missing evidence reference'
    Reject 'EscapingEvidenceReference' {param($d) $d.snapshots[-1].assessments[0].evidence=@([pscustomobject]@{kind='source';reference='../outside.md'})} 'Missing evidence reference'
    Reject 'UnknownProperty' {param($d) $d | Add-Member extraScore 100} 'unknown field'
    Reject 'UnsupportedSchema' {param($d) $d.schemaVersion=2} 'Unsupported readiness schema'
    Reject 'TextSchema' {param($d) $d.schemaVersion='1'} 'Unsupported readiness schema'
    Reject 'CaseMismatchedRubric' {param($d) $d.snapshots[-1].rubricVersion=$d.snapshots[-1].rubricVersion.ToUpperInvariant()} 'Unknown rubric'
    Reject 'LeadingZeroVersion' {param($d) $d.snapshots[-1].release='0'+$d.snapshots[-1].release} 'canonical x.y.z'
    Reject 'OverweightCriterion' {param($d) $d.rubrics[0].criteria[0].weight=1001} 'Invalid weight'
    Reject 'SnapshotRewrite' {param($d) $d.snapshots[0].summary='Changed after release'} 'was rewritten' -History
    Reject 'RubricRewrite' {param($d) $d.rubrics[0].criteria[0].weight=2} 'rubric' -History
    [IO.File]::WriteAllText($candidate, $original.Replace('"schemaVersion": 1', '"schemaVersion": 1, "schemaVersion": 1'))
    if ([IO.File]::ReadAllText($candidate) -ceq $original) { throw 'Duplicate-property mutation did not apply.' }
    $caught=$null
    try { & $checker -RepositoryRoot $repo -LedgerPath $candidate -ExpectedReleaseVersion $version | Out-Null } catch { $caught=$_.Exception.Message }
    if ($caught -notlike '*Duplicate JSON field*') { throw "Duplicate JSON key was not rejected: $caught" }
    Write-Output 'READINESS_CHECK_TEST_PASSED Case=DuplicateJsonProperty'
    # Retain prior rubric/history when introducing new criteria, without recalculating older releases.
    $data=$original | ConvertFrom-Json
    $newRubric=($data.rubrics[0] | ConvertTo-Json -Depth 100) | ConvertFrom-Json
    $newRubric.version='test-next-rubric'
    $data.rubrics=@($data.rubrics)+@($newRubric)
    $newSnapshot=($data.snapshots[-1] | ConvertTo-Json -Depth 100) | ConvertFrom-Json
    $nextVersion=([version]$version)
    $nextVersion="$($nextVersion.Major).$($nextVersion.Minor).$($nextVersion.Build+1)"
    $newSnapshot.release=$nextVersion;$newSnapshot.rubricVersion=$newRubric.version
    $data.snapshots=@($data.snapshots)+@($newSnapshot)
    Save $data
    & $checker -RepositoryRoot $repo -LedgerPath $candidate -ExpectedReleaseVersion $nextVersion -BaselinePath $baseline | Out-Null
    Write-Output 'READINESS_CHECK_TEST_PASSED Case=NewRubricKeepsHistory'
    # The reviewed-append helper is idempotent for the exact existing release and refuses overwrites.
    $snapshotFile=Join-Path $fixture 'reviewed.json'
    $snapshot=($original | ConvertFrom-Json).snapshots[-1]
    [IO.File]::WriteAllText($snapshotFile, ($snapshot | ConvertTo-Json -Depth 100))
    $hashBefore=(Get-FileHash -LiteralPath $baseline).Hash
    $result=& $helper -RepositoryRoot $repo -LedgerPath $baseline -Release $version -SnapshotPath $snapshotFile
    if ($result.Action -cne 'AlreadyRecorded' -or (Get-FileHash -LiteralPath $baseline).Hash -cne $hashBefore) { throw 'Identical append changed history.' }
    $snapshot.summary='Overwrite attempt'
    [IO.File]::WriteAllText($snapshotFile, ($snapshot | ConvertTo-Json -Depth 100))
    $caught=$null
    try { & $helper -RepositoryRoot $repo -LedgerPath $baseline -Release $version -SnapshotPath $snapshotFile | Out-Null } catch { $caught=$_.Exception.Message }
    if ($caught -notlike '*History is immutable*') { throw "Different same-release append was not rejected: $caught" }
    Write-Output 'READINESS_CHECK_TEST_PASSED Case=IdempotentAppendAndOverwriteRefusal'
    # Raw duplicate fields must be rejected before the helper normalizes JSON.
    $snapshot=($original | ConvertFrom-Json).snapshots[-1]
    $raw=($snapshot | ConvertTo-Json -Depth 100)
    $raw=$raw.Replace('"release": "'+$version+'"', '"release": "'+$version+'", "release": "'+$version+'"')
    [IO.File]::WriteAllText($snapshotFile,$raw)
    $caught=$null
    try { & $helper -RepositoryRoot $repo -LedgerPath $baseline -Release $version -SnapshotPath $snapshotFile | Out-Null } catch { $caught=$_.Exception.Message }
    if($caught -notlike '*Duplicate JSON field*'){throw "Duplicate raw helper input escaped: $caught"}
    Write-Output 'READINESS_CHECK_TEST_PASSED Case=HelperRejectsRawDuplicateFields'
    $draft=Join-Path $fixture 'draft.json'
    & $helper -RepositoryRoot $repo -LedgerPath $baseline -Release $nextVersion -SourceRevision ('a'*40) -DraftPath $draft | Out-Null
    $draftData=[IO.File]::ReadAllText($draft) | ConvertFrom-Json
    if (@($draftData.assessments | Where-Object status -CNE 'unknown').Count -or @($draftData.assessments | ForEach-Object evidence).Count) { throw 'Draft invents readiness.' }
    $caught=$null
    try { & $helper -RepositoryRoot $repo -LedgerPath $baseline -Release $nextVersion -SnapshotPath $draft | Out-Null } catch { $caught=$_.Exception.Message }
    if ($caught -notlike '*Complete the assessment*') { throw 'Unreviewed draft was accepted.' }
    Write-Output 'READINESS_CHECK_TEST_PASSED Case=UnreviewedDraftCannotShip'
    $draftHash=(Get-FileHash -LiteralPath $draft).Hash
    $caught=$null
    try { & $helper -RepositoryRoot $repo -LedgerPath $baseline -Release $nextVersion -SourceRevision ('a'*40) -DraftPath $draft | Out-Null } catch { $caught=$_.Exception.Message }
    if($caught -notlike '*Draft already exists*' -or (Get-FileHash -LiteralPath $draft).Hash -cne $draftHash){throw 'Draft overwrite was not refused.'}
    Write-Output 'READINESS_CHECK_TEST_PASSED Case=DraftOverwriteRefused'
    # A successful append is tested against a complete private source/evidence fixture.
    $appendRoot=Join-Path $fixture 'append-root'
    [void][IO.Directory]::CreateDirectory($appendRoot)
    $project=Join-Path $appendRoot 'Sati.csproj'
    [IO.File]::WriteAllText($project, '<Project><PropertyGroup><Version>'+ $nextVersion +'</Version></PropertyGroup></Project>')
    $appendLedger=Join-Path $appendRoot 'docs/readiness/readiness.json'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($appendLedger))
    [IO.File]::WriteAllText($appendLedger,$original)
    $source=$original | ConvertFrom-Json
    $references=@($source.rubrics | ForEach-Object criteria | ForEach-Object references)+@($source.snapshots | ForEach-Object assessments | ForEach-Object evidence | ForEach-Object reference)
    foreach($reference in $references){
        if($reference -match '^https://'){continue}
        $relative=($reference -split '#',2)[0] -replace ':\d+(?:[-,]\d+)*$',''
        $path=[IO.Path]::GetFullPath((Join-Path $appendRoot $relative))
        if(-not $path.StartsWith($appendRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe synthetic reference.'}
        if(-not [IO.File]::Exists($path)){
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
            [IO.File]::WriteAllText($path,'Synthetic retained evidence fixture. No application acceptance claim.')
        }
    }
    $snapshot=($original | ConvertFrom-Json).snapshots[-1]
    $snapshot.release=$nextVersion;$snapshot.summary='Synthetic append transaction test; unchanged stages.'
    [IO.File]::WriteAllText($snapshotFile,($snapshot | ConvertTo-Json -Depth 100))
    $result=& $helper -RepositoryRoot $appendRoot -Release $nextVersion -SnapshotPath $snapshotFile
    if($result.Action -cne 'SnapshotAppended'){throw 'New snapshot was not appended.'}
    & $checker -RepositoryRoot $appendRoot -BaselinePath $baseline | Out-Null
    $before=(Get-FileHash -LiteralPath $appendLedger).Hash
    $result=& $helper -RepositoryRoot $appendRoot -Release $nextVersion -SnapshotPath $snapshotFile
    if($result.Action -cne 'AlreadyRecorded' -or (Get-FileHash -LiteralPath $appendLedger).Hash -cne $before){throw 'Repeated successful append changed history.'}
    # Hold the exact advisory lock to prove another helper cannot overwrite the ledger.
    $lock=[IO.FileStream]::new(($appendLedger+'.readiness.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None,1,[IO.FileOptions]::DeleteOnClose)
    try{
        $caught=$null
        try{& $helper -RepositoryRoot $appendRoot -Release $nextVersion -SnapshotPath $snapshotFile | Out-Null}catch{$caught=$_.Exception.Message}
        if(-not $caught -or (Get-FileHash -LiteralPath $appendLedger).Hash -cne $before){throw 'Exclusive append lock did not protect history.'}
    }finally{$lock.Dispose()}
    Write-Output 'READINESS_CHECK_TEST_PASSED Case=SuccessfulAtomicAppendReplayAndWriterExclusion'
    Write-Output 'READINESS_CHECK_TEST_PASSED Case=ConsistentSource'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -ine [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') -or [IO.Path]::GetFileName($resolved) -cnotmatch '^SatiReadiness_[0-9a-f]{32}$') { throw 'Unexpected readiness fixture path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
