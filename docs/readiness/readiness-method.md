# How Sati readiness is measured

The readiness thermometer describes documented progress toward safe multi-agency cloud operation. It is an evidence index, not a probability of safety, uptime measurement, regulatory certification, or comparison with the average product on the market. A high score cannot overrule an unresolved launch gate.

## One ledger and immutable history

[readiness.json](readiness.json) is the single machine-readable rubric and release ledger. Settings, release checks and documentation consume it; do not keep a second hand-maintained score in a ViewModel, README or agenda.

Schema version 1 contains:

- `rubrics`: immutable versions, each with `version` and its complete `criteria` array.
- `snapshots`: append-only release records with `release`, `assessedAt`, `sourceRevision`, `rubricVersion`, `summary`, `nextSteps` and exactly one assessment for every criterion in that rubric.
- Each criterion: `id`, `dimension`, plain-language `title`, positive `weight`, `blocking`, accountable `owner`, `requiredEvidence`, `failureModes`, `acceptance` and repository/primary-source `references`.
- Each assessment: `criterionId`, `status`, `evidence` entries containing `kind` and `reference`, and plain-language `notes`.

Evidence kinds are `source`, `synthetic`, `sql`, `live` and `independent`. Criterion design references may be traceable repository-relative paths or official source URLs. Assessment evidence must reference a retained repository artifact, optionally with line references, describing Sati's implementation or actual executed acceptance. An external standard explains a design; it cannot prove Sati passed acceptance. No credentials, PHI, unrestricted vendor responses, or private configuration belong in this ledger.

Once a rubric is referenced by a sealed release snapshot, its criteria, weights, blockers and requirements are immutable. New concerns or revised acceptance produce a new rubric version. Old snapshots continue to use their original denominator. Preserve old entries; do not rewrite them to improve the current score. Before sealing a candidate, correct mistakes openly. After sealing, record an advisory/incident and the next corrected release snapshot. An installed offline application displays its dated shipped assessment and cannot discover a later incident automatically. Apply actual activation restrictions through the approved operating process; distribute a corrected release rather than claiming a stale installed thermometer has been remotely revoked.

Each release snapshot must identify the actual audited source revision. If work is staged, name that fact and its reviewed diff/evidence in the notes; a baseline commit alone must not imply uncommitted changes were included in its tests. File-backed history is preserved through version control and release validation; JSON is not a tamper-proof audit store.

## Fixed points and fixed denominators

Schema version 1 fixes the following stage points. These points describe an evidence stage, not the fraction of defects repaired.

| Status | Points | Meaning |
|---|---:|---|
| Unknown | 0 | Adequate scope/evidence has not been established. |
| Planned | 0 | Named work exists, but a completed control is not credited. |
| Blocked | 0 | A concrete failed invariant or unresolved prerequisite prevents credit. State the reason. |
| Implemented | 25 | A meaningful mechanism exists in source, with incomplete acceptance. Source evidence is mandatory. |
| Tested | 50 | Source and meaningful executed synthetic or SQL acceptance are recorded. State the tested scope and outstanding live/independent gates. |
| Verified | 100 | The owner reviewed acceptance and all required evidence kinds/artifacts are adequate for the intended scope. |

A test body, suite name, green historical count or an unsupported claim of implementation is not execution evidence. The initial baseline references the assessment's recorded execution and explicitly states it was not rerun in this documentation task. A test record can support a bounded tested stage without proving all providers, configurations or failure modes.

A verified entry must contain every evidence kind listed in that criterion's `requiredEvidence`. Software checks the minimum kind set; a responsible reviewer must inspect relevance, completeness, provenance and freshness. Adding five arbitrary links is not verification. Source references in a planned, unknown or blocked entry describe the gap and earn no points.

For each dimension:

`dimension score = sum(weight × stage points) / sum(weights)`

For the whole rubric:

`overall score = sum(weight × stage points for every criterion) / sum(all weights)`

The initial rubric has 42 criteria, 14 in each of multitenancy, idempotency and operations, all weight 1. Each dimension therefore contributes equally. Missing, unknown, planned and blocked criteria stay in the denominator. There is no hidden “not applicable” subtraction. A feature can be deliberately unavailable: prove its boundary, document the supported scope in acceptance, and assess that scope honestly. Changing the supported target changes the rubric rather than silently dropping obligations.

Every criterion marked `blocking` must be verified with its required evidence before readiness can be declared. A score alone never opens a gate. Owner approvals, permitted data/use scope and deployed environment acceptance are also required: the thermometer does not authorize activation. Even a fully verified rubric means its declared acceptance passed; new risks remain possible.

## Initial baseline and future comparison

Release 1.3.37, assessed October 8, 2026 against `a1af92129f60728a0bbcf0dd27c42190644b1e3b`, is the **retrospective baseline**. Its evidence is the architecture assessment, inspected source and that assessment's recorded tests. No prior release snapshots were reconstructed. This readiness documentation and display work is not counted as previously shipped in that baseline.

The first comparison says **“Baseline — no prior snapshot.”** Do not invent an earlier score, upward trend or earned improvement. Compare two releases numerically only when both are valid and reference the same rubric version. A revised rubric says **“Criteria revised — no direct comparison.”** Scores under different criteria can be shown separately, but must not be presented as a measured gain/loss.

