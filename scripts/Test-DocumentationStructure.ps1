[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

# Source-only documentation gate. Never connects to a database, network, Azure or private config.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$failures = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $failures.Add($Message) } }
function Resolve-Document([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Document path is empty.' }
    if ([IO.Path]::IsPathRooted($Path)) { throw "Document must use a repository-relative path: $Path" }
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $Path))
    $prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Document escapes repository: $Path" }
    return $absolute
}
function Read-Document([string]$Path) {
    $absolute = Resolve-Document $Path
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { $failures.Add("Missing document: $Path"); return '' }
    return [IO.File]::ReadAllText($absolute)
}
function Section([string]$Text, [string]$Name) {
    $match = [regex]::Match($Text, '(?ms)^## ' + [regex]::Escape($Name) + '\r?\n.*?(?=^## |\z)')
    return [regex]::Replace($match.Value, '(?m)^\*Last updated:.*\r?\n?', '').Replace("`r`n", "`n").Trim()
}
function Markdown-Anchor([string]$Heading) {
    return [regex]::Replace($Heading.ToLowerInvariant().Trim(), '[^\p{L}\p{N}\- _]', '').Replace(' ', '-')
}

$manifest = (Read-Document 'docs/documentation-index.json') | ConvertFrom-Json
$attributes = Read-Document '.gitattributes'
foreach ($required in @('docs/archive/** -text -whitespace','docs/decisions/records/** -text -whitespace','docs/backlog/legacy-open-items.md text eol=lf')) {
    Check (($attributes -split '\r?\n') -ccontains $required) "Historical byte-preservation attribute is missing: $required"
}
Check ($manifest.schemaVersion -eq 1) 'Unsupported documentation manifest version.'
Check ($manifest.factsOwner -ceq 'DATABASE_ENVIRONMENTS.md') 'Current deployment facts must have one owner: DATABASE_ENVIRONMENTS.md.'
Check ($manifest.briefingOwner -ceq 'AGENTS.md') 'Assistant briefing must have one owner: AGENTS.md.'
Check ($manifest.backlogOwner -ceq 'AGENDA.md') 'Active backlog owner must be AGENDA.md.'
Check ($manifest.readinessOwner -ceq 'docs/readiness/README.md') 'Readiness must use its canonical entry.'
$documents = @($manifest.documents)
$rootNames = @(Get-ChildItem -LiteralPath $root -Filter '*.md' -File | ForEach-Object Name)
Check (@($documents.path | Select-Object -Unique).Count -eq $documents.Count) 'A document is mapped more than once.'
foreach ($name in $rootNames) { Check (@($documents | Where-Object path -CEQ $name).Count -eq 1) "Unmapped root Markdown: $name" }
foreach ($doc in $documents) {
    Check ($doc.path -cin $rootNames) "Manifest root path is absent or not root Markdown: $($doc.path)"
    Check ($doc.role -cin @('canonical','forwarder')) "Invalid document role: $($doc.path)"
    Check ($doc.state -cin @('current','historical','mixed-evidence')) "Invalid document state: $($doc.path)"
    Check (-not [string]::IsNullOrWhiteSpace($doc.topic)) "Missing canonical topic: $($doc.path)"
    Check (-not [string]::IsNullOrWhiteSpace($doc.reason)) "Missing document disposition: $($doc.path)"
    Check (Test-Path -LiteralPath (Resolve-Document $doc.canonicalPath) -PathType Leaf) "Broken canonical link: $($doc.path) -> $($doc.canonicalPath)"
    if ($doc.role -eq 'canonical') { Check ($doc.path -ceq $doc.canonicalPath) "Canonical root must own its path: $($doc.path)" }
    else {
        $body = Read-Document $doc.path
        Check ($body.Contains('](' + $doc.canonicalPath + ')')) "Forwarder lacks its canonical link: $($doc.path)"
    }
}
$scoped = @($manifest.scopedOwners)
Check (@($scoped.path | Select-Object -Unique).Count -eq $scoped.Count) 'A scoped document is mapped more than once.'
foreach ($entry in $scoped) {
    Check (-not [string]::IsNullOrWhiteSpace($entry.topic)) "Missing scoped canonical topic: $($entry.path)"
    Check ($entry.state -cin @('current','historical','mixed-evidence')) "Invalid scoped-owner state: $($entry.path)"
    Check ($null -eq $entry.role -or $entry.role -cin @('canonical','derived')) "Invalid scoped-owner role: $($entry.path)"
    Check (Test-Path -LiteralPath (Resolve-Document $entry.path) -PathType Leaf) "Missing scoped owner: $($entry.path)"
    if ($entry.role -eq 'derived') {
        Check (Test-Path -LiteralPath (Resolve-Document $entry.canonicalPath) -PathType Leaf) "Derived document lacks its canonical source: $($entry.path)"
    }
}
$owners = @($documents | Where-Object role -CEQ 'canonical') + @($scoped | Where-Object { $_.role -ne 'derived' })
foreach ($group in @($owners | Group-Object topic)) { Check ($group.Count -eq 1) "Duplicate canonical topic owner: $($group.Name)" }
foreach ($entry in @($documents | Where-Object role -CEQ 'forwarder') + @($scoped | Where-Object role -CEQ 'derived')) {
    $sourceOwner = @($owners | Where-Object { $_.path -ceq $entry.canonicalPath -and $_.topic -ceq $entry.topic })
    Check ($sourceOwner.Count -eq 1) "Forwarded/derived topic lacks one matching canonical owner: $($entry.path)"
}

