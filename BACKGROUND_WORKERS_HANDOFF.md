# Handoff — background workers and operational watchdogs

**For:** Codex, or any implementer starting without prior context.
**Written:** 2026-10-03, against release 1.3.34 (`master` @ `9d2d06a`).
**Status:** W1–W6 source, tests, documentation, and operator scripts implemented on
`codex/background-workers`, based on merged `master` @ `89c2c1e`. Final verification completed
2026-10-03: **3,948 passed, zero failed, one local AI evaluation skipped**. W7 remains deferred.
No cloud publication, configuration change, migration, or restore rehearsal was performed.

### Multitenancy scope review — October 8, 2026

W8 below adds tenant workload isolation and fairness investigation/acceptance work; it has not
been implemented by this documentation review. The review used `master` @ `a1af921` (1.3.37)
and `reports/SATI_ARCHITECTURE_ENGINEERING_ASSESSMENT_2026-10-08.md`. Hosting constraint 6
links to the authoritative dated environment inventory. D1–D4, W1–W7, activation gates
and the original implementation record remain in force. No production source was changed.

### Implementation record — October 3, 2026

- **W1/W4:** `Sati.DemoRefresh/DemoWatchdog` runs a daily read-only check after the reset;
  `Shared/DemoWatchdog.ps1` owns detection and content-free result traces.
  `scripts/Set-DemoWatchdogAlerts.ps1` prepares Josh's action group and two alert rules with
  `-WhatIf` and a current regional pricing review gate. The schedule, receiver, and alert rules
  are not live. Existing poison entries are preserved and intentionally produce findings.
- **W2:** `NoteAbandonmentRules` and `SystemActor` in Contracts are the shared owners. The
  default-off API worker catches up on wake, coordinates with the Demo reset lease, and writes
  revision-checked changes and summary audits together. The desktop-local path uses its existing
  triggers, the same rule, and a bounded batch. Admin activity displays actor 0 as automation.
- **W3:** incident envelopes retain their originating account. Sending requires that account
  and its captured credential session; bounded retries stop on sign-out or account switch.
  Legacy, malformed, and expired envelopes are held for manual review. Platform Health exposes
  pending age and held-envelope counts.
- **W5:** the existing desktop timer and activation event coalesce day-change refreshes through
  `DateRolloverRefreshCoordinator`. Dirty editors defer refresh; stale account/navigation results
  are discarded, and failed calendar refreshes remain due for retry.
- **W6:** `scripts/Invoke-DemoRestoreVerification.ps1` and the `OPERATIONS.md` runbook validate
  an approved historical manifest against an isolated Demo scratch restore. Fake-operation
  tests cover ownership, cleanup, evidence failures, manifest validation, and replay prevention.
  An actual Azure rehearsal still requires separate approval.

Acceptance used `scripts/Test-IsolatedLocalDb.ps1 -FullSolution` with synthetic fixtures in a
new private LocalDB instance. The final five-project Release run passed: desktop 2,823, API 994,
signatures 119, portal 8, and Carika 4. The only skip is the explicitly gated on-device AI model
evaluation. The private instance was stopped and deleted. TRX evidence is under
`TestResults/BackgroundWorkersFullSql`, with filenames dated `2026-10-03_17_26_*`.

An earlier full run was interrupted by workstation connected standby and subsequently failed
inside WPF theme resource evaluation. Windows power-event timestamps matched the pause. The
focused theme suite then passed 153/153, and the final full rerun passed with a temporary,
process-scoped wake request. No theme assertions or persistent power settings were changed.

Security/concurrency regressions were demonstrated failing when the relevant attribution,
revision, audit, reset-lease, stale-result, and draft guards were removed, then passed after
restoration. The local batch-limit and malformed restore-manifest checks were also verified
against unfixed variants. Operator activation and deferred work are tracked at the top of
`AGENDA.md`; response procedures and restore evidence requirements are in `OPERATIONS.md`.

The sections below preserve the original scope and acceptance criteria for future review.

Read `AGENTS.md` first, then `ARCHITECTURE.md`, `DECISIONS.md`, and the
original release 1.3.34 evidence linked below. This brief is a map with file references, not a
replacement for those documents. Where this brief and a later recorded decision disagree, the
decision wins. Record your own choices in `DECISIONS.md`.

### Consolidated ownership and remaining launch work — October 8, 2026

Current priorities are in [AGENDA.md](AGENDA.md); the original 1.3.34 release evidence is in the
[preserved agenda](docs/archive/2026-10-08/AGENDA.md). W8 is SATI-WRK-001. Its completion covers
worker fairness, bounded dependency use, and dispatch/admission isolation; it does not complete
the wider cloud Production launch gate. Tenant data constraints/identity/lifecycle, durable replay
and external-send recovery, current billing correctness, and complete-service recovery have their
own acceptance owners in the [readiness registry](docs/readiness/README.md) and stable agenda IDs.

Read the [known failure inventory](docs/readiness/multitenancy-contingencies.md) before implementation
and record any uncovered failure there. Use the [protocol baseline](docs/readiness/protocol-baseline.md)
for explicit delivery/retry guarantees. Capture W8 results under the relevant versioned criteria
in [readiness.json](docs/readiness/readiness.json); source tests and live hosting proof remain distinct.
The Settings thermometer changes only through that reviewed evidence, never because W8 is checked off.

