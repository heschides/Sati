# Working evidence ledger

This canonical ledger records significant work between release assessments. Entries are dated
working evidence; they do not alter [sealed release snapshots](readiness.json), immutable rubrics,
activation state or regulatory conclusions. Follow [the standing workflow](../../AGENTS.md#standing-work-and-documentation-upkeep)
and [the assessment method](readiness-method.md). Link a reviewed release snapshot later when
applicable; retain the earlier working record and its limits.

## 2026-10-08 — Documentation consolidation and standing workflow

**Stable work ID:** SATI-DOC-001. **Status:** implemented and locally verified; unreleased.
**Scope:** canonical documentation owners, preserved source history/open tasks, enforced structure,
readiness references, Settings thermometers, release gates, and the user-requested upkeep/next-work workflow. The next-work phrase has
not been invoked by adding its instructions.

| Evidence field | Dated result or required entry |
|---|---|
| Source/revision and changed owners | Protected starting source `master` at `a1af92129f60728a0bbcf0dd27c42190644b1e3b`, version 1.3.37, plus this uncommitted documentation/Settings/gate change. Owners: root briefing/index/backlog/environment/operations/release/handoff, `docs/` registries, `Services/ReleaseReadiness.cs`, Settings panel/ViewModel, embedding project and readiness tests/scripts/CI. No business-worker/API/persistence implementation changed. |
| Actual verification | Complete Release solution build passed. `scripts/Test-IsolatedLocalDb.ps1 -FullSolution`: desktop 3,013 passed/1 skipped; API 1,119; signatures 119; portal 8; Carika 4. Total 4,263 passed, zero failed, one gated local AI evaluation skipped. Fresh private synthetic instance was stopped/deleted. Demo client build passed. Portal Node suite passed 9/9. Documentation gate and 22 negative proofs passed; readiness/helper 43 cases passed including atomic append, exact replay, raw duplicate refusal and writer exclusion; DATT source consistency and its 9 cases passed. Current report validation passed. After the final plain summary edit, all 58 focused readiness/parser/WPF tests passed and previews were rerendered. Historical captures and imported decision text have enforced Git byte-preservation attributes; the retained backlog uses LF. |
| Failed or unrun checks | Initial testhost/atomic-file operations were blocked by sandbox IPC/file-replacement permissions, then passed under bounded approved synthetic test execution. A real WPF render regression caught empty thermometer fill and was repaired; a denominator mutation intentionally inflated 25% to 50%, failed, was restored, and passed. Existing build warnings remain; fresh dependency vulnerability metadata was unavailable because NuGet access failed. No live activation/hosting/vendor/restore or independent review was performed. |
| Product/runtime/vendor proof | Real WPF panel bindings/rendering, visible fill, text wrapping, enlarged text, simulated high contrast, non-color and automation status, missing/malformed reports, release/regression/rubric history and byte/snapshot boundaries have local synthetic acceptance. No actual installed customer-device or live vendor/cloud acceptance is established. |
| Readiness criteria affected | Local working evidence supports the OP14 release-display traceability implementation within this change; it does not establish independent/live acceptance or repair other launch blockers. The 1.3.37 retrospective baseline's scores remain unchanged. Review these new results for the next actual release. |
| Decisions | [DEC-0219](../decisions/current/2026-10-08-DEC-0219.md), [DEC-0220](../decisions/current/2026-10-08-DEC-0220.md), [DEC-0221](../decisions/current/2026-10-08-DEC-0221.md). |
| Checkout and drift proof | A fresh synthetic Git checkout of 2,171 explicitly selected public files with `core.autocrlf=true` passed the documentation gate, retaining archived hashes, imported decision reasoning and multiline backlog continuations. New-file whitespace checks passed in that disposable index; tracked whitespace checks passed in the actual repository. The real Git index was untouched. Final source preflight and readiness validation passed in the actual repository; immutable committed-ledger comparison is not yet available because this ledger is newly added and uncommitted. |
| Remaining blockers and limits | Unresolved items and release blockers remain in [the agenda](../../AGENDA.md), topic owners and readiness ledger. Imported tasks require explicit revalidation. |
| Next eligible slice | [The agenda pointer](../../AGENDA.md#next-eligible-work) selects recoverable per-agency note-worker failure isolation, with fail-first proof and existing controls preserved. This task did not implement that worker change. |

**Evidence locations:** verification ran in an isolated public-source copy under
`C:\Users\Joshu\Documents\Codex\2026-10-08\referenced-chatgpt-conversation-this-is-an\work\Sati`.
Full TRX records are in `TestResults/BackgroundWorkersFullSql` there; final focused readiness TRX
is in `TestResults/ReleaseReadinessFinal`. User-facing previews/results/change manifests are in
the task's `outputs` folder. The guarded synchronization manifest records each exact original and
final SHA-256. These locations identify working artifacts, not published release evidence.

## 2026-10-08 — SATI-WRK-001 note-worker agency-failure continuation

**Stable work ID and bounded slice:** SATI-WRK-001, first local implementation slice:
recoverable per-agency failures in `NoteAbandonmentWorker.RunDueAsync`.

**Status and source/revision:** implemented and locally verified; unreleased and default-off.
Starting source was `master` at `a1af92129f60728a0bbcf0dd27c42190644b1e3b` plus the existing
uncommitted consolidation/Settings/gate work, which was preserved. Josh directly requested
review of the consolidated summary and the next task; this was not a DATT invocation.

**Changed behavior, ownership and canonical paths:** the API worker catches only the
agency sweep's classified recoverable failures, leaves failed A due and global completion
false, and gives healthy B a turn in the same pass. Only returned committed counts are credited.
A is invoked once per worker pass; after the fault clears, a later same-day pass completes A
without rerunning or auditing completed B. Cancellation and unknown/fatal/shared failures
propagate. An at-limit batch remains due. Safe diagnosis records agency ID and failure type.
The sweep, its execution strategy, 100-note bound, revision guards and atomic audit transaction,
SQL/reset locks, default-off setting and hourly cadence are unchanged. Source owner:
`Sati.Api/Infrastructure/NoteAbandonmentWorker.cs`; test owner:
`Sati.Api.Tests/NoteAbandonmentWorkerTests.cs`. [W8](../../BACKGROUND_WORKERS_HANDOFF.md) owns
current implementation scope, [the worker architecture](../architecture/workers.md) its boundary,
and [DEC-0222](../decisions/current/2026-10-08-DEC-0222.md) the operational fault policy.
No route, model/schema or shared business-rule ownership changed.

**Actual tests/checks, commands, results and evidence locations:**

| Executed check | Result and evidence |
|---|---|
| Unfixed worker, new A-to-B audit-fault regression | **1 failed, 0 passed**, as required: A's concurrency exception propagated at the original sweep call before B progressed. `TestResults/WorkerIsolation/worker-isolation-before.trx`. |
| Fixed worker focused suite | **22 passed, 0 failed, 2 SQL-only skips**. `TestResults/WorkerIsolation/worker-isolation-after.trx`. Demonstrates real SQLite rollback after the guarded A update/audit fault, B's exact one-note revision and matching system audit, once-per-pass A attempts, later fake-hour recovery/B skipping, deliberate EF/SQL fault classification, cancellation/race, shared discovery/context/coordination faults, exact 100-note boundary, default-off/next-day and disable-before-B behavior. |
| Existing API sweep tests | **2 passed, 0 failed**: exact audit/tenant scope and concurrent-edit revision protection. `TestResults/WorkerIsolation/note-abandonment-api.trx`. |
| Shared portable rule tests | **7 passed, 0 failed**. `TestResults/WorkerIsolation/note-abandonment-rules.trx`. |
| Existing isolated SQL coordination proofs | **2 passed, 0 failed/skipped**: independent-host sweep exclusion and Demo reset exclusive-lease refusal. `TestResults/IsolatedSqlServer/Api/Joshu_LONGCHENPA_2026-10-08_14_48_25_net10.0.trx`. The new private `SatiSqlTests_dd0efb88351549d1a0c0a766e8b9d3da` instance was stopped and deleted; no shared instance or working database was connected to. |
| Test barrier cleanup follow-up | Independent review found a failing barrier could leave the disablement task undrained before fixture disposal. Added test-owned cancellation and unconditional task draining; the changed case passed **1/1** after rebuilding. `TestResults/WorkerIsolation/worker-disablement-cleanup.trx`. This overlaps the focused suite and is not another unique case. |
| Builds and documentation | Serial Release API/desktop test-project builds passed with existing unrelated warnings. Documentation structure gate and its **22 negative mutation proofs** passed. Whitespace checks passed for this slice. Sealed readiness/rubric/history and preserved documentation captures were not edited. |

Build commands were `dotnet build Sati.Api.Tests/Sati.Api.Tests.csproj --configuration Release
--no-restore --disable-build-servers -m:1 -v minimal` and the same command for
`Sati.Tests/Sati.Tests.csproj`. Portable executions used `dotnet test` with `--configuration
Release --no-build --no-restore`, focused filters for `NoteAbandonmentWorkerTests`,
`NoteAbandonmentApiTests`, `NoteAbandonmentRulesTests` and the changed disablement case,
named TRX loggers and `--results-directory TestResults/WorkerIsolation`. The before-fix filter
was `RecoverableAuditFailureRollsBackAgencyAAndAllowsBThenRetriesOnlyAOnLaterPass`.
SQL execution was `pwsh -NoProfile -File scripts/Test-IsolatedLocalDb.ps1 -NoteAbandonmentOnly`.
Documentation checks were `scripts/Test-DocumentationStructure.ps1` and
`scripts/Test-DocumentationStructureChecks.ps1`. These are focused checks, not a full-solution run.

**Failed/unrun checks, reason and verification limits:** the intended before-fix regression
failed as recorded. The sandboxed testhost could not complete; bounded approved test execution
outside its IPC restriction supplied meaningful results. A plain build initially stopped without
useful diagnostics; serial build-server-disabled builds succeeded. One stale-assembly attempt
after an unsuccessful build was discarded and supplies no acceptance evidence. No full-solution,
load, real SQL deadlock/retry-exhaustion, live-host/vendor, alert, restore or installed-client
acceptance was run. Synthetic `SqlException` construction and EF wrapper injection test the
classification policy through the actual SQLite sweep; they do not simulate SQL Server's
deadlock detector or actual provider retry timing. The shared connection probe uses a synthetic
context-factory failure. Other cancellation/failure/batch/disablement checks are preserved-control
acceptance; only the main A-to-B regression has the supplied unfixed-code proof.

**Relevant readiness criterion IDs and evidence class:** local source/synthetic working evidence
for the bounded scenario 12 portions of MT06/MT07, plus preservation of ID03/ID04/OP06
revision/atomic-audit invariants and the existing MT10 SQL lock/reset proofs. This does not close
those criteria, prove full fairness or advance a sealed release score. No runtime activation,
deployment, cloud/security change, live data access or real external send occurred.

**Durable decisions, alternatives and supersession links:**
[DEC-0222](../decisions/current/2026-10-08-DEC-0222.md) records the explicit EF concurrency and
all-1205/nonfatal SQL policy, deliberate wrappers and cancellation precedence. Blanket catches,
message classification, arbitrary wrapper traversal, immediate retries and lock/concurrency
changes were rejected. No earlier governing decision, D1–D4 or operating authority is superseded.

**Remaining risks/blockers, dependencies and deferred work:** agency enumeration and bookkeeping,
healthy-agency wait/total-run bounds, clearinghouse poison/backoff isolation, fair account/feed
selection, aggregate budgets/API admission and multi-host load/interactive-latency evidence remain
W8 work. Existing EF retry timing is unchanged. Fatal/shared failures still terminate the pass;
healthy progress is conditional on available shared dependencies. Activation/operations,
structural tenancy, billing R1/R2 and complete recovery retain their separate owners/gates.

**Next eligible stable ID and bounded slice:** SATI-WRK-001, extend the Claim.MD HTTP exchange's
existing 45-second budget through response-body reads using fake time, preserving SQL
wait/pacing/quota/locks and uncertain-send semantics. [The agenda pointer](../../AGENDA.md#next-eligible-work)
records revalidated source anchors, dependencies, exact acceptance and separate action boundaries.

**Later reviewed release snapshot, if any:** pending; the sealed 1.3.37 assessment is unchanged.

## 2026-10-08 — SATI-WRK-001 Claim.MD HTTP exchange deadline

**Stable work ID and bounded slice:** SATI-WRK-001, second local implementation slice:
extend the existing 45-second HTTP budget through response-body I/O.

**Status and source/revision:** implemented and locally verified; unreleased and default-off.
Starting source was `master` at `a1af92129f60728a0bbcf0dd27c42190644b1e3b` plus preserved
uncommitted consolidation/Settings/gate and first worker-slice changes. Josh directly requested
the next task. No DATT, activation, release or external-action authority was inferred.

**Changed behavior, ownership and canonical paths:** `ClaimMdSandboxConnector` owns the
single named 45-second exchange deadline using injected `TimeProvider`, linked to the
coordinated caller token. The deadline starts after admission/pacing and covers sending/headers,
stream acquisition and every body read, with cancellation checks before send and before return.
`Program.cs` delegates client configuration to the connector helper, which removes the competing
client timer. No retry is added. Existing request/response/stream disposal unwinds on cancellation.
The upload worker remains unchanged: an exception after committed `Sending` produces
`OutcomeUnknown`, and neither state is automatically resent. Fixed host, key/redirect validation,
16 MiB cap, gates, SQL admission/command timeouts, quota/pacing and locks are preserved.

Source owners: `Sati.Api/Infrastructure/ClaimMdSandboxConnector.cs`, `Sati.Api/Program.cs`.
Test owners: `Sati.Api.Tests/ClaimMdSandboxConnectorTests.cs`,
`Sati.Api.Tests/ClearinghouseDispatchApiTests.cs`; three explicit connector constructors in
`ClaimMdStatusProcessorTests.cs` receive the new time-provider dependency. The shared manual
clock is test-only. [W8](../../BACKGROUND_WORKERS_HANDOFF.md),
[worker architecture](../architecture/workers.md), [the sandbox runbook](../../CLAIMMD_SANDBOX_RUNBOOK.md)
and [DEC-0223](../decisions/current/2026-10-08-DEC-0223.md) own current scope, boundary and reasons.
No route, model/schema or portable business-rule owner changed.

**Actual tests/checks, commands, results and evidence locations:**

| Executed check | Result and evidence |
|---|---|
| Unfixed transport, stalled body and real-connector upload regressions | **2 failed, 0 passed** as required. The body token remained uncancelled at 45 fake seconds; the upload remained pending in Sending and failed the bounded observation wait. Controlled cleanup canceled/released and drained both tasks. `artifacts/test-results/claimmd-http-deadline/claimmd-http-deadline-fail-first.trx`. |
| Fixed connector/status/dispatch/coordination/reset focused suite | **49 passed, 0 failed, 4 SQL-only skips**, total 53. Connector 19, status processor 10, dispatch 18, portable coordination 1 and reset coordination 1 passed. `artifacts/test-results/claimmd-http-deadline/claimmd-http-deadline-passing.trx`. |
| Guarded existing API SQL selection | **34 passed, 0 failed/skipped**. Includes the four previously skipped request/poller/reset coordination cases, independent-host/dispatch/reset exclusion and billing concurrency acceptance, plus selected portable pipeline cases. `TestResults/IsolatedSqlServer/Api/Joshu_LONGCHENPA_2026-10-08_15_03_46_net10.0.trx`. The harness created private `SatiSqlTests_095362f2800045ac848759017f5c5b81`, then reported successful stop and deletion. No shared instance or working database was connected to. These results overlap existing cases; they are not 34 new deadline cases. |
| Builds and documentation | Serial Release API test-project build passed with existing unrelated warnings. Documentation structure gate and its **22 negative mutation proofs** passed; tracked diff and changed current-document whitespace checks passed. The sealed readiness report's SHA-256 remained `292ACBA3C331FF98B00125CBEAA23073C5EC916403C157CD08426ED639E63B48`. Rubric/history and preserved captures were not edited; the documentation gate validated preserved snapshots. |

Build used `dotnet build Sati.Api.Tests/Sati.Api.Tests.csproj --configuration Release --no-restore
--disable-build-servers -m:1 -v minimal`. The before-fix filter selected
`StalledBodyDeadlineReleasesCallbackAndAllowsFollowingHealthyRequest` and
`HttpExchangeDeadlineKeepsAnUploadUnknownAndNeverRetriesItAutomatically`.
The final portable execution was:

```powershell
dotnet test Sati.Api.Tests/Sati.Api.Tests.csproj --no-build --no-restore -c Release --filter 'FullyQualifiedName~ClaimMdSandboxConnectorTests|FullyQualifiedName~ClaimMdStatusProcessorTests|FullyQualifiedName~ClaimMdSandboxCoordinationTests|FullyQualifiedName~ClearinghouseDispatchApiTests|FullyQualifiedName~DemoWorkerResetCoordinationTests' --logger 'trx;LogFileName=claimmd-http-deadline-passing.trx' --results-directory artifacts/test-results/claimmd-http-deadline
pwsh -NoProfile -File scripts/Test-IsolatedLocalDb.ps1 -ApiOnly
```

Documentation checks used `pwsh -NoProfile -File scripts/Test-DocumentationStructure.ps1` and
`pwsh -NoProfile -File scripts/Test-DocumentationStructureChecks.ps1`. Whitespace verification
used `git diff --check` plus a trailing-space/tab scan of every changed current document,
including the untracked registry/decision/architecture/evidence owners. This is not a release
preflight, independent assessment or full-solution run.

Fake-time acceptance covers 44.999 seconds pending/45 seconds canceled for the first and a
later body read, one headers/body budget (headers at 40 seconds leave five), stalled headers and
stream acquisition, earlier/already-canceled callers, excluded coordination wait, disposal,
callback release/following healthy progress and exact/over 16 MiB cap. The real-connector
synthetic upload test observes committed Sending before expiry, an uncancelled caller at
deadline, one request/attempt, disposed body, persisted OutcomeUnknown and no Transmitted
event or automatic replay. Existing gate/host/key/cursor and status-processing checks remain.

**Failed/unrun checks, reason and verification limits:** the two intended regressions failed
before the production fix. The initial connector Fact was later expanded to first/later-read
theory cases; only the original stalled-body variant has its own recorded fail-first run.
Additional boundary/preservation cases are acceptance, not separate fail-first claims.
The sandboxed testhost's local IPC restriction required bounded approved execution outside
that restriction. No full-solution, load, installed-client, live-host/vendor, notification or
restore acceptance was run. No real key was resolved and all HTTP content was synthetic.

New deadline tests use synthetic serial/no-op coordination and cooperative HTTP content.
Upload state commits use the actual workflow over SQLite. The separate SQL run proves existing
coordination controls, not a new end-to-end deadline through real SQL locks. Cancellation is
conditional on the handler/content honoring it; synchronous decoding/parsing and disposal are
not interruptible by this token. Admission/pacing, lock cleanup, evidence commit, account turns
and whole-pass/fair-wait budgets are not bounded by this exchange timer. A timeout cannot prove
remote nonreceipt or actual provider cancellation. Final source review found no actionable flaw.

**Relevant readiness criterion IDs and evidence class:** local source/synthetic working
evidence for the component-deadline portion of scenario 13 (MT07/MT08/ID10), preservation of
ID06 uncertain-send quarantine and existing MT09/MT10 SQL quota/reset coordination. These
criteria remain partial; no total budgets, fair capacity, live activation or score advancement
is established. No deployment, cloud/security change, real-data access or provider send occurred.

**Durable decisions, alternatives and supersession links:**
[DEC-0223](../decisions/current/2026-10-08-DEC-0223.md) records the single connector timer and
configuration ownership. Header-only timeout, a fresh body budget, competing clock timers and
deadline-driven upload retries were rejected. Only source timeout ownership changed; no earlier
governing decision, D1–D4, sealed snapshot or operating permission is superseded.

**Remaining risks/blockers, dependencies and deferred work:** bounded agency discovery and
bookkeeping, total operation/account/pass budgets, fair account/lane scheduling, API admission,
aggregate capacity and live vendor/progress/alert evidence remain W8 work. Dispatch's oldest
Queued preflight poison path still needs a durable known-unsent hold/backoff/reopen policy and
additive schema proposal; current missing-key acceptance correctly records no send/attempt.
CancelledBeforeSend or process-memory suppression would not supply that recovery policy.
Separate activation, tenancy, billing R1/R2, recovery and independent-review gates remain.

**Next eligible stable ID and bounded slice:** SATI-WRK-001, named 100-agency keyset discovery
pages for the note worker within a captured finite ID range, with bounded observed higher-ID
growth detection. This is a per-query materialization bound, not frozen membership, bounded
bookkeeping or complete provisioning fairness. [The agenda pointer](../../AGENDA.md#next-eligible-work)
owns dependencies, dynamic-insert limits, fail-first acceptance and separate action boundaries.

**Later reviewed release snapshot, if any:** pending; the sealed 1.3.37 assessment is unchanged.

## 2026-10-08 — Sequential work authority and documentation commit boundary

Josh requested the remaining tasks in sequence, ordinary commits/pushes after each significant
chunk and progress explanations every 30 minutes. Work proceeds on
`codex/agenda-sequence-2026-10-08`; this is not DATT or release/activation authorization.
The chat's 30-minute progress heartbeat is active. Local eligible slices are revalidated in
agenda order; schema/policy, operating and live-evidence blockers keep their separate gates.

**Prerequisite chunk (SATI-DOC-001):** review and preserve the already local consolidated
documentation/link/hash closure, public dated assessment Markdown, canonical registries and
offline documentation/readiness/DATT gates with CI integration. The exact staged export was
checked before commit, as recorded below. This establishes the documentation
foundation required by subsequent worker commits. It does not change sealed scores/history.

The earlier consolidation entry's Settings/parser/panel evidence describes preserved local
work; those product changes and tests are excluded from this documentation prerequisite commit.
The first two worker slices also remain separate source chunks. Assessment PowerShell/TRX,
private settings, generated outputs and other unrelated working changes are excluded. No
application build, migration, cloud call or real-data inspection is part of the prerequisite.

**Actual verification:** `tmp/agenda-sequence-staged-docs` contains the exact staged public index
plus unchanged tracked source. Its documentation gate and **22** negative proofs, readiness
validation/helper **43** cases, and source-consistency gate with **9** mutation/positive cases
passed. Commands were the six `Test-DocumentationStructure*`, `Test-ReleaseReadiness*` and
`Test-DattPreflight*` scripts run with `pwsh -NoProfile -File` from that export. The readiness
helper's file replacement/writer exclusion used bounded approved disposable synthetic fixtures.
The initial sealed ledger has no earlier committed baseline yet; scores and blockers remain
as sealed. Export hash/link checks passed. The initial full index whitespace check found the
dated assessment's two intentional Markdown hard breaks; their bytes are preserved by an exact
file Git-attribute exception registered with a bounded reason in the documentation manifest.
Final index whitespace verification follows that preservation rule. Prior dated records remain as
recorded and do not substitute for these checks. Commit/push identities will be recorded
in a subsequent working entry once available; no successful push is claimed in advance.

The first push attempt was rejected by automatic approval review: it classified the consolidated
internal documentation/assessment payload as potentially sensitive and the GitHub destination
as insufficiently authorized. No push occurred. The configured remote is
`https://github.com/heschides/Sati.git`; GitHub CLI metadata inspection could not authenticate,
so repository visibility remains unverified. Exact payload/destination approval is requested
before any retry, while authorized local implementation and commits continue.

**Next local work:** complete the current [agency discovery slice](../../AGENDA.md#next-eligible-work),
then bound stale-day completion-cache retention. Shared billing residual-compliance handling,
diagnostic-sink redaction and original/dispatch release gates require their own fail-first
revalidation. Dispatch unsent-backoff implementation still requires the policy/schema proposal
in [W8](../../BACKGROUND_WORKERS_HANDOFF.md). No cloud/live/approval blocker is silently closed.

## 2026-10-08 — Documentation publication and HTTP source commit

The reviewed prerequisite is local commit `b5ec4d4` and was successfully pushed to
`origin/codex/agenda-sequence-2026-10-08`. After the automatic-review rejection, Josh explicitly
approved this repository and documentation payload. The subsequent push succeeded without
force, branch reconciliation, deployment or activation. This approval covers ordinary verified
source/documentation chunks on that branch; other operating gates remain unchanged.

The prerequisite contains 296 reviewed public documentation/gate files and excludes preexisting
Settings/readiness product changes. The dated assessment's intentional hard breaks are retained
under the documented exact-file attribute exception; index whitespace and the final staged
documentation gate passed. The export was temporary and has been removed. Its location under
the repository inadvertently entered a later WPF XAML build, causing duplicate generated members;
that attempt supplies no test evidence. Bounded cleanup removed only the owned export, and build
tooling cleaned generated output before normal verification resumed. Future source exports must
be outside all project item globs.

**SATI-WRK-001 HTTP source chunk:** the connector/client registration and three affected test
files are committed separately from the still local note-worker discovery change. Actual deadline
acceptance remains the [HTTP entry above](#2026-10-08--sati-wrk-001-claimmd-http-exchange-deadline):
two original failures, 49 focused passes/four SQL skips and the separate 34-pass guarded selection.
No source change after that verification alters this chunk. The sealed ledger is unchanged.
The local agency paging suite is now green, but its completion/SQL proof and commit are recorded
in the next entry rather than claimed as part of this HTTP chunk.

## 2026-10-08 — SATI-WRK-001 note-worker agency discovery pages

**Stable work ID and bounded slice:** SATI-WRK-001, third local implementation slice:
replace unbounded note-worker agency-ID materialization with named 100-agency keyset pages.

**Status and source/revision:** implemented and locally verified; unreleased. Portable synthetic
acceptance, two existing guarded SQL preservation proofs and updated documentation/whitespace
checks passed within their recorded scope. This entry does not yet claim a paging commit/push. Reviewed branch:
`codex/agenda-sequence-2026-10-08`, with documentation foundation `b5ec4d4` and HTTP source
chunk `ca14bbb` already pushed to the approved `origin` branch. The paging work preserves the
earlier local note-failure implementation and unrelated Settings/readiness product changes.
Josh directly requested remaining eligible tasks in sequence with ordinary verified commits/pushes;
this is not DATT, worker activation or a release invocation.

**Changed behavior, ownership and canonical paths:** `NoteAbandonmentWorker.RunDueAsync`
captures a nullable maximum agency ID and reads ordered keyset pages of at most
`AgencyDiscoveryPageSize` (100). A nullable first cursor preserves damaged nonpositive-ID
failure behavior. Each page's last observed ID advances discovery, including completed and
failed agencies; the distinct `NoteAbandonmentSweep.WorkerBatchSize` (100 notes) is retained.
If the pass could otherwise complete, a bounded `AnyAsync` above the captured maximum keeps
the day due when growth is observed; an initially empty range checks for any agency. An already
due pass needs no extra growth check. Max/page/growth errors remain outside the agency-sweep
recoverable catch. Cancellation/disablement are checked around discovery/turns/final completion.
The sweep's revision guards, atomic audits/committed counts, successful-agency/day skipping,
global SQL/reset coordination and default-off/hourly/no-idle-SQL behavior are retained.

Source owner: `Sati.Api/Infrastructure/NoteAbandonmentWorker.cs`; synthetic test owner:
`Sati.Api.Tests/NoteAbandonmentWorkerTests.cs`. [W8](../../BACKGROUND_WORKERS_HANDOFF.md),
[worker architecture](../architecture/workers.md),
[the contingency owner](multitenancy-contingencies.md) and
[DEC-0224](../decisions/current/2026-10-08-DEC-0224.md) record ownership, policy and limits.
No route, schema, authoritative business-rule owner, provider/account scheduler or deployment changed.

**Actual tests/checks, commands, results and evidence locations:**

| Executed check | Result and evidence |
|---|---|
| Unfixed agency discovery, new materialization regression | **1 failed, 0 passed**. `AgencyDiscoveryMaterializesAtMostOneHundredIdsAndVisitsEveryStableAgencyOnce` observed **251 IDs in one query**, failing the required 0–100 range. `artifacts/test-results/note-agency-discovery/note-agency-discovery-fail-first.trx`. |
| Fixed main regression | **1 passed, 0 failed**; this overlaps the class run, not another unique case. `artifacts/test-results/note-agency-discovery/note-agency-discovery-main-passing.trx`. |
| Fixed complete worker class | **34 total: 32 passed, 0 failed, 2 SQL-gated skipped**. `artifacts/test-results/note-agency-discovery/note-agency-discovery-passing.trx`. Ten added cases cover 251 stable agencies with reader-observed page bounds/exact visits and counts/audits, recoverable fault plus exact note limits across pages, between-page disablement/cancellation with unconditional cleanup, zero/negative IDs, higher-ID growth, initially empty growth and shared max/growth failures. Existing worker controls remain in this class. |
| Release API test-project builds | Passed before each of the three recorded portable executions, with existing warnings and no build errors. No full-solution build is claimed. |
| Existing isolated SQL sweep/reset preservation proofs | **2 passed, 0 failed/skipped**: separate-host sweep exclusion and Demo reset exclusive-lease refusal. `TestResults/IsolatedSqlServer/Api/Joshu_LONGCHENPA_2026-10-08_15_38_21_net10.0.trx`. The guarded script created `SatiSqlTests_a000ffc20876410cb9526617aa3e8dd1` and reported its stop/delete in `finally`; no existing/shared instance or working database was used. This portable class's two skips remain separate from the SQL execution. |
| Updated documentation and whitespace | The current documentation structure gate and its **22 negative mutation proofs passed**. It validated 46 root documents, 28 scoped owners, 12 preserved snapshots, 11 active items, 454 retained legacy items, 218 imported decisions and six current decisions. `git diff --check` and `git diff --cached --check` passed. Sealed readiness/rubric/history and byte-preserved captures were not edited. |

Each portable build used `dotnet build Sati.Api.Tests/Sati.Api.Tests.csproj --no-restore
--disable-build-servers -m:1 -c Release -v minimal`. Tests used `dotnet test
Sati.Api.Tests/Sati.Api.Tests.csproj --no-build --no-restore -c Release`, with
`--results-directory artifacts/test-results/note-agency-discovery`. The fail-first and main passing
filter was `FullyQualifiedName~AgencyDiscoveryMaterializesAtMostOneHundredIdsAndVisitsEveryStableAgencyOnce`;
their `--logger` values were `trx;LogFileName=note-agency-discovery-fail-first.trx` and
`trx;LogFileName=note-agency-discovery-main-passing.trx`. The complete class used
`--filter FullyQualifiedName~NoteAbandonmentWorkerTests` and
`--logger trx;LogFileName=note-agency-discovery-passing.trx`. Shell execution quoted each filter
and logger value. Bounded approved test execution supplied local IPC access outside the sandbox
restriction. The executing agent confirmed commands; counters and the 251-ID failure were also
independently read from the three TRX files. Earlier API/rule and SQL results remain in their
dated entries and are not silently counted again here. The new guarded SQL command was
`pwsh -NoProfile -File scripts/Test-IsolatedLocalDb.ps1 -NoteAbandonmentOnly`. Updated documentation
checks were `pwsh -NoProfile -File scripts/Test-DocumentationStructure.ps1` and
`pwsh -NoProfile -File scripts/Test-DocumentationStructureChecks.ps1`, plus the tracked/staged
whitespace checks above. These new checks verify this chunk, rather than borrowing the prerequisite's result.

**Failed/unrun checks, reason and verification limits:** the intended fail-first regression failed
as required. Only that new materialization regression has unfixed-code proof; the other new cases
are boundary/preserved-control acceptance. Synthetic SQLite reader observation demonstrates
materialized ID cardinality and tested local behavior, not SQL Server query plans, retry timing,
locks under load, runtime memory, total-pass duration or aggregate capacity. No new full-solution,
performance/load, live-host, provider, alert, restore or installed-client acceptance was run for
this slice. The new SQL run preserves existing coordination/reset behavior; it does not measure
SQL paging plans/capacity or dynamic membership. Documentation validation proves ownership,
registry/link/history structure and tested refusals, not runtime workload behavior.

The captured maximum is a finite key range, not frozen membership. Within-range inserts after
the last discovery query can be missed even above the last cursor, as can lower-ID inserts or
reseeding behind it and inserts after the final existence check. Observed higher-ID/initially-empty
growth leaves the day due; complete provisioning/membership invalidation is unimplemented.
Do not claim that every start-time or newly inserted agency was visited.

**Relevant readiness criterion IDs and evidence class:** local source/synthetic working evidence
for the bounded discovery portions of MT06/MT08, scenario 16, and preserved ID03/ID04/OP06
revision/audit rules and the separate existing MT10 SQL sweep/reset preservation proofs. None of these
criteria is closed, and no sealed release score advances. Discovery bounds each query, not
dictionary cardinality, total turns/time or healthy-agency wait. No worker activation, cloud or
security change, working-data access or real external send occurred.

**Durable decisions, alternatives and supersession links:**
[DEC-0224](../decisions/current/2026-10-08-DEC-0224.md) records keyset progression, nullable
range/cursor, bounded observed-growth recheck and the independent agency/note bounds. Unbounded
lists, zero initial cursors, chasing unbounded higher-ID growth and enlarging this slice into
membership snapshots were rejected. Only source discovery materialization changes; no earlier
governing decision, D1–D4, route/schema ownership, sealed snapshot or operating permission is superseded.

**Remaining risks/blockers, dependencies and deferred work:** stale-day completion-cache retention,
current-day agency cardinality/churn, provisioning invalidation, fair lane selection, total
operation/account/pass budgets, API admission, aggregate capacity and live progress/alert/vendor
proof remain open. Dispatch known-unsent poison isolation still requires the durable hold/backoff/
reopen policy and additive schema proposal recorded in W8. Activation, structural tenancy,
billing R1/R2, recovery and independent-review gates retain their separate owners.

**Next eligible stable ID and bounded slice:** SATI-WRK-001, retire non-current-local-day
`completedByAgency` entries under `runGate`, preserving current-day retry/skip and next-day/
no-idle-SQL controls. The target is current-day retention cardinality, not 100 entries or complete
memory/fairness. [The agenda pointer](../../AGENDA.md#next-eligible-work) owns fail-first retention
acceptance, dependencies and action boundaries. Ordinary source commits/pushes remain subject
to verified staged scope; no paging commit or push is claimed in advance here.

**Later reviewed release snapshot, if any:** pending; the sealed 1.3.37 assessment is unchanged.

## 2026-10-08 — SATI-WRK-001 note-worker completion-cache day lifecycle

**Stable work ID and bounded slice:** SATI-WRK-001, fourth local implementation slice:
retire completion-cache entries and storage from other captured local dates.

**Status and source/revision:** implemented and locally verified; unreleased. Portable
fail-first/main/class verification and this chunk's documentation/whitespace checks passed. Reviewed branch:
`codex/agenda-sequence-2026-10-08`, after paging commit `b42f226` was pushed to the approved
`origin` branch. The earlier documentation foundation `b5ec4d4` and HTTP chunk `ca14bbb` remain
separate commits. No cache commit/push is claimed in advance. Preexisting Settings/readiness
product work and unrelated billing tests are preserved outside this chunk. Josh's sequential-work
request authorizes ordinary verified local source/documentation chunks and approved branch pushes;
no DATT, activation or release authority is inferred.

**Changed behavior, ownership and canonical paths:** `NoteAbandonmentWorker` now owns a
`completionCacheDay`. After an enabled call acquires `runGate`, it captures `ApiClock.Today`,
replaces `completedByAgency` when the date differs and records that captured date before the
existing global completed-day idle check. Every cache write uses the same captured date under
the gate. Same-day successful agencies stay skipped while recoverable-fault/at-limit agencies
remain due. Replacing the dictionary also retires the worker's reference to prior backing storage;
that is a source assertion, not a garbage-collection or measured-memory result. Disabled or
already-canceled calls retain their no-SQL/coordination controls; rotation waits for an enabled
gate-acquired check instead of adding idle SQL or another timer.

Source owner: `Sati.Api/Infrastructure/NoteAbandonmentWorker.cs`; test owner:
`Sati.Api.Tests/NoteAbandonmentWorkerTests.cs`. [W8](../../BACKGROUND_WORKERS_HANDOFF.md),
[worker architecture](../architecture/workers.md),
[the contingency owner](multitenancy-contingencies.md) and
[DEC-0225](../decisions/current/2026-10-08-DEC-0225.md) own behavior, lifecycle rationale and limits.
The separate 100-agency discovery and 100-note sweep bounds, finite-range/growth membership
caveats, classified faults, audit/count rules, default-off/hourly/next-day cadence and SQL/reset
coordination remain. No shared business rule, route, schema, worker activation or provider policy changed.

**Actual tests/checks, commands, results and evidence locations:**

| Executed check | Result and evidence |
|---|---|
| Original cache, new stale-retention regression | **1 failed, 0 passed**. `CompletionCacheRetainsOnlyCurrentDayAgenciesAndPreservesSameDayRetries` expected 110 current-day successes but observed 261 retained entries after a 251-agency first day and changed second-day population/recoverable fault. `artifacts/test-results/note-completion-cache/note-completion-cache-fail-first.trx`. |
| Fixed main regression | **1 passed, 0 failed**. Retained counts were 251 on day one, 110 after day-two failure, 111 after same-day recovery and 66 on day three. The fixture proves exact attempts, same-day skip/retry and idle/disabled/canceled no-SQL/coordination behavior, bounded discovery and zero notes/audit effects. `artifacts/test-results/note-completion-cache/note-completion-cache-main-passing.trx`. This overlaps the class run, not another unique case. |
| Fixed complete worker class | **35 total: 33 passed, 0 failed, 2 SQL-gated skipped**. `artifacts/test-results/note-completion-cache/note-completion-cache-passing.trx`. Includes the one added retention case and existing paging/fault/cancellation/limit/atomic-audit controls. The two skipped cases remain real-SQL sweep exclusion/reset proofs, not portable acceptance of those guarantees. |
| Serial Release API test-project builds | Passed with no errors and existing warnings; before-fix build reported six warnings and fixed build seven. No full-solution build is claimed. |
| SQL coordination preservation | No additional SQL run for the cache slice. The two-case paging selection and private-instance cleanup remain recorded in the earlier paging entry; they are not rerun or recounted as new cache evidence. This source change does not alter the SQL coordination/sweep/business-rule owners. |
| Updated documentation/whitespace | The current structure gate and all **22 negative mutation proofs passed**, with 46 root documents, 28 scoped owners, 12 snapshots, 11 active items, 454 legacy items, 218 imported decisions and seven current decisions; the next pointer is SATI-BIL-001. `git diff --check` passed. Sealed readiness SHA-256 remains `292ACBA3C331FF98B00125CBEAA23073C5EC916403C157CD08426ED639E63B48`; rubrics/history and byte-preserved captures were not edited. |

The serial build used `dotnet build Sati.Api.Tests/Sati.Api.Tests.csproj --no-restore
--disable-build-servers -m:1 -c Release -v minimal`. Tests used `dotnet test
Sati.Api.Tests/Sati.Api.Tests.csproj --no-build --no-restore -c Release` with
`--results-directory artifacts/test-results/note-completion-cache`. The fail-first and fixed main
filter was `FullyQualifiedName~CompletionCacheRetainsOnlyCurrentDayAgenciesAndPreservesSameDayRetries`,
with quoted logger values `trx;LogFileName=note-completion-cache-fail-first.trx` and
`trx;LogFileName=note-completion-cache-main-passing.trx`. The complete class used
`--filter FullyQualifiedName~NoteAbandonmentWorkerTests` and quoted logger value
`trx;LogFileName=note-completion-cache-passing.trx`. Bounded approved test execution supplied
local IPC access. Counters and the 110-versus-261 failure were independently read from the TRX files.
Updated documentation commands were `pwsh -NoProfile -File scripts/Test-DocumentationStructure.ps1`
and `pwsh -NoProfile -File scripts/Test-DocumentationStructureChecks.ps1`, followed by
`git diff --check`; these are this chunk's results rather than borrowed earlier checks.

**Failed/unrun checks, reason and verification limits:** the intended retention regression failed
against the unfixed worker and passed after rotation. This case uses zero-note agencies and
reflection over retained cache contents; its zero note/audit assertions are deliberate fixture
controls, not proof of new clinical effects. Existing class cases retain nonzero exact changes/
atomic-audit evidence. No allocation/heap/GC measurement, load/fairness test, new SQL run,
full-solution, live-host, provider, alert, restore or installed-client acceptance was performed
for this slice. Documentation checks validate the current ownership/link/registry/history structure
and tested refusals, not measured worker memory or capacity.

After an enabled date-change check, the worker retains successes for its captured current day.
This does not cap current-day agency cardinality/churn or remove agencies completed then deleted
within that day. Prior storage can remain while disabled/before the next gate-acquired check,
and object collection timing is not guaranteed. Successful-day membership/provisioning invalidation,
finite-range insertion caveats, restart persistence and total-pass/healthy-agency wait remain open.

**Relevant readiness criterion IDs and evidence class:** local source/synthetic working evidence
for the historical-bookkeeping portion of MT06/MT08, scenario 16. Existing ID03/ID04/OP06 audit
and revision controls remain in the passing class; earlier MT10 SQL evidence retains its original
scope. These checks do not close criteria, establish total capacity/fairness or advance a sealed
release score. No cloud/security change, working-data access or external effect occurred.

**Durable decisions, alternatives and supersession links:**
[DEC-0225](../decisions/current/2026-10-08-DEC-0225.md) records captured-local-day rotation and
storage replacement under the existing gate. Lifetime retention, entry-only clearing/pruning,
a mistaken 100-entry cache cap, repeated same-day eviction and a new cleanup scheduler were
rejected. Only the source's prior-day cache lifecycle changes; no governing decision, D1–D4,
shared business rule, route/schema ownership, sealed snapshot or operating permission is superseded.

**Remaining risks/blockers, dependencies and deferred work:** current-day completion cardinality/
churn, same-day removed agencies, provisioning invalidation, total operation/account/pass budgets,
fair account/feed selection, API admission, aggregate capacity and live progress/alert/vendor proof
remain W8 gaps. Dispatch's known-unsent poison path still needs durable hold/backoff/reopen policy
and an additive schema proposal. Activation, structural tenancy, recovery and independent review
retain their separate owners. Billing R1 accepted-original lifecycle and full queue/pre-send R2
compliance release remain open.

**Next eligible stable ID and bounded slice:** SATI-BIL-001, preserve authoritative residual
`complianceErrors` in the shared `BillingExportGate` even with a complete matching stored
supervisory exception. API/local callers already account for exact-obligation exceptions and Admin
recovery; the release gate must retain remaining blockers without rewriting frozen evidence.
[The agenda pointer](../../AGENDA.md#next-eligible-work) owns pure/API/local fail-first acceptance,
fresh generation/replay cases, positive exceptions/recovery and the explicit R1/full-send R2 limits.
This is an independent local shared-rule slice, not activation or a broader financial workflow repair.

**Later reviewed release snapshot, if any:** pending; the sealed 1.3.37 assessment is unchanged.

## 2026-10-08 — SATI-BIL-001 authoritative residual export errors

**Stable work ID and bounded slice:** SATI-BIL-001, preserve caller-authoritative residual
compliance errors in the shared export/replay gate even with a complete matching stored exception.

**Status and source/revision:** implemented and locally verified; unreleased. Reviewed branch:
`codex/agenda-sequence-2026-10-08`, after the committed/pushed cache prerequisite `80193cc`.
The current billing source/tests/documentation await their own verified commit; no billing push,
release, activation or financial transmission is claimed. Josh's direct sequential-work request
supplies the bounded local and ordinary commit/push authority. Unrelated dirty changes are preserved.

**Changed behavior, ownership and canonical paths:** `Sati.Contracts.V1.BillingExportGate`
now always appends the caller's authoritative remaining `complianceErrors` after existing frozen
source/exception provenance checks, preserving ordinal deduplication. API and local callers
already resolve service-date policy, exact selected obligations and immutable Admin recovery;
the gate must retain their residual blockers and cannot grant a blanket waiver or recalculate a
second decision from today's requirements. Caller comments now describe that ownership and the
separate non-waivable form-work deadline accurately; no gate call or transaction/serialization
shape changes. Source files are `Sati.Contracts/V1/BillingExportGate.cs`,
`Sati.Api/Endpoints/ApiEndpoints.BillingExport.cs` and `Data/Billing/IdeService.cs`.

Test owners are `Sati.Tests/BillingExportGateTests.cs`,
`Sati.Api.Tests/BillingExportComplianceTests.cs` and
`Sati.Tests/LocalBillingExportComplianceTests.cs`. The older positive exception fixtures now
retain the exact selected PCP obligation ID and explicit confirmation, with matching stored actor,
time and reason. New API/local cases first retain an export with a valid PCP exception and a
completed unselected Comprehensive Assessment, then revoke that assessment through the real
attestation owner. Fresh keys and exact replays refuse the remaining assessment blocker, keeping
frozen claim JSON, retained content/file name, generation/audit/submission-event counts and local
file bytes. Two additional positives record exact Admin recovery through the real API/local owner
while the synthetic note is unbilled, then stage the frozen claim and prove export/replay remains
valid without setting a supervisory exception. The local fixture captures a fresh session after
test permission changes and cleans only its own random-key synthetic EDI files.

[The sandbox runbook](../../CLAIMMD_SANDBOX_RUNBOOK.md#current-export-and-replay-compliance--local-source-october-8-2026),
[billing architecture](../architecture/billing.md),
[the contingency owner](multitenancy-contingencies.md) and
[DEC-0226](../decisions/current/2026-10-08-DEC-0226.md) own behavior, decisions and limits.
No route, schema, external transport, correction policy or exception/recovery authority changes.

**Actual tests/checks, commands, results and evidence locations:**

| Executed check | Result and evidence |
|---|---|
| Unchanged gate, pure residual-error regression | **1 failed, 0 passed/skipped**. `StoredExceptionCannotDiscardAuthoritativeRemainingComplianceErrors` returned an empty collection instead of retaining the assessment error; its matched-exception/empty-residual positive assertion passed first. `artifacts/test-results/billing-export-residual-errors/billing-export-residual-errors-fail-first.trx`. |
| Unchanged gate, API revocation/fresh-key/replay cases plus repaired positive | **2 intended failures, 1 passed, 0 skipped**. Both actual revoke calls succeeded; export returned **200 instead of 409** for the new key and exact replay. The repaired exact-PCP-exception positive passed. `artifacts/test-results/billing-export-residual-errors/billing-api-residual-errors-fail-first.trx`. |
| Unchanged gate, local revocation/fresh-key/replay cases plus repaired positive | **2 intended failures, 1 passed, 0 skipped**. Real local revocation succeeded; both exports threw **no exception** instead of refusing the residual blocker. The repaired exact-PCP-exception positive passed. `artifacts/test-results/billing-export-residual-errors/billing-local-residual-errors-fail-first.trx`. |
| Fixed focused desktop/shared acceptance | **66 passed, 0 failed/skipped**. Complete shared export, local export, exact-exception, service-date policy/recovery, local recovery-service and local form-work classes, including the pure/two local residual-error regressions and the real Admin recovery export/replay positive. `artifacts/test-results/billing-export-residual-errors/billing-desktop-residual-errors-passing.trx`. |
| Fixed focused API acceptance | **31 passed, 0 failed/skipped**. Complete API export, recovery and form-work classes, including both new revocation cases, corrected exact-exception positive and the real Admin recovery export/replay positive. `artifacts/test-results/billing-export-residual-errors/billing-api-residual-errors-passing.trx`. |
| Serial Release project builds | Desktop and API test-project builds passed with existing unrelated warnings and no errors. A final incremental desktop build/rerun followed the fresh-session correction in the new positive fixture. No full-solution build is claimed. |
| Documentation/whitespace | Current structure gate and all **22 negative mutation proofs passed**: 46 root documents, 28 scoped owners, 12 snapshots, 11 active items, 454 legacy items, 218 imported decisions and eight current decisions. The next pointer is SATI-SEC-001. `git diff --check` passed. Sealed readiness SHA-256 was independently rechecked as `292ACBA3C331FF98B00125CBEAA23073C5EC916403C157CD08426ED639E63B48`; sealed rubrics/history and preserved captures were not edited. |

These passing selections contain **97 cases total**, not an additional count for separately
rerunning their main cases. The five intended unfixed failures and two positive baseline controls
are separate observations of the earlier source, not further unique passing acceptance. The added
Admin recovery positives are preserved-behavior tests, not fail-first claims. Counters and the two
Admin recovery passing results were independently read from the final TRX files.

Serial builds used `dotnet build Sati.Tests/Sati.Tests.csproj -c Release --no-restore
--disable-build-servers -m:1 -v minimal` and the same command for
`Sati.Api.Tests/Sati.Api.Tests.csproj`. Test commands used `dotnet test` with their respective
project, `-c Release --no-build --no-restore`, quoted filter/logger values and
`--results-directory artifacts/test-results/billing-export-residual-errors`. Pure baseline filter:
`FullyQualifiedName=Sati.Tests.BillingExportGateTests.StoredExceptionCannotDiscardAuthoritativeRemainingComplianceErrors`.
API baseline selected `BillingExportComplianceTests.RevokedUnselectedObligationBlocksExportAndReplayWithoutChangingRetainedEvidence`
and `CompleteStoredExceptionPermitsExportAndRetryWithoutRebuildingFrozenInputs`; the local
baseline selected the corresponding local revocation case and
`CompleteFrozenExceptionStillExportsAndReplaysTheOriginalBytes`.
Final desktop filter was `FullyQualifiedName~BillingExportGateTests|FullyQualifiedName~LocalBillingExportComplianceTests|FullyQualifiedName~BillingComplianceExceptionTests|FullyQualifiedName~BillingCompliancePolicyAndRecoveryTests|FullyQualifiedName~BillingComplianceRecoveryServiceTests|FullyQualifiedName~LocalFormWorkBillingTests`.
Final API filter was `FullyQualifiedName~BillingExportComplianceTests|FullyQualifiedName~BillingComplianceRecoveryApiTests|FullyQualifiedName~ApiFormWorkBillingTests`.
The five logger file names are the TRX names in the table, prefixed by `trx;LogFileName=`.
Bounded approved test execution supplied local testhost IPC and access only to the local fixture's
owned random-key EDI output/cleanup. No cloud or working database was used.
Documentation commands were `pwsh -NoProfile -File scripts/Test-DocumentationStructure.ps1`
and `pwsh -NoProfile -File scripts/Test-DocumentationStructureChecks.ps1`, followed by
`git diff --check`; these are this billing chunk's checks rather than borrowed earlier results.

**Failed/unrun checks, reason and verification limits:** all five intended regressions failed
against the unchanged gate and passed after the repair. The first local integration attempt
stopped before the target assertion because its older actor fixture omitted CaseManagement;
the correct caseload permission was restored before the retained fail-first result above. A new
local Admin positive initially stopped at stale-session refusal after fixture permission changes;
capturing a fresh session corrected the setup and the final 66-case run passed. Neither setup
failure is product-regression evidence. An earlier no-output test attempt supplied no evidence;
an incidental temporary source export caused a WPF duplicate-XAML build failure until its owner
removed that export and normal `dotnet clean`/serial build succeeded. No incidental project
exception or unrelated source repair was introduced for this billing work.

The synthetic SQLite/shared-rule executions prove tested local export/replay behavior and
preservation. No SQL Server source-change race, query/lock/load proof, full-solution, physical
vendor upload, payer duplicate/correction policy, deployed incident/alert, installed-client,
real-data, restore or regulatory acceptance was run. R1/full R2 remain unresolved; this repair
does not prove complete send eligibility or close a readiness criterion.

**Relevant readiness criterion IDs and evidence class:** source/synthetic working evidence for
the export/replay prerequisite of ID08 and scenario 30; preserved frozen/atomic/refusal controls
relate to ID03/ID04, MT01/MT02 and OP06. ID07/scenario 29 original-claim lifecycle remains open;
ID08's complete queue/immediately-pre-send gate and SQL race proof remain open. No sealed release
score advances. No activation, cloud/security change, working-data access or real external effect occurred.

**Durable decisions, alternatives and supersession links:**
[DEC-0226](../decisions/current/2026-10-08-DEC-0226.md) records retaining authoritative residual
errors in the single shared gate. Blanket exception bypass, a new decision from today's mask,
duplicated caller rules, weakened provenance/recovery and rewriting frozen financial content are
rejected. Only the prior residual-error omission is replaced; existing decisions and operating
authority remain governing.

**Remaining risks/blockers, dependencies and deferred work:** R1 needs an original/correction
lifecycle owner using actual receipt/uncertainty facts rather than treating retained generation as
send proof; preserve known-unsent cancellation and authoritative ConfirmedNotReceived recovery.
Full R2 needs a trusted system-scoped validator for the exact retained original/correction subset,
current source/compliance/form-work/overlap checks within existing coordinated write boundaries,
a source-change race proof and a purpose policy for payer-held voids. Background workers must not
impersonate the requesting human to reuse an authorization wrapper. Vendor/payer and live evidence
remain separate. W8 fairness/budgets/admission/provisioning/hold policy, structural tenancy,
recovery and independent-review gaps retain their owners.

**Next eligible stable ID and bounded slice:** SATI-SEC-001, contain exceptions escaping
downstream request execution in the real Program pipeline before unrestricted failure data reaches
general logging/hosting diagnostic sinks. [The agenda](../../AGENDA.md#next-eligible-work) owns
actual Program fail-first logger/DiagnosticListener proof, generic 500/correlation/safe incident
acceptance, request-aborted 499 and contained started-response/secondary failures. Non-retrying
SQLite isolates that boundary; the production incident aggregator's explicit transaction without
an outer execution scope is a separately revalidated follow-up, before health diagnostics.
Independent EF/provider, health/startup and callback gaps remain open; no full logging-redaction
claim or production incident-persistence credit is implied.

**Later reviewed release snapshot, if any:** pending; the sealed 1.3.37 assessment is unchanged.

## 2026-10-08 — SATI-SEC-001 escaping request-exception boundary

**Stable work ID and bounded slice:** SATI-SEC-001, contain exceptions escaping downstream
execution in the actual Program request pipeline before general logger/framework diagnostic sinks.

**Status and source/revision:** implemented and locally verified; unreleased. Reviewed branch
`codex/agenda-sequence-2026-10-08` after billing prerequisite `e1ea61f`, committed and pushed to
Josh's explicitly approved repository/branch. This request-boundary source/tests/docs await their
own verified commit. Josh's direct remaining-work request supplies bounded local and ordinary
commit/push authority; this was not DATT. Unrelated Settings/readiness work is preserved.

**Changed behavior, ownership and canonical paths:** new constructor-injected
`Sati.Api/Infrastructure/ApiExceptionBoundaryMiddleware.cs` replaces the raw-exception
`UseExceptionHandler` delegate in `Sati.Api/Program.cs`, first in the explicit pipeline. It logs
fixed operation, type capped at 160 characters, HResult and server correlation ID without the
Exception object, message/inner/Data/stack or request content. Existing `ApiIncidentRecorder`
remains the best-effort safe fingerprint/envelope owner. Writable failures return the generic
`server_error` 500 with correlation/security/no-store headers. Started responses and secondary
logger/store/write/abort failures are contained and aborted without rethrowing raw exceptions.
Cancelled request operation/I/O failures retain 499 without an incident or Error log; uncancelled
operation cancellation remains unexpected. Existing authentication/authorization/tenant order,
DTOs and route authority remain unchanged. No request replay or automatic retry is added.

[The logging owner](../../LOGGING_DESIGN.md#api-request-boundary--source-october-8-2026),
[architecture](../architecture/identity.md),
[API audit scope correction](../../API_SECURITY_AUDIT.md#october-8--escaping-request-exception-boundary-and-logging-scope-correction),
[contingencies](multitenancy-contingencies.md) and
[DEC-0227](../decisions/current/2026-10-08-DEC-0227.md) own behavior/choice/limits. The historical
API audit's broad logging assurance is explicitly corrected without deleting its dated evidence.

Test owner `Sati.Api.Tests/ApiExceptionRedactionTests.cs` runs a unique synthetic throw seam inside
the real Program pipeline via a test-only startup filter. Disposable seeded parent/child hosts
preserve an authenticated agency actor and use a child non-retrying SQLite context solely to
isolate the request boundary. Per-host log capture observes messages, state, scopes, exception
references and Data; path-filtered DiagnosticListener subscriptions positively observe hosting
start/stop and raw exception events. Gates prove downstream execution, throwing, actual response
start/write and caller cancellation. Finally-based cleanup drains/releases/disposes owned hosts,
requests, listeners, cancellation sources and private fixture storage, including setup failure.

**Actual tests/checks, commands, results and evidence locations:**

| Executed check | Result and evidence |
|---|---|
| Revised unchanged-boundary nested/uncancelled/start/write baseline | **4 intended failures, 0 passed/skipped** after isolating the separate incident transaction defect. All four positively observed requests reached raw-narrative logger assertions. Normal generic500/auth/scoped-incident assertions passed first; started and write-fault paths observed client failure. `artifacts/test-results/api-exception-redaction/api-exception-redaction-fail-first-bounded.trx`. |
| Final harness baseline | **5 failed, 0 passed/skipped**: four genuine narrative exposures; Data-only initially stopped at the old missing Cache-Control before its leak assertion. This mixed run is preserved and is not credited as five leak proofs. `artifacts/test-results/api-exception-redaction/api-exception-redaction-final-fail-first.trx`. |
| Reordered Data-only assertion against unchanged handler | **1 intended failure, 0 passed/skipped**. A safe message's `Exception.Data` sentinel appeared in the actual HandledException diagnostic reference. Positive enter/throw/start/stop counts were all one, with one raw exception payload and nine log entries. `artifacts/test-results/api-exception-redaction/api-exception-data-fail-first.trx`. |
| Fixed complete new boundary class | **9 passed, 0 failed/skipped**, 1m1s. Nested/uncancelled failure, safe-message Data, started response, secondary write/store/logger failures, cancelled operation and cancelled I/O. Every case observed one enter/throw/hosting start/stop and zero raw framework exception payloads. Write failure was injected once; logger/store failure counts were each asserted once. `artifacts/test-results/api-exception-redaction/api-exception-redaction-passing.trx`. |
| Focused existing API controls | **41 passed, 0 failed/skipped**, 6s. API surface 5; schema-health 4; SSN/form 15; tenant authorization 11 (incident 8 and protected/renewal 3); user-session 6. `artifacts/test-results/api-exception-redaction/api-exception-boundary-controls.trx`. |
| Serial Release API test-project build | Passed, **0 errors / 7 existing warnings**: endpoint CS8602 and six test nullable/xUnit warnings. No test correction was needed after the source fix. No full-solution run is claimed. |
| This slice's documentation/whitespace checks | `scripts/Test-DocumentationStructure.ps1` passed: 46 root documents, 28 scoped owners, 12 snapshots, 11 active items, 454 legacy items, 218 imported decisions and nine current decisions; SATI-SEC-001 pointer selects the incident slice. All **22 negative mutation proofs** passed via `scripts/Test-DocumentationStructureChecks.ps1`. `git diff --check` passed; structure/whitespace are rerun after recording these results. |

Passing acceptance contains **50 cases total**. Separate baseline/rerun counts are not additional
unique passing tests. The four narrative and separately reordered Data observations establish
five genuine escaping-content regressions; other failure-handling/cancellation cases are positive
acceptance rather than separate fail-first claims.

Commands: `dotnet build Sati.Api.Tests/Sati.Api.Tests.csproj --no-restore --disable-build-servers
-m:1 -c Release -v minimal`; `dotnet test Sati.Api.Tests/Sati.Api.Tests.csproj --no-build
--no-restore -c Release` with quoted filter/logger arguments and
`--results-directory artifacts/test-results/api-exception-redaction`. New-class filter:
`FullyQualifiedName~ApiExceptionRedactionTests`; logger
`trx;LogFileName=api-exception-redaction-passing.trx`.
Existing-control filter:
`FullyQualifiedName~SsnAndFormApiTests|FullyQualifiedName~SchemaDriftHealthCheckTests|FullyQualifiedName~ApiSurfaceTests|(FullyQualifiedName~TenantAuthorizationTests&FullyQualifiedName~Incident)|FullyQualifiedName~TenantAuthorizationTests.MatchingWindowsRecordEnrichesPendingCrashWithoutCountingASecondCrash|FullyQualifiedName~TenantAuthorizationTests.ProtectedEndpointRejects|FullyQualifiedName~TenantAuthorizationTests.ActiveSessionCanRenewItsShortLivedAccessToken|FullyQualifiedName~UserSessionApiTests.MissingOrMalformedSecurityVersionFailsClosedEvenWithValidSignature|FullyQualifiedName~UserSessionApiTests.FailedPasswordCheckLeavesTheCurrentSessionAndVersionIntact`;
logger `trx;LogFileName=api-exception-boundary-controls.trx`. Final baseline selected the first
four method names (nested/uncancelled theory supplies two cases); Data-only then ran separately
with its exact method name and `api-exception-data-fail-first.trx` logger.

**Failed/unrun checks, reason and verification limits:** the first baseline, preserved as
`api-exception-redaction-fail-first.trx`, had two genuine started/write leak failures but two normal
cases stopped at missing incident rows. Investigation reproduced a separate production-source
gap: configured retrying EF refuses the aggregator's explicit transaction outside a strategy
scope. Switching only the boundary child's synthetic context to non-retrying SQLite isolated
request containment for the revised proof above; it is not a production fix or incident-persistence
claim. Final five-case baseline's Data header failure likewise is not silently counted as leakage;
the subsequent unchanged-handler Data-only run establishes that separate proof.

This control removes exception-handler-specific framework events/metrics. Tests positively retain
ordinary hosting start/stop and safe Error logs; no live exporter/alert/metric behavior was tested.
Independent EF/provider logs and diagnostics may publish failures before they reach this boundary.
Health/startup, cancellation/response-completion callbacks, host failures and a complete enabled-sink
inventory remain outside this proof. No full logging-redaction, live SQL locking, deployment,
real-data, restore, provider or legal acceptance was run. Generic JSON alone is not sink proof.

**Relevant readiness criterion IDs and evidence class:** source/synthetic working evidence for
OP06 and scenario 42 redaction, with MT14 safe support-envelope and OP03 independent-review dependencies.
This does not close those criteria or advance any sealed score. Readiness SHA-256 remains
`292ACBA3C331FF98B00125CBEAA23073C5EC916403C157CD08426ED639E63B48`; sealed rubrics/history are unchanged.

**Durable decisions, alternatives and supersession links:**
[DEC-0227](../decisions/current/2026-10-08-DEC-0227.md) rejects response-only assurance,
log-level-only repair, suppression of successful handler diagnostics alone and request replay.
It replaces the raw request handler and scopes the broad historical logging claim; desktop
curated-envelope policy, route authority, D1–D4 and operating permissions remain governing.

**Remaining risks/blockers, dependencies and deferred work:** direct incident aggregation under
a configured retrying strategy and refusal of a retrying outer scope have two genuine failing
baselines in `artifacts/test-results/incident-aggregation-execution/incident-aggregation-execution-fail-first.trx`.
Eight focused incident execution cases are prepared, not yet passing. Implement a zero-retry
scope around the entire existing transaction and refuse a retrying outer scope before work.
Keep immediate last-reference replay limits explicit; no new historical dedupe/schema is added.
Health-check exception/Data redaction follows separately; startup/provider/callback/global sink
work remains open. Billing R1/full R2, W8 budgets/fairness, tenancy, recovery and independent review
retain their canonical owners and separate operating authority.

**Next eligible stable ID and bounded slice:** SATI-SEC-001, the incident single-attempt
execution-scope repair in [the agenda](../../AGENDA.md#next-eligible-work), with direct persistence,
no-work outer-retry refusal, rollback/cancellation/commit-ack attempt counts and existing incident
controls. Health redaction is prepared as a later local slice, not executed evidence.

**Later reviewed release snapshot, if any:** pending; the sealed 1.3.37 assessment is unchanged.

## 2026-10-08 — SATI-SEC-001 incident single-attempt execution

**Stable work ID and bounded slice:** SATI-SEC-001, give `IncidentAggregator.UpsertAsync` its
complete zero-retry execution scope and refuse retrying outer callers before any work. Preserve
safe incident envelopes, valid single-attempt request scopes and current aggregation behavior.

**Status and source/revision:** implemented and locally verified after request-boundary commit
`3393a45` (DEC-0227). This record accompanies the bounded incident source/test/documentation
change and DEC-0228; its ordinary commit hash is pending. No DATT, deployment, working-data access,
schema, cloud/security setting or live provider action was invoked. Unrelated dirty files and
sealed release evidence remain preserved.

**Changed behavior, ownership and canonical paths:**
[`IncidentAggregator`](../../Sati.Api/Infrastructure/IncidentAggregator.cs) first checks
cancellation, then `ExecutionStrategy.Current?.RetriesOnFailure` before acquiring its striped
gate or creating a context. An active retrying caller is refused; a valid single-attempt scope is
supported. Named `IncidentSingleAttempt` owns the entire existing Serializable transaction,
query, save and both commit branches with zero retries. Existing short-lived context, gate
release and SQL `UPDLOCK, HOLDLOCK` query are retained. No automatic retry is added for save,
cancellation or an ambiguous commit acknowledgement.

[`The logging owner`](../../LOGGING_DESIGN.md#api-incident-execution--source-october-8-2026),
[API audit](../../API_SECURITY_AUDIT.md#october-8--escaping-request-exception-boundary-and-logging-scope-correction),
[architecture](../../ARCHITECTURE.md), [identity boundary](../architecture/identity.md) and
[DEC-0228](../decisions/current/2026-10-08-DEC-0228.md) own behavior and limits. The same stored
last reference can enrich diagnostics without another occurrence; older references are not a
durable deduplication history. A/B/A can count A again. There is no new schema or locking policy.

**Actual tests/checks, commands, results and evidence locations:**

| Executed check | Result and evidence |
|---|---|
| Unchanged aggregator direct regressions | **2 intended failures, 0 passed/skipped**: direct aggregation with a configured retrying strategy fails the explicit-transaction guard; an active retrying outer scope is not refused before work. `artifacts/test-results/incident-aggregation-execution/incident-aggregation-execution-fail-first.trx`. |
| Actual Program authenticated write baseline | **1 intended failure, 0 passed/skipped**: no persisted incident after the real `SingleAttemptWriteFilter` unwinds. Before the missing-row assertion, enter/throw = 1/1, authentication and generic 500/security/correlation/cache headers pass, the endpoint observes a single-attempt scope, and the one incident context observes configured retries with no active outer scope. `artifacts/test-results/incident-aggregation-execution/api-request-incident-execution-fail-first.trx`. |
| Corrected focused acceptance | **26 passed, 0 failed/skipped**, 1m8s: direct execution 8, full request-boundary class 10, existing endpoint incident controls 8. `artifacts/test-results/incident-aggregation-execution/incident-aggregation-execution-passing.trx`; counters and individual cases independently read from TRX. |
| Serial Release API test-project build | Corrected build passed, **0 errors / 7 existing warnings**. Initial wrapper selected a Task-returning overload and failed with **2 CS8031 errors / 1 existing CS8602 warning**; no tests ran from that discarded compile attempt. The explicit generic state/result overload corrected source typing; no test change followed the baseline. |
| This slice's documentation/whitespace checks | `scripts/Test-DocumentationStructure.ps1` passed: 46 root documents, 28 scoped owners, 12 snapshots, 11 active items, 454 legacy items, 218 imported decisions and ten current decisions; SATI-SEC-001 pointer selects the two-check health slice. All **22 negative mutation proofs** passed in one run of `scripts/Test-DocumentationStructureChecks.ps1`. `git diff --check` passed; structure/whitespace are rerun after recording these results. |

Build command:

```powershell
dotnet build Sati.Api.Tests/Sati.Api.Tests.csproj --no-restore --disable-build-servers -m:1 -c Release -v minimal
```

All incident tests use `dotnet test Sati.Api.Tests/Sati.Api.Tests.csproj --no-build --no-restore
-c Release`, the filter below, `--logger 'trx;LogFileName=<listed TRX filename>'` and
`--results-directory artifacts/test-results/incident-aggregation-execution`. Bounded approval
allows local synthetic testhost IPC. The direct baseline used
`FullyQualifiedName~IncidentAggregationExecutionTests.DirectIncidentAggregationPersistsWithAConfiguredRetryingStrategy|FullyQualifiedName~IncidentAggregationExecutionTests.RetryingOuterScopeIsRefusedBeforeAnyContextTransactionOrWrite`;
its preceding serial build passed with 0 errors / 6 existing warnings. The actual Program baseline selects
`FullyQualifiedName~ApiExceptionRedactionTests.EscapingAuthenticatedWriteFailurePersistsIncidentAfterSingleAttemptFilterUnwinds`.
Passing filter:

```text
FullyQualifiedName~IncidentAggregationExecutionTests|FullyQualifiedName~ApiExceptionRedactionTests|(FullyQualifiedName~TenantAuthorizationTests&FullyQualifiedName~Incident)|FullyQualifiedName~TenantAuthorizationTests.MatchingWindowsRecordEnrichesPendingCrashWithoutCountingASecondCrash
```

The eight direct cases independently count contexts, transaction starts, queries, save attempts,
commit attempts and completed commits, then inspect committed state through a fresh context.
They cover direct configured-strategy persistence; zero-work outer-retry refusal; immediate
same-reference/new-reference behavior with and without a valid single-attempt caller; pre-cancel;
cancellation after an actual database save; a configured retriable save fault with rollback; and
a post-commit acknowledgement fault without automatic replay. Later calls prove gates are
released. The commit-fault fixture knows its private SQLite commit completed; a real caller
cannot infer that from the exception. Finally/disposal cleans owned storage and contexts.

The passing configured-request case retains enter/throw = 1/1, actual filter single-attempt
scope, one incident context, configured retries and absent outer scope; it now retains exactly
one safe Agency/Api/Error incident, correct actor role/reference/count, generic 500 and safe
headers. The other nine boundary cases continue to cover raw sink containment, cancellation and
secondary failure controls. Eight existing incident cases preserve agency/platform scope,
concurrent counts, immediate enrichment, metadata refusal and audited Admin/status denial.
These are 26 cases in this focused run, not additional unique credit for controls already run.

**Failed/unrun checks, reason and verification limits:** three genuine regression failures above
are retained as fail-first evidence. The compile failure is not runtime evidence. No full-solution,
SQL Server locking/race, hosted incident service, health acceptance, live sink/provider, real-data,
restore or independent security/regulatory review was run in this slice. SQLite with configured
retrying strategies proves EF execution/cancellation/transaction semantics and the synthetic
actual Program path, not production engine behavior. Original boundary fixtures remain
non-retrying to isolate HTTP containment; the added case separately observes configured retries.
Immediate last-reference replay is not historical deduplication or exactly-once recording.

**Relevant readiness criterion IDs and evidence class:** source/synthetic working evidence for
OP06 safe error recording and scenario 42, with MT14 safe scoped envelopes. OP03 is the
independent security/tenant-review dependency, not an operational-persistence criterion. No
criterion is closed and no sealed score advances. The sealed readiness SHA-256 was independently
rechecked as `292ACBA3C331FF98B00125CBEAA23073C5EC916403C157CD08426ED639E63B48`.

**Durable decisions, alternatives and supersession links:**
[DEC-0228](../decisions/current/2026-10-08-DEC-0228.md) rejects save-only wrapping, configured
automatic transaction retry, relying on the unwound endpoint filter and refusing valid
single-attempt callers. It supersedes only missing incident execution ownership and unrestricted
retrying outer calls. DEC-0227 request containment, safe envelope/fingerprint policy, tenant
authority, desktop diagnostics, D1–D4, operating permissions and imported/history evidence remain.

**Remaining risks/blockers, dependencies and deferred work:** independent EF/provider,
startup identity, registration-construction, callback/on-completed and host diagnostics plus a
complete enabled-sink inventory remain open. SQL concurrency and live incident evidence are
unverified; historical occurrence deduplication would require a separate accepted design/schema
slice. Billing R1/full queue/pre-send R2, worker budgets/fairness, tenancy, recovery and independent
review retain their owners and separate authority.

Prepared health work has **11 genuine target failures and four passing controls**, across three
non-overlapping runs against unchanged health source; it is not implemented or passing acceptance:

| Prepared health baseline | Result and evidence |
|---|---|
| Direct failures, framework failures and actual Program readiness route | **5 failed, 0 passed/skipped**. Both direct checks retain the original result exception; both framework cases expose raw logger fields; Program reaches anonymous 503/Unhealthy and both registered checks before the sink assertion. `artifacts/test-results/health-check-redaction/health-check-redaction-fail-first.trx`. |
| Schema/identity/framework controls | **2 target failures, 4 passed, 0 skipped**. Identity mismatch and missing marker retain raw report exceptions. Matching and missing-column schema cases, framework pre-cancel/no-probe, and case-insensitive matching identity pass. `artifacts/test-results/health-check-redaction/health-check-redaction-baseline-controls.trx`. |
| Direct operation cancellation | **4 failed, 0 passed/skipped**: two checks × cancelled/uncancelled token. All reach the existing Unhealthy classification, then fail raw exception retention. `artifacts/test-results/health-check-redaction/health-check-redaction-cancellation-fail-first.trx`. |

The prepared health build uses the serial API build command above and passed with 0 errors /
7 existing warnings. Tests share `dotnet test Sati.Api.Tests/Sati.Api.Tests.csproj --no-build
--no-restore -c Release --results-directory artifacts/test-results/health-check-redaction`, each
listed TRX logger filename and its non-overlapping filter:

```text
FullyQualifiedName~HealthCheckExceptionRedactionTests.DirectProbeFailureReturnsContentFreeUnhealthyWithoutTheOriginalException|FullyQualifiedName~HealthCheckExceptionRedactionTests.FrameworkHealthServiceLogsVisibleSafeFailureWithoutExceptionPayload|FullyQualifiedName~HealthCheckExceptionRedactionTests.ProgramReadinessRouteKeepsAnonymous503AndBothChecksLogSafeFailure
FullyQualifiedName~HealthCheckExceptionRedactionTests.SchemaHealthyAndMissingModelColumnRemainDistinct|FullyQualifiedName~HealthCheckExceptionRedactionTests.FrameworkPreCancellationInvokesNoProbeAndEmitsNoFailure|FullyQualifiedName~HealthCheckExceptionRedactionTests.RealIdentityValidatorKeepsHealthyMismatchAndMissingMarkerResults
FullyQualifiedName~HealthCheckExceptionRedactionTests.DirectOperationCancellationRetainsCurrentSafeUnhealthyClassification
```

There are no health setup failures. Faulting factories fail before EF/provider logging; Program
replaces only concrete health dependencies while preserving ordinary request storage and actual
registrations. Controlled fake identity commands exercise the real validator without a SQL
server. The missing-column control changes only disposable synthetic SQLite. These baseline
observations do not establish global health/provider/startup/callback redaction or live readiness.

**Next eligible stable ID and bounded slice:** SATI-SEC-001, the two actual health checks'
exception/result and framework logger redaction in [the agenda](../../AGENDA.md#next-eligible-work).
Keep direct cancellation Unhealthy and framework pre-cancel/no-probe behavior; preserve known
schema/identity distinctions and anonymous 503/Unhealthy. Broader sink work remains separate.

**Later reviewed release snapshot, if any:** pending; the sealed 1.3.37 assessment is unchanged.

## 2026-10-08 — SATI-SEC-001 two-check health redaction and repair-sequence handoff

**Stable work ID and bounded slice:** SATI-SEC-001, remove caught raw exceptions from the two
actual health checks' results and the schema check's failure logger. Preserve current status,
cancellation, readiness response and schema/identity validation.

**Status and source/revision:** implemented and locally verified. The preceding incident repair
(DEC-0228) and this health repair (DEC-0229) remain **uncommitted**; `3393a45` is the last completed
commit/push in this sequence. Josh explicitly requested completion of the current worker,
billing-export and request/incident/health repairs before a completion handoff and further Git,
migration or release actions. Those further actions are held. No DATT, cloud/security change,
working-data access or live provider action was invoked; these repairs added no schema migration.
Unrelated dirty files and sealed release evidence remain preserved.

**Changed behavior, ownership and canonical paths:**
[`SchemaDriftHealthCheck`](../../Sati.Api/Infrastructure/SchemaDriftHealthCheck.cs) logs caught
failures using only a fixed operation, type capped at 160 characters and HResult. It supplies no
raw exception argument. Both it and
[`DatabaseIdentityHealthCheck`](../../Sati.Api/Infrastructure/DatabaseIdentityValidator.cs)
return fixed Unhealthy descriptions with no exception and empty failure Data. Framework
Unhealthy Error/status observations remain visible without raw exception/message/inner/Data/stack
payloads. Anonymous `/health/ready` still returns 503/`Unhealthy` for failures.

Known missing model-object diagnostics, healthy schema/identity results and the real identity
validator's case-insensitive matching/mismatch/missing-marker behavior remain. Both direct
checks keep operation cancellation Unhealthy, including a cancelled caller token; framework
pre-cancellation still invokes no probe. `DatabaseIdentityHostedService`, startup identity
validation, check registrations, routes, schema and authorization are unchanged.

[The health logging owner](../../LOGGING_DESIGN.md#api-health-failures--source-october-8-2026),
[API audit follow-up](../../API_SECURITY_AUDIT.md#october-8--escaping-request-exception-boundary-and-logging-scope-correction),
[architecture](../../ARCHITECTURE.md), [identity boundary](../architecture/identity.md),
[DEC-0229](../decisions/current/2026-10-08-DEC-0229.md) and
[contingencies](multitenancy-contingencies.md) own the bounded behavior and remaining evidence.

**Actual tests/checks, commands, results and evidence locations:**

| Executed check | Result and evidence |
|---|---|
| Unchanged health direct/framework/Program baseline | **5 intended failures, 0 passed/skipped**. Status/probe/anonymous 503 controls passed before raw result or logger assertions. `artifacts/test-results/health-check-redaction/health-check-redaction-fail-first.trx`. |
| Unchanged health schema/identity/pre-cancel baseline | **2 intended failures, 4 passed, 0 skipped**. Actual identity mismatch/missing marker retain raw report exceptions; matching/missing-column schema, matching identity and framework pre-cancel/no-probe controls pass. `artifacts/test-results/health-check-redaction/health-check-redaction-baseline-controls.trx`. |
| Unchanged health direct cancellation baseline | **4 intended failures, 0 passed/skipped**, two checks × cancelled/uncancelled tokens; existing Unhealthy status passes before raw exception retention fails. `artifacts/test-results/health-check-redaction/health-check-redaction-cancellation-fail-first.trx`. |
| Fixed focused health and regression acceptance | **37 passed, 0 failed/skipped**, 38s: health redaction class 15, existing schema-health 4, full request-boundary class 10, existing incident controls 8. `artifacts/test-results/health-check-redaction/health-check-redaction-passing.trx`; TRX counters and class groupings independently verified. |
| Serial Release API test-project build | Passed, **0 errors / 1 existing CS8602 warning** at ApiEndpoints:7598. This incremental build did not re-emit test warnings; it is not warning cleanup. No failed acceptance attempt or test correction followed the baseline. |
| This slice's documentation/whitespace checks | `scripts/Test-DocumentationStructure.ps1` passed: 46 root documents, 28 scoped owners, 12 snapshots, 11 active items, 454 legacy items, 218 imported decisions and eleven current decisions; SATI-BIL-001 pointer selects future bounded R1 fact/policy design. All **22 negative mutation proofs** passed in one run of `scripts/Test-DocumentationStructureChecks.ps1`. `git diff --check` passed; only structure/whitespace are rerun after recording these results. |

There are **11 genuine target failures and four positive controls across 15 non-overlapping
baseline cases**. The [preceding incident record](#2026-10-08--sati-sec-001-incident-single-attempt-execution)
retains their exact three filters, build command, setup controls and limits. The passing 37-case
selection includes repeated request/incident controls; it is not 37 newly added cases or a
full-solution run. The direct eight incident execution cases were verified separately in that
record's 26-case run and are not included in this health filter.

Actual acceptance commands, with bounded approved local synthetic testhost IPC:

```powershell
dotnet build Sati.Api.Tests/Sati.Api.Tests.csproj --no-restore --disable-build-servers -m:1 -c Release -v minimal
dotnet test Sati.Api.Tests/Sati.Api.Tests.csproj --no-build --no-restore -c Release --filter 'FullyQualifiedName~HealthCheckExceptionRedactionTests|FullyQualifiedName~SchemaDriftHealthCheckTests|FullyQualifiedName~ApiExceptionRedactionTests|(FullyQualifiedName~TenantAuthorizationTests&FullyQualifiedName~Incident)|FullyQualifiedName~TenantAuthorizationTests.MatchingWindowsRecordEnrichesPendingCrashWithoutCountingASecondCrash' --logger 'trx;LogFileName=health-check-redaction-passing.trx' --results-directory artifacts/test-results/health-check-redaction
```

[`HealthCheckExceptionRedactionTests`](../../Sati.Api.Tests/HealthCheckExceptionRedactionTests.cs)
checks raw references, nested message/inner/Data sentinels, descriptions and result/report Data.
It observes framework logger messages/state/scopes/exception arguments with positive failure
fields, not just HTTP response text. Direct and framework tests count the failing dependency
probe. Program's anonymous readiness request runs both actual registered checks and counts two
probes while retaining the ordinary request database factory. Pre-cancel counts zero probes and
no failure log. Matching/missing-column schema and real identity validator controls preserve
meaningful distinctions. Faulting factories fail before EF/provider logging; controlled fake
identity commands run the actual comparison without a SQL server. The missing-column case
changes only owned disposable SQLite. Owned provider/host/log/storage cleanup remains in scope.

**Failed/unrun checks, reason and verification limits:** all 11 genuine failing baseline cases
are retained; no setup failures or failed acceptance run occurred. No full-solution, live SQL,
deployment, installed client, hosted readiness, provider account, real-data, restore or independent
security/regulatory review was run. These two caught-failure paths do not sanitize registration
construction, arbitrary other checks, a failing logger, startup identity warnings, independent
EF/provider diagnostics or callback/host sinks. The hosted startup warning still accepts a raw
SqlException and remains explicit future work. No full logging-redaction claim follows. Incident
SQL locking, historical occurrence deduplication and live recording remain outside this proof.

**Relevant readiness criterion IDs and evidence class:** source/synthetic working evidence for
OP06 and scenario 42, with OP03 independent security/tenant-review dependency. No criterion is
closed or sealed score advanced. The sealed readiness SHA-256 was independently rechecked as
`292ACBA3C331FF98B00125CBEAA23073C5EC916403C157CD08426ED639E63B48`; sealed rubrics/history and
preserved captures are unchanged.

**Durable decisions, alternatives and supersession links:**
[DEC-0229](../decisions/current/2026-10-08-DEC-0229.md) rejects response-only assurance,
log-level suppression, replacing raw exceptions with unnecessary exception payloads, and
changing direct cancellation behavior. It supersedes only the two raw health-result fields and
schema logger argument. DEC-0227 request containment, DEC-0228 incident execution, safe-envelope
policy, tenant authority, desktop diagnostics, D1–D4 and operating permissions remain governing.

**Current project completion and remaining risks/blockers:** the bounded worker fault isolation,
HTTP exchange deadline, agency discovery and date-cache retirement; billing residual export-error
preservation; and request/incident/two-check health repairs are locally implemented and verified.
This completes the current repair sequence for a completion handoff. It does not complete the
broader active agenda or authorize further Git, migration or release actions. Incident/health
changes remain uncommitted and earlier evidence remains retained at its recorded revision.

Future work still includes full billing R1/original-release and exact-subset queue/pre-send R2,
cross-mode/rejection/void-purpose decisions, worker budgets/fairness, admission and distributed
login controls, structural tenancy, independent sink/startup/callback inventory, recovery and
independent review. Their canonical owners and operating permissions remain authoritative.

**Next eligible stable ID and bounded slice:** SATI-BIL-001, future bounded R1 fact/policy design
in [the agenda](../../AGENDA.md#next-eligible-work) when work resumes. Specify one shared owner for
original release, exact retained claim mapping, generated-only/known-unsent positives, malformed
physical-history hold, receipt/uncertainty/nonreceipt precedence and deterministic SQL barrier
design across current different lock paths. Cross-mode history and generic rejection need an
explicit policy decision before full guard implementation. No billing source/test implementation
or SQL execution is part of the current handoff; the proposal is not an implemented fact matrix.

**Later reviewed release snapshot, if any:** pending; the sealed 1.3.37 assessment is unchanged.

## 2026-10-08 — DATT preflight and audited source checkpoint preparation

**Scope and authority:** Josh's exact `Invoke DATT!` starts the bounded release in
[the playbook](../../RELEASE_PLAYBOOK.md), following the completed repair handoff. It replaces
the hold on ordinary release Git/publication work, while preserving separate cloud-database,
security-setting, Production, PHI and provider-activation boundaries. Candidate patch `1.3.38`
has no API ZIP, installer/checksum, acceptance-record or distribution filename collision.

**Audited source:** fetched remote default `master` remains
`a1af92129f60728a0bbcf0dd27c42190644b1e3b`; this checkout and its upstream remain
`3393a4549ab40758c4e4acece9377560a0d29872` before the checkpoint. The reviewed pending source
contains DEC-0228 incident execution, DEC-0229 health redaction, their tests/documentation,
and the Settings readiness reader/panel/tests plus embedding project changes. The forthcoming
checkpoint must include those inputs; `3393a45` alone does not represent them. The only subsequent
incident edit corrected closing-brace indentation without changing behavior. Published prior
worker/billing/request chunks and their failed-before/passed-after evidence remain retained above.

**Preflight observations:** [the environment inventory](../../DATABASE_ENVIRONMENTS.md#demo-api-release-preflight--october-8-2026-230345-utc)
owns the bounded live API checks. The diff against fetched default has no persistence migrations,
entity/context changes or API Data changes; Contracts changes only the billing export rule.
No migration, baseline capture, firewall access or reset is needed for this slice. Old API ZIP
`artifacts/SatiApi-1.3.37-fx-x86.zip` remains retained (11,828,115 bytes;
SHA-256 `6BE37C25C124F30A239A8BA90FA5EB335A24123290C75DC52C14D53C9646F7E9`).
Its 70 ZIP entries had no unsafe paths, duplicates or private desktop settings; assembly version
was `1.3.37.0`. This is rollback inventory, not authority to redeploy a rollback.

**Installer prerequisites:** durable `artifacts/Prerequisites/SqlLocalDB.msi` is 63,508,480 bytes,
SQL Server 2022 LocalDB 16.0.1000.6, Authenticode Valid with Microsoft Corporation signer;
SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
Required SDK 10.0.401, Windows packaging executables and icon are present. Both exact playbook
distribution folders exist and can be listed; actual publication writes/renames remain untested.
No Sati process was running at preflight. Builders do not require Inno Setup.

**Fresh source acceptance:** `dotnet build Sati.Tests/Sati.Tests.csproj --configuration Release
--no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -v minimal` passed:
12 existing warnings, zero errors, 50.64 seconds. Under the signed-in Windows profile,
`dotnet test ... --configuration Release --no-build --no-restore --filter
'FullyQualifiedName~ReleaseReadinessTests|FullyQualifiedName~ReleaseReadinessViewTests'` passed
**58**, failed **0**, skipped **0**. Retained TRX:
`artifacts/datt-1.3.38/test-results/readiness-source-checkpoint.trx`. Root inspected all four
rendered PNGs under `artifacts/datt-1.3.38/readiness-source-qa` (actual 1.3.37 baseline,
synthetic ordinary/enlarged/high-contrast states); visible text and wrapped meters remained legible.
The candidate embedded report is not yet appended or built. These checks do not establish human
screen-reader acceptance, installed-device acceptance, live load or independent readiness review.

**Preserved exclusions and branches:** `assessment-working/Remove-TemporaryDemoFirewallRule.ps1`
and seven old assessment TRX files are preexisting untracked work, retained and excluded from the
source/artifacts. The obsolete helper was read but never executed. No force/stash/reset or branch
deletion occurred. The detached 1.3.31 worktree is retained. Older local background-worker and
remote Claude branches have no commits unique to fetched default, but active/retention intent is
not independently established; retain them. The current agenda branch remains active until its
verified source is reconciled with default.

**Readiness and next work:** review all 42 existing criteria against the audited checkpoint and
actual retained acceptance. Preserve the 1.3.37 snapshot/rubric; add no Verified state merely for
a version/test count. Full release build/tests, exact-source default push, Demo publication,
installer acceptance/publication and final evidence remain pending. SATI-BIL-001 remains the
future bounded fact/policy design pointer after release, not extra release implementation.

## Entry template for the next significant portion

Copy this structure under a new dated heading; complete every field, using an explicit unknown
or not-run reason when evidence is unavailable. Keep results factual and bounded to their scope.

**Stable work ID and bounded slice:**

**Status and source/revision:**

**Changed behavior, ownership and canonical paths:**

**Actual tests/checks, commands, results and evidence locations:**

**Failed/unrun checks, reason and verification limits:**

**Relevant readiness criterion IDs and evidence class:**

**Durable decisions, alternatives and supersession links:**

**Remaining risks/blockers, dependencies and deferred work:**

**Next eligible stable ID and bounded slice:**

**Later reviewed release snapshot, if any:** pending until independently assessed and sealed.