Select snapshots by validated numeric release version, not text sorting. Require unique releases, chronological record order, valid dates/source revision, known rubric versions, known status/evidence values and exactly one assessment per criterion. Resolve historical snapshots against their own rubric. Unknown rubric versions, duplicate/missing criteria, malformed evidence or current assembly-version mismatch yield **“Readiness unavailable”** with a useful reason; they must never yield a optimistic score.

The current implementation and these documents must agree on that validation and formula. A later change to stage points or validation semantics requires a schema/versioned-method change, migration of readers and preservation of original historical calculations.

## Ledger durability and growth

Readers and release helpers cap the input at 16 MiB, 1,024 snapshots, 128 rubrics, and 512 criteria per rubric. All record arrays and text have further bounds. These limits protect the offline Settings view from malformed or unbounded input. Never truncate older releases or raise a score to fit a limit. At 75% of either byte or snapshot capacity, schedule a versioned reader/ledger migration: retain append-only history partitions, their integrity manifests and original rubric denominators; embed current/previous assessments and the intended history view with tested compatibility. Prove old installed readers fail safely and new readers retain each historic result before adopting a new schema. The normal release gate rejects an over-limit report.

The append helper serializes cooperating writers with an exclusive transient lock, creates drafts exclusively, validates raw JSON before normalization, rechecks the ledger hash before replacement, and renames a same-directory temporary file. Arbitrary editors/Git do not obey that advisory lock; keep the release operation exclusive and review Git state. Version control and the CI change-base comparison detect changed committed history; this file is not an independently trusted audit service.

## Release assessment workflow

1. Read the active backlog, [contingency catalog](multitenancy-contingencies.md), [protocol baseline](protocol-baseline.md), current rubric and latest snapshot. Generate the actual source/project/deployment inventory instead of copying historical counts.
2. Audit material changes and enabled scope. Add new risks to a new rubric when needed. Map every criterion to an owner, control and exact evidence; preserve explicit gaps.
3. Run meaningful synthetic and guarded private SQL tests for affected guarantees. Security, concurrency, replay and fairness fixes must fail against the unfixed control before they are credited. Use deterministic barriers/time; test observable effects.
4. For live, vendor or independent acceptance, obtain the separate authorization and retain the approved scope/result. The ledger does not permit live calls, PHI access, migrations, deployments or Azure security changes.
5. Record environment, source revision, commands, result counts/skips, failure boundaries and artifact locations. Build/test success cannot substitute for an unperformed deployment, permission, delivery or restore drill.
6. Append the candidate release snapshot with all criteria, truthful states and plain-language remaining steps. Validate structural rules, unchanged sealed history, evidence completeness and current version.
7. An accountable release/security/operations reviewer signs off the release record and allowed use scope outside the score. Seal it with the release artifacts. A green thermometer cannot waive a separately recorded restriction.

Changes to routes, schemas, tenant keys, joins, file/cache/search stores, identity/permissions, background queues, providers, retry settings, retention, restores, hosting, release protocols or client versions trigger mandatory tenancy/replay review. Incidents and discovered omissions also trigger review. This is how the catalog stays useful without claiming every future failure has already been predicted.

## Plain-language Settings presentation

Show the current release/date and three scores: **Overall readiness**, **Agency separation**, and **Safe repeat requests**. The overall score also includes the operating-evidence dimension; it is not an average of only the two displayed component scores. Explain that the scores come from documented checks, and show the unavailable/not-ready state, remaining launch checks and the snapshot's next steps. Staff should be able to read what exists and what is unfinished without HTTP or database terminology.

Do not expose secret references, clinical identifiers or low-level failure payloads in this view. Keep evidence/rubric details in the linked technical documentation. A stale or invalid current record shows unavailable; source stages and a thermometer must not be described as proof the service is production-ready. Historical subjective discussion ratings are not directly comparable to this finer evidence-stage rubric.

## Release helper commands

Follow the [release playbook](../../RELEASE_PLAYBOOK.md) and [authoritative release bars](authoritative-release-bars.md). These helpers operate on the review ledger; they do not deploy, test a database, or automatically improve a score.

From the repository root, create a draft for the next real release using its audited revision:

```powershell
./scripts/New-ReleaseReadinessSnapshot.ps1 -Release <new-version> -SourceRevision <audited-40-character-revision> -AssessedAt <YYYY-MM-DD> -DraftPath work/readiness/<new-version>.json
```

Review every criterion, replace all review-required placeholders and retain the evidence. Then append the reviewed record:

```powershell
./scripts/New-ReleaseReadinessSnapshot.ps1 -Release <new-version> -SnapshotPath work/readiness/<new-version>.json
./scripts/Test-ReleaseReadiness.ps1
```

Use the helper's explicit rubric parameter when adopting a new rubric. Supply the assessment's actual local calendar date (America/New_York for this baseline); the helper's omitted-date default is UTC. The release validator checks the current project version, exact criterion coverage, minimum evidence stages, referenced files and unchanged prior history when supplied its baseline. It can correctly pass an honest **not-ready** release record; it must reject false or malformed evidence accounting. The separate allowed-use/production gates remain in force. Placeholder command values must be replaced before execution; do not invent a new release or audited commit merely to record progress.
