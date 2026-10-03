# Handoff — background workers and operational watchdogs

**For:** Codex, or any implementer starting without prior context.
**Written:** 2026-10-03, against release 1.3.34 (`master` @ `9d2d06a`).
**Status:** scoped and verified against the code. Nothing here is implemented yet.

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

## 3. The common worker pattern

Every server worker in this brief must:

- put its decision rule in one named owner in `Sati.Contracts.V1`, used by both the API and the
  local service;
- be idempotent and safe to run twice or concurrently: take an `sp_getapplock` per job, and leave
  scale-out to the database lock, not the host;
- catch up on wake (constraint 1), and record each run that changes data as **one summary audit
  event** per agency (counts and ids, no narratives). The last such event is the run ledger: no
  new table is needed for "when did this last run";
- work in bounded batches with a cancellation token, and stop at the batch limit;
- respect optimistic concurrency: increment `Revision`, and never overwrite a row whose revision
  changed since it was read;
- log failure type, job name, and counts only. No narratives, names, payloads, tokens, or vendor
  response text;
- sit behind a configuration flag that defaults to **off**, with the flag checked on every pass;
- take its clock from `TimeProvider`/`ApiClock` so tests can drive it;
- have tests that **fail against the code before the change** (an `AGENTS.md` rule).

## 4. Decisions Josh must make — ask before building the affected item

| # | Decision | Affects | Recommended default |
|---|---|---|---|
| D1 | Where alerts go (who gets paged, by what channel) | W1, W4 | Application Insights availability test on `/health/operations`, with an action group that emails Josh. Check current pricing first. |
| D2 | Whether a wake ping is acceptable on F1 (a timer that requests `/health/live`) | W1, W2 | A ping from the existing Function App every 30–60 minutes, bounded against the CPU quota. |
| D3 | How system-initiated audit events identify their actor | W2, W3 | Follow the `0` precedent behind a named constant, and document it in `AUDIT_EVENTS.md`. |
| D4 | Approval to create Azure resources, roles, or app settings | W1, W4, W6 | Codex writes the script and code. Josh runs or approves anything that touches Azure. |

---

## 5. Work items, in priority order

### W1 — Operations watchdog and alert routing *(Significant now; Critical before a production pilot)*

**Problem.** Health checks exist, but no failure reaches a person. On 2026-10-03 a Demo reset
request was accepted (HTTP 202), reached the poison queue with no `demo.reset.*` audit, and
`/health/ready` stayed Healthy (`AGENDA.md`, release 1.3.34). `OPERATIONS.md` "Health, monitoring,
and alerts" lists alert routing as pre-pilot work. The reset writes its own outcome, so a host
crash leaves nothing behind. Detection has to look for a **missing** outcome.

**Build:**
1. An `OperationsWatchdog` rule owner in Contracts. It takes facts (last nightly outcome time,
   poison count, worker heartbeats, the expected schedule) and returns a list of named, PHI-free
   conditions with a severity.
2. An API health check (`OperationsWatchdogHealthCheck`) exposed on a **separate** route,
   `/health/operations`, not `/health/ready`. A watchdog finding must not make the API look
   unready. The anonymous response is a status word only; detail goes behind Admin
   (`/admin/operations`, `ApiEndpoints.cs:391`, extended in `AdminOperationsDto`).
3. Conditions to start with:
   - no `demo.reset.completed` or `demo.reset.failed` audit since the most recent scheduled run
     plus a grace period (e.g. 45 minutes after 03:15 Eastern);
   - the latest outcome is `demo.reset.failed`;
   - poison-queue count above zero. This needs read access to the Function App's storage queue:
     a new least-privilege role assignment (**D4**). If D4 is not granted, ship the audit-based
     conditions and leave this one documented as pending;
   - a registered API worker (W2, W4) has not reported a heartbeat within twice its interval.
4. A provisioning script for the alert (**D1**). Josh runs it; the code path must not create
   Azure resources.

**Never** replay a poison message or start a reset automatically. Alert a person.

**Accept when:** a test clock past the grace period with no outcome audit turns
`/health/operations` Unhealthy, and the Admin detail names the condition. A failed outcome does
the same. A completed outcome clears it. The anonymous route leaks no detail.
`OPERATIONS.md` names the alert owner and acknowledgement expectation.

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
   (`note.abandoned-by-system` or similar; count plus note ids). Add the action to
   `AUDIT_EVENTS.md`.
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

**Build:** watchdog conditions fed into W1's `OperationsWatchdog`:
- dispatches stuck in a non-final state beyond a threshold;
- uploads whose outcome is uncertain;
- signed requests with no prepared package after a threshold;
- notifications that exhausted retries.

Each worker reports a heartbeat. **Never** resubmit an uncertain upload or resend a notification
automatically. Surface it for an operator.

**Accept when:** each condition can be produced in a test and appears on `/health/operations` and
in the Admin detail. A disabled feature produces no condition.

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
- `DECISIONS.md`: D1–D4 outcomes, the system-actor choice, and the abandoned-envelope policy.
- `AUDIT_EVENTS.md`: every new action. `API_AUTHORIZATION.md`: any new or rescoped route.
- `OPERATIONS.md`: the alert owner, the conditions, and the operator response for each.
- `AGENDA.md`: anything deferred, with the reason.
