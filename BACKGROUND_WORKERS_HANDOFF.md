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
The later [idle/wake implementation](#october-9--dispatch-idle-wake-implementation) supersedes
only that pending cadence work. Remaining W8 includes fairness under sustained backlog/contention, aggregate sessions/dependency budgets,
API admission, configuration consistency and owner-run operational/alert evidence.

#### October 9 — dispatch idle-wake implementation

**Status:** SATI-WRK-001 approved scheduling slice implemented locally; unreleased, default-off.
[DEC-0237](docs/decisions/current/2026-10-09-DEC-0237.md) records adoption and scoped supersession.
The retained [policy below](#october-9--dispatch-idle-wake-scheduling-proposal) owns the exact
numeric cadence and allowlist. Its dated inventory describes the pre-change source, not current
runtime. No deployment, schema change, activation, client heartbeat or version bump was performed.

**Ownership and integration:** constructor-injected singleton `ClearinghouseDispatchSchedule`
owns monotonic activity, turn spacing, cooldown and coalesced generation/wait registration. The
existing hosted worker remains the only runner. Fixed scalar state carries no tenant/account
payloads. Gate and cancellation checks precede turns. `ProcessTurnAsync` distinguishes Disabled,
Processed, Empty and Contended; the old bool wrapper remains only for direct-call compatibility.
The worker obtains one scalar empty-selection snapshot combining newly eligible Queued work
with the earliest future Deferred time joined to same-agency Queued work. SQL still owns current
readiness and every account/reset/dispatch lease. Stale snapshots authorize only another read.

Login and billing workspace handlers publish after their existing validated successful work.
Queue/replay success is inspected outside the entire owned queue workflow, so its transaction,
write scope and recovery scopes dispose before notification. Admin reopen signals only after
account coordination returns and releases its lease. Denials, rollback, failed/stale reopen,
renewal, generic reads and health do not signal. No API route, network DTO or authorization scope
changed. Notifications perform no SQL and never await dispatch completion.

**Waiting behavior:** startup gets one ordinary catch-up without granting activity. After activity
expires, the loop parks on an in-memory signal, without due/reconciliation SQL or active timers.
Active waits use the relevant UTC due instant and monotonic reconciliation/expiry. A backward UTC
jump can wake an old timer, but eligibility is recomputed in memory before any early database
turn; reconciliation still permits a bounded fresh read while active. In-flight work retains its
existing cancellation/receipt/uncertainty rules; expiry does not cancel an upload or renew activity.
Shared/unclassified faults and contention enter the local cooldown, unaffected by hint floods.
Immediately before admission, the owner takes a fresh generation/activity/monotonic timestamp
under its lock and records that same timestamp as the turn start. A pause during the earlier
wait decision cannot admit a new turn from an expired snapshot. Already admitted work may finish
later under the retained in-flight rules; admission counts are not measured physical SQL timing.

**Verification and limits:** [the working ledger](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-dispatch-idle-wake-implementation)
owns executed checks and retained failures. The real pre-change hosted idle loop failed the new
regression by starting a second database turn. Fake-time tests exercise the runtime scheduling
owner, actual hosted SQLite/private SQL turns and actual validated API publishers. Negative
mutations check pacing and reopen notification order. Existing SQL multi-host/reset/account and
billing compliance/uncertain-send regressions remain required. Counting turns around the actual
reset-owned database callback proves no subsequent dormant callback; it is not instrumentation
of every EF command, provider session, CPU cost or query plan. No measured aggregate resource,
fair-wait, sustained-load, live vendor or intended-host acceptance follows from these tests.
The broad verification exposed expired token reuse in the shared test fixture after a host
wall-clock discontinuity. A fail-first fixture regression and cache-expiry check ensure a new
test client signs in again; existing-client headers and every product authentication control
remain unchanged. The ledger retains the aborted run and final acceptance separately.

**Remaining dependencies and next slice:** broader W8 aggregate/tenant budgets, fair selection,
request admission, full operation/dependency bounds, consistent configuration, query plans and
owner alert/load evidence remain open. The next slice at this checkpoint was a bounded fairness
inventory/design, subsequently adopted and implemented in
[task 1.3.2](#october-9--dispatch-fairness-implementation--task-132). The agenda owns the current
next slice. Do not activate or replace the Demo inactivity
tradeoff with unattended polling; future cloud Production requires its own reviewed wake policy.

#### October 9 — shared SQL/session admission design task 1.4.2
**Main review — October 9, source `9132a59`:** task 1.4.2 is complete as a reviewed/integrated
proposal. The retained investigator text below describes its original handoff state and source.
[DEC-0242](docs/decisions/current/2026-10-09-DEC-0242.md) registers the proposal with **proposed**
status; no backend, numerical budget or runtime admission policy is adopted. The accounting,
protected classes, no-SQL waiting, conservative nested cost, uncertainty and fencing requirements
are useful planning constraints. Fixed slots are a candidate only: intended-host exclusivity,
restart/rolling overlap, complete enrollment, external consumers and pooled sessions are unproved.
Configuration identity or a process lease alone cannot justify reusing unresolved SQL credits.

Current dispatch/polling corrections are in [the integrated inventory](#october-9--resource-limit-inventory-task-141).
Polling discovery is now bounded/durable, but reset/poller sessions still span multiple agency
feeds. Its new selector does not satisfy this proposal's dispose-before-agency-admission contract.
Dispatch selection is short, but initial reset/discovery still needs enrolled unscoped admission;
none of the selectors supplies shared resource credits. Note review preserves pass/cache gaps.
Signature hosted reset participation and mail's claim-before-capacity are prerequisites; package
and mail revocation transaction spans must remain accounted until a replacement safety proof.

**Disposition:** preserve the proposed vector/accounting and eight deterministic acceptance
families. Defer backend/value adoption and runtime implementation pending an explicit bounded
hosting/consumer/cost/enrollment review; no arbitrary defaults or global-cap claim. Independent
signature fairness/source-safety design can proceed as task 1.3.3.5 while those requirements remain
open. This review did not run application tests, access an existing database or change hosting.

**Retained investigator proposal:**

**Status:** proposed; awaiting main-thread review and policy adoption. Josh explicitly assigned
SATI-WRK-001 **1.4.2** to this second investigation thread. This is a documentation contract,
not an implemented limiter or accepted architectural decision. The preceding
[1.4.1 inventory](#october-9--resource-limit-inventory-task-141) remains awaiting review;
[dated working evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-task-142-shared-sqlsession-admission-design)
owns this proposal's exact source identity, input capture, checks and limits. Main 1.3.1 owns fair
selection; 1.3.3 owns broader worker turns, 1.5 owns API workload integration and 1.6 owns combined
acceptance. No host count, connection allowance, provider quota or latency is measured here.

**Accounting contract — recommended for review.** Count a **checked-out SQL connection** from
the beginning of an open attempt until verified close/disposal completes, including connection
establishment, application-lock wait/hold, transaction, provider wait and cleanup. Count explicitly
opened lease connections separately from EF contexts: an idle context is not a checked-out
connection, and several nested owners may open connections at once. Reserve an operation's
conservative maximum simultaneous count, K, before its first open; retries/replacement connections
must fit K, including any unresolved previous open. A connection that cannot be confirmed closed
keeps its credit charged. This bounds enrolled concurrent connection use; it does not bound
SQL commands, query cost, duration, idle pooled physical sessions or all database clients.

Returning a connection to a pool does not prove the server's physical session disconnected.
If physical-session protection is also required, adopt a separate P envelope for the sum of pool
maxima across every process/connection-string/identity pool, nonpooled connections, administration
and reserved external consumers. Pool configuration/rotation/rolling overlap must be inventoried
without exposing credentials. A per-process pool maximum or agency lock cannot establish a
deployment-wide P. No explicit pool envelope is present in the inspected source.

| Proposed symbol/unit | Required invariant and ownership |
|---|---|
| C: total concurrent checked-out connection envelope for one exact environment/database target | C = I + B + R + E. I is protected interactive capacity, B is worker capacity, R is separately protected reset/recovery/control-plane capacity, E is a conservative external-consumer allowance. Sum of enrolled reservations plus E must not exceed C. Unknown/unbounded outside consumers prevent claiming a database-wide cap. Demo/Production never share an admission namespace or credits. |
| K: one operation's maximum simultaneous connections | Includes outer reset/record/account/provider sessions, selection/business contexts when open, transaction/commit and cleanup. Whole-operation reservation persists across non-SQL phases and cannot be expanded by a nested owner. An operation with unknown K is ineligible for the promised cap until audited; observed minimum lease count is not K. |
| A: aggregate worker allowance for one validated agency | Weighted reservations for that agency across hosts/workers do not exceed A, within B. Account-specific exclusion remains separate; an account ID supplies no extra capacity. Interactive per-agency limits require 1.5 design and cannot be impersonated by worker calls. |
| Q, Q_a and M: total pending admission entries, entries for an agency and active agency bookkeeping entries | Finite registered limits; count waiting calls and queued metadata, not durable business rows. No new dictionary entry after M is reached; remove an entry only when it has no waiter or live reservation. Q_a constrains one agency's queued demand; over-limit attempts return a bounded busy result. |
| D_admit: admission wait deadline | Monotonic injected time, linked caller cancellation and gate checks. No database connection, transaction, blob/payload or business lease while waiting for resource admission. Expiry removes the waiter exactly once. A finite wait is not a guarantee of eventual service. |
| P and dependency budgets | Physical pool/session, SQL-command/work, CPU/memory, key/storage/mail/vendor concurrency/rate and whole-operation time need distinct owners/limits. Holding K bounds concurrent SQL use during a dependency stall, not the stall or provider quota. Adopt their values and acceptance in later bounded 1.4 work. |

**One logical admission owner, proposed API contract.** Use constructor injection of a server
infrastructure owner (suggested name `IWorkloadAdmission`, not an existing interface). It accepts
an exact target, compatible configuration epoch, workload class, stored/validated agency or
explicit unscoped-discovery class, and a registered operation kind with fixed K. K and class are
server-owned metadata; callers cannot lower cost, call themselves interactive/recovery, supply
an arbitrary target or acquire multiple account shares. Authoritative eligibility/billing rules
remain in Contracts and their current persistence owners.

A grant atomically accounts for the whole K in total, class and agency bounds; otherwise the call
waits in its bounded queue or returns typed `Busy`, `WaitExpired`, `Cancelled`,
`Disabled` or `ConfigurationUnavailable`. Grant-versus-cancel races have one terminal owner.
The disposable reservation carries an opaque owner/generation and tracks nested SQL opens.
Children consume its remaining connection credits; they never reacquire the outer budget.
Opening K+1 is refused before opening SQL. Releasing twice is harmless; release before verified
disposal is refused/quarantined. Retrying an uncertain admission request uses the same identity
to recover its grant, not create another. A failed/unknown close or stale owner cannot donate
credits to a replacement operation.

Workers use **try-admission with no parked waiter by default**; a denial leaves durable work due
and releases discovery/selection state. Existing bounded scheduler opportunities may retry;
denial does not create a faster SQL loop, renew dispatch activity, count a missing-key failure,
clear readiness or mutate Queued/Sending/provider outcome. An interactive waiting policy may
use finite Q/D_admit under 1.5 review. Business queue length, fairness/continuation and client
retry/rate limits remain separate; resource admission alone cannot promise queue drain or F turns.

**Cross-host mechanism and alternatives.** Recommend **fixed process-slot envelopes** as the
first implementation candidate, with no borrowing or automatic reassignment. This is conservative
and avoids a per-admission SQL coordinator connection or an idle coordination heartbeat.
A reviewed finite slot manifest gives each permitted process incarnation h a C_h, I_h, B_h,
R_h, A_h, Q_h, Q_ah and M_h. Enforce:

- each C_h = I_h + B_h + R_h, with no borrowing between classes;
- sum(C_h) + E <= C; sum(B_h) <= B; sum(I_h) <= I; sum(R_h) <= R;
- for every agency, sum(A_h) <= A, with agency reservations also charged to B_h;
- sum(Q_h) <= Q, sum(Q_ah) <= Q_a and sum(M_h) <= M.

One atomic local allocator enforces each slot's vector; all workers and enrolled API opens use
it. Slot counts include scale-out, multiple processes on a host, rolling overlap and old versions.
One process-local semaphore per worker or multiplying a per-host limit by an observed host count
is insufficient. Startup must have exclusive, enforceable ownership of a non-reused slot and
the agreed epoch **before any protected SQL open**, including startup/health/authentication.
A token in configuration alone cannot enforce exclusivity. Main integration must choose and prove
the slot ownership/fencing mechanism against the intended hosting model; none exists in this
proposal or inspected source. If the platform cannot enforce a finite set of exclusive slots,
this candidate cannot claim cross-host limits or proceed to activation.

Fixed allocations strand idle credits and divide an agency's allowance among hosts. If K does
not fit a slot or no positive compatible A_h is available, route eligible work to a reviewed
slot or deny it; do not round shares up or silently borrow. They provide safety caps, not global
work conservation or fairness across hosts. Main 1.3.1/1.3.3 must account for placement and these
conditions when defining progress.

An alternative is an **external strongly consistent admission coordinator** atomically granting
total/class/agency credits and bounded waiter slots, outside the protected business SQL pool.
It can share unused capacity dynamically, but adds availability, authorization, state-size,
idempotency and recovery dependencies. Its own connections/CPU must be independently bounded.
A coordinator outage rejects new grants; an unknown grant/release stays charged. Automatic lease
expiry does not prove old SQL connections or provider work stopped: reclaimed credits require
verified owner fencing and closure, otherwise safety wins over capacity recovery. Reviewing a new
service/deployment needs separate authority. Neither Redis-like TTL counters nor a lease clock
alone establish that proof.

Using **SQL as the budget coordinator** requires a reserved, bounded control-plane open before
the business grant and explicit connection accounting. Existing sp_getapplock waiters already
open SQL before admission; reusing them as a free C limiter is circular. A durable SQL ledger
would also need atomic accounting, bounded queues and stale-owner proof. It is an alternative,
not selected here. Independent worker semaphores, pools per agency and resource-key proliferation
are rejected as substitutes: none alone caps shared target use. Main review must adopt a backend
and numbers in a registered decision before implementation; this subsection changes no boundary.

**Admission and existing safety order.** Perform a bounded discovery read under its own unscoped
class reservation when the agency is not yet known. Close/dispose it and every discovery lease,
then request the complete agency operation reservation. A discovered ID is only a hint:
reacquire current reset/record/account/agency decision owners and revalidate scope, eligibility,
revision, gate and caller cancellation before any effect. Never hold a discovery connection or
SQL lock while awaiting another resource reservation, and never upgrade a partial reservation
inside a transaction. Two-phase discovery requires main fairness/safety review and source changes;
it is not a claim that present global selections already obey A.

Within a granted operation, retain the current lock order: Demo reset first; dispatch then account
preflight for dispatch; common serializable claim-release agency lock first within its decision
transaction before narrower decision locks. Reopen retains reset → account → agency and never
waits for a dispatch lock. Note reset precedes its sweep lock on their same connection. Provider
serialization remains authoritative. Existing SQL/provider lock waiting happens **inside charged K**;
it is distinct from the no-SQL resource admission queue. Do not shorten required safety spans
or change the existing Claim.MD 120-second wait to make a capacity assertion.

Reserve capacity needed to finish an irreversible attempt at initial grant, including protection,
evidence writes/rollback and cleanup. Cancellation/disablement stops new attempts, not their
accounting or necessary outcome retention. Release credits only after inner transactions,
commands and connections finish/dispose, followed by outer leases; admission release is last.
No deadline or budget rejection justifies upload replay, evidence loss, incorrect cursor advance,
premature mail completion or changing a revocation decision.

| Current source path | Required integration boundary and preserved safety |
|---|---|
| [Dispatch](Sati.Api/Infrastructure/ClearinghouseDispatchWorker.cs), ProcessTurnAsync / PrepareUnderAccountLeaseAsync / UploadAndRetainAsync; [schedule](Sati.Api/Infrastructure/ClearinghouseDispatchSchedule.cs) | Separate bounded unscoped candidate discovery from the reviewed 1.3.1 agency turn; reopen/revalidate under current owners. Cost preparation and upload/evidence phases, including reset + dispatch + account or provider connections and independently open business contexts. Three explicit leases during HTTP are a lower-bound fact, not an adopted K. Grant must cover required post-Sending CancellationToken.None protection/retention before allowing Sending. Busy maps to a distinct scheduling outcome, preserves intent and cannot renew activity. |
| [Polling](Sati.Api/Infrastructure/ClaimMdSandboxPoller.cs), PollOnceAsync / PollAccountsAsync; [provider admission](Sati.Api/Infrastructure/ClaimMdSandboxCoordination.cs) | Current reset and poller sessions span unpaged discovery and all accounts. A conservative global pass cap is possible, but does not implement agency shares. Before per-agency admission, 1.3.3/later finite-pass design must review paged account/feed continuation and safe lease release/reacquisition. Never await A while holding the global pass's SQL leases. Preserve request serialization, account/feed binding and atomic cursor/effects; no provider calls or cursor effects on denial. |
| [Notes](Sati.Api/Infrastructure/NoteAbandonmentWorker.cs), RunDueAsync / SqlNoteAbandonmentCoordination | runGate precedes one connection carrying reset + sweep locks across discovery/all agencies; sweep transactions add separately checked-out connections. A pass reservation can cap concurrent SQL but cannot claim fair per-agency service. Review bounded turns/continuation and global-lock scope before agency grants; denied/partial work cannot mark allCompletedOn or failed agencies complete. Preserve revision/audit transaction, completed-day zero-SQL behavior, next-day eligibility and default-off gating. |
| [Signature processing](Sati.Api/Infrastructure/SignatureProcessingService.cs), ProjectCompliance / PreparePackages; [package worker](Sati.Signatures/SignatureCompletionWorker.cs) | Admit discovery separately and each stored-agency item before its owned transaction. Package transaction spans blob/PDF/key work, so reserve that connection for the entire span; nested opens must fit audited K. Preserve immutable/idempotent package evidence and retained orphan review. Resolve the hosted service's missing shared-reset participation as a separate safety dependency; no live reset race is asserted and no transaction shortening is authorized here. |
| [Signature mail](Sati.Signatures/SignatureMailWorker.cs), ClaimAsync / PrepareSubmissionAsync / SubmitAsync / StoreAsync | Avoid claiming a row or incrementing an attempt just to wait for resource capacity; a reviewed stored-agency candidate hint and subsequent conditional claim are needed. Reserve before durable five-minute lease/operation-ID work. Keep final serializable revocation checks and send span, stable GUID, stale-owner checks and later GET-only recovery. Cancellation/unknown release cannot free credits while the sender/SQL is unresolved; denial must not consume the five-attempt budget. |
| [API registration/middleware](Sati.Api/Program.cs), [stored actor filter](Sati.Api/Security/TenantAccess.cs), [Demo mutation lease](Sati.Api/Infrastructure/DemoMutationLeaseMiddleware.cs) and outside consumers | Current mutation middleware opens a direct connection before endpoint stored-actor validation. Use bounded unscoped interactive/authentication admission before that open; a JWT/caller agency cannot authorize agency accounting. Later validated endpoint work needs reviewed nonblocking transition or release/reacquire/revalidation, never nested waiting under reset. Protect I without allowing expensive jobs to misclassify themselves. GET/health/startup, portal, reset/Function/watchdog and provider users must be enrolled or separately bounded in E/R/P. API routes/status/quotas are 1.5 work, not added here. |

**Failure, restart and configuration semantics.** Gate/identity/epoch checks precede admission
and repeat immediately before protected work. Never fall back to unbounded execution on missing
configuration, unknown cost, slot contention or coordination error. A disabled feature keeps
existing uncertainty/evidence handling and charged in-flight work until disposal. Cooperative
D_admit expiry owns waiting only; phase/connect/command/provider/evidence/cleanup deadlines need
separate later design. A hung operation may retain capacity indefinitely; this proposal cannot
promise wall-clock availability or forcible safe interruption.

Host shutdown removes pending waiters, drains admitted work under current uncertainty rules and
keeps unresolved credits/slot ownership quarantined. A restart, partition, clock jump or expired
mail lease cannot make the old process's SQL/session accounting reusable. Fencing must stop all
old opens and establish closure before slot reuse; logical generation rejection alone does not
close a physical session. If that proof is unavailable, operator recovery is required and the
envelope loses availability, not safety. Operator action/schema/cloud changes remain separately
authorized. Reset needs its own protected R reservation before exclusive reset admission; it
cannot bypass C or deadlock by waiting for capacity held by its own nested owners.

One versioned configuration defines target, slot manifest, operation costs, queues, class/agency
shares and compatibility. Mixed unbudgeted hosts invalidate an aggregate claim. Reducing a limit
below live allocations stops new grants until draining proves compliance; it does not revoke
safety-critical work or label the lower cap already met. Pool/identity rotation overlaps count
both old and new pools under P. No heartbeat/poll of SQL is added while Demo is dormant (D2);
recovery verification and hosting/backend choice must respect that boundary.

Content-free bounded metrics later report reservation/open/waiter counts by workload, busy reason,
wait age, unresolved cleanup and configuration epoch; agency visibility remains authorized and
bookkeeping bounded. No payloads, narratives, keys, connection strings or full provider errors.
Alert destination/receipt and operational acceptance stay with D1, operations and 1.7.

**Deterministic acceptance plan — proposed, not executed here.** Register positive symbolic
budgets and audited costs, use barriers/fake time, and retain an unfixed-code failure for each new
runtime guard. Existing context factories, SQL command/connection interceptors, injected
coordination, dispatch fake-time tests and package/mail mocks are seams from 1.4.1; the admission
owner, slot controller, universal open accounting and hosted signature clock seam do not exist yet.

| Case | Required assertions and dependency |
|---|---|
| Weighted grants and nested opens | Two independent slot allocators grant differing K up to sum(C_h); next grant opens no SQL. All partial-vector failures roll back total/class/agency counts; K+1 child open fails before provider open. Count open attempts, outer leases, business transactions and concurrent retries separately. After verified disposal exactly the released weight is available; duplicate release cannot inflate it. |
| Interactive and agency boundaries | Fill B/B_h and A/A_h across hosts using two agencies; I remains available to an authorized interactive operation fitting I_h. Caller account/agency/class spoofing cannot gain a grant. A's rejected attempt changes no counters; B can acquire when its agency and shared class limits permit. Agency maxima are not minimum reservations for every agency. Fair progress is conditional on adopted 1.3.1/1.3.3 placement/turn policy; no arbitrary elapsed-time or global F assertion. |
| Finite pending state | At Q/Q_a/M boundaries reject the next entry without connection, payload retention or new map growth. Barrier a grant/cancel race and exact D_admit fake-time expiry: one outcome, no abandoned count or duplicate grant. Worker try-admission parks no waiter; queue removal and idle-map eviction preserve outstanding ownership. |
| Failure at every acquisition/disposal step | Fault connection establishment, app-lock acquisition, transaction commit/rollback, command/connection disposal and outer release. Confirm charged maximum and uncertainty retention; unknown close quarantines credit. Stalled cleanup cannot produce a replacement open exceeding C. No new work consumes protected I/R to bypass B. |
| Restart/partition/rolling overlap | Old and new hosts compete for the same slot/epoch; the newcomer performs zero protected opens until fencing/closure is proved. Expire control/mail leases and jump wall time while old SQL is barrier-held: no capacity credit is reclaimed. Coordinator unknown grant/release is idempotently recovered or retained; mismatch/missing configuration rejects new work. |
| Worker safety on busy/cancellation | Before Sending/claim, denial changes no readiness failure, mail attempt, cursor, completion cache or immutable evidence. After physical-send initiation or committed provider GUID, cancellation preserves upload uncertainty and GET-only mail recovery, never a second POST. Barrier staff revocation at final send preserves ordering; poll effects/cursor and note writes/audits commit together or neither. |
| Complete consumer/physical accounting | Instrument factory and direct middleware opens across hosts, including auth, health, recovery and outside consumers. Verify unscoped discovery disposes before agency admission and current scope is rechecked. Separately inventory pool keys/maxima and rolling/credential overlap for P; logical mock counts cannot prove physical-server sessions or E. Actual SQL/host observations need separate disposable/intended-host authority. |
| Idle and overload recovery | At dispatch inactivity/completed note day, no admission timer triggers SQL. Release capacity then exercise only an existing eligible wake/turn; work remains due without faster polling. Adopted fair selection and resource admission jointly preserve healthy B, scoped diagnostics and cleanup while A fails. Numeric latency/load acceptance is later 1.6 evidence. |

**Review order and next bounded step.** Main integration first reconciles 1.4.1/this proposal with
current source and 1.3.1, then reviews the logical unit/reserves and fixed-slot versus external
coordinator tradeoff. Resolve exclusive slot enforcement, complete consumer/cost inventory and
signature/reset safety before backend adoption. Define polling/note/mail turn prerequisites and
API 1.5 ownership; obtain separately authorized hosting/pool/provider facts before choosing numbers.
Record the adopted choice in an unused registered DEC record through main integration. The next
bounded slice is **admission-contract/backend adoption review**, followed by a separately assigned
single-owner implementation with fail-first synthetic proofs. This does not activate another
numbered task, alter the main pointer, migrate/deploy or declare 1.4 complete.

#### October 9 — resource-limit inventory task 1.4.1
**Main integration review — October 9, source `72cec51`:** task 1.4.1 is complete for
source/documentation inventory integration. The investigator text below is a retained dated
handoff; its awaiting-review wording describes its original state. Its 550-file source digest
still matches the captured worktree. Nine existing source paths differ from main and eight
source paths were added, all in reviewed dispatch/polling integration; note/signature dependency
owners are unchanged. The main ledger records exact identity and drift. Read this baseline
matrix with the following current-source corrections, not as a current defect list:

- Dispatch now has [durable agency/account rotation](#october-9--dispatch-fairness-implementation--task-132),
  a bounded short selector disposed before send admission, distinct lane-skip versus shared
  contention, and normal three-second lane-skip pacing. Its globally-oldest/no-agency-fairness
  baseline is superseded within that source slice; within each selected lane oldest intent stays.
- Polling now has [durable agency/account/feed rotation](#october-9--durable-polling-selection--task-1333),
  one-candidate probes and at most 100 distinct feed offers per pass. The fresh scoped account/feed
  read is disposed before HTTP. Complete bounded ERA discovery stays, followed by one oldest
  artifact per offer. Current enabled Claim.MD test-account uniqueness means K ≤ 1 per agency.
  Unpaged account materialization/full ERA draining are superseded; status byte versus row limits,
  retained outer lease sessions and absence of whole-pass/dependency budgets remain.
- [Note review](#october-9--note-maintenance-fairness-review--task-1334) confirms existing
  one-batch-per-agency continuation with current 35-case acceptance, preserving crash/membership/
  cache and total-pass gaps. This adds no resource quota or durable note scan position.
- New scheduling metadata/indexes are source migrations 130/131. No existing schema apply,
  reset-baseline uptake or compatible-host rollout is established by the inventory integration.
- Prioritized accounting/connection-lifetime and signature reset/transaction-span findings are
  accepted as source review inputs. Proposed resource controls and acceptance groups remain
  proposals. Task 1.4.2 was separately assigned by Josh in the investigation chat and awaits
  main review; no global SQL/session cap, numeric capacity or runtime policy is adopted here.

**Retained investigator handoff (captured source identity below):**

**Status:** awaiting main-thread review; source investigation only. Josh explicitly assigned
SATI-WRK-001 task **1.4.1** to this second investigation thread. No runtime policy is adopted here.
Task 1.3.1 fairness design and integration remain with the main thread.
[Working evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-task-141-resource-limit-inventory)
owns the exact base/worktree, included uncommitted inputs, drift and check results. All five paths
were present, including the uncommitted idle/wake owner and current recovery/coordination docs.
Observed deployment facts remain solely in [DATABASE_ENVIRONMENTS.md](DATABASE_ENVIRONMENTS.md);
this investigation performed no runtime or quota verification.

**Reading the matrix:** **Enforced** means a control is expressed in the inspected source, not
newly tested or measured here. **Gap** means this path has no explicit control for the stated
resource; it does not prove an exploit or an operational outage. Query result limits do not bound
rows scanned, query plans, locks, elapsed time or total SQL commands. Live DbContexts do not by
themselves mean live SQL connections: distinguish explicitly opened session leases and transaction
connections from contexts that EF opens/closes around commands.

| Path and source owner | Scope and selection/materialization bounds | Admission order and SQL connection lifetime | Concurrency, queued work and dependencies | Deadlines, cancellation, retry/restart and gates |
|---|---|---|---|---|
| **Dispatch:** [ClearinghouseDispatchWorker](Sati.Api/Infrastructure/ClearinghouseDispatchWorker.cs), `ProcessTurnAsync`, `EligibleQueued`, `PrepareUnderAccountLeaseAsync`, `UploadAndRetainAsync`; [schedule](Sati.Api/Infrastructure/ClearinghouseDispatchSchedule.cs) | **Enforced:** globally oldest eligible Queued row, ordered by RequestedAtUtc/Id; same-agency/account readiness excludes Held and future Deferred. Empty selection obtains one scalar earliest eligible/future-due snapshot, no account list. Account/generation/readiness and current retained-subset release are rechecked before Sending. [History adapter](Sati.Api/Infrastructure/ApiClaimReleaseHistory.cs) and [loader](Sati.Persistence/Data/ClaimReleaseHistoryLoader.cs) cap collections at 10,000 rows and aggregate retained file content at 16 Mi characters, failing closed on incomplete history; this is substantially more work than selecting one dispatch. **Gap:** no agency-turn fairness, queue-depth admission or whole-turn SQL-command budget. | **Enforced:** Demo shared reset session → exclusive dispatch session → exclusive agency/account preflight session → serializable agency claim-release transaction, whose first decision lock is `Sati:ClaimRelease:{agencyId}`. Preparation key/receipt protection precedes that transaction. Account admission is released after Sending commits, before upload; reset and dispatch sessions persist through transport, response protection and evidence retention. Claim.MD request admission then opens another session. Result retention re-enters agency claim admission. **Gap:** zero-wait record/account locks do not reserve pool capacity; connections open before acquisition. | **Enforced:** one hosted schedule runner/turn per process; scalar/coalesced activity hints, no payload or per-tenant hint map. Minimum three seconds between starts, at most 20 starts per half-open minute per host; one startup catch-up. Record lease excludes cooperating hosts for the same dispatch. Claim.MD request serialization is shared with polling and reconciliation. **Gap:** H hosts multiply local start caps; distinct dispatches and other workers share SQL/key capacity. Durable queue length and waiting lease callers are not bounded by this schedule. | **Enforced:** five-minute monotonic activity, 60-second active reconciliation/contention cooldown; dormant waits hold no contexts/leases and due time alone cannot wake SQL. One 45-second connector I/O deadline starts after request admission/pacing. Zero-retry dispatch/claim execution; missing-key account state has persisted 1/5/15/60-minute delays plus stable 0–10% jitter, then fifth-failure Held, with Admin reopen. Sending/OutcomeUnknown are never ordinary Queued selection after restart. **Gap:** no whole-turn deadline; post-upload protection/write intentionally use CancellationToken.None, so safe evidence retention can outlast host cancellation. Gate: exact Demo/Testing identity, mutually exclusive explicit synthetic/real sandbox opt-in, enabled test account and current release checks. |
| **Polling:** [ClaimMdSandboxPoller](Sati.Api/Infrastructure/ClaimMdSandboxPoller.cs), `PollOnceAsync`, `PollAccountsAsync`, `PollStatusAsync`, `PollEraAsync`; [connector](Sati.Api/Infrastructure/ClaimMdSandboxConnector.cs) | **Enforced:** account rows must be enabled, test Claim.MD accounts with a non-null secret reference; globally ordered by account Id. Feed cursor lookup uses agency/account/feed; processors resolve stored agency and revalidate account and matching retained submissions. Separate durable status/ERA cursors advance with encrypted effects. ERA list: at most 20 pages of at most 100 entries, repeated IDs/refusal at a full twentieth page; collect/sort then download serially. Response I/O ≤16 MiB; X12 parser ≤2 Mi characters; ERA matching reads 501 candidate generations and refuses >500. **Gap:** account discovery ToListAsync has no page/count bound; status claim materialization is byte-capped, without a separate row cap. Matching/effect queries are not all independently row-capped. | **Enforced:** shared reset session → exclusive deployment/database poller session → global Claim.MD request session. Reset/poller sessions span the entire account pass, including one-second inter-feed/download delays, HTTP and receipt processing. The request session spans its own one-second pacing and exchange. Discovery and cursor contexts live across work but do not explicitly pin connections; transactional processors open additional connections. [Status processor](Sati.Api/Infrastructure/ClaimMdStatusProcessor.cs) and [ERA intake](Sati.Api/Infrastructure/ClaimResponseIngestion.cs) use agency claim-release admission, dispose the transaction before wrapping, then repeat checks and atomically commit cursor/effects. **Gap:** whole-pass lease duration/session occupancy unbounded by a run budget. | **Enforced:** one cooperating poll pass per database, sequential accounts/feeds/ERA downloads; one coordinated Claim.MD exchange at a time across hosts and paths. **Gap:** no bounded wait-caller queue, per-account vendor share, total feed/download count across the pass or reserved interactive SQL capacity. Up to 2,000 ERA list entries is a materialization ceiling, not a reasonable per-account time guarantee. | **Enforced:** immediate enabled startup pass, five-minute delay after each pass; per-feed faults log fixed safe context and allow later work; caller cancellation propagates. Uninitialized cursor refuses feed calls. Each connector exchange has 45 seconds of cooperative I/O after admission. Connector has no HTTP replay loop; receipt processors use single-attempt claim execution, while other EF reads can inherit registered provider retries. Failed/ambiguous processing does not advance uncommitted cursor; restart rereads persisted checkpoints. Real sandbox gate only; cursor initialization/transport operating approval separate. **Gap:** perpetual enabled polling has no dispatch-style activity/dormancy policy, no account/pass deadline or durable per-feed failure backoff/hold. Fixed Task.Delay uses system time, not an injected poll scheduler clock. |
| **Note abandonment:** [NoteAbandonmentWorker](Sati.Api/Infrastructure/NoteAbandonmentWorker.cs), `RunDueAsync`, `SqlNoteAbandonmentCoordination`; [sweep](Sati.Api/Infrastructure/NoteAbandonmentSweep.cs), `RunAsync` | **Enforced:** all stored agencies visited by Id within captured nullable maximum; 100-ID keyset pages; sweep uses same-agency note/person predicates, optional user scope, oldest eligible Pending notes, 100 worker candidates per agency. Revision/status/date/ownership guards and note/audit commit are atomic. Bounded growth check keeps day due on observed higher-ID additions. **Gap:** finite key range is not frozen membership; same-day provisioning invalidation, current-day completion-cache cardinality, total agencies/commands per pass and fair wait remain open. | **Enforced:** process-local runGate → one explicitly opened coordination connection holding shared Demo reset then exclusive global sweep session locks → per-agency sweep execution strategy/transaction. Both coordination locks use the same connection through all pages/agency transactions. Strategy-context creation itself does not pin a connection. **Gap:** global sweep lease and its connection can span an arbitrarily large finite pass; 100-record transactions do not cap that lifetime. | **Enforced:** one run per worker instance, one cooperating sweep per database, one 100-note batch per due agency per pass. Successful agencies skipped for captured day; completion dictionary storage replaced at the next enabled local-date check. **Gap:** no total run/agency budget or current-day cache capacity. Removing agencies during the day does not remove their completion entries. Local semaphore has no explicit waiter count limit. | **Enforced:** default-off EnableNoteAbandonmentWorker; immediate catch-up then hourly injected-clock delay; no SQL once current day complete. Cancellation/disablement checked between pages/agencies; recoverable concurrency/all-nonfatal-deadlock sweep failures leave agency due and continue, shared/unknown failures propagate. Sweep runs within provider execution strategy (API registration enables SQL retry); unlike external-send scopes it may retry a whole batch transaction. Restart loses local completion cache, rechecks durable eligibility, and never silently replays an external send. **Gap:** no sweep/pass deadline or explicit total retry-time budget; no new immediate agency retry. |
| **Signature processing/package:** [SignatureProcessingService](Sati.Api/Infrastructure/SignatureProcessingService.cs), `ProjectCompliance`, `PreparePackages`; [projection](Sati.Api/Infrastructure/SignatureComplianceProjectionService.cs), `ProjectCompletionAsync`; [completion worker](Sati.Signatures/SignatureCompletionWorker.cs), `BuildAsync` | **Enforced:** global completion order; ≤10 projection calls, ≤10 package candidate IDs and ≤10 mail calls per hosted iteration, sequential phases. Package cursor skips failed IDs for later candidates, wraps to zero when no higher candidate exists. Projection/package decision reads bind agency/request/person/frozen/session/consent. Package retains selected signing episode with Take(11) event query. [Builder](Sati.Signatures/SignaturePackageBuilder.cs) checks original hash/count and 15 MiB original ceiling; generated package ≤30 MiB. **Gap:** global counts are not per-agency fairness; a failed earliest projection can repeat next tick. Projection Includes of release attestations/authorization history are not explicitly paged. Byte size bounds neither PDF page count nor synchronous rendering time/peak allocations. | **Enforced:** fresh context and zero-retry SignatureStaffSingleAttempt for each item. Projection uses a serializable SQL transaction. Package uses a serializable transaction from existing-package check through blob read, synchronous PDF build, write-once upload, invitation decryption, receipt protection and evidence commit. **Gap:** package holds transaction connection/read locks across storage/key/CPU work. Hosted SignatureProcessingService does not call IDemoWorkerResetCoordination or the HTTP reset middleware; its reset-exclusion coverage needs explicit integration review, not assumption from the other workers. | **Enforced:** sequential execution within this hosted service; idempotent package lookup, transactional evidence and immutable blob writes. Azure blob read/write caps stored bytes at 30 MiB; writes check private container and use conditional PUT; identical prior bytes can be accepted, never overwritten. **Gap:** no shared host-count/concurrency or key/storage/CPU quota, no bounded durable package backlog, no durable package claim or failure backoff. Transaction/idempotency/uniqueness protect records; they are not aggregate admission. | **Enforced:** 10-second PeriodicTimer, feature.Enabled plus WorkersEnabled per tick; exact Demo/Testing signature identity. Startup waits first tick; local package cursor starts at zero on restart. Per-package faults advance scan, projection/package failures still permit subsequent phases. Shared signature transport: 30-second HttpClient send timer, separate 30-second body timer; Key Vault client retries explicitly zero. **Gap:** no common credential+headers+body/whole-package deadline, no cancellation checkpoints inside synchronous PDF build, no durable package retry cap/hold. Global/worker flags are not per-agency resource reservations; the agency route switch is enforced by SignatureEnabledFilter, not by this hosted scan. |
| **Signature mail:** [SignatureMailWorker](Sati.Signatures/SignatureMailWorker.cs), `ClaimAsync`, `PrepareSubmissionAsync`, `SubmitAsync`, `Apply`; [Azure sender/transport](Sati.Signatures/AzureSignatureProviders.cs) | **Enforced:** one globally earliest due uncompleted outbox row by NextAttemptAtUtc/Id, expired/no lease only; agency/request/purpose/generation and latest eligibility/payload revalidated. Durable invitation and receipt work share selection. **Gap:** no per-agency or notification-purpose share, durable outbox depth cap or oldest-age service bound. | **Enforced:** serializable claim transaction commits a five-minute row lease/attempt; payload unwrap outside transaction; prepare transaction commits stable provider operation GUID before POST. Final serializable transaction revalidates revocation and holds read locks during SendAsync to prevent a staff revocation committing before a later stale POST; release only after result write/commit/rollback. Existing-operation GET is outside that send transaction; retention gets its own transaction. **Gap:** no aggregate mail admission; do not remove final read-lock span without replacement revocation proof. Lease expiry fencing is a record control, not a five-minute whole-operation deadline. | **Enforced:** hosted service ≤10 sequential mail calls per iteration; durable row lease excludes competing owner and stale result writes. ACS operation GUID prevents uncertain submission being re-POSTed; operation-response body is capped at 16 KiB. **Gap:** multiple hosts can work on different rows; no shared ACS request/concurrency budget, bounded waiting queue or tenant allocation. Hosted serialization also permits long package phases to delay mail. | **Enforced:** at most five counted processing attempts (including status polls), nonterminal base delays of 30/60/120/240 seconds (the fifth counted attempt becomes terminal), later provider RetryAfter wins when within 0–86,400 seconds; terminal NeedsReview on exhaustion. Submit has linked 35-second cancellation across sender call, but validation/SQL commit/cleanup are outside that timer. Cancellation retains lease/operation for expiry recovery; uncertain/ended requests poll existing operation, never resend. EmailEnabled and exact test-recipient allowlist gate new sends; disabling email after prior submission yields review, not false suppression. **Gap:** GET polling/key access lack the submit timer; separate send/body timers plus credential acquisition do not establish whole-mail deadline. |

**Control scope and shared dependency accounting.**

- **Database/deployment coordination among cooperating hosts sharing one database:** shared
  `SatiDemo.FullReset` exclusion is a safety lock, not a concurrency cap; exclusive
  `Sati.NoteAbandonmentSweep` and `Sati.ClaimMdSandbox.Poller` allow one sweep/pass respectively.
  [Reset/session coordination](Sati.Api/Infrastructure/DemoWorkerResetCoordination.cs) opens before
  lock acquisition (10-second acquire command, zero lock wait, five-second uncancelled release).
  Note coordination uses the same acquire/release command bounds. These command timers do not
  bound connection establishment, callback duration or total cleanup.
- **Vendor request control:** [SqlClaimMdSandboxCoordination.RequestAsync](Sati.Api/Infrastructure/ClaimMdSandboxCoordination.cs)
  opens before an exclusive global request lock, waits up to 120 seconds (130-second acquire
  command), then holds session through one-second pacing and HTTP; release command five seconds.
  Source therefore serializes cooperating requests and imposes at least one-second start spacing.
  Its comment cites a 100/minute vendor cap; current vendor quota, quota ownership and other
  consumers were **not verified**. This is database-scoped coordination, not protection across
  independent databases/deployments using the same provider quota. Waiting callers can each hold
  an open connection. Dispatch and poller can each retain reset plus dispatch/poller plus request
  sessions during HTTP (three explicit lease sessions per active path); business transactions may
  add connections. This is source accounting, not measured occupancy or a pool maximum.
- **Tenant/account:** `Sati:ClaimRelease:{agencyId}` is the first agency decision lock inside the
  serializable transaction ([ClaimReleaseWriteScope](Sati.Persistence/Data/ClaimReleaseWriteScope.cs):
  ten-second lock wait, 15-second command). `Sati.ClearinghousePreflight:{agency}:{account}`
  serializes known-unsent preparation/reopen; persisted readiness is scoped to that pair.
  Neither grants SQL capacity or caps all work for an agency. Feed checkpoints bind account/feed
  to stored agency, preventing cursor mixing rather than reserving a quota.
- **Record:** dispatch session key protects one dispatch; signature outbox row lease/revision and
  operation ID fence one notification. Signature transactions, immutable files and selected
  uniqueness/idempotency protect evidence, not independent agency service levels.
- **Process-local:** dispatch runner/activity hints/pacing, note runGate/date cache, signature
  timer/cursor and sequential phase batches. The note cache is O(successful agencies this day);
  the dispatch hint state is scalar. No inspected owner enforces a deployment-wide SQL-session,
  dependency-concurrency, pending-waiter or combined worker/interactive budget.
- **Other shared consumers are dependencies, not new scope:** API queue/reopen/reconciliation,
  billing history and expensive document endpoints; reset/Function/watchdog; signature portal,
  interactive storage and key access. [Program](Sati.Api/Program.cs) registers shared database
  retry behavior and all workers after identity validation. [KeyVaultKeyWrapper](Sati.Api/Security/KeyVaultKeyWrapper.cs)
  uses SDK-default cryptography options and a per-key client dictionary with no explicit
  operation timer/version-count cap, whereas signature cryptography disables SDK retries.
  Neither owner supplies a shared key quota. Source does not supply current SQL pool settings,
  active host counts, remaining vendor/mail/storage/key quota or intended-host capacity evidence;
  consult the dated environment owner and obtain separate authority for later observations.

**Prioritized dependencies and proposed controls — not adopted or implemented.**

1. **First: agree on the accounting and safety boundary.** Main integration reconciles this
   snapshot with 1.3.1 fairness design and preserved release/recovery/reset rules. Define whether
   a budget counts open lease connections, commands, transaction occupancy, provider starts,
   CPU work or pending waiters; count simultaneous outer leases and include interactive/Function/
   portal consumers. Review signature hosted reset exclusion and the two external-I/O transaction
   spans explicitly. Retain required uncertainty, cursor, revocation and immutable-evidence
   protections; this inventory neither proves a reset race nor authorizes narrowing a lock.
2. **Next bounded design: shared SQL/session admission.** Under 1.4, propose a cross-host total
   and per-agency allowance, bounded admission queue and reservation for interactive work.
   Place admission before opening coordinated connections; declare lease order, timeout/fairness,
   overload/retry behavior and release on every failure/restart path. Use symbolic limits until
   the main thread reviews values/hosting evidence. Fair selection comes from 1.3.1/1.3.3;
   resource admission is a separate control. API admission implementation remains task 1.5.
3. **Then: finite discovery and operation budgets.** Propose paged account discovery, bounded
   status/effect collections and per-account/feed/download/whole-pass work limits with durable
   continuation. Define separate admission, credential, exchange, parsing/rendering, evidence
   retention and cleanup budgets. Preserve post-send evidence even on cancellation; a deadline
   cannot justify automatic replay, receipt loss or declaring nonreceipt. Durable signature
   package retry/continuation and note cache capacity need their own bounded design.
4. **Before activation/hosting choice:** obtain dated provider quota ownership, database/pool and
   active-host facts through separately authorized work; derive numbers and tradeoffs, then
   validate on synthetic intended-host workloads under 1.6. Expose bounded, content-free counters
   and progress/overload outcomes with a named alert owner. D1–D4 remain authoritative.
   This inventory recommends no Azure change, numeric capacity promise or new scheduler.

**Existing seams and deterministic acceptance proposals for later budget work.** These are
inspection results and proposed cases, **not test executions in task 1.4.1**. No additional
application tests or source changes were made. Declare reviewed limits first: C for aggregate
admission, A for an agency share, Q for queued waiters, F for an adopted fair-turn bound and D for
an owned deadline. Do not substitute elapsed arbitrary sleeps for admission/progress assertions.

| Existing source/test seam | Deterministic later case and required observations |
|---|---|
| Injected IDbContextFactory, IDemoWorkerResetCoordination/IClaimMdSandboxCoordination, connection/command interceptors; [coordination tests](Sati.Api.Tests/ClaimMdSandboxCoordinationTests.cs), [reset tests](Sati.Api.Tests/DemoWorkerResetCoordinationTests.cs) | With barriers hold C admitted operations in two independent hosts; the next attempt must not open SQL before admission, Q+1 waiters receive the adopted bounded overload result, and one release admits exactly one waiter. Count explicitly opened lease sessions separately from contexts/commands and record maximum simultaneous occupancy; cancellation/fault/lease loss restores counters without exceeding C. Existing coordination proof uses wall-clock spacing and does not prove C/Q. Later SQL execution needs separate disposable-instance authority. |
| [Dispatch scheduling tests](Sati.Api.Tests/ClearinghouseDispatchSchedulingTests.cs), injected TimeProvider/connector, [isolation reproducer](Sati.Api.Tests/ClearinghouseDispatchIsolationReproducerTests.cs), [preflight tests](Sati.Api.Tests/ClearinghousePreflightApiTests.cs) | Saturate A's adopted share; B and reserved interactive work obtain admission within the reviewed F bound. Budget denial/contention must not increment missing-key failures or change Queued intent. At activity expiry, no new SQL/lease admission; already-Sending work retains uncertainty/evidence. One startup turn per host stays distinct from the shared C cap. Main 1.3.1 supplies fairness policy. |
| [Connector deadline tests](Sati.Api.Tests/ClaimMdSandboxConnectorTests.cs): fake time, stalled headers/stream/body and coordination barriers | Account separately for admission, credentials, exchange and cleanup. Hold admission past 45 seconds: no premature exchange timer consumption; release then stall body to its exact deadline. Flood callers to test Q and cancellation without retained sessions. Later healthy callback proceeds under C; timeout after Sending never triggers a second physical upload. Do not claim provider nonreceipt from local cancellation. |
| Poller PollOnceAsync, injectable connector HttpMessageHandler/keys/scopes/coordination; [status tests](Sati.Api.Tests/ClaimMdStatusProcessorTests.cs) and [receipt/claim admission tests](Sati.Api.Tests/ClaimReleaseAtomicityTests.cs) | Seed more accounts than adopted page/turn allowance; intercept materializations to prove a page bound and durable continuation, not merely returned count. Exercise 20 full ERA pages, duplicates, byte/row thresholds and cancellation between feeds/downloads. On budget stop/fault/restart, cursor/effects commit together or neither; no cross-account bytes/cursor effects. Existing source has no direct hosted poller budget suite or injected pass clock. |
| [Note worker tests](Sati.Api.Tests/NoteAbandonmentWorkerTests.cs): fake time, factory observers, recoverable audit faults and SQL-gated coordination | Exceed adopted total-agency/turn and cache bounds, stop at an exact deterministic boundary, then resume without lost note/audit effects or false day completion. Verify failed A remains due, B meets adopted F, and cancellation/disablement releases both global locks. Include same-day membership churn; existing 100-ID/100-note/date-rotation tests do not prove a total pass/cache budget. |
| SignatureComplianceProjectionService.ProcessNextAsync; [projection tests](Sati.Api.Tests/SignatureComplianceProjectionTests.cs); service's factory/feature/options and package/mail dependencies | A damaged earliest projection plus later B and saturated packages must exercise the adopted phase/agency budget, without inventing successful projection or starving mail. Count candidate materializations, nested histories and calls. Drive the real hosted service after a scheduler seam is reviewed; its timer is currently system-time, so fake worker clocks alone do not control the hosted scan. |
| [Package tests](Sati.Signatures.Tests/SignaturePackageTests.cs), ISignatureBlobStore/outbox key mocks; [provider tests](Sati.Signatures.Tests/AzureSignatureProviderTests.cs) | Barrier-stall private-container check, blob read/write or key wrapping at each phase; inspect transaction/session occupancy and adopted D. Include bounded bytes with expensive PDFs, render cancellation/continuation and two-host same-completion contention. Commit failure preserves original and orphaned immutable blob for review; budget expiry must not auto-delete or create a duplicate retained package/receipt. Synchronous builder needs a reviewed cancellation/resource seam before claiming a CPU deadline. |
| [Mail tests](Sati.Signatures.Tests/SignatureMailWorkerTests.cs): fake time, blocking key/sender, independent contexts; Azure handler/credential mocks | Saturate C/A across hosts using distinct outbox rows; expire one five-minute lease and fence its late result. Cancel before and after operation GUID commit; restart must poll the same GUID, never re-POST. Barrier staff revocation at final submission validates retained ordering; a stalled sender honors the adopted deadline and rolls back safely. Verify five counted attempts, RetryAfter boundary and separate invitation/receipt fairness; no test may label provider completion as inbox delivery. |

For each later implemented limit retain an unfixed-code failure demonstrating the specific missing
control, then passing boundary/fault/restart proof against the real owner. Component mocks and
SQLite cases cannot establish SQL application-lock, cross-host budget or intended-host latency.
Synthetic load/benchmarks, databases, cloud/vendor calls, policy adoption and runtime changes
remain outside this assignment. Main integration must recheck findings if source changes before
review; task **1.4.1** remains awaiting review, not integrated or broader W8 complete.

#### October 9 — note-maintenance fairness review — task 1.3.3.4

**Status:** bounded source review and existing synthetic acceptance complete; no runtime change.
[NoteAbandonmentWorker](Sati.Api/Infrastructure/NoteAbandonmentWorker.cs) already visits due
stored agencies in ascending ID, within a captured finite maximum, using 100-ID keyset pages.
For each uncached agency it calls [the sweep](Sati.Api/Infrastructure/NoteAbandonmentSweep.cs)
once with the separate 100-note limit, then moves on. Filling that batch leaves the agency due
for a later hourly pass; it does not drain that agency before visiting the next. DEC-0222's
classified recoverable failure also leaves work due and continues; shared/unknown failures and
cancellation stop the pass. The sweep owns oldest-note ordering, provider transaction retries,
revision/ownership guards and atomic note/audit changes. No business-rule or scheduler owner moves.

**Conditional opportunity:** for a stable finite membership of N due agencies, an admitted pass
whose earlier operations terminate and whose gate remains enabled visits each once, after at
most N − 1 other agency sweep calls. Each call initially materializes at most 100 candidates;
provider retries can repeat a batch transaction. This is a per-pass invocation bound, not a
command, retry, time, successful-change or global-host fairness bound. Other hosts serialize
through the shared sweep session; whichever host acquires it restarts discovery at the first ID.
Process-local successful-day skips are an optimization, not durable scheduling progress.

**Evidence:** all **35 existing NoteAbandonmentWorkerTests passed, 0 failed/skipped**, including
two real private-SQL sweep/reset coordination cases. The 251-agency case has pages 100/100/51;
a recoverably failing first agency and a full second-agency batch still permit the last agency's
note/audit commit, while subsequent passes retry only due agencies. The exact-100-note case
commits healthy B in the same pass. Existing cancellation, disablement, shared-failure, date/cache,
observed-growth and damaged-ID cases passed with current source. No new defect regression or
unfixed-code failure is claimed; this task verifies an existing control. Exact identity/results
belong to [working evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-1334-note-maintenance-fairness-review).

**Remaining gaps and dependencies:** crashes or repeated cancellation before B can repeat
low-ID work because the scan/cache are process-local; no durable crash-resilient opportunity
bound is established. Full pass/global lock duration, current-day cache cardinality and resource
budgets remain open. Captured key range is not frozen membership; within-range/provisioning and
post-check additions can miss completed-day invalidation. A cache/batch cap alone cannot repair
that ownership problem. Preserve DEC-0224/0225 caveats and default-off/hourly/no-completed-day-SQL
behavior. Budget/deadline and provisioning owners must precede any stronger claim or scheduler
replacement. First integrate/revalidate the second thread's inventory and admission proposal;
then define signature fairness separately. No application/schema/test edits, real data, existing
database, external call, activation, release or sealed evidence change occurred here.

#### October 9 — durable polling selection — task 1.3.3.3

**Status:** bounded source implementation, verification and main integration complete under DEC-0241; unreleased.
The constructor-injected [ClearinghousePollSelector](Sati.Api/Infrastructure/ClearinghousePollSelector.cs)
now owns a short zero-retry transaction under the existing outer reset/poller leases. Three
Persistence scheduling rows retain agency/account/feed pivots independently of account business
revisions and response checkpoint receipts. Selection advances an offer before work; strict SQL
ownership and scoped revisions prevent an uncommitted/lost-lease offer from being returned.
Known selection contention stops the pass; it is not an account failure.

`ClaimMdSandboxPoller` no longer materializes every account. A pass admits at most 100 offers,
with at most 100 locally visited checkpoint IDs. Candidate queries select one scoped agency,
account and Status/ERA checkpoint with next-greater/wrap probes. Fresh enabled/test/account/feed
binding and ASCII-digit cursor are rechecked in a disposed read context before HTTP. Missing key
or malformed feed consumes its position while later healthy agencies can run. Selection/read
infrastructure failures stop the pass outside feed containment; cancellation propagates. Feature
checks precede further offers and ERA requests. Existing default-off identity gates, reset/poller/
vendor leases, one-second pacing and five-minute hosted post-pass delay remain.

**Inventory correction:** the existing filtered unique account index permits **one enabled test
Claim.MD account per agency**. The first design's many-active-account test fixture violated that
rule and failed during setup; the rule is retained. Current K is therefore at most 1, F at most 2.
Account pivots remain independent of business state to handle replacement/stale position; tests
exercise supported disable/replace rather than dropping uniqueness. General future multi-account
weight/order proof is not established. The design's general A × K × F count is conditional;
current source acceptance uses the actual constrained cohort and account replacement.

The shared model adds singleton/agency/account scheduling tables with seed, scope checks,
revision tokens, immutable scope/deletion guards and restrict relationships. Existing account
uniqueness/checkpoint indexes remain; new agency/account/feed and eligibility indexes are additive.
Source migration **20261010032625_AddClearinghousePollRotation** is migration 131; Up preserves
existing columns/indexes/receipts/cursors. Down refuses with 51046 after any scheduling position
advanced or state is inconsistent. Source generation/synthetic schema acceptance changes no
existing database. Migration 130/131 apply, reviewed rollback, compatible all-host rollout and
reset-baseline coverage remain separate operational work; the readiness-129 apply helper cannot
apply these migrations. Neither schema nor transport activation is established here.

**Actual evidence:** the original agency-opportunity test failed with A:Status/A:Era before
B:Status; fixed ordering is A:Status/B:Status/A:Era/B:Era. Tests cover 24 durable offers across
recreated selectors, unchanged receipt/checkpoint/account business revisions, visited-feed exclusion,
replacement, gate/cancellation/missing seed, scoped writes/concurrency, preserved model/index rules,
100 offers among 106 damaged feeds with zero HTTP and preserved checkpoints, missing-key A versus
B, deadline-stalled A versus B, stale account after offer and immediate stop boundaries.
Always-first-agency/feed mutants each failed; removed SQL ownership validation failed a real
private SQL lease-loss test. Exact source bytes were restored before final builds.

Private SQL acceptance covers serialization/disposal, persisted restart, supported account
replacement, lost ownership for Selected/Empty, pre/post commit failures without execution replay,
additive Up/fresh Down and refusal after use. Early fixture/hook failures are retained in working
evidence rather than counted as runtime proof. Existing receipt/wrapping/reset/vendor-budget
regressions passed with the final combined source: **119 passed, 0 failed/skipped** in a newly
owned private SQL instance. Cancellation precedence also failed twice before its guard fix.
Documentation structure, all 22 negative proofs and whitespace passed. Detailed results belong to
[the ledger](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-1333-durable-polling-selection).

**Limits:** per-feed/page work and offer/query materialization are bounded; full dependency/pass
elapsed time, response memory, aggregate sessions/quotas, actual vendor behavior, live reset,
load/capacity and alert delivery remain unverified. Reset/poller/vendor connections still span
external work. Existing session coordination does not prove safety after arbitrary connection
loss. No cloud, real-data, deployment, release, schema apply or sealed readiness change. The next
main fairness slice reviews note-maintenance opportunities before selecting implementation;
second-thread 1.4.1 remains independently assigned and unintegrated.
#### October 9 — bounded ERA feed quantum — task 1.3.3.2

**Status:** bounded source implementation/verification complete under DEC-0241; unreleased.
`ClaimMdSandboxPoller.PollEraAsync` retains every existing listing-page, duplicate and cursor
check, then processes only the numerically oldest artifact. Later artifacts stay available from
the last committed receipt cursor on the next visit. Empty/failed/stale imports advance no cursor;
receipt/effects and ordering remain owned by existing ingestion. No schema, gate, cadence, vendor
admission or reset change. Account discovery/agency-feed rotation remain task 1.3.3.3.

The two-artifact regression failed against `4a5e530` with downloads `[20, 21]` instead of `[20]`;
the first receipt had committed before the second download/payment conflict. The fixed regression
asserts one download, one receipt/deposit, ERA cursor 20, untouched Status cursor and processed
count 1. Existing out-of-order multi-page discovery, uninitialized cursors, reset, status/ERA
atomicity, onboarding and HTTP deadline tests passed in a **35-test** local run, zero failed/skipped.
[Working evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-1332-bounded-era-feed-quantum)
records exact commands/failures/limits. No vendor call, live data, existing database, capacity
measurement, deployment, activation or sealed score change. Re-listing later work costs additional
listing requests; budgets and actual vendor behavior remain unverified.
#### October 9 — polling fairness design — task 1.3.3.1

**Status:** main source/design review complete; local policy selected under Josh's October 9
continued-work approval in [DEC-0241](docs/decisions/current/2026-10-09-DEC-0241.md).
Implementation and its tests are separate numbered chunks. No operating acceptance is established.

**Captured source:** `38ab061e321eb70170909b09027d7cb47e314b41`, main `master` checkout
`C:/Users/Joshu/source/repos/heschides/Sati`. No uncommitted application inputs are included.
Preexisting project formatting and assessment scratch are preserved and excluded. Resource
inventory 1.4.1 remains with the second thread; this review covers polling selection only.

| Source owner / symbol | Enforced behavior at captured source | Gap relevant to this design |
|---|---|---|
| [ClaimMdSandboxPoller](Sati.Api/Infrastructure/ClaimMdSandboxPoller.cs), `PollAccountsAsync` | Enabled test Claim.MD accounts with nonnull secret references, ordered by account ID; per-feed exceptions contained with content-free logs; caller cancellation propagated. | Entire account list materialized; no agency-first rotation, durable position or total feed-offer cap. A failing/slow early feed delays later work. |
| Same owner, `PollStatusAsync` | Account/agency/Status checkpoint lookup; initialized ASCII-digit cursor required before HTTP; one response page. | DbContext survives HTTP; no independent scheduling pivot. Missing cursor is not permission to initialize one. |
| Same owner, `PollEraAsync` | Complete listing: maximum 20 pages, each at most 100 entries; duplicates/reversed or nonadvancing IDs refused; full page 20 refuses before downloading. Numeric ascending artifact order. | Up to 1,999 downloads and receipt commits per feed visit; later account opportunities depend on draining them. No whole-feed deadline. |
| [Coordination](Sati.Api/Infrastructure/ClaimMdSandboxCoordination.cs), `PollOnceAsync` / `RequestAsync` | Database-shared zero-wait session-exclusive poller lease; vendor lease shared with uploads spans one-second pacing and exchange, with 120-second acquisition budget. | Poller/reset connections survive the pass. Vendor lease is not an agency quota; actual provider capacity is unverified. |
| [Reset coordination](Sati.Api/Infrastructure/DemoWorkerResetCoordination.cs), `RunAsync` | Shared reset exclusion acquired before polling; held through reads, external work and evidence. | Keep order/exclusion; no narrowing or new reset policy in this slice. |
| [Connector](Sati.Api/Infrastructure/ClaimMdSandboxConnector.cs) | Fixed host, server-only key resolution, 16 MiB response cap, 45-second cooperative headers/body deadline after admission. ERA page size 100; status is byte-bounded, without a separate row-count cap. | Admission, sync parsing, key wrapping and cleanup not covered by HTTP deadline; a pass has no elapsed-time bound. |
| [Status processor](Sati.Api/Infrastructure/ClaimMdStatusProcessor.cs) / [ERA ingestion](Sati.Api/Infrastructure/ClaimResponseIngestion.cs) | Zero-retry authoritative writes under agency claim-release admission; current account/cursor/matching rechecks; wrapping outside decision transaction then revalidation; encrypted receipt/effects/checkpoint commit together. Stale expected cursor refuses; ERA duplicate/payment conflict requires review. | These retained business/receipt owners must not store scheduling completion or skip failed artifacts. |
| [Feed model](Sati.Persistence/Models/Billing/ClearinghouseFeedCheckpoint.cs) / [shared configuration](Sati.Persistence/Data/ClearinghousePersistenceModel.cs) | Independent account/feed cursors, revision concurrency and immutable provenance; Status and ERA only scheduled. | Modifications feed remains unscheduled. Scheduling pivots must have separate revisions and cannot reset these cursors. |
| `ExecuteAsync` / [gate](Sati.Api/Infrastructure/ClearinghouseDispatchGate.cs) | Default-off, exact synthetic identity plus real sandbox opt-in and synthetic-dispatch exclusion; startup pass then five-minute delay after each pass. | No new keep-alive, timer Function or shortened idle polling. Dispatch activity policy does not govern this poller. |

**Selected bounded policy.** Keep the existing outer reset → global poller admission. Within a
pass make at most **100 offers**, tracking at most 100 checkpoint IDs locally so each feed is
visited at most once in that pass. Each offer uses a short constructor-injected selector with
one transaction-exclusive zero-wait SQL lock, ReadCommitted and a named zero-retry execution
scope. Only 0/1 grants ownership, -1 is shared contention, other results fail closed; verify
ownership before returning Empty or committing an offer. Release its transaction/context before
key resolution, HTTP, wrapping or receipt work. The global poller lease still protects external
same-feed work; the selector lock/transaction protects durable scheduling state, not receipt effects.

Select the next greater eligible agency ID and wrap once, then next greater account in that
agency using provider GUID ordering and wrap once, then next Status/ERA feed and wrap once.
Persist a seeded singleton last-agency row, an agency-scoped last-account row and an account-
scoped last-feed row with their own revisions. Each query projects one candidate, with at most
2 agency + 2 account + 2 feed probes and 3 pivot reads/writes; no full account list or tenant map.
A missing global seed/unsupported provider fails closed. Strict Testing SQLite is a transactional
stand-in only. Candidate predicates retain enabled/test/Claim.MD/nonnull-reference account rules
and require an existing nonnull 1–20-character Status/ERA cursor. Validate ASCII digits and the
fresh account/agency/feed binding before any HTTP; damaged cursor/reference or stale account
spends an offer and makes no request. Do not infer dispatch readiness from a poll failure.

A status offer retrieves/processes one complete response page. An ERA offer first validates all
listing pages within the existing 20 × 100 bounds, then downloads/imports **only the numerically
oldest artifact**. On its next offer re-list from the last committed receipt cursor. Never move
to a later ERA after a failure or use a listing high-water ID as a receipt cursor. This trades
repeat listing cost for retained ordering and bounded artifact work; vendor totals still need
budget/operating evidence. Empty or invalid feeds do not advance business checkpoints. Advance
scheduling pivots before feed work, so a malformed or missing-key feed cannot monopolize selection
on a later pass or restart. A crash after the offer may repeat a read later, with receipt ownership
and expected-cursor checks retaining authority. Upload states/replay rules are unchanged.

Keep the five-minute hosted delay after each bounded pass, global vendor admission and existing
per-feed containment. Selection/discovery/shared coordination errors stop the pass; content-free
logging preserves existing unknown feed containment but does not classify it as an account fault.
Stop cancellation immediately. Recheck the feature gate before each offer/request; no new expiry,
automatic key recovery, weights, worker concurrency, account quotas or cadence shortening.

**Account cohort clarification (implementation review):** the existing filtered unique index
allows at most one enabled test Claim.MD account per agency. K is currently at most 1;
multi-account fixtures must not bypass that invariant. Account replacement remains supported.

**Conditional opportunity.** For a stable finite continually offered cohort of A agencies, at most
K eligible accounts in a chosen agency and F ≤ 2 feeds in a chosen account, hierarchical committed
rotation gives that agency an opportunity within A offers, the account within A × K and its feed
within A × K × F. These are offer counts across permitted turns; local already-visited exclusions
only remove candidates for the current pass. Restart preserves pivots but not exclusions.
Membership changes, a stalled operation, host suspension, shared admission/cancellation/failed
selection commits and gates invalidate any elapsed-time inference. A scheduling opportunity is
not successful ingestion or bounded wall time. The 100-offer cap limits per-pass bookkeeping and
selection count; it does not bound query cost, SQL sessions, response memory, dependency time,
number of passes or metadata cardinality. No measured capacity or cross-host resource quota.

**Schema/operations dependencies.** Add three scheduling tables in the shared Persistence model,
scoped immutable keys/revision guards, restrict agency/account relationships and selection indexes;
source migration seeds only the singleton. Do not change existing business revisions, retained
receipts or checkpoints. Refuse Down after any scheduling position advanced. Existing database
apply, rollback/runbook, reset baseline coverage and compatible all-host rollout remain separately
controlled. A host with missing state must refuse selection, not restart FIFO or repair schema.
The current dispatch source migration 130 is not evidence of polling schema uptake.

**Deterministic acceptance plan.** Task 1.3.3.2 first proves that two valid ERA artifacts no longer
drain in one visit, and that out-of-order listing pages still select the oldest with no cursor
advance on failure; retain unfixed failure. Task 1.3.3.3 proves agency/account/feed opportunity
bounds with sustained A backlog and supported account replacement, invalid/missing-key A versus healthy B,
100-distinct-feed cap, reset/feature-disable/cancellation before HTTP, no checkpoint movement from
selection, and stale account/checkpoint refusal. Reader observers assert projection bounds and
absence of all-account materialization; deliberate always-first-agency/feed and stale-account-selection mutants must
fail. Existing `ClaimMdStatusProcessorTests`, `ClaimMdSandboxConnectorTests`, onboarding and
`ClaimReleasePreparationSqlTests` supply receipt/wrapping/deadline seams. Private SQL proves two
independent hosts, lock return/ownership loss, rollback, restart position, scope/revision conflicts,
provider GUID ordering, no duplicate active feed and additive migration/guarded Down. Run relevant
billing/reset/connector regressions. No test claim substitutes for vendor/load or deployed evidence.

**Alternatives and later work:** flat account turns reward agencies with more accounts; draining
ERA recreates the bottleneck; process-only pivots lose restart position; receipt/dispatch-state
reuse mixes owners; dedicated agency workers or narrowed vendor quotas lack supporting evidence.
Resource budget 1.4, combined load 1.6 and monitoring 1.7 remain open. Link the reviewed second-thread
1.4.1 results when available. This section does not adopt its matrix or take its next assignment.

#### October 9 — dispatch fairness implementation — task 1.3.2

**Status:** task 1.3.2 source implementation and main acceptance/integration complete; unreleased
and default-off. [DEC-0240](docs/decisions/current/2026-10-09-DEC-0240.md) records Josh's explicit
approval of the retained design below. [Working evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-132-dispatch-fairness-implementation)
owns failures, actual results and remaining verification. Task 1.4.1 remains independently assigned.

**Ownership and behavior:** `ClearinghouseDispatchSelector.SelectAsync` owns persisted agency-first,
then account rotation with SQL-provider Guid comparison/order and oldest `RequestedAtUtc, Id`
within the selected lane. Shared Persistence entities hold one seeded global pivot and at most
one pivot per retained offered agency, with separate revision concurrency tokens. Both EF models
use `ClearinghousePersistenceModel`; an offer changes no business account/readiness revision.
Selection produces no tenant list, in-memory map, dispatch reservation or send evidence.

`ProcessTurnAsync` first acquires existing shared reset admission. The selector refuses a retrying
outer scope, owns a zero-retry transaction, and obtains one fixed SQL transaction-owned exclusive
application lock with zero wait. Only explicit Testing/SatiApiTests SQLite may substitute a
serializable stand-in. Other providers and missing singleton state fail closed; no automatic
initialization or FIFO fallback occurs. SQL lock mode is reverified before an Empty return or
offer commit; lost ownership rolls back and prevents upload. All selector resources dispose before scoped dispatch
reload, existing dispatch/account leases, key/wrapping preflight, claim-release decisions and
physical upload. Pivots advance before admission; a positively identified busy/stale lane spends
that offer. `LaneSkipped` permits another active turn at normal three-second pacing, without
renewing activity or counting account failure. Shared reset/selector contention and unknown faults
retain sixty-second cooldown. Startup/activity/expiry/in-flight rules and all authoritative
preparation checks remain. Sending and OutcomeUnknown remain excluded; uncertain/result failures
never become automatic resend authority.

**Query/storage limits:** scalar probes wrap at most once per hierarchy level: at most two agency
candidates, two account candidates and one oldest dispatch projection, plus fixed cursor reads
and at most two cursor writes. Empty uses the retained scalar due snapshot and commits no pivot
change; shared contention does not advance position. Finite removal/hold/reopen retains pivots.
Conditional stable-cohort offer bounds remain A / A × K with the adopted design's exclusions.
Rows examined, query duration, connection counts, admission time, delivery and total resources
are not bounded by this policy.

**Additive source schema:** `20261010021210_AddClearinghouseDispatchRotation` adds two pivot tables,
singleton seed, checks/restrict agency FK and filtered `IX_ClearinghouseDispatches_QueuedLane` on
`AgencyId, AccountId, RequestedAtUtc, Id` for Queued state. All prior indexes are retained, including
the unfiltered agency/account FK index. Source count is 130; observed deployments and sealed
129-migration release evidence remain unchanged. Down refuses advanced global position or any
retained agency row with 51045. Model writes refuse deletion, scope changes or missing revision
increments; direct SQL remains a separate privileged boundary.

**Operational dependencies:** pause dispatch on every host before separately authorized apply or
rollback. Mixed global-FIFO workers do not honor the selector; all participating hosts must be
compatible before enabling. Restore/reset baselines must include schema and reviewed pivot state;
the retained readiness migration helper targets 129 and cannot apply this new 130th migration.
A new reviewed apply/rollback preparation and baseline uptake are needed for later rollout.
No cursor reset/restore is performed here. Broader budgets, other-worker fairness, API admission,
configuration consistency and combined load/alert acceptance remain open. No existing/cloud/working
database, real data, vendor call, deployment or activation is touched.

**Actual acceptance:** original busy-head regressions failed both cases before implementation;
always-first-agency/account and long-lane-cooldown mutants failed, with source restoration verified.
SQL ownership-loss injection failed before the guard and passed in the final full API acceptance:
**1,330 passed, zero failed/skipped**, including eight actual SQL fairness cases. Tests cover
finite three-original A backlog/B progress, agency/account shares and restart, true held dispatch/
account leases, shared selector serialization and release during blocked upload, rollback/ambiguous
commit/no retry/no send, ownership loss, bounded projections/writes, finite lane removal, cursor
concurrency, both-model metadata, additive schema and guarded rollback. Existing recovery, due/mode,
reopen, billing, uncertainty, reset and fake-clock host acceptance also passes. Shared persistence
boundary passed one case. The ledger retains initial fixture/build failures and final artifact
exclusion used for that boundary build. No broader 1.6 load/operating guarantee follows.
Next main slice is 1.3.3.1 polling fairness design; 1.4.1 stays independently assigned for review.

#### October 9 — dispatch fairness design — task 1.3.1

**Status at capture:** source-grounded design deliverable awaiting review; policy, schema and runtime changes
are **proposed**, not adopted or implemented. [DEC-0239](docs/decisions/current/2026-10-09-DEC-0239.md)
records the recommendation and alternatives. Inventory baseline is `8e6509fc4c99fd5c32d8a63715ae7a98941be7f6`
in the main checkout, `C:\Users\Joshu\source\repos\heschides\Sati`. No included uncommitted application
inputs; unrelated project formatting and assessment scratch are excluded. Task 1.4.1 owns the
separate resource-limit matrix in its dedicated worktree. This section covers dispatch selection
only and does not adopt that thread's future findings or change sealed release evidence.

**Later adoption:** Josh explicitly approved this policy and additive source-schema proposal;
[DEC-0240](docs/decisions/current/2026-10-09-DEC-0240.md) supersedes only pending adoption and the
specified local lane-contention cooldown. The design below is retained as the exact adopted
contract; [task 1.3.2](#october-9--dispatch-fairness-implementation--task-132) now records source
implementation/acceptance separately from any existing DB operation.

**Current selection, scope and admission inventory:**

| Current source owner / symbol | Enforced behavior and fairness consequence |
|---|---|
| [Worker](Sati.Api/Infrastructure/ClearinghouseDispatchWorker.cs), `EligibleQueued` / `ProcessUnderResetLeaseAsync` | One globally oldest `Queued` row by `RequestedAtUtc, Id`. Missing readiness means eligible; Ready is eligible, due Deferred is eligible only in real-sandbox mode, future Deferred/Held are excluded. No agency/account rotation or cursor. Selection does not filter out disabled/changed accounts: preparation must cancel or reject invalid intent under the existing rules. A long older A backlog can consume every offer before B. |
| Worker, `PrepareUnderAccountLeaseAsync` | Composite agency/account and agency/generation reads; reload state, readiness revision, due time, current account binding/profile and current retained-subset compliance/history before Sending. A scheduling candidate grants no authority. Source/account changes and release holds may finish by cancellation before send. |
| [Reset coordination](Sati.Api/Infrastructure/DemoWorkerResetCoordination.cs), `RunWithLeaseAsync` | In exact Demo/SatiDemo on SQL Server: shared reset session lease, then exclusive dispatch session lease, then exclusive `(AgencyId, AccountId)` preflight session lease. Each acquire is zero-wait; return -1 means known contention. Other negative results fail. Separate open connections back these leases. Outside exact Demo the implementation invokes an uncoordinated stand-in; generic Testing/SQLite tests do not prove SQL ownership. |
| Worker, `Preparation.Idle` / `ProcessTurnAsync` | Reset/dispatch/account lease misses and stale state/readiness can all become `Contended`. The caller cannot currently distinguish a busy lane from a shared barrier. No bounded scan past the selected head is performed. |
| [Scheduling owner](Sati.Api/Infrastructure/ClearinghouseDispatchSchedule.cs), `RunAsync` | One local runner, adopted activity/pacing/empty reconciliation and shared-failure cooldown. Every successful or contended turn still selects from the same global head. Pacing bounds local starts; it neither rotates lanes nor reserves aggregate capacity. Dormant/startup rules remain authoritative. |
| [Release owner](Sati.Persistence/Data/ClaimReleaseWriteScope.cs), `BeginAsync` / `AcquireAsync` | Owns Serializable billing decision transaction, first agency claim-release decision lock, up to 10-second application-lock wait, 15-second command timeout, and zero-retry execution. Narrower billing locks follow it. Account/reset/dispatch sessions are not released by ending this write transaction. This wait is not a safe zero-wait lane miss. |
| Worker, `UploadAndRetainAsync` | Account admission releases after Sending commits, before upload. Reset/dispatch sessions remain through upload, wrapping and evidence retention; result write re-enters claim-release admission with `CancellationToken.None`. Sending/OutcomeUnknown are never automatic candidates. A slow in-flight offer can delay the next offer on that host; selection fairness cannot bound total operation time. |
| [Vendor coordination](Sati.Api/Infrastructure/ClaimMdSandboxCoordination.cs), `RequestAsync` | Deployment/database-shared request lease spans pacing and HTTP and can wait up to 120 seconds for admission. Upload and polling share it. It remains a shared dependency, not an account-failure signal or a quota that fairness can partition. Vendor capacity/actual limits are not verified in this inventory. |
| [Shared model](Sati.Persistence/Data/ClearinghousePersistenceModel.cs), `Configure` / `ProtectWrites` | Composite account/generation tenant links; immutable intent scope, revision checks and immutable attempts. Account uniqueness allows one enabled account per agency/connector/test tuple, not unlimited enabled Claim.MD accounts. Neither account/readiness revision nor feed cursor is a dispatch fairness cursor. |

**Existing indexes and proposed query shape:** the model and the foundation/readiness migrations
declare dispatch indexes `(AgencyId, State, RequestedAtUtc)`, `(State, RequestedAtUtc, Id)` including
agency/account, and `(AgencyId, AccountId)` from the composite link; unique generation identity
and dispatch primary key remain. Readiness has composite primary key `(AgencyId, AccountId)` and
`(Disposition, NextEligibleAtUtc)`. These declarations establish source layout, not deployed
indexes, query plans or cost. No current index directly supplies ordered oldest work within a
chosen agency/account/state lane including the Id tie-breaker.

Recommend a later additive filtered Queued index `(AgencyId, AccountId, RequestedAtUtc, Id)`
with `State = Queued`, subject to provider/model/migration and query-plan review. Retain the
existing global/due indexes until evidence supports any separate change. Agency selection uses
the trusted Agencies key and an `EXISTS` eligible-Queued predicate; account selection uses scoped
ClearinghouseAccounts and the same predicate. Each level performs at most one `TOP (1)` next-key
probe and one wrap probe. Within the chosen lane take one oldest `RequestedAtUtc, Id` dispatch.
At most five candidate SELECTs, scalar cursor reads and at most two cursor-row writes per offer;
no materialized tenant/account list, aggregate roster count, tenant dictionary or internal drain
loop. Empty selection retains the existing one scalar relevant-due snapshot. Projections and
query count are bounded; rows examined, blocking, SQL sessions and execution time are not.
Implementation must validate actual SQL translation and command counts rather than count a
client-side sort or hidden enumeration as a keyset probe. Account ordering/comparison uses SQL
Server's `uniqueidentifier` order consistently; do not mix it with .NET Guid ordering.

**Recommended policy: persisted hierarchical round robin, one offer per turn.** Rotate agencies
first, then accounts within that agency. Preserve FIFO only inside the selected account lane.
This gives each eligible agency one offer per agency rotation, even if A has more files/accounts.
An offer means a selected opportunity, including a known lane miss; it is not a completed upload,
claim approval, vendor reservation or resource entitlement. Equal offer shares need not mean
equal elapsed time, file size, CPU, SQL or successful-send shares.

Proposed schema is separate scheduling metadata in Persistence, configured once for both EF
models: one fixed-job global row with nullable `LastAgencyId` and a concurrency `Revision`; one
row per offered agency with primary/foreign key `AgencyId`, nullable `LastAccountId` and its own
`Revision`. IDs are ordering pivots, not timestamps or authority. Nullable pivots represent an
unvisited rotation; a pivot can outlive an eligible lane and wrapping still works. The per-agency
row retains a restricted agency link; its last-account pivot does not assert an account binding.
Require one fixed-key global row by primary key/check constraint, positive agency IDs and positive
revisions; seed the global row once in the proposed migration. A first agency row is inserted
with its final selected pivot in one write, rather than inserted empty and updated again.
Never reuse or increment business account/readiness revisions for a scheduling offer. Store no
EDI, note content, key reference/value, human identity or send claim in these rows. Persistent
cardinality is one global row plus at most one row per retained agency that has been offered;
this avoids a process-local tenant map but is not a numeric storage/tenant quota. Task 1.4 owns
that capacity work. No automatic state/history deletion or new cleanup scheduler is proposed.

1. Reuse current gate/cancellation/activity checks and shared reset exclusion. In a short,
   separate selection context/transaction, acquire a new fixed-job exclusive SQL transaction
   application lock with zero wait. All participating hosts sharing this database use it.
   A known selector-lock miss is shared contention, not an account fault. Unsupported schema,
   missing seeded global state, lost ownership, ambiguous commit or unknown SQL errors fail
   safely under the shared-failure path; never fall back to global oldest selection.
2. Choose the next eligible agency strictly after `LastAgencyId`, wrapping once if needed.
   Read/create only that agency's cursor row under this lock. Choose its next eligible account
   strictly after `LastAccountId`, wrapping once, then its oldest Queued candidate. Use the
   exact existing readiness/mode predicate at every probe; preparation still rechecks it.
   Disabled/changed account intent can receive an offer for existing cancellation handling.
3. Advance both applicable pivots and revisions and commit the offer **before** attempting
   dispatch/account admission. Advance on the selected opportunity, not on successful upload.
   A committed agency whose eligible accounts vanished during discovery advances the agency
   pivot only; a vanished selected dispatch still spends that account offer. These are scoped
   stale-lane skips, not proof that the whole queue is empty. No second lane is scanned that turn.
4. Dispose the selection transaction, application lock and context before acquiring the existing
   dispatch/account sessions, key/wrapping calls or claim-release decision lock. The existing
   outer reset lease remains. Proceed with current reload, binding, readiness, billing admission,
   Sending commit, upload and evidence rules. The selection commit does not mutate Queued intent,
   create an attempt or reserve physical work. Concurrent hosts may later choose the same record;
   existing dispatch/account exclusion and the Sending recheck still decide whether it can send.
5. Add a proposed distinct `LaneSkipped` scheduling result only for a positively identified
   dispatch/account zero-wait miss or scoped stale selection before Sending. Continue with
   ordinary adopted pacing while active; do not renew activity or immediately loop. Reset or
   selector contention, claim-release timeout, key wrapping, vendor coordination and unknown
   faults retain the existing shared cooldown. The specific missing-account-key exception keeps
   its approved Deferred/Held/audit behavior; a busy lease consumes no account failure. Cancellation
   propagates. Never classify a generic exception or post-Sending failure as a harmless lane skip.

The proposed `LaneSkipped` distinction is a narrowly scoped amendment to DEC-0237's current
blanket contention cooldown, pending adoption. Its timing constants, startup exception, default-off
gate, activity publishers, no-idle-SQL rule and in-flight uncertainty policy remain unchanged.
Do not make a lane rotation a new activity source. Global/shared failures still stop all local
automatic work for the existing cooldown; fairness does not justify hammering a shared dependency.

**Lock order and restart contract:** shared reset → temporary selector transaction/lock →
release selector completely → dispatch session → account session → claim-release decision
transaction → existing narrower billing locks. The selector never takes claim-release or external
dependency admission; queue, reopen, reconciliation and other writers do not take the selector
lock from inside their existing scopes. Selector cursor writes occur only through its named API
owner in a zero-retry execution scope. SQLite may supply a deterministic test stand-in, but only
real SQL proves shared lock/commit behavior. Retain current gate/cancellation rechecks at the send
boundaries. Cursor advancement never renews activity. Expiry does not revoke an already admitted
in-flight turn: it finishes/cancels under the accepted rules and cannot start a subsequent turn.
Cancellation or a crash after offer commit may spend the opportunity without starting its work.

Host restart resumes the persisted pivots; it does not reset every host to the first agency/account
or create a new recurring poll. The existing one startup catch-up offer remains the sole exception
without activity. Rollback before offer commit changes no pivot. A crash or uncertain acknowledgement
after offer commit may spend one opportunity without sending; reread current pivots on the next
ordinary turn without replaying the selection write. Cursors do not encode whether a send happened.
Restore/intentional Demo reset can restore an older rotation with the database; verify cursor tables
and baseline/reset ownership under exclusive reset before later deployment. No imported cursor
from another database identity is valid. Mixed old/new workers would ignore this policy: dispatch
must be paused across hosts for an approved additive migration/compatible rollout or rollback.
No migration is created or applied here, and no Demo baseline is accessed or changed.

**Conditional turn bounds and explicit exclusions:** for a stable finite set of `A` selection-
eligible agencies, a continuously eligible agency gets an offer within the next `A` committed
agency offers across participating hosts. For a continuously eligible account among `K` eligible
accounts in that agency, its lane gets an offer within `K` visits to that agency, hence at most
`A × K` committed agency offers. Each cursor moves past the selected key and wraps once, so a
fixed eligible key cannot be passed twice before the target is visited. This is the reasoning to
test, not measured runtime proof. If the target is oldest in its lane, those opportunities select
it; a deeper target additionally depends on preceding lane work completing/removing. No bounded
drain is promised for an indefinitely blocked head within one account.

The bound assumes current schema/participating hosts, continuing permitted turns, successful
selector commits, stable selection membership/order, and target eligibility. Successful sending
also requires target dispatch/account leases, SQL, shared keys/vendor admission and current billing
checks to remain available. Other lanes' known zero-wait misses spend their offers and therefore
cannot monopolize the next agency. A target's own repeated lease misses do not guarantee its send.
Global failures, hung in-flight work, shutdown/dormancy, infinite agency/account churn, incompatible
hosts, restore and ambiguous selector commits are outside this bound. Finite membership changes
are re-evaluated from authoritative state, but no frozen cohort or latency guarantee is invented.
Under real-sandbox's current enabled-account uniqueness, ordinary active account counts are small;
historical invalid Queued lanes still need safety cleanup, not an assumed provisioning quota.

Example with stable A/B, accounts A1/A2 and B1, both A lanes backlogged: after initial null pivots
the offers are A1, B1, A2, B1, A1, B1. A dispatch/account zero-wait miss at A1 still permits B1 on
the next committed offer; it does not impose the shared cooldown on B. This is a design trace,
not an executed test. Cross-host serialization defines the aggregate offer order, while adopted
per-host pacing and provider request admission remain separate limits. No seconds, throughput,
aggregate connection/concurrency budget, cost or interactive-latency claim follows from `A × K`.

**Deterministic acceptance plan for 1.3.2 / 1.6 — proposed, not run:**

| Case and existing seam | Required observation / fail-first proof |
|---|---|
| Older sustained A backlog / healthy B; `ClearinghouseDispatchApiTests.Fixture`, retained generation/queue endpoints | Queue at least three independent valid A originals before B using distinct claims/periods; keep A backlogged. B's oldest eligible lane receives an offer by the second committed offer for A=2. Record actual selected scopes. Original global-oldest code fails this bound; preserve failed TRX before implementation. Never manufacture several originals for the same claim to build backlog. |
| Agency-first versus account-first; fixture `AddAccountAsync` / `GenerateAsync` | In synthetic mode use valid OfficeAlly and ClaimMd lanes for A and one lane for B, respecting enabled-account uniqueness. Trace A1/B1/A2/B1 with deterministic SQL Guid order. Flat lane rotation fails equal agency opportunities; an always-first-account mutant fails the K=2 bound. Do not bypass model constraints to invent many active same-kind accounts. |
| Repeated dispatch/account contention; `IDemoWorkerResetCoordination`, SQL coordination fixtures | Hold A's exact lease with a separate session and barriers; A's offer advances pivots, adds no failure/audit/attempt, and returns LaneSkipped. B proceeds next at normal pacing. Current global head and blanket-cooldown behavior fail. Also contend only an A account while another account in A is ready. |
| Shared reset/selector contention / unknown faults; `DemoWorkerResetCoordinationTests`, scheduling fake time | No offer commit or key/upload before reset admission. Selector miss uses shared cooldown; shared/unknown errors cannot become LaneSkipped or failure counts. Hint storms cannot bypass cooldown, and expiry parks with all selection resources disposed. |
| Two independently constructed SQL hosts; `SyntheticPipelineFactory`, `SqlDemoWorkerResetCoordination` | Barrier around selector commit proves one serial pivot order, then release selector before paused upload. Another host can offer B while A uploads; same-dispatch/account exclusions and exact retained bytes/audits/attempts still hold. Do not infer SQL locking from a shared in-process object or SQLite. |
| Offer commit rollback, loss and restart; commit interception seams in `ClaimReleaseAtomicityTests` | Before-commit failure changes neither cursor; after-commit crash/response loss preserves the offer but creates no send evidence. Recreated host continues after the persisted pivots; no write replay on ambiguous success. Lost/unsupported selector state fails safely. A reset-cursor-on-start mutant fails. |
| Deferred/Held, due recovery and human reopen; `ClearinghousePreflightApiTests`, missing-key isolation regression | Exact mode/due eligibility retained; no key before due, no Held lane, no readiness reset on contention, only typed known-unsent failure increments. Reopen/due return lane to rotation without resetting others. UTC changes never override final due check. |
| State/readiness changes between probes and admission; reload/binding/revision barriers | Scoped stale candidate spends at most that one offer; bounded queries, no whole-queue Empty or unbounded retry loop. Foreign scope and changed account/profile/compliance are refused/cancelled by current preparation. Changing another agency's pivot cannot change its business revisions, credentials, bytes or audits. |
| Sending/OutcomeUnknown, late receipts and failed evidence commit; dispatch/compliance/`ClaimReleaseSqlTests` | Selection excludes uncertain work; retain single physical attempt, current claim history admission, current retained-subset/action/purpose checks and reconciliation/reset exclusions. Cursor advance is never evidence of nonreceipt. A selection-only success cannot authorize a resend. |
| Hosted integration; `ClearinghouseDispatchSchedulingTests`, ManualTimeProvider and SQL barriers | LaneSkipped respects the adopted pacing and activity limit; no new turn at exact expiry, existing in-flight work retains its safety rules, no completion-driven renewal, one startup turn after restart, zero subsequent dormant selector/due SQL. Generic shared failure retains current cooldown. Mutants removing pacing, expiry or classification must fail. |
| Query/state bounds and finite churn; command interceptor plus real SQL execution | Observe ≤5 candidate SELECTs, one existing Empty due snapshot only where applicable, ≤2 cursor writes and scalar materialization; no enumeration of all lanes or cursor writes on empty/shared contention. Add/remove/reopen lanes and wrap over removed pivots without cross-scope work. Churn cases establish safety, not the stable-cohort bound. Verify proposed index/model consistency on both EF models; inspect plans in later authorized synthetic capacity work. |

Private SQL acceptance later uses `scripts/Test-IsolatedLocalDb.ps1` with a dedicated named
fairness filter after a serial build; expand to complete API acceptance when the implementation
and schema guards are ready. Coordinate machine/private-instance resources with the second
thread. Record commands, every failure/count/skip and cleanup; do not silently substitute stand-in
locking or skip SQL cases. No app build/test, database access, benchmark or live vendor call is
needed for this documentation-only task. Existing tests are seams and prior evidence, not passing
fairness tests. Broader 1.6 load proof includes polling/note/signatures and aggregate budgets.

**Dependencies, alternatives and next bounded step:** recommend this policy over global FIFO
(backlog/head contention dominance), flat account rotation (more accounts buy an agency more
offers), process-local cursors (restart/cross-host phase bias and tenant-map lifecycle), and
holding a global selector through upload (healthy lanes wait on one dependency). Weighted/deficit
fairness needs adopted weights/cost measurements; queue infrastructure needs a separate wake/
capacity owner. Neither is warranted by this bounded design. Preserve D1–D4 and the accepted
missing-key recovery/inactivity tradeoff. The second thread's 1.4.1 facts inform later shared
budgets without blocking this policy review or authorizing new runtime controls.

Next is review/adoption of the hierarchical policy, the narrow LaneSkipped cooldown amendment,
and the additive scheduling-metadata/index source proposal under §8, then task 1.3.2's fail-first
implementation. Existing database migration, rollout/rollback/baseline operations, deployment,
activation, actual quota verification and load/alert evidence remain separately authorized work.
[Dated evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-131-dispatch-fairness-design)
owns actual source-drift and documentation checks; no measured capacity or sealed score changes.

#### October 9 — dispatch idle-wake scheduling proposal

**Retained design status at capture:** SATI-WRK-001 source inventory and concrete design complete;
runtime policy was **proposed** in [DEC-0236](docs/decisions/current/2026-10-09-DEC-0236.md).
Its pending adoption/implementation status is superseded by DEC-0237 and the implementation above.
The following preserves the original proposed reasoning/acceptance. No application,
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

**Source paths from the original W8 diagnosis:** the dated implementation sections above
own subsequent corrections; this list retains the starting failure/selection hypotheses.

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
