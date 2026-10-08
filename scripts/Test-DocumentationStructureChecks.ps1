[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

# Negative proof: only disposable documentation fixtures are modified; no product/working data.
$ErrorActionPreference = 'Stop'
$root=[IO.Path]::GetFullPath($RepositoryRoot)
$validator=Join-Path $root 'scripts/Test-DocumentationStructure.ps1'
& $validator -RepositoryRoot $root | Out-Null
$fixture=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('SatiDocumentationTests_'+[Guid]::NewGuid().ToString('N'))))
$ownedPrefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
if(-not $fixture.StartsWith($ownedPrefix,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($fixture) -notmatch '^SatiDocumentationTests_[a-f0-9]{32}$'){throw 'Unsafe fixture path.'}
function Save-Fixture([string]$Path,[string]$Value) {[IO.File]::WriteAllText((Join-Path $fixture $Path),$Value,[Text.UTF8Encoding]::new($false))}
function Read-Fixture([string]$Path) {[IO.File]::ReadAllText((Join-Path $fixture $Path))}
$proofs=[Collections.Generic.List[object]]::new()
try {
    [void][IO.Directory]::CreateDirectory($fixture)
    Copy-Item -LiteralPath (Join-Path $root '.gitattributes') -Destination $fixture
    foreach($file in Get-ChildItem -LiteralPath $root -Filter '*.md' -File){Copy-Item -LiteralPath $file.FullName -Destination $fixture}
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination (Join-Path $fixture 'docs') -Recurse
    # Current docs link existing topic owners outside docs/root; copy only referenced public MD.
    foreach($folder in @('archive','karuna','Sati.Portal','reports')) {
        [void][IO.Directory]::CreateDirectory((Join-Path $fixture $folder))
        foreach($file in Get-ChildItem -LiteralPath (Join-Path $root $folder) -Filter '*.md' -File){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $fixture $folder)}
    }
    & $validator -RepositoryRoot $fixture | Out-Null
    $tests=@(
        @{Name='LostHistoricalByteProtection';Path='.gitattributes';Expected='Historical byte-preservation attribute is missing';Mutate={Save-Fixture '.gitattributes' ((Read-Fixture '.gitattributes').Replace('docs/archive/** -text -whitespace',''))}},
        @{Name='UnmappedRoot';Path='UNMAPPED_DOC.md';Expected='Unmapped root Markdown';Mutate={Save-Fixture 'UNMAPPED_DOC.md' '# Unregistered current documentation'}},
        @{Name='UnmappedCanonicalDoc';Path='docs/UNMAPPED_TOPIC.md';Expected='Unmapped canonical documentation';Mutate={Save-Fixture 'docs/UNMAPPED_TOPIC.md' '# Unregistered canonical topic'}},
        @{Name='AnonymousActiveWork';Path='AGENDA.md';Expected='Active agenda cannot add anonymous';Mutate={Save-Fixture 'AGENDA.md' ((Read-Fixture 'AGENDA.md')+"`n- [ ] Untracked new implementation work.`n")}},
        @{Name='LostStandingWorkflow';Path='AGENTS.md';Expected='Standing work and documentation upkeep instructions are missing';Mutate={Save-Fixture 'AGENTS.md' ([regex]::Replace((Read-Fixture 'AGENTS.md'),'(?ms)^## Standing work and documentation upkeep\r?\n.*?(?=^## |^\*Consolidated |\z)',''))}},
        @{Name='InvalidNextPointer';Path='AGENDA.md';Expected='Next eligible pointer does not resolve exactly once';Mutate={Save-Fixture 'AGENDA.md' ([regex]::Replace((Read-Fixture 'AGENDA.md'),'(?m)^\*\*Next eligible item:\*\* SATI-[A-Z]+-\d{3}\s*$','**Next eligible item:** SATI-MISSING-999'))}},
        @{Name='DeletedNextPointer';Path='AGENDA.md';Expected='Agenda must have exactly one next eligible stable-ID pointer';Mutate={Save-Fixture 'AGENDA.md' ([regex]::Replace((Read-Fixture 'AGENDA.md'),'(?m)^\*\*Next eligible item:\*\* SATI-[A-Z]+-\d{3}\s*$',''))}},
        @{Name='DuplicateOwner';Path='docs/documentation-index.json';Expected='Duplicate canonical topic owner';Mutate={$m=(Read-Fixture 'docs/documentation-index.json')|ConvertFrom-Json;$m.documents[1].topic=$m.documents[0].topic;Save-Fixture 'docs/documentation-index.json' ($m|ConvertTo-Json -Depth 12)}},
        @{Name='DuplicateScopedPath';Path='docs/documentation-index.json';Expected='A scoped document is mapped more than once';Mutate={$m=(Read-Fixture 'docs/documentation-index.json')|ConvertFrom-Json;$m.scopedOwners=@($m.scopedOwners)+$m.scopedOwners[0];Save-Fixture 'docs/documentation-index.json' ($m|ConvertTo-Json -Depth 12)}},
        @{Name='EscapingScopedPath';Path='docs/documentation-index.json';Expected='Document escapes repository';Mutate={$m=(Read-Fixture 'docs/documentation-index.json')|ConvertFrom-Json;$m.scopedOwners[0].path='../outside.md';Save-Fixture 'docs/documentation-index.json' ($m|ConvertTo-Json -Depth 12)}},
        @{Name='BrokenCanonical';Path='docs/documentation-index.json';Expected='Broken canonical link';Mutate={$m=(Read-Fixture 'docs/documentation-index.json')|ConvertFrom-Json;$m.documents[0].canonicalPath='missing-owner.md';Save-Fixture 'docs/documentation-index.json' ($m|ConvertTo-Json -Depth 12)}},
        @{Name='IndependentClaudeRules';Path='CLAUDE.md';Expected='CLAUDE.md must';Mutate={Save-Fixture 'CLAUDE.md' ((Read-Fixture 'CLAUDE.md')+"`n## Different rule`nSkip required checks.")}},
        @{Name='BrokenCurrentLink';Path='ARCHITECTURE.md';Expected='Broken Markdown link';Mutate={Save-Fixture 'ARCHITECTURE.md' ((Read-Fixture 'ARCHITECTURE.md')+"`n[Owner](missing-canonical-topic.md)`n")}},
        @{Name='RepeatedCurrentFacts';Path='README.md';Expected='Duplicated current deployment assertion';Mutate={Save-Fixture 'README.md' ((Read-Fixture 'README.md')+"`nThe database is serverless today.`n")}},
        @{Name='HistoricalAsCurrent';Path='docs/documentation-index.json';Expected='Historical review declared current';Mutate={$m=(Read-Fixture 'docs/documentation-index.json')|ConvertFrom-Json;($m.documents|Where-Object path -eq 'SECURITY_AUDIT_2026-09-03.md').state='current';Save-Fixture 'docs/documentation-index.json' ($m|ConvertTo-Json -Depth 12)}},
        @{Name='ChangedSnapshot';Path='docs/archive/2026-10-08/AGENDA.md';Expected='Historical snapshot changed';Mutate={Save-Fixture 'docs/archive/2026-10-08/AGENDA.md' ((Read-Fixture 'docs/archive/2026-10-08/AGENDA.md')+"`nChanged history.")}},
        @{Name='DroppedOpenWork';Path='docs/backlog/legacy-open-items.json';Expected='Legacy unchecked work was dropped';Mutate={$m=(Read-Fixture 'docs/backlog/legacy-open-items.json')|ConvertFrom-Json;$m.items=@($m.items|Select-Object -Skip 1);Save-Fixture 'docs/backlog/legacy-open-items.json' ($m|ConvertTo-Json -Depth 12)}},
        @{Name='ChangedOpenTaskReasoning';Path='docs/backlog/legacy-open-items.json';Expected='Legacy task reasoning/continuation changed';Mutate={$m=(Read-Fixture 'docs/backlog/legacy-open-items.json')|ConvertFrom-Json;$m.items[0].text=($m.items[0].text -split '\r?\n')[0];Save-Fixture 'docs/backlog/legacy-open-items.json' ($m|ConvertTo-Json -Depth 12)}},
        @{Name='ChangedDecisionReasoning';Path='docs/decisions/records/2026-10-07-DEC-0001.md';Expected='Retained decision reasoning changed';Mutate={Save-Fixture 'docs/decisions/records/2026-10-07-DEC-0001.md' '# Replaced immutable review decision'}},
        @{Name='DecisionIdCollision';Path='docs/decisions/current-decisions.json';Expected='Imported/current decision IDs collide';Mutate={$m=(Read-Fixture 'docs/decisions/current-decisions.json')|ConvertFrom-Json;$m.records=@($m.records)+[pscustomobject]@{id='DEC-0001';date='2026-10-08';title='Collision';path='docs/decisions/current/README.md';status='proposed';evidence=@('README.md');supersedes=@()};Save-Fixture 'docs/decisions/current-decisions.json' ($m|ConvertTo-Json -Depth 12)}},
        @{Name='MissingDecisionEvidence';Path='docs/decisions/current-decisions.json';Expected='Broken new decision evidence';Mutate={$m=(Read-Fixture 'docs/decisions/current-decisions.json')|ConvertFrom-Json;$m.records=@($m.records)+[pscustomobject]@{id='DEC-9999';date='2026-10-08';title='Unsupported decision';path='docs/decisions/current/README.md';status='proposed';evidence=@('missing-evidence.md');supersedes=@()};Save-Fixture 'docs/decisions/current-decisions.json' ($m|ConvertTo-Json -Depth 12)}},
        @{Name='LostDattSafeguard';Path='AGENTS.md';Expected='Governing assistant section changed';Mutate={Save-Fixture 'AGENTS.md' ((Read-Fixture 'AGENTS.md').Replace('The invocation never authorizes changing a security setting','The invocation authorizes changing a security setting'))}}
    )
    foreach($test in $tests) {
        $path=Join-Path $fixture $test.Path;$existed=Test-Path -LiteralPath $path;$original=if($existed){[IO.File]::ReadAllBytes($path)}else{$null}
        try {
            & $test.Mutate
            $rejected=$false
            try { & $validator -RepositoryRoot $fixture | Out-Null }
            catch { if($_.Exception.Message -notlike ('*'+$test.Expected+'*')){throw "Wrong rejection for $($test.Name): $($_.Exception.Message)"};$rejected=$true }
            if(-not $rejected){throw "Mutation escaped the documentation gate: $($test.Name)"}
            $proofs.Add([pscustomobject]@{Case=$test.Name;Rejected=$true;Expected=$test.Expected})
        } finally {if($existed){[IO.File]::WriteAllBytes($path,$original)}else{Remove-Item -LiteralPath $path}}
    }
    & $validator -RepositoryRoot $fixture | Out-Null
    [pscustomobject]@{Gate='DocumentationNegativeProofs';Passed=$true;Cases=$proofs.Count;Proofs=@($proofs)}
} finally {
    # Resolve and verify the exact owned synthetic fixture before recursive deletion.
    $resolved=[IO.Path]::GetFullPath($fixture)
    if($resolved.StartsWith($ownedPrefix,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved) -match '^SatiDocumentationTests_[a-f0-9]{32}$'){
        if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
    }else{throw 'Refused fixture cleanup outside the owned temp directory.'}
}