foreach ($path in @('SECURITY_AUDIT_2026-09-03.md','CONCURRENCY_AUDIT.md','SATI_STRUCTURAL_REVIEW_2026-09-28.md','SATI_SYSTEM_MAP.md')) {
    Check (@($documents | Where-Object { $_.path -ceq $path -and $_.state -ceq 'historical' }).Count -eq 1) "Historical review declared current: $path"
}
$claude = Read-Document 'CLAUDE.md'
Check ($claude.Contains('[AGENTS.md](AGENTS.md)') -and ($claude -split '\r?\n').Count -le 14) 'CLAUDE.md must be a short canonical briefing pointer.'
Check ($claude -notmatch '(?im)^## |^\d+\. |^- ') 'CLAUDE.md must not carry independent governing rules.'
$agents = Read-Document 'AGENTS.md'
$originalAgents = Read-Document 'docs/archive/2026-10-08/AGENTS.md'
foreach ($section in @('Product direction','Engineering rules','DATT release trigger','Healthcare and regulatory posture','Working with Josh')) {
    Check ((Section $agents $section) -ceq (Section $originalAgents $section)) "Governing assistant section changed during consolidation: $section"
}
Check ($agents.Contains('docs/documentation-governance.md')) 'AGENTS must require documentation governance.'
$standingWorkflow = Section $agents 'Standing work and documentation upkeep'
Check (-not [string]::IsNullOrWhiteSpace($standingWorkflow)) 'Standing work and documentation upkeep instructions are missing.'
foreach ($required in @('After each significant portion of work','Perform the next thing on the list','case-insensitive comparison','Next eligible item','Announce the concrete stable ID','eligible reversible local slice','not invoke DATT','Before the final response','sealed release score','docs/readiness/work-evidence.md')) {
    Check ($standingWorkflow.Contains($required)) "Standing workflow safeguard is missing: $required"
}
$upkeep = Section (Read-Document 'docs/documentation-governance.md') 'Upkeep after significant work'
Check ($upkeep.Contains('AGENTS.md#standing-work-and-documentation-upkeep') -and $upkeep.Contains('sealed release scores')) 'Documentation governance must link the standing workflow and preserve sealed release evidence.'
Check ((Read-Document 'README.md').Contains('Perform the next thing on the list')) 'README must expose the next-work phrase.'
Check (@($scoped | Where-Object { $_.path -ceq 'docs/readiness/work-evidence.md' -and $_.topic -ceq 'working-evidence-ledger' -and $_.state -ceq 'current' }).Count -eq 1) 'Working evidence ledger must have one registered current owner.'