After each significant W8 slice, follow [the standing upkeep and next-work workflow](AGENTS.md#standing-work-and-documentation-upkeep):
update this handoff's current findings/implementation/acceptance state, the stable agenda item and
next eligible pointer, affected owners/decisions, and [dated working evidence](docs/readiness/work-evidence.md).
Record what was actually tested, unrun gates, remaining dependencies and the next concrete slice.
Preserve the original implementation record and D1–D4; append dated updates rather than rewriting
historical evidence. The user can start the selected bounded slice with `Perform the next thing on the list`.

---

## 1. Which environment this work is for

- **The product is the cloud platform.** "Production" means a future cloud deployment that does
  not exist yet. Server-side workers are built for it and proven in **Demo**
  (`sati-demo-api-satilogica`, Azure SQL `SatiDemo`, synthetic data only).
- **`SatiProduction` (LocalDB) is Josh's personal working environment.** It holds real PHI from
  daily use alongside the employing agency's system, which remains the official record. It is
  not a deployment target and does not need production hardening. It is a PHI store, so
  `OPERATIONS.md` "Developer workstation rules" apply to anything that creates copies.
- **Keep local and server behavior in sync through shared rules, not a second scheduler.** Each
  worker's decision logic lives in `Sati.Contracts.V1`. The API hosts the schedule. The local EF
  path calls the same rule from the triggers it already has (dashboard load, existing timers).
  Do not add new background schedulers to the WPF client for local mode.

## 2. Hosting constraints that shape the design

Read [DATABASE_ENVIRONMENTS.md](DATABASE_ENVIRONMENTS.md) for authoritative dated hosting facts.
The following are worker design constraints and implementation references; they do not authorize
infrastructure changes. Revalidate observed hosting limits before a different environment is used.

1. **Catch up after host sleep**, using the dated API hosting limits in
   [the inventory](DATABASE_ENVIRONMENTS.md#observed-demo-inventory--october-8-2026).
   An in-process `BackgroundService` timer
   does not fire while the site is asleep. Every API worker must therefore be **catch-up on wake**:
   on start and on each tick, it asks "is a run overdue?" and runs once if so, rather than
   assuming it fired at the scheduled minute.
2. **Existing API workers to copy:** `SignatureProcessingService` (`PeriodicTimer` loop, logs
   only failure type), `ClaimMdSandboxPoller` with `ClaimMdSandboxCoordination` (single runner via
   `sp_getapplock`), `ClearinghouseDispatchWorker` (feature gate checked each pass). All are
   registered in `Sati.Api/Program.cs:91,143-147`.
3. **The Demo reset lives in a separate Function App** (`sati-demo-refresh-satilogica`,
   Consumption plan, PowerShell 7.6; `scripts/Publish-DemoRefresh.ps1`). `RefreshCaseload` is the
   nightly timer (`DemoRefreshSchedule=0 15 3 * * *`, Eastern). `ResetDemo` queues Admin requests
   on `demo-reset-requests`. `ResetDemoWorker` runs them with `maxDequeueCount` 1, so a failure
   moves to `demo-reset-requests-poison` and is never replayed (`DECISIONS.md`, 2026-09-26).
   1.3.34 did not republish this Function App; read the 1.3.34 notes in `AGENDA.md` before
   changing anything under `Sati.DemoRefresh/`.
4. **Audit events are append-only** (`EnsureAuditEventsAreAppendOnly`, `ApiDbContext.cs:811`).
   `ServerAuditEvent.ActorUserId` is a non-null `int`. The scheduled reset already records system
   work with actor `0` (`Sati.DemoRefresh/Shared/DemoReset.ps1`). `AuditTrail.Record` takes an
   `Actor`; a system sweep needs a deliberate system-actor representation, recorded in
   `DECISIONS.md`. Don't invent a fake user row.
5. **Agency time:** use `ApiClock` / `TenantClock`, never `DateTime.Now`. The banned-symbol list
   is `architecture/BannedSymbols.ServerClock.txt`.
6. **Bound shared database work** against the dated SQL capacity in
   [the inventory](DATABASE_ENVIRONMENTS.md#observed-demo-inventory--october-8-2026).
   Agencies share capacity; their IDs do not reserve database resources. Preserve D2's
   no-wake-ping decision and bounded catch-up work.
   **Nothing in this brief may poll SQL on a short fixed interval while nobody is using Demo.**
   Reassess pause/allowance constraints if the selected SQL tier changes.
7. **Reuse the Function's established telemetry architecture.** The preserved 1.3.33 release
   evidence records the reset's `DEMO_COMPLIANCE_HISTORY_COMPLETE` marker. Validate current
   telemetry access and managed-identity grants through the inventory and activation gate;
   historical success is not proof of present permissions or received alerts.

## 3. The common worker pattern

Every server worker in this brief must:

- put its decision rule in one named owner in `Sati.Contracts.V1`, used by both the API and the
  local service;
- be idempotent and safe to run twice or concurrently: document each `sp_getapplock` resource
  and scope (deployment/reset, agency/account, or record), and coordinate across hosts through
  SQL. Preserve existing global locks where their invariant requires them; W8 investigates
  narrower scope and fairness rather than assuming a job-global lock isolates agencies;
- catch up on wake (constraint 1) without polling SQL while idle (constraint 6). Record each
  run that changes data as **one summary audit event** per agency (counts and ids, no
  narratives), using the system actor from D3;
- work in bounded batches with a cancellation token, and stop at the batch limit;
- for new or revised multi-agency execution, follow W8: bounded agency turns and total run/
  dependency budgets, recoverable agency failure isolation, and explicit progress evidence;
  existing W1–W6 implementations are not claimed to satisfy the new W8 criteria;
- respect optimistic concurrency: increment `Revision`, and never overwrite a row whose revision
  changed since it was read;
- log failure type, job name, and counts only. No narratives, names, payloads, tokens, or vendor
  response text;
- sit behind a configuration flag that defaults to **off**, with the flag checked on every pass;
- take its clock from `TimeProvider`/`ApiClock` so tests can drive it;
- have tests that **fail against the code before the change** (an `AGENTS.md` rule).

## 4. Decisions (made 2026-10-03)

Josh deferred these to the reviewer's recommendation. They are recorded in `DECISIONS.md`
(2026-10-03, "Background worker operating decisions"). Build to them; raise a concern with Josh
rather than silently departing from one.

| # | Decision | Affects | Outcome |
|---|---|---|---|
| D1 | Where alerts go | W1, W4 | **Email to Josh through an Azure Monitor action group**, driven by log-search alert rules on the Function App's **existing** Application Insights (constraint 7). No new monitoring resource and no SMS or paging service. Two rules: a watchdog finding was logged; and **no** watchdog result at all in 26 hours, which catches a watchdog that itself stopped running. Codex confirms current alert-rule and email pricing in the script's header before Josh runs it. |
| D2 | Wake ping | W1, W2 | **No wake ping.** A ping that keeps F1 awake buys little, and one that touches SQL breaks constraint 6. Detection runs once a day in the Function App, after the nightly reset, when the database is awake anyway. API workers catch up whenever someone uses Demo. A day's delay in Demo is acceptable. Cloud Production will run on a tier with Always On, so revisit this then rather than engineering around F1. |
| D3 | System actor in audit events | W2, W4 | **`ActorUserId` 0 behind one named constant** in Contracts (for example `SystemActor.UserId`), matching the reset's existing precedent. `MetadataJson` carries `"actorKind":"system"` and the job name. `AgencyId` is the affected agency. The Admin activity feed must show it as an automatic action, not a person; verify how it renders the reset's existing actor-0 rows and reuse that. Document it in `AUDIT_EVENTS.md`. |
| D4 | Azure changes | W1, W6 | **Codex writes code and idempotent scripts with a `-WhatIf` mode; Josh runs anything that touches Azure.** Under the D1/D2 design, W1 needs **no new role assignment**: the watchdog uses the Function App's existing SQL identity and storage connection. The only Azure change is the action group plus two alert rules. W6 rehearsal waits for a separate go-ahead. |

---

## 5. Work items, in priority order

### W1 — Operations watchdog and alert routing *(Significant now; Critical before a production pilot)*

**Problem.** Health checks exist, but no failure reaches a person. On 2026-10-03 a Demo reset
request was accepted (HTTP 202), reached the poison queue with no `demo.reset.*` audit, and
`/health/ready` stayed Healthy (`AGENDA.md`, release 1.3.34). `OPERATIONS.md` "Health, monitoring,
and alerts" lists alert routing as pre-pilot work. The reset writes its own outcome, so a host
crash leaves nothing behind. Detection has to look for a **missing** outcome.

**Build:**
1. A new timer function, `DemoWatchdog`, in `Sati.DemoRefresh/`. Run it once a day at 04:00
   Eastern, 45 minutes after the nightly reset (its own app setting, like
   `DemoRefreshSchedule`). It reuses `Get-DemoSqlToken`/`New-DemoConnection` from
   `Shared/DemoReset.ps1`; split those into a shared helper if that keeps the reset script
   unchanged in behavior. Keep it separate from the reset functions so a reset that hangs
   doesn't stop the watchdog.
2. It checks, read-only:
   - no `demo.reset.completed` or `demo.reset.failed` audit since today's scheduled run;
   - the latest outcome is `demo.reset.failed`;
   - `demo-reset-requests-poison` holds messages. Read the approximate count through the app's
     existing `AzureWebJobsStorage` connection. Never read message bodies into logs, and never
     dequeue or delete;
   - W2 outcome check: Pending notes older than their agency's window plus two days. If any
     exist, the sweep is not running. This checks the outcome, so no worker heartbeat table is
     needed;
   - W4 conditions, once those features are activated (below).
3. It writes exactly one trace per run: `SATI_WATCHDOG_OK`, or `SATI_WATCHDOG_FINDING` followed
   by the condition names. Use names and counts only. No request ids beyond the reset's own GUID,
   no people, and no queue payloads.
4. `scripts/Set-DemoWatchdogAlerts.ps1`, run by Josh (D4), with `-WhatIf`. It creates or updates
   the action group (email to Josh) and the two log-search alert rules from D1 on the Function
   App's Application Insights. It must be idempotent and must not touch firewall rules or any
   other resource.
5. Optional, and only if cheap: show the latest `demo.reset.*` outcome prominently on the Admin
   operations panel (`/admin/operations`, `ApiEndpoints.cs:391`). That data is already in
   `AuditEvents`. Don't build a second watchdog evaluator in the API.

The Function App's publication is not part of this brief. 1.3.34 deliberately did not republish
it, and the next publication must carry the updated seed source (`AGENDA.md`, release 1.3.34).
Codex prepares the change; Josh publishes it through the normal release path.

**Never** replay a poison message or start a reset automatically. Alert a person.

**Accept when:** script-level tests show that a missing outcome, a failed outcome, and a nonempty poison queue each produce
`SATI_WATCHDOG_FINDING` with the right condition name, and a clean state produces
`SATI_WATCHDOG_OK`. Follow the existing pattern: `scripts/Test-DemoResetRuntime.ps1` runs the
real orchestrator against in-process fakes, driven by `Sati.Tests/DemoResetRuntimeTests.cs`. Each
check is confirmed failing before its fix. `OPERATIONS.md` names Josh as
the alert owner, says that an alert means "investigate and recover manually," and links the
1.3.34 recovery as the worked example.

### W2 — Overdue-note abandonment sweep *(Significant)*

**Problem.** Pending notes past the documentation window become Abandoned only when their own
case manager's dashboard loads (`CaseManagerDashboardViewModel.cs:1458`, plus the hourly timer at
`:2089`). A case manager who doesn't sign in never has their notes aged out. Both paths are
scoped to the caller's own caseload. Neither path records an audit event (`AGENDA.md`, "Audit the
abandonment sweep"). Both now increment `Revision`, which partly closes the agenda's concurrency
item. The rule has also drifted: the local service checks `NoteWorkflow.CanSystemAbandon`
(`Data/NoteService.cs:222`), while the API hard-codes `Status == 1` (`ApiEndpoints.cs:5244`).

**Build:**
1. One rule: a note is system-abandonable when `NoteWorkflow.CanSystemAbandon(status)` holds and
   `ProductivityForecast.IsDocumentationWindowClosed(eventDate, today, AbandonedAfterDays)` is
   true. Use it in both the API route and `NoteService.UpdateAbandonedNotesAsync`. Add a
   shared-case test that runs both implementations against the same inputs.
2. An API `NoteAbandonmentWorker`. Once per agency-local day, per agency, it applies the rule
   using that agency's `AbandonedAfterDays`. It writes one summary audit event per agency
   (`note.abandoned-by-system` or similar; count plus note ids) with the D3 system actor. Add
   the action to `AUDIT_EVENTS.md`.
3. Replace `ExecuteUpdateAsync` with a revision-checked update, so a note saved between read and
   write is skipped and picked up on the next run.
4. Keep `POST /notes/abandon-overdue` for the desktop's existing trigger, but route it through the
   same service so it audits too. Update `API_AUTHORIZATION.md` if its scope changes.
5. Local mode keeps its existing triggers and gains the shared rule and an audit event. No new
   local scheduler.

**Accept when:** a Demo agency whose case manager never signs in has eligible notes abandoned
after the next wake past the day boundary, with one audit event. A concurrent edit is never
overwritten. Removing the audit call fails a test. Removing the revision check fails a test.

### W3 — Incident outbox: attribution first, then retry *(Significant; cloud mode only)*

**Problem.** `IncidentOutbox` (`Data/Cloud/IncidentOutbox.cs`) stores envelopes in one folder per
Windows profile. `IncidentReportRequest` (`Sati.Contracts/V1/IncidentContracts.cs:160`) has no
user or agency. `POST /api/v1/incidents` attributes the incident to whoever sends it
(`ApiEndpoints.cs` ~99-116). The shell sends queued envelopes right after a sign-in or account
switch (`Views/ShellWindow.xaml.cs:460,533`). So a failure queued under account A is filed under
account B's agency. Separately, a queued envelope waits for the next incident, sign-in, or
switch before it is retried.

**Build:**
1. Record the originating user id and agency id in the **local** envelope wrapper, not as a
   request field the server trusts. `FlushAsync` sends only envelopes whose recorded account
   matches the current session, keeping FIFO order per account. Decide, and record, what happens
   to envelopes from an account that never returns (e.g. age them into `Rejected` after N days).
2. Then add a bounded in-session retry: exponential backoff with a cap and jitter, only while
   signed in, and stopped on sign-out or switch. Expose the oldest pending envelope's age where
   support can see it.
3. `LocalIncidentReporter` writes straight to the local database under the current user. It does
   not have this defect and needs no change.

**Accept when:** an envelope queued under A, followed by a switch to B and a flush, sends nothing
under B. A transient API failure is retried without another incident occurring. Each test fails
against the current code. (This repeats a suggested task already queued; do it once.)

### W4 — Billing and signature watchdogs *(Significant when activated)*

**Problem.** `ClearinghouseDispatchWorker`, `ClaimMdSandboxPoller`, and
`SignatureProcessingService` exist, are gated off, and log only content-free warnings.
`OPERATIONS.md` "Electronic signature operations" requires external alerting for unprepared
signed copies and failed notifications. Uncertain clearinghouse uploads need reconciliation, not
resubmission.

**Build:** read-only conditions added to W1's daily `DemoWatchdog`, each one checking the
feature's own state tables for rows stuck past a threshold:
- dispatches in a non-final state;
- uploads whose outcome is uncertain;
- signed requests with no prepared package;
- notifications that exhausted retries.

Each condition runs only when its feature is enabled for some agency. Confirm the Function's
`SELECT` grant covers those tables, and list any missing grant for Josh rather than widening it
yourself. **Never** resubmit an uncertain upload or resend a notification automatically. Surface
it for an operator.

**Accept when:** each condition can be produced in a test and yields `SATI_WATCHDOG_FINDING`
with its name. A disabled feature produces no condition.

### W5 — Desktop day-change refresh *(Trivial; client-side, so both environments get it)*

**Problem.** `ShellWindow`'s `_dateRolloverTimer` (`Views/ShellWindow.xaml.cs:270`) only shows
check-request prompts. The dashboard matrix, upcoming events, and calendar keep the date they were
loaded with (`CaseManagerDashboardViewModel.cs:1455`) until navigation or reload.

**Build:** on day change (and when the window is activated after a day change), refresh the
date-sensitive views once. Take a `LatestRequestTracker` identity per `AGENTS.md`. Do not refresh
while an editor holds unsaved work.

**Accept when:** a test advancing the clock past midnight triggers exactly one refresh of each
date-sensitive view, and none while a draft is dirty.

### W6 — Backup restore verification for cloud Production *(Critical before a production pilot; design and script only now)*

**Problem.** `OPERATIONS.md` "Remaining production work" lists restore drills. A backup that has
never been restored is not evidence of recovery.

**Build:** a runbook and script that restores Azure SQL point-in-time into an isolated, uniquely
named scratch database, checks `dbo.SatiDatabaseIdentity`, table and row counts, and migration
history, records the result as evidence, and deletes the scratch database. Rehearse against
`SatiDemo` only with Josh's approval (**D4**). Do not schedule it, and do not grant roles.

### W7 — Local `SatiProduction` backup hygiene *(optional; Josh's workstation only)*

The official record is the agency's system, so this is for convenience, not records. Pre-migration
backups under `%LOCALAPPDATA%\Sati\schema-backups` are never pruned or verified
(`AGENDA.md:7889`). Each one is a PHI copy. If built: verify with `RESTORE VERIFYONLY` before
migrating, keep the newest N, and never write outside `%LOCALAPPDATA%`. Do not add a scheduled
local backup without asking Josh.

### W8 — Tenant workload isolation and worker fairness *(Significant now; required evidence before a multi-agency pilot)*

**Status:** first four SATI-WRK-001 slices implemented and locally verified on 2026-10-08; actual
verification and limits are recorded below and in working evidence. Current-day cardinality,
fairness, total-budget, admission and capacity work is open.
W1–W6 remain implemented as recorded above; their activation gates remain.

#### October 8 — bounded note-worker agency-failure isolation

`NoteAbandonmentWorker.RunDueAsync` now continues after a narrowly classified recoverable
agency sweep failure. The failed agency stays due, is invoked once per pass and prevents
global day completion. Later healthy agencies can finish in the same pass; their committed
counts and successful agency/day skipping are retained when the failed agency is attempted
on a later existing hourly pass. At-limit batches still remain due.

[DEC-0222](docs/decisions/current/2026-10-08-DEC-0222.md) owns the classification and rejected
alternatives: EF concurrency faults and a nonempty all-1205, nonfatal SQL error collection,
including deliberate EF update/retry-limit wrappers. Cancellation takes precedence. Shared
agency-list/connection/coordination, timeout, fatal, mixed SQL and unknown failures propagate.
The catch encloses only the agency sweep. Diagnostics contain agency ID and failure type,
without exception objects/messages, note narratives or credentials.

The sweep transaction, execution strategy, 100-note batch, guarded revisions and atomic
note/audit writes are unchanged. The global SQL lock and Demo reset lease, default-off option,
next-local-day behavior and hourly cadence are retained. This is failure continuation, not a
new scheduler, immediate retry or a guarantee of fair capacity or bounded agency wait.

The fail-first regression injects an EF concurrency fault at A's audit insertion after its
guarded update. Against the original worker it failed with that propagated exception before
B could progress. Passing acceptance and its limits are recorded in
[the dated working evidence](docs/readiness/work-evidence.md); synthetic SQL exception injection
is distinct from a real SQL deadlock/retry-exhaustion proof. No worker activation, schema,
live-data access, provider send, cloud change or deployment is part of this slice.

#### October 8 — bounded Claim.MD HTTP exchange deadline

`ClaimMdSandboxConnector` now owns the existing 45-second budget through headers, stream
acquisition and every response-body read. A single `TimeProvider` deadline starts inside the
coordinated callback after SQL admission/pacing, linked to caller cancellation. Client registration
uses the connector's configuration helper and removes the competing `HttpClient` timer.
[DEC-0223](docs/decisions/current/2026-10-08-DEC-0223.md) records ownership and rejected alternatives.

The stalled-body connector and real-connector upload regressions both failed against the original
transport. Fake-time acceptance proves cancellation at 45 seconds, including a delayed-header
exchange's remaining body budget, response/body disposal and following healthy callback progress.
An upload remains `Sending` while stalled and becomes `OutcomeUnknown` after deadline cancellation;
there is one physical request/attempt and no automatic replay. Existing SQL coordination/reset
proofs passed separately in a disposable private instance. Counts, commands and TRX evidence are
in [the dated working evidence](docs/readiness/work-evidence.md).

The fixed host, redirect/key validation, response cap, default-off gates, provider-wide quota,
SQL lease/command budgets, pacing, reset/poller/dispatch locks and uncertainty policy are retained.
This cooperative I/O deadline does not bound admission, synchronous decoding/parsing, disposal
or lock cleanup, a whole account turn or a worker pass. Tests establish local component behavior,
not actual provider cancellation, remote nonreceipt, deployed configuration or fair agency latency.
No worker/transport activation, schema, live-data access, provider call or deployment occurred.

#### October 8 — bounded note-worker agency discovery

`NoteAbandonmentWorker.RunDueAsync` now captures a nullable maximum agency ID and discovers
ordered IDs in pages of at most `AgencyDiscoveryPageSize` (100). Its nullable initial cursor
applies no first-page lower bound, preserving rejection of damaged nonpositive IDs. Every page's
last observed ID advances discovery, including IDs whose agency already completed or failed.
The separate `NoteAbandonmentSweep.WorkerBatchSize` (100 notes) is unchanged.
[DEC-0224](docs/decisions/current/2026-10-08-DEC-0224.md) owns this paging policy.

This traverses a finite key range, not a frozen membership snapshot. If the pass could otherwise
complete, a bounded final existence check for IDs above the captured maximum leaves the day due
when growth is observed; an initially empty range checks for any agency. The next existing hourly
pass can discover that growth while skipping completed agencies. Inserts within the range after
the last discovery query can be missed even above the last cursor, as can lower-ID inserts or
reseeding behind it and inserts after the final check. Complete provisioning/membership cache
invalidation remains open. No all-start-time/new-agency visitation guarantee is claimed.

The reader-observer regression measured 251 materialized IDs in one query against the unfixed
worker and failed the 100-ID bound. The fixed class passed 32 cases with two SQL-only skips;
ten added cases cover stable visitation/counts/audits, failure and exact note limits across pages,
between-page cancellation/disablement with cleanup, damaged IDs, finite higher-ID and initially
empty growth, and shared range/growth failures. Only the main materialization regression has
the new fail-first proof. The two existing guarded SQL sweep/reset proofs passed separately in
a new private instance that was stopped/deleted. The updated documentation gate, its 22 negative
proofs and tracked/staged whitespace checks passed; exact results and limits belong to
[working evidence](docs/readiness/work-evidence.md).

Global SQL/reset leases, classified sweep-fault policy, cancellation/disablement, atomic audits,
committed counts, successful-agency/day skipping, next-day/default-off and hourly/no-idle-SQL
behavior are retained. This bounds each agency-ID materialization, not the retained day cache,
total pass time/turns or a healthy-agency wait. No route, schema, worker activation, provider call
or deployment changed.

#### October 8 — note-worker daily completion-cache rotation

`NoteAbandonmentWorker.RunDueAsync` now tracks the cache's captured local day. After acquiring
`runGate`, it replaces `completedByAgency` storage when `ApiClock.Today` differs, before the
existing global successful-day idle check. Every completion is written for that same captured
day under the gate. Current-day successful agencies remain skipped while failed/at-limit agencies
remain due. Disabled or already-canceled checks still avoid SQL/coordination; rotation occurs
only when an enabled call acquires the gate, not through an idle cleanup scheduler.
[DEC-0225](docs/decisions/current/2026-10-08-DEC-0225.md) records the lifecycle choice.

The regression used 251 zero-note agencies on day one, removed 150 and added ten on day two,
then kept one agency due with a recoverable fault. The original cache retained 261 entries where
110 current-day successes were expected. After the fix, retained counts are 251, then 110,
111 after same-day recovery, and 66 on day three; exact attempts and zero note/audit effects are
checked. The main regression and full class passed (33 passes/two SQL-gated skips), followed by
the updated documentation gate, 22 negative proofs and tracked whitespace check. Exact results
belong to [working evidence](docs/readiness/work-evidence.md). Prior SQL paging/coordination proof is
retained without rerunning or counting it as new cache evidence.

Replacing the dictionary retires its old storage as well as entries; source review establishes
that choice, not an immediate garbage-collection or measured process-memory bound. This limits
historical retention after an enabled date-change check, not current-day cardinality/churn or
the number of removed agencies completed earlier the same day. The distinct 100-agency discovery
and 100-note sweep limits, finite-range membership caveats, global leases, atomic audits, fault
classification, cancellation/disablement, default-off/hourly/no-idle-SQL and next-day rules remain.

**Next eligible local work at the October 8 checkpoint:** the agenda switches to SATI-BIL-001's shared `BillingExportGate`
residual-compliance-error repair. [The agenda](AGENDA.md#next-eligible-work) owns precise
dependencies and fail-first acceptance. This does not close assessment R1 or full queue/pre-send
R2. W8 total budgets, current-day capacity and fair wait/admission evidence remain later work.

**Dispatch poison isolation blocker at the earlier checkpoint:** the globally oldest Queued dispatch could be selected
repeatedly after a missing-key preflight failure, before any send. Earlier acceptance correctly
leaves it Queued with no attempt or unknown-send evidence. The current model has no durable
preflight due/backoff/hold/reopen owner. Implementing that isolation needs a reviewed known-unsent
recovery policy and additive schema proposal under §8; it is not eligible as a silent local
state workaround. `CancelledBeforeSend` would discard temporarily blocked due work, while a
process-memory failed-ID list would lose protection across restart/hosts. Local policy/design and
reproduction can precede any separately approved schema work. Fair lane selection, API admission,
aggregate capacity and live progress/alert evidence also remain open.

The approved bounded implementation below supersedes this missing recovery owner/source status;
the listed broader W8 work remains open.

#### October 9 — known-unsent dispatch isolation proposal

The proposal below is retained as the reviewed design. Josh's subsequent explicit **Approved**
accepts its policy and additive source schema under §8; [DEC-0235](docs/decisions/current/2026-10-09-DEC-0235.md)
supersedes only its pending approval/implementation status. Current behavior is recorded below.

#### October 9 — known-unsent dispatch isolation implementation

**Status:** bounded local source implementation and complete project-suite verification finished;
activation and deployment remain pending. Actual results and prior failures are owned by the
linked working evidence below, not a new sealed release assessment.
Contracts `ClearinghousePreflightRules` owns validated Ready/Deferred/Held state, four persisted
1/5/15/60-minute delays plus stable 0–10% delay, fifth-failure hold and reopening eligibility.
Persistence owns `ClearinghouseDispatchReadiness` and its shared model guards. The API worker
classifies only typed missing environment account keys before Sending; it commits readiness and
a fixed system audit together under common admission. Queued intent, revision, bytes and upload
history are retained. Discovery excludes the whole deferred/held account; no process skip list
or local desktop scheduler exists. Successful due preparation clears the cycle only with normal
current release validation. Cancellation, shared wrapping, SQL/lease and unclassified faults do
not consume account failures. Sending/OutcomeUnknown remain subject to existing reconciliation.

The bounded A/B target is next successful worker turn after A's deferral commits, under the
prerequisites in the reviewed proposal. This is not a global fairness or latency bound. The
account session lease is zero-wait and precedes common SQL admission. It spans preparation and
the short decision, ends before upload, and is checked before decision writes and commit.
Reset/dispatch leases and single-attempt physical upload remain. Session ownership checks detect
observed lease loss; they are not an atomic external fencing token and do not prove arbitrary
network-partition exactly-once delivery. Common admission and retained Sending state remain
essential. Key preparation runs outside SQL transactions.

`GET /billing/clearinghouse` returns same-agency safe readiness facts and current account/readiness
revisions. `POST /admin/clearinghouse/accounts/{accountId}/preflight/reopen` requires current
same-agency Administration, an enabled test gate, exact expected revisions and restored key/
receipt protection. Actor, full account binding and revisions are checked again under admission.
It atomically clears readiness with a human audit, changes no dispatch and sends nothing.
Repeated/stale commands conflict; clients refresh and review. WPF shows text status and an
accessible Admin recovery command through CloudBillingService with captured session identity.

`ApiSurface` declares the recovery route and readiness contract; source fingerprint is
`D30D44631876` (269 routes, 70 named shapes). This is a source compatibility boundary, not a
newly deployed release. Earlier deployed observations remain in the environment inventory.

Migration `20261009183720_AddClearinghousePreflightReadiness` adds only the readiness table,
composite account FK/state CHECK/eligibility index and global dispatch selection index. Missing
row means Ready. Down refuses unresolved Deferred/Held rows with SQL 51044. Source generation
used `--synthetic-design`; that implementation's approval covered source/private synthetic
verification only. The later bounded Demo migration/baseline/reset approval and observed preflight
belong to [the environment inventory](DATABASE_ENVIRONMENTS.md#demo-readiness-migration-preflight--october-9-2026).
Pause dispatch on every host for rollout/rollback. Old workers ignore readiness; mixed active
versions are unsafe. Keep additive storage and audits during operational rollback, review queued/
uncertain work, and verify compatible hosts before activation. Baseline/reset compatibility and
actual deployed query plans require separate release preflight.

The controlled runner is `scripts/Apply-ClearinghousePreflightReadinessMigration.ps1`, with reviewed
SQL in its `.guarded.sql` companion. Run in Windows PowerShell 5.1. Its default preflight makes no
persistent target changes; `-WhatIfOnly` rehearses within an outer rollback transaction and
`-Apply` is the explicit mutation mode. The runner pins the Demo subscription/server/database and
SQL hash. SQL validates the full predecessor migration-ID set and actual column, key, constraint
and index semantics under the exclusive reset lock. Compatible untracked objects may be adopted;
incompatible or missing tracked objects stop for investigation. Temporary local metadata is used
to compare SQL Server's compiled CHECK expression. The original `.generated.sql` is a source
artifact, not the controlled cloud execution path. Preparation/test status is recorded in
[working evidence](docs/readiness/work-evidence.md#2026-10-09--readiness-migration-runner-preparation).
Cloud rehearsal/apply/rerun still requires the approved release sequence and all-host dispatch
pause; this runner never publishes an API, captures a baseline, queues a reset or changes a
firewall rule. Keep dispatch disabled until its separate activation requirements are satisfied.

[Working evidence](docs/readiness/work-evidence.md#2026-10-09--missing-key-dispatch-isolation-implementation)
owns actual fail-first, fake-time, private SQL, UI and suite results and limits.
Remaining W8 includes reviewed idle/wake scheduling (the existing three-second idle loop is
unchanged), fairness under sustained backlog/contention, aggregate sessions/dependency budgets,
API admission, configuration consistency and owner-run operational/alert evidence.

#### October 9 — dispatch idle-wake scheduling proposal

**Status:** SATI-WRK-001 source inventory and concrete design complete; runtime policy **proposed**
in [DEC-0236](docs/decisions/current/2026-10-09-DEC-0236.md), awaiting Josh's review. No application,
schema, client, worker flag or hosting setting changes in this slice. D1–D4 and the approved
account recovery rules remain in force. The next source implementation depends on adoption of
this policy; activation additionally depends on the broader W8 gates. This section owns the
proposal; indices and operational notes link here instead of copying its numeric policy.

**Inventory at `d223b1736df18202e397cea53a328984c50c3416`:**

| Source owner | Observed behavior and design consequence |
|---|---|
| `ClearinghouseDispatchWorker.ExecuteAsync` | Default-off startup exits before work. When enabled, first turn runs immediately; a true result continues without delay, false or caught failure delays three seconds. The delay does not use the injected clock. An enabled empty queue therefore causes about 20 selection attempts per minute while the host remains alive; this is a cadence calculation, not a measured SQL command/CPU bound. |
| `ProcessOneAsync` / `ProcessUnderResetLeaseAsync` | Each enabled turn enters reset coordination and creates a context before selecting the globally oldest eligible Queued row. Durable Deferred time is checked with `TimeProvider`; Held and future Deferred accounts are excluded. It does not return the earliest future due time. False also means lease contention, changed/stale work or disabled gate, so false cannot safely mean an empty queue. |
| `SqlDemoWorkerResetCoordination` | Reset, dispatch and account session leases can open separate SQL connections. Reset/account locks are zero-wait; they are not resource reservations. Even an empty turn may do reset lock acquire/release in addition to selection. Count these separately from the selection budget. All connections/leases must be disposed before a scheduling wait. |
| Queue/reopen endpoints | New queue intent is saved and committed before success; reopen commits restored readiness with the human audit. There is no post-commit scheduling notification. Queue's exact-file replay/recovery paths can return an existing row and must not fabricate a new commit or send. |
| Login / `ValidatedActorFilter` | Login verifies stored enabled identity, current password/security/agency/role/permissions, saves its audit and issues a database-bound token. Ordinary API routes validate stored identity and database instance before the handler. There is no general trusted user-presence timestamp or dispatch activity service. Authentication middleware alone is too early. |
| Billing workspace / client calls | The billing GET is gated and scoped, and currently loads on billing navigation, explicit refresh and related command completion in `BillingSubmissionsViewModel`; no periodic dispatch timer was found in that ViewModel. It is a possible validated work signal, not proof of human presence. Token renewal, health probes, incident delivery and WebSocket traffic are unsuitable activity sources. |
| Startup/restart and existing tests | The first turn reevaluates durable UTC readiness, so overdue work is eligible after restart. A suspended surviving process has no explicit wake hook. Current account due tests invoke `ProcessOneAsync` directly: they prove eligibility, not hosted waiting or inactivity. There is no cross-host wake channel. |

**Recommended activity contract.** Add one constructor-injected API scheduling owner used only
by the existing hosted dispatch loop and the exact successful handlers below. It holds scalar
activity/deadline state and a capacity-one coalesced signal, with no per-user/agency/account map.
The signal is only a hint to reread authoritative work; it grants no permission, account selection
or release eligibility. Never enqueue a file, key, token or caller-supplied timestamp in it.

- Successful **login** for a supported, positive-agency ordinary user, after current stored
  checks, audit persistence and token issuance; exclude PlatformOperator/support identities.
- Successful, enabled **billing clearinghouse workspace GET**, after stored actor validation,
  billing permission, scope checks and completion of the response projection.
- Newly committed **queue** intent, or an authorized exact-file replay/recovery whose returned
  row remains Queued for that same agency/account. A replay hint changes no audit or business state.
- Successfully committed **Admin preflight reopen**, after its second authority/binding/revision
  checks and commit. Failed, rolled-back, stale, foreign, busy or key-unavailable responses do not
  signal. A commit followed by response loss may still signal; response success is not its authority.

Exclude `/auth/renew`, generic `/me`/GETs, health, telemetry/watchdogs, worker progress, reset logs,
chat keep-alives and anonymous/failed requests. No request header, UI idle timestamp or JWT claim
creates activity. Existing request checks are reused; the signal performs no extra SQL and never
weakens revocation. Notification failure must not turn a successful business commit into a retryable
error. For queue/reopen, capture the committed outcome inside the owned write, but publish only
after its transaction/account coordination scopes release: waking a worker while the reopening
lease is still held would manufacture contention and a needless cooldown. Do not await work
completion from the request. Automatic clients must not add a heartbeat/refresh loop to these routes. The supported
contract means qualifying authenticated API use, not guaranteed physical user presence: an
authorized caller can automate an allowed action. Expensive-request admission remains SATI-SEC-001.

**Recommended wait policy (Demo/Testing only):**

| State/event | Proposed action and bound |
|---|---|
| Disabled, stopping or canceled before a turn | Zero dispatch scheduling SQL or key/provider calls; clear pending hints/waits and stop. Enabling requires a compatible host restart under the existing configuration model, not an idle SQL feature-flag poll. |
| Enabled host start | Exactly one catch-up turn against persisted Queued/readiness state. Startup alone does not grant an active period, drain a backlog or schedule future retries. This is a bounded startup exception, not a recurring idle poll; repeated hosting restarts are a separate capacity risk. |
| Qualifying activity | Grant/renew a **five-minute** local active period from server monotonic time and coalesce a catch-up hint. Start the next turn when its pacing/cooldown allows; recheck the gate and cancellation first. |
| Any dispatch turn | One turn at a time per host, with **at least three seconds between turn starts**, including successful turns. Thus at most **20 starts in any half-open 60-second interval per host**; current immediate successful-loop draining is deliberately removed. This bounds starts, not duration or total SQL. |
| Active with eligible backlog | Continue paced turns only until activity expires; work completion never renews activity. Preserve the existing one-row selection, leases, account readiness and current release checks. No fairness or queue-drain deadline is promised. |
| Active, truly empty selection | Obtain one bounded schedule snapshot: scalar existence of eligible Queued work plus earliest future Deferred UTC time joined to same-agency Queued work. Held, Sending, OutcomeUnknown and future Deferred in synthetic-only mode never cause a retry timer. Materialize no account list. |
| Active, waiting | Wait in memory until the earliest of a coalesced hint, relevant due instant, **60-second active reconciliation**, active expiry or cancellation. The reconciliation checks cross-host commits/lost hints only while active, through this same loop. No separate scheduler or SQL-held timer. |
| Active expiry / dormant | Dispose all contexts/leases and cancel due/reconciliation timers. **Zero further dispatch scheduling SQL for any idle duration**, even if a Deferred account becomes due. Wait for a qualifying signal or a real host restart. Due work remains durable and overdue; the next catch-up reevaluates it. |
| Reset/dispatch/account contention or shared/unclassified failure | Do not interpret it as empty or consume an account failure. Apply a **60-second local cooldown**, bounded by activity expiry, before another automatic turn. A new hint coalesces but cannot bypass cooldown. No automatic startup-only retry. Preserve safe logging and existing uncertainty. |

Persisted account due times retain UTC and the approved 1/5/15/60-minute recovery delays; they are
earliest eligibility, not promised delivery times. Five-minute activity/pacing/cooldown use the
injected TimeProvider's monotonic timestamps. In active state recheck UTC at least at the
60-second reconciliation boundary to handle clock changes; a backward change must never probe
before the current persisted due instant. Never replay each missed tick after suspend/restart.
On expiry do not start another turn. An in-flight turn finishes or cancels under its existing
safe write/retention rules: inactivity does not roll back Sending, discard a receipt or schedule
another upload. The 45-second connector exchange deadline is not a whole-turn deadline.

**SQL and signal integration:** replace the ambiguous scheduling bool with internal typed outcomes
for processed, empty-with-snapshot, contended, disabled and failed turns; retain a compatibility
wrapper if the existing one-turn tests need it. Do not treat account/stale-selection lease misses
as empty. The empty snapshot runs under reset exclusion and has bounded scalar projection;
SQL-provider indexes/query-plan evidence remains required. Per host, the proposal caps selection
starts at 20/minute and allows at most one schedule-snapshot command for a truly empty turn.
This is at most 40 top-level selection/snapshot commands/minute under continuous activity, plus
existing locks, validation, history and write commands. It is **not a total SQL/connection budget**.
With H active hosts those local caps can multiply by H; aggregate admission remains open W8 work.

Capture a signal generation before discovery and recheck it atomically when arming the wait;
commit/signal between an empty read and parking must not disappear. Repeated signals merge into
one pending hint, never spawn another processor or accumulate payloads. A stale due snapshot
can cause only a requery, never override current readiness/gate/authority. Failure to publish a
local hint or a commit on another host is recovered within the active reconciliation opportunity,
subject to pacing/available dependencies; a dormant other host remains dormant. No new distributed
event service, schema or cross-host latency guarantee is invented. Existing SQL coordination
continues to exclude duplicate sends; signal delivery is not exactly-once execution.

**Fake-time acceptance to implement after review (not executed in this design slice):** count
selection/snapshot commands and coordination opens separately, with deterministic barriers;
drive the actual hosted loop and real signal publishers, not only a second copy of the policy.

| Case | Required observation |
|---|---|
| Disabled / pre-canceled | Zero scheduling contexts, coordination opens and connectors, regardless of signals/time advances. |
| Enabled cold start, empty or overdue | One catch-up turn; no automatic backlog drain or second startup-only query after 24 hours. Overdue work passes the ordinary release gate; held/uncertain work is never sent. |
| Five-minute inactivity | After an authorized hint and any already-started turn, advance through exact expiry and another 24 hours: zero additional scheduling SQL. Fake due times during dormancy do not wake SQL. |
| Ready backlog and hint flood | No start at 2.999 seconds after the prior start, next allowed at three seconds; at most 20 starts in any half-open minute, one processor and one pending hint. Completed work does not extend expiry. |
| Active empty queue | No three-second SQL polling: reconciliation only at 60-second boundaries unless a qualifying hint or relevant due time intervenes. At expiry, no reconciliation SQL even if its timer also fires. |
| Deferred before/at due | With an active period covering due, no key/connector before the exact persisted instant. At due, one paced ordinary turn; if due follows expiry it remains parked until activity. No drift-based early probe or missed-tick replay. |
| Held / synthetic-only deferred | No due timer from either state; unrelated healthy Queued work still gets paced turns. |
| Publisher authorization and commit order | Only the four allowlisted successful paths signal. Revoked/foreign/failed/unsupported/renewal/health requests signal zero times. Queue rollback emits no hint; barriers prove commit and owned lease disposal precede the hint. Valid committed/replayed Queued identity coalesces without a second business effect. |
| Empty-read/commit race | Commit just before wait-arm and just after wait-arm with barriers: neither local hint is lost; no parallel processing. |
| Cross-host or lost hint | Active host learns committed work at its next 60-second reconciliation opportunity; dormant host does zero SQL. Private SQL two-host tests retain same-dispatch/reset/account exclusion and exact retained bytes/audits. |
| Contention / shared failure | Retry not before 60 seconds while still active; signal storms cannot bypass cooldown. No account failure consumed, no tight false-result loop; expiry parks. |
| Restart / surviving-host wake | Restart gets one catch-up from persisted UTC; an existing process gets catch-up only through qualifying activity. Neither needs a wake ping or fires missed intervals in a burst. |
| Clock change | Monotonic activity/pacing remain stable; forward/backward UTC changes requery eligibility without early send or an inactive query. |
| Disable / cancel / expiry in flight | Gate/cancel before start avoids I/O; cancellation/expiry after Sending preserves uncertainty and receipt handling, with no resend. Wait cancellation disposes timers/readers and all lease/context ownership. |

Regression acceptance must fail against the unchanged idle loop for zero-idle-SQL, pacing after
success and publisher/race cases before any implementation is kept. Existing direct-call due,
billing admission, missed-key recovery and uncertainty tests remain necessary but do not prove
hosted scheduling. Do not count these proposed cases as passing tests or readiness credit.

**Tradeoff for review:** an overdue retry may wait beyond an hour, until the next qualifying use
or host start; long active backlogs stop when activity expires. This deliberately preserves D2
instead of keeping Demo awake for unattended dispatch. Future cloud Production needs a separately
reviewed durable wake/capacity/service objective, not automatic reuse of this Demo policy. No
shorter polling, client heartbeat, new timer Function, periodic health-driven wake, per-account
scheduler, implicit key reopen or increased concurrency is proposed. Broader fairness, aggregate
resource budgets, measured query cost, alert receipt and intended-host load remain activation gates.

#### October 9 — retained isolation proposal detail

**Reviewed proposal status before acceptance:** SATI-WRK-001 design and bounded failure reproduction; application scheduling is
unchanged. [DEC-0234](docs/decisions/current/2026-10-09-DEC-0234.md) is proposed for Josh's review,
not an accepted schema change. The later [billing completion](CLAIMMD_SANDBOX_RUNBOOK.md#coordinated-release-and-current-subset-compliance--local-source-october-9-2026)
supersedes the October 8 pending R1/R2 source status only. [Working evidence](docs/readiness/work-evidence.md#2026-10-09--missing-key-dispatch-isolation-design)
owns actual reproduction, checks and remaining limits.

**Failure boundary and conditional progress target.** The oldest Queued dispatch is selected
without a due time or account failure disposition. Its valid configured account-key reference
can resolve to no host key before Sending, so neither an upload nor an uncertain-send record is
created. Repeating ProcessOneAsync, even through a second host, selects that same dispatch.
For exactly two otherwise eligible agencies A/B, with A oldest and its key missing, the proposed
bound is: after A's failure disposition commits, B is selected on the next successful worker
turn while A is deferred. This counts turns, not seconds, and assumes available reset/dispatch/
account leases, SQL and shared receipt keys. A sustained ready backlog, repeated lease contention,
other failed accounts and shared outages need the broader fairness/budget work; no global bound
is claimed here. A missing environment key is host configuration, not proof that the vendor
or every host is unavailable. Account-wide deferral deliberately protects the lane across hosts;
require consistent key configuration at activation and record this tradeoff during review.
Deferred times are earliest eligibility, not a guaranteed timer delivery. The existing hosted
loop's three-second idle SQL selection is a separate cadence gap: this design does not approve
that as a future no-user polling strategy. No new wake ping or shorter SQL polling is allowed.
Catch-up on host start/wake and a reviewed idle-wait/activity/due scheduling integration are
activation requirements; numeric backoff does not establish idle capacity protection.

**Proposed authoritative owner.** Contracts `ClearinghousePreflightRules` owns disposition,
delay and reopening rules. Persistence owns the account readiness model/configuration shared by
ApiDbContext and SatiContext; only the API worker and an authorized API command update it.
The desktop does not gain a local scheduler or secret resolver. This is dispatch readiness,
separate from physical delivery, claim adjudication, poller checkpoints and account enablement.

**Recommended known-unsent recovery policy:**

| Observed result | Proposed response |
|---|---|
| Typed missing key for a valid, unchanged account reference, before Sending and before any upload | Persist an account-scoped Deferred disposition and fixed `account_key_unavailable` code. Keep every dispatch Queued and its exact bytes/request identity intact. |
| First through fourth committed failures in a recovery cycle | Earliest next probe is 1, 5, 15 and 60 minutes after the respective failure, plus deterministic 0–10 percent delay from account/cycle/failure identity. Persist the absolute UTC due time using TimeProvider. |
| Fifth committed failure | Held, no automatic next probe. Preserve the due work until an agency administrator restores the key and explicitly reopens the account. |
| Key restored during a deferred cycle | At the persisted due time, preflight again; successful preflight clears the failure cycle under admission and normal current release checks decide Sending. A successful key check alone is not billing approval. |
| Caller cancellation, shared receipt wrapping/Key Vault failure, SQL/lease failure or unknown exception | Propagate; do not label it an account fault, consume an account failure count, or manufacture a send outcome. Shared dependency protection is separate work. |
| Sending, OutcomeUnknown or actual upload evidence | Existing uncertainty/reconciliation rules only. A readiness command cannot put these back in Queued or authorize physical replay. |

These numeric delays are proposed engineering defaults, not a payer rule or measured service
objective. Missing environment configuration is the only newly classified fault in the first
implementation: introduce a fixed typed result at `EnvironmentClaimMdSandboxKeySource`, never
catch all InvalidOperationException or infer nonreceipt from a post-Sending connector failure.
Invalid/changed profiles retain the existing source/refusal policy; shared wrapping failures must
not be hidden behind account backoff. Do not use live vendor calls to probe readiness.

**Concrete additive schema proposal — not generated or applied:** one
`ClearinghouseDispatchReadiness` row per clearinghouse account, with primary key
`(AgencyId, AccountId)` and restrictive composite foreign key to the existing account alternate
key. Proposed fields are `Disposition` (Ready/Deferred/Held), `FailureCount` (0–5),
`RecoveryCycleId` (GUID), `NextEligibleAtUtc` (nullable UTC), `LastFailureAtUtc`,
`SafeFailureCode` (bounded fixed vocabulary), `ValidatedAccountRevision` and concurrency
`Revision`. Missing row means Ready for an otherwise eligible existing account. CHECK constraints
require Deferred to have a due time/count 1–4, Held to have count 5/no due time, and Ready to have
count 0/no due time/no active failure code. Account identity cannot change. Retain failure history
in append-only audits; do not store raw exceptions, keys, key references or claim bytes here.
Add an eligibility index to readiness and review the existing dispatch index/query plan for
`State, RequestedAtUtc, Id, AgencyId, AccountId`; no unbounded account/failed-ID materialization.
No existing claim, dispatch enum, receipt, attempt or correction lineage is rewritten.

**Admission, staging and stale work.** Discovery returns one globally oldest due Queued row,
excluding Deferred-before-due and Held accounts through trusted same-agency joins. Recheck this
after lease acquisition; a new queue cannot clear account backoff. Preserve shared reset first,
exclusive dispatch second, then add a zero-wait session-owned account preflight lease
`Sati.ClearinghousePreflight:{agencyId}:{accountId:N}`. The account lease excludes parallel probes
of different queued rows for one failing account across hosts. Hold it through key preparation
and the short final admission decision; release before the physical upload. Key resolution and
receipt-key wrapping remain outside SQL transactions. ClaimReleaseWriteScope remains the first
SQL decision lock, ahead of period/service locks where used. Reopen takes reset, account lease,
then common admission and never waits for a dispatch lease; receipt processing needs no account
lease. No reverse account/common acquisition is permitted.
The account lease adds one open SQL session during preparation. Existing reset/dispatch session
leases still hold connections over key/network work; this proposal establishes no aggregate
connection, dependency-time or capacity bound. Measure and budget those separately under W8.

A classified failed probe reloads the account, its revision/reference, readiness revision and
dispatch under fresh common admission. Verify the exact retained candidate remains Queued,
known-unsent and unchanged, with no attempt/physical receipt or transmission evidence, before
committing readiness plus audit. Otherwise discard the stale disposition and let current release/
reconciliation facts govern. Do not cancel a valid queued claim because a key is temporarily
missing. Recheck the gate and caller token before decision work; failed/cancelled admission commits
no success or deferred state. A lost failure-commit reply is resolved from readiness/revision,
without an immediate extra probe. Lease loss stops stale writes before commit or upload; the
implementation must supply provider proof rather than assuming an application lock is fencing.
After successful preparation, reload the exact source/account/readiness and run all existing
R1/R2, correction-purpose and financial-review guards before committing Sending. Single-attempt
upload and result/receipt ordering from DEC-0233 remain unchanged.

**Visibility and controlled reopening.** Extend the clearinghouse workspace with non-secret
readiness, failure count, next due time and fixed reason per account, without changing the claim's
Queued/financial status. Use text and accessible automation labels. Proposed same-agency Admin
reopen command carries expected readiness and account revisions; actor/permissions are revalidated
under admission. Resolve the current key and verify receipt protection outside SQL, then repeat
authority, binding and revisions before atomically resetting Ready with an append-only human
audit. Stale/repeated commands return a safe conflict with no second effect; clients refetch and
review rather than blindly retry. Reopen only resumes ordinary evaluation of retained Queued
work. It never creates a new original, changes bytes, clears delivery history or modifies
Sending/OutcomeUnknown. Changing account configuration or adding a new queue is not an implicit
reopen. Document the route in API_AUTHORIZATION and fixed defer/held/recovered/reopened audit
actions in AUDIT_EVENTS when implemented. Worker audits use SystemActor.UserId; safe diagnostics
contain IDs, counts, fixed code/job and exception type only. Alert activation stays separate.

**Migration, rollout and rollback acceptance.** After approval, author only additive source
migration/model changes and safe scripts using the controlled migration process. Prove apply
from the prior schema, existing Ready-by-absence behavior, constraints, malformed/foreign writes
and replay/history preservation in an owned private synthetic SQL instance. No working/cloud
database is part of source approval. Disable dispatch on every old/new host during rollout;
verify all active worker versions honor readiness before enabling transport. An old worker ignores
this table, so mixed-version active dispatch and rolling back to it while Deferred/Held work
exists are unsafe. Keep additive storage on rollback, pause dispatch and review readiness/queued
work; do not drop unresolved scheduling evidence or transform it to CancelledBeforeSend.
Reactivation requires owner-reviewed reconciliation, supported code/schema and unchanged
uncertain-send quarantine. No infrastructure or release authority follows from this design.

**Required fail-first and passing acceptance after approval:** promote the opt-in reproducer to
an ordinary regression; A's first classified failure defers the entire account, B uploads exactly
once on the next turn, A stays Queued with zero attempts, and retained bytes/effects remain scoped.
With fake time prove exact due boundaries, four delays/jitter bounds, fifth-failure hold, same
account multiple queued rows, successful deferred recovery and explicit reopen after restoration.
Test host disposal/recreation, independently constructed SQL hosts, cancellation/disablement,
actor/account/revision changes between preparation and commit, unavailable receipt keys,
unknown/shared failures, failed readiness/audit commit and replay/repeated reopen conflicts.
Use actual SQL barriers for per-account exclusion/reset/reconciliation, stale owners and lock
cleanup. Preserve duplicate/compliance/void/late-receipt and Sending/OutcomeUnknown regression
coverage. Private migration/constraint proof is distinct from deployed migration, sustained load,
live vendor/quota evidence and general healthy-agency latency.

**Concrete next step:** Josh reviews the proposed recovery policy and additive source schema;
implementation is conditional on that approval under §8. This supersedes the missing proposal
status in the blocker above, not its unimplemented runtime status or the broader W8 acceptance.

**Problem.** Agency authorization and same-agency billing account/file linkage protect identity
and record integrity. Agencies still share API CPU/memory, SQL/connection capacity, worker
selection, and some external-service budgets. Agency or clearinghouse IDs do not create dedicated
Azure resources. A separate worker per agency is one option to evaluate; it does not by itself
isolate shared SQL, key/storage/mail services, or vendor quotas.

**Already covered — extend these owners rather than rebuilding them:**

| Existing work | Evidence and remaining distinction |
|---|---|
| Structural tenancy | `AGENDA.md`, "Tenant model"; `SATI_STRUCTURAL_REVIEW_2026-09-28.md` S-2. Explicit predicates and selected composite tenant keys exist. General query-filter/RLS/database-tenancy strategy remains planned; no current cross-tenant exploit was demonstrated by the October 8 assessment. |
| W2 maintenance safety | Shared rules, 100-candidate agency batches, revision-checked writes, atomic system audits, catch-up on wake and cross-host/reset coordination exist. Bounded batches do not establish failure isolation or a maximum wait for later agencies. |
| Clearinghouse integrity | Same-agency account/generation links, retained dispatch intent, per-dispatch leases, independent feed cursors and uncertain-send quarantine exist. `ClaimMdSandboxCoordination` intentionally coordinates a global request budget and one poller across hosts; rate safety does not establish agency fairness. |
| Resource and operations backlog | `AGENDA.md` already tracks summary projections for fat loading, distributed sign-in guard state, watchdog activation and restore evidence. SATI-SEC-010 in `SECURITY_AUDIT_2026-09-03.md` already proposed request/parser limits and per-actor/IP rate/concurrency partitions with bounded queues. `SECURITY_REVIEW_2026-09-10.md` B11 records body/X12 limits implemented, with expensive-operation per-user limits still a platform gap. Recheck current source. Validated agency admission, worker starvation and interactive-latency acceptance need explicit coverage. |

**Source paths to reproduce before claiming a fix:**

- `Sati.Api/Infrastructure/ClearinghouseDispatchWorker.cs` selects the globally oldest Queued
  dispatch and awaits upload. Secret/key preflight failure before Sending can leave that row
  Queued and selected again, delaying healthy agencies.
- `ClaimMdSandboxPoller.cs` visits ordered accounts serially. It already catches per-feed
  exceptions; slow exchanges can still delay later accounts. `ClaimMdSandboxCoordination.cs`
  holds the shared request lease through pacing and HTTP. Verify the vendor's actual quota scope
  before changing that lock. The local connector deadline above now covers HTTP body I/O after
  `ResponseHeadersRead`; admission/processing/cleanup, whole-account/pass bounds and live
  provider deadline proof remain open.
- `NoteAbandonmentWorker.cs` still visits agencies in ID order under the global sweep lock.
  The October 8 slice above isolates only classified recoverable sweep failures; unknown/shared
  failures still stop the pass. Agency discovery pages and prior-day cache retention are now bounded;
  current-day capacity, total-pass budgets and healthy-agency wait
  bounds remain open. Preserve `NoteAbandonmentSweep` transaction/audit rules.
- `SignatureProcessingService.cs` already advances its package scan beyond damaged rows;
  `Sati.Signatures/SignatureMailWorker.cs` has durable leases, due times and bounded retries.
  Preserve those controls while checking fairness across agencies and notification types.

**Build:**

1. Inventory each worker and expensive API workflow: trusted agency/account owner, due selection,
   query/page/batch limits, lock keys and order, held SQL connections, dependency quotas,
   deadlines, retry/uncertainty states, feature flags and continuation after restart. Map shared
   App Service, SQL, Function/reset, key, storage, mail and vendor capacity using dated evidence.
2. Define measurable service bounds: eligible healthy agency B gets a turn within a configured
   number of agency turns; define a deadline and maximum concurrency for each dependency and a
   total run budget. Bound query materialization and tenant bookkeeping as agency count grows.
   Rotate or fairly select agency/account/feed lanes; preserve ordering only where required.
3. Isolate recoverable agency/account failures, use bounded backoff for work proven unsent,
   continue healthy lanes, and propagate cancellation. Distinguish an agency fault from a shared
   dependency outage. Cap total concurrency and queued work across hosts as well as per agency;
   prioritize capacity needed for interactive case management. A process-local semaphore alone
   cannot enforce a limit across hosts.
4. Classify each lock/quota as deployment-wide, tenant/account, or record-specific. Keep Demo
   reset exclusion, single-attempt external send and necessary vendor-wide limits. Narrow locks
   only with evidence of independent ownership/quota and SQL multi-host regression tests.
5. Add admission and concurrency limits for expensive authenticated API paths, derived from
   the validated stored actor/agency, with predictable bounded overload responses. Keep existing
   authorization/revocation checks; profile their SQL cost before calling authorization a
   serialized bottleneck or introducing a cache that could weaken revocation.
6. Expose content-free queue depth/oldest eligible age, last progress, lease wait, processing
   duration, backoff/quarantine and budget rejection. Keep agency-level diagnostic access scoped;
   avoid unbounded metric labels, narrative/payload/secret/vendor-response logging. Extend W1/W4
   outcomes/runbooks with stalled agency/feed progress and a named owner, preserving D1–D4.
7. Compare a fair shared pool, partitioned pools and selected dedicated hosting/database options
   against measured capacity, costs and service objectives. Record the choice in `DECISIONS.md`;
   infrastructure proposals remain for Josh. Queue/session-per-agency is not proof of fairness.

**Accept when:** synthetic A/B agencies demonstrate bounded B progress while A has a sustained
backlog, missing key, slow headers/body, repeated recoverable failures or throttling. Test note
sweeps and signature work as well as dispatch/polling. Define the scheduling bound before the
test; do not rely on an arbitrary sleep or eventual completion. With fake time and deterministic
barriers, test cancellation, feature disablement, agency/account eligibility changes, restart,
expired leases and recovery. Across two independent API hosts with disposable SQL Server, prove
same-work exclusion, reset/reconciliation safety and aggregate limits. No cross-agency bytes,
credentials, cursor effects or audits; no resend of Sending/OutcomeUnknown after timeout or
failed evidence commit. Measure healthy interactive latency under a defined synthetic noisy
neighbor workload, including query/connection pressure. Regression tests must fail against the
original implementation; retain failing and passing evidence plus commands, counts and skips.

The October 8 assessment's billing R1/R2 (another original generation after accepted dispatch;
incomplete current documentation/compliance recheck before send) are separate existing findings.
Link their remediation and preserve billability/uncertainty gates; fairness changes do not close
them. Watchdog activation, actual alert delivery and actual vendor/cloud acceptance remain
distinct from source tests. A schema requirement needs a proposal and §8 approval; this W8 entry
does not authorize a migration, deployment, live vendor call or Azure configuration change.

---

## 6. Considered and deliberately not built

- **Annual compliance-cycle preparation job.** `GET /caseload` creates obligations through the
  next cycle (`ApiEndpoints.cs:1953-2042`), so they exist about a year ahead of any load,
  including a supervisor's. Billing already treats a missing row as incomplete
  (`ExpectedBillingComplianceObligations.IncludeMissingForms`). The only gain would be taking
  writes out of a GET. Revisit if that becomes a measured problem.
- **Deadline and overdue digests.** This is a new feature, not a missing job. No channel for
  notifying staff exists outside the signature portal, and emailing deadline content raises the
  PHI-in-email questions in `REGULATORY_CONCERNS.md`. A channel decision comes first.
- **Statistics aggregation job.** No measured performance problem.
- **Automated retention or deletion.** Blocked on the legal-hold prerequisites in
  `OPERATIONS.md` "General retention prerequisites."

## 7. Related defects outside this brief

- **Comprehensive Assessment submit after a failed save.** `SaveAsync` swallows the failure
  (`ViewModels/ClientDocuments/ComprehensiveAssessmentViewModel.cs:259`). `SubmitForReviewAsync`
  submits anyway (`:275`), and the server accepts the older stored document because the revision
  did not change. The completeness rule exists only in the ViewModel. The feature is off by
  default. This is a bug fix, not a worker, and has its own suggested task.

## 8. Not authorized by this brief

Running a migration against any database; publishing the Function App or API; creating or
changing Azure resources, role assignments, firewall rules, or app settings; touching
`SatiProduction` data; replaying a poison message; and resubmitting a claim or notification.
`invoke DATT!` does not extend to any of these beyond what `RELEASE_PLAYBOOK.md` already allows.
A schema change needs Josh's approval and the controlled migration process in
`DATABASE_ENVIRONMENTS.md`. The pattern in §3 is designed so none of W1–W5 needs one.
W8 may require a schema proposal; its scope does not waive that approval or D4.

## 9. Done means

- Code, fail-first tests, and a green full solution test run.
- `ARCHITECTURE.md`: the new rule owners and workers, and where each runs.
- `DECISIONS.md`: anything that departs from D1–D4, the system-actor constant's final name, and the
  abandoned-envelope policy.
- `AUDIT_EVENTS.md`: every new action. `API_AUTHORIZATION.md`: any new or rescoped route.
- `OPERATIONS.md`: the alert owner, the conditions, and the operator response for each.
- `AGENDA.md`: anything deferred, with the reason.
- W8: declared scheduling/capacity bounds, synthetic A/B failure and load evidence, and
  SQL multi-host results; distinguish source verification from owner-run live acceptance.
