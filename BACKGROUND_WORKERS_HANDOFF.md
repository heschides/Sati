# Handoff — background workers and operational watchdogs

**For:** Codex, or any implementer starting without prior context.
**Written:** 2026-10-03, against release 1.3.34 (`master` @ `9d2d06a`).
**Status:** W1–W6 source, tests, documentation, and operator scripts implemented on
`codex/background-workers`, based on merged `master` @ `89c2c1e`. Final verification completed
2026-10-03: **3,948 passed, zero failed, one local AI evaluation skipped**. W7 remains deferred.
No cloud publication, configuration change, migration, or restore rehearsal was performed.

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

Read `AGENTS.md` (or `CLAUDE.md`) first, then `ARCHITECTURE.md`, `DECISIONS.md`, and the
release 1.3.34 section at the top of `AGENDA.md`. This brief is a map with file references, not a
replacement for those documents. Where this brief and a later recorded decision disagree, the
decision wins. Record your own choices in `DECISIONS.md`.

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

These are facts about the current deployment. Design around them; do not change them.

1. **The Demo API runs on App Service Free F1 with `alwaysOn` false** (`AGENDA.md:5827`). The site
   sleeps when idle and has a 60-minute daily CPU quota. An in-process `BackgroundService` timer
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
6. **The Demo database is serverless on a free monthly allowance** (`GP_S_Gen5_2`, 0.5 minimum
   vCore, auto-pause when the allowance is exhausted; `DATABASE_ENVIRONMENTS.md`). Anything that
   queries SQL every few minutes around the clock keeps the database from pausing and spends that
   allowance. Once it runs out, Demo pauses for the rest of the month. **Nothing in this brief may
   poll SQL on a short fixed interval while nobody is using Demo.**
7. **The Function App already reports to Application Insights.** The 1.3.33 release notes
   (`AGENDA.md`) record the reset's `DEMO_COMPLIANCE_HISTORY_COMPLETE` marker arriving there. Its
   managed identity can already read `dbo` and its own storage queues.

## 3. The common worker pattern

Every server worker in this brief must:

- put its decision rule in one named owner in `Sati.Contracts.V1`, used by both the API and the
  local service;
- be idempotent and safe to run twice or concurrently: take an `sp_getapplock` per job, and leave
  scale-out to the database lock, not the host;
- catch up on wake (constraint 1) without polling SQL while idle (constraint 6). Record each
  run that changes data as **one summary audit event** per agency (counts and ids, no
  narratives), using the system actor from D3;
- work in bounded batches with a cancellation token, and stop at the batch limit;
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

## 9. Done means

- Code, fail-first tests, and a green full solution test run.
- `ARCHITECTURE.md`: the new rule owners and workers, and where each runs.
- `DECISIONS.md`: anything that departs from D1–D4, the system-actor constant's final name, and the
  abandoned-envelope policy.
- `AUDIT_EVENTS.md`: every new action. `API_AUTHORIZATION.md`: any new or rescoped route.
- `OPERATIONS.md`: the alert owner, the conditions, and the operator response for each.
- `AGENDA.md`: anything deferred, with the reason.