foreach ($snapshot in @($manifest.snapshots)) {
    $p = Resolve-Document $snapshot.path
    if (Test-Path -LiteralPath $p -PathType Leaf) {
        Check ((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $snapshot.sha256) "Historical snapshot changed: $($snapshot.path)"
    } else { $failures.Add("Missing historical snapshot: $($snapshot.path)") }
}
foreach ($source in @('AGENTS.md','CLAUDE.md','README.md','ARCHITECTURE.md','DECISIONS.md','AGENDA.md','DATABASE_ENVIRONMENTS.md','OPERATIONS.md')) {
    Check (@($manifest.snapshots | Where-Object source -CEQ $source).Count -eq 1) "Pre-consolidation core snapshot must remain registered once: $source"
}
Check (@($manifest.snapshots.path | Select-Object -Unique).Count -eq @($manifest.snapshots).Count) 'Historical snapshot registered more than once.'

# Every current canonical page is checked even if its editor forgets requiredLinks. Preserved
# historical relative links retain their original root context.
$currentPages = @($documents | Where-Object state -CEQ 'current' | ForEach-Object path) + @($scoped | Where-Object state -CEQ 'current' | ForEach-Object path)
$newRegistry = (Read-Document 'docs/decisions/current-decisions.json') | ConvertFrom-Json
$linkPages = @($manifest.requiredLinks) + $currentPages + @($newRegistry.records.path)
foreach ($path in @($linkPages | Where-Object { $_ -cmatch '\.md$' } | Select-Object -Unique)) {
    $body = Read-Document $path
    foreach ($link in [regex]::Matches($body, '\]\(([^)]+)\)')) {
        $target = $link.Groups[1].Value.Trim('<','>')
        if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $target -notmatch '\.md(?:#|$)') { continue }
        $parts = $target -split '#', 2
        $relative = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent (Resolve-Document $path)) $parts[0]))
        Check ($relative.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) "Canonical link escapes root in $path"
        if (-not (Test-Path -LiteralPath $relative -PathType Leaf)) { $failures.Add("Broken Markdown link in ${path}: $target"); continue }
        if ($parts.Count -eq 2) {
            $destination = [IO.File]::ReadAllText($relative)
            $anchors = @([regex]::Matches($destination, '(?m)^#{1,6}\s+(.+)$') | ForEach-Object { Markdown-Anchor $_.Groups[1].Value })
            Check ($parts[1] -cin $anchors) "Broken Markdown anchor in ${path}: $target"
        }
    }
}
# Inventory literals may identify commands/targets elsewhere; current SKU/quota assertions may not.
$factPattern = '(?im)^.*(?:runs on App Service Free F1|database is serverless|Current SQL evidence is Basic|Compute\s*\|.*(?:GP_S_|Basic)|alwaysOn.*false|has a 60-minute daily CPU quota|free monthly allowance is exhausted).*$'
foreach ($path in @(@($currentPages) + @($manifest.currentFactConsumers) | Where-Object { $_ -cne $manifest.factsOwner -and $_ -cmatch '\.md$' } | Select-Object -Unique)) {
    Check ((Read-Document $path) -notmatch $factPattern) "Duplicated current deployment assertion outside inventory: $path"
}
$architecture = Read-Document 'ARCHITECTURE.md'
Check (($architecture -split '\r?\n').Count -le 150 -and $architecture -notmatch '(?im)^## Session Changelog|^## October \d') 'Current architecture must remain a focused index, not release/history paragraphs.'

$agenda = Read-Document 'AGENDA.md'
$activeIds = @([regex]::Matches($agenda, '(?m)^\| (SATI-[A-Z]+-\d{3}) \|') | ForEach-Object { $_.Groups[1].Value })
Check ($activeIds.Count -ge 10 -and @($activeIds | Select-Object -Unique).Count -eq $activeIds.Count) 'Active backlog stable IDs missing or duplicated.'
Check ($agenda -notmatch '(?m)^\s*- \[ \]') 'Active agenda cannot add anonymous unchecked tasks; register a stable SATI ID.'
$nextPointers = @([regex]::Matches($agenda, '(?m)^\*\*Next eligible item:\*\* (SATI-[A-Z]+-\d{3})\s*$'))
Check ($nextPointers.Count -eq 1) 'Agenda must have exactly one next eligible stable-ID pointer.'
if ($nextPointers.Count -eq 1) {
    $nextId = $nextPointers[0].Groups[1].Value
    Check (@($activeIds | Where-Object { $_ -ceq $nextId }).Count -eq 1) "Next eligible pointer does not resolve exactly once in the active backlog: $nextId"
}
$nextSlice = Section $agenda 'Next eligible work'
foreach ($required in @('**Eligibility:**','**Bounded slice:**','**Dependencies and owners:**','**Boundaries and completion evidence:**','AGENTS.md#standing-work-and-documentation-upkeep','docs/readiness/work-evidence.md')) {
    Check ($nextSlice.Contains($required)) "Next eligible slice is missing scope/dependency/evidence authority: $required"
}
$legacy = (Read-Document 'docs/backlog/legacy-open-items.json') | ConvertFrom-Json
$legacySource = Read-Document $legacy.source
$sourceLines = $legacySource -split '\r?\n'
$sourceOpen = @([regex]::Matches($legacySource, '(?m)^\s*- \[ \]\s+'))
Check (@($legacy.items).Count -eq $sourceOpen.Count) 'Legacy unchecked work was dropped or added without a source disposition.'
Check (@($legacy.items.id | Select-Object -Unique).Count -eq @($legacy.items).Count) 'Legacy backlog IDs duplicated.'
Check (@($legacy.items.sourceLine | Select-Object -Unique).Count -eq @($legacy.items).Count) 'Legacy source tasks duplicated.'
$legacyPage = Read-Document 'docs/backlog/legacy-open-items.md'
foreach ($item in @($legacy.items)) {
    Check ($item.id -cmatch '^LEG-\d{4}$') "Invalid legacy ID: $($item.id)"
    Check ($item.state -cin @('requires-revalidation','promoted','merged','superseded','closed')) "Invalid legacy disposition: $($item.id)"
    $firstLine = ($item.text -split '\r?\n')[0]
    Check ($item.sourceLine -gt 0 -and $item.sourceLine -le $sourceLines.Count -and $sourceLines[$item.sourceLine-1] -ceq $firstLine) "Legacy source text/line lost: $($item.id)"
    if ($item.sourceLine -gt 0 -and $item.sourceLine -le $sourceLines.Count) {
        $sourceBlock = @($sourceLines[$item.sourceLine-1]); $next = $item.sourceLine
        while ($next -lt $sourceLines.Count -and $sourceLines[$next] -match '^\s{2,}\S') { $sourceBlock += $sourceLines[$next]; $next++ }
        Check ($item.text.Replace("`r`n","`n") -ceq ($sourceBlock -join "`n")) "Legacy task reasoning/continuation changed: $($item.id)"
    }
    Check ($legacyPage.Contains('## ' + $item.id) -and $legacyPage.Contains($item.text.Replace("`r`n","`n"))) "Legacy task not visible in the backlog: $($item.id)"
    if ($item.state -ne 'requires-revalidation') { Check (-not [string]::IsNullOrWhiteSpace($item.evidence)) "Legacy disposition lacks evidence: $($item.id)" }
}
$decisionRegistry = (Read-Document 'docs/decisions/records.json') | ConvertFrom-Json
$decisionSource = Read-Document $decisionRegistry.source
$decisionMatches = [regex]::Matches($decisionSource, '(?m)^## .+$')
Check (@($decisionRegistry.records).Count -eq $decisionMatches.Count) 'Decision records were dropped.'
Check (@($decisionRegistry.records.id | Select-Object -Unique).Count -eq @($decisionRegistry.records).Count) 'Decision record IDs duplicated.'
$decisionIndex = Read-Document 'docs/decisions/README.md'
for ($i=0; $i -lt @($decisionRegistry.records).Count; $i++) {
    $record = $decisionRegistry.records[$i]
    $body = Read-Document $record.path
    Check ($record.id -cmatch '^DEC-\d{4}$') "Invalid decision ID: $($record.id)"
    $end = if ($i+1 -lt $decisionMatches.Count) { $decisionMatches[$i+1].Index } else { $decisionSource.Length }
    $expected = $decisionSource.Substring($decisionMatches[$i].Index,$end-$decisionMatches[$i].Index).Replace("`r`n","`n")
    Check ($body.EndsWith($expected, [StringComparison]::Ordinal)) "Retained decision reasoning changed: $($record.id)"
    Check ($decisionIndex.Contains('| ' + $record.id + ' |')) "Decision missing from index: $($record.id)"
}
Check ((Read-Document 'DECISIONS.md').Contains('## Explicit supersession')) 'Decision supersession register is missing.'
$currentDecisions = (Read-Document 'docs/decisions/current-decisions.json') | ConvertFrom-Json
Check ($currentDecisions.schemaVersion -eq 1) 'Unsupported current-decision registry.'
Check (@($currentDecisions.records.path | Select-Object -Unique).Count -eq @($currentDecisions.records).Count) 'New decision paths duplicated.'
$allDecisionIds = @($decisionRegistry.records | ForEach-Object { $_.id }) + @($currentDecisions.records | ForEach-Object { $_.id })
Check (@($allDecisionIds | Select-Object -Unique).Count -eq $allDecisionIds.Count) 'Imported/current decision IDs collide.'
$currentDecisionIndex = Read-Document 'docs/decisions/current/README.md'
foreach ($record in @($currentDecisions.records)) {
    Check ($record.id -cmatch '^DEC-\d{4}$') "Invalid new decision ID: $($record.id)"
    Check ($record.date -cmatch '^\d{4}-\d{2}-\d{2}$') "New decision date is missing: $($record.id)"
    Check ($record.status -cin @('proposed','accepted','superseded')) "New decision status is missing: $($record.id)"
    Check (-not [string]::IsNullOrWhiteSpace($record.title)) "New decision title is missing: $($record.id)"
    Check (@($record.evidence).Count -gt 0) "New decision evidence is missing: $($record.id)"
    foreach ($evidence in @($record.evidence)) {
        Check (-not [string]::IsNullOrWhiteSpace($evidence)) "New decision evidence is empty: $($record.id)"
        if ($evidence -notmatch '^https?://') { Check (Test-Path -LiteralPath (Resolve-Document $evidence) -PathType Leaf) "Broken new decision evidence: $($record.id) -> $evidence" }
    }
    $newBody = Read-Document $record.path
    Check ($newBody.Contains($record.id)) "New decision body lacks its stable ID: $($record.id)"
    Check ($currentDecisionIndex.Contains('| ' + $record.id + ' |')) "New decision missing from current index: $($record.id)"
    foreach ($previous in @($record.supersedes)) { Check ($previous -cin $allDecisionIds) "New decision supersedes an unknown ID: $($record.id) -> $previous" }
}
# Newly introduced canonical documents must be registered, not only root files. Immutable
# archives retain their own hash registry; individual retained decisions use their stable registry.
$mappedDocs = @($scoped.path) + @($decisionRegistry.records.path) + @($currentDecisions.records.path)
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'docs') -Filter '*.md' -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\','/')
    if ($relative.StartsWith('docs/archive/',[StringComparison]::Ordinal)) { continue }
    Check ($relative -cin $mappedDocs) "Unmapped canonical documentation: $relative"
}
if ($failures.Count) { throw ("Documentation structure failed:`n- " + ($failures -join "`n- ")) }
[pscustomobject]@{Gate='DocumentationStructure';Passed=$true;RootDocuments=$documents.Count;ScopedOwners=$scoped.Count;Snapshots=@($manifest.snapshots).Count;ActiveItems=$activeIds.Count;NextEligibleItem=$nextId;LegacyOpenItems=@($legacy.items).Count;ImportedDecisionRecords=@($decisionRegistry.records).Count;NewDecisionRecords=@($currentDecisions.records).Count}
