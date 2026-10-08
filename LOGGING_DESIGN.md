# Sati Diagnostic Logging and Support Bundle — Design

Status: **partially implemented.** As of 2026-09-10, the stable folder, PHI-minimized JSONL writer,
all managed crash handlers, per-process files, 5 MB rolling files, 30-day retention, 50 MB folder
ceiling, durable Demo incident outbox, PID/heartbeat run markers, bounded Windows Application-log
readback, unclean-run replay, and Admin incident/path surfaces exist. The error catalog,
breadcrumbs, support bundle, framework logger provider, and desktop/API correlation join remain
proposed and are tracked in `AGENDA.md`.

Goal: when Sati fails on a user's workstation, that user can hand Josh a single file that explains
what happened, and Josh can read it without a debugger, without the user's database, and without
receiving protected health information.

---

## 1. What exists today

| Piece | File | What it does |
|---|---|---|
| Crash-to-reference dialog | `App.xaml.cs:36` | Handles `DispatcherUnhandledException`, dedupes by fingerprint, shows a reference number |
| Local diagnostic record | `Services/AppErrorLog.cs` | Appends one JSON object per failure to `%LOCALAPPDATA%\SatiLogica\Sati\Logs\sati-yyyyMMdd-<pid>.jsonl` |
| Exception fingerprint | `AppErrorLog.CreateFingerprint` | One-way hash of type, HResult, and target-method shape |
| Aggregated incidents | `Data/LocalIncidentReporter.cs`, `Data/Cloud/CloudIncidentReporter.cs` | Deduplicated incident groups with severity, release, occurrence count |
| Durable send queue | `Data/Cloud/IncidentOutbox.cs` | Atomic write-then-move envelopes, quarantine for unreadable files |
| Unclean shutdown detection | `Services/ApplicationRunState.cs` | Per-agency PID/heartbeat marker; a surviving marker at next launch reports a Critical incident with a stable retry reference |
| External fault signature | `Services/WindowsCrashEventReader.cs` | After restart, queries Application Error 1000 and .NET Runtime 1026 in a heartbeat-bounded window and requires process name plus PID |
| API exception boundary and correlation | `Sati.Api/Infrastructure/ApiExceptionBoundaryMiddleware.cs`, registered first in `Program.cs` | Contains escaping request failures; logs only bounded type, HResult and `TraceIdentifier`, preserves generic errors and `X-Correlation-ID` |

Two existing decisions govern everything below and are not being reopened:

- Agency Admins see a curated incident envelope, never stack traces or exception messages
  (`DECISIONS.md`, 2026-08-13).
- Raw workstation diagnostics stay workstation-only support material and are never copied into the
  aggregated incident table (same entry).

### API request boundary — source, October 8, 2026

`ApiExceptionBoundaryMiddleware` owns escaping downstream request failures. A writable response
receives the generic `server_error` 500 DTO, correlation/security headers and no-cache/no-store
headers. The boundary logs a fixed operation label, exception type capped at 160 characters,
HResult and server correlation ID. It never gives the general logger an exception object,
message, inner exception, `Data`, stack or request body. `ApiIncidentRecorder` remains the
best-effort safe-envelope owner; failure to record does not replace the request's response.

An `OperationCanceledException` or `IOException` with an actually cancelled `RequestAborted`
token retains the 499 path without an incident. An unrelated cancellation exception receives the
generic failure path. A response that has started is aborted; secondary logger, incident, response
write and abort failures are contained without rethrowing raw exceptions through hosting.
Authentication, authorization and tenant middleware remain downstream in their existing order.
No request is re-executed and no automatic retry is added.

The previous `UseExceptionHandler` delegate supplied a raw exception to both its own logger and
framework exception diagnostics. Suppressing successful-handler diagnostics alone leaves
started-response and handler-failure paths uncovered. The replacement also removes that
middleware's exception-specific events/metrics; safe Error records, ordinary HTTP status/duration
observability and best-effort incidents remain. See the reviewed
[ASP.NET Core 10.0.12 exception handler](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.12/src/Middleware/Diagnostics/src/ExceptionHandler/ExceptionHandlerMiddlewareImpl.cs)
and [hosting diagnostics](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.12/src/Hosting/Hosting/src/Internal/HostingApplicationDiagnostics.cs).

[DEC-0227](docs/decisions/current/2026-10-08-DEC-0227.md) records the choice;
[working evidence](docs/readiness/work-evidence.md) owns fail-first proofs, actual passing results
and limits. Real Program synthetic tests observe logger messages/state/scopes/exception references
and hosting/exception diagnostic payloads, with positive request and listener observations.
Their original non-retrying SQLite context isolates request containment. A separate configured
retrying-strategy request case covers incident persistence after the request's single-attempt
filter unwinds; the execution owner below governs that path. Neither fixture establishes deployed
SQL Server incident persistence.

This is a request-boundary control. Independent EF/provider logs and diagnostic payloads,
startup failures, cancellation callbacks, response-completion callbacks, host failures and
the full enabled-sink inventory remain open. The configured API logger is JSON console; a log
level or disabled sensitive-parameter logging does not sanitize arbitrary exception payloads.
No deployment, live sink, real-data or compliance acceptance is established by this source slice.

### API incident execution — source, October 8, 2026

`IncidentAggregator.UpsertAsync` owns the complete aggregation transaction inside named
`IncidentSingleAttempt`, an EF execution strategy with zero retries. It checks cancellation and
refuses an active `ExecutionStrategy.Current` whose `RetriesOnFailure` is true before acquiring
the striped gate or creating a context. A valid existing single-attempt scope remains supported.
The existing short-lived context, Serializable transaction, query, save and both commit paths
stay together; SQL Server's existing `UPDLOCK, HOLDLOCK` query remains unchanged.

The early refusal matters because EF's outermost active strategy controls nested execution;
adding a nested zero-retry strategy cannot prevent a retrying caller from replaying the write.
See the matching [EF Core 10.0.10 implementation](https://raw.githubusercontent.com/dotnet/efcore/v10.0.10/src/EFCore/Storage/ExecutionStrategy.cs)
and [connection-resiliency guidance](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency).
Incident transactions are never automatically replayed, including an ambiguous commit failure.
An error response remains best effort if its incident sink fails; no success is inferred from
an exception after commit.

An immediate replay of the stored `LastReference` can enrich diagnostics without incrementing
the occurrence count. This is not historical reference deduplication: A, then B, then A can count
A again. No new occurrence ledger, schema, cross-process locking policy or exactly-once guarantee
is introduced. [DEC-0228](docs/decisions/current/2026-10-08-DEC-0228.md) records the choice;
[working evidence](docs/readiness/work-evidence.md) owns fail-first and acceptance results.
Synthetic strategy, attempt-count, rollback and committed-state observations do not establish
production SQL locking, a live incident service or complete logging redaction. The two-check
health boundary below separately covers caught check failures; independent provider, startup,
callback and complete enabled-sink work remains open.

### API health failures — source, October 8, 2026

`SchemaDriftHealthCheck` and `DatabaseIdentityHealthCheck` keep caught failures Unhealthy with
their fixed descriptions, no retained exception and empty failure Data. The schema check logs
only its fixed operation, type capped at 160 characters and HResult, without an exception
argument, message, inner exception, Data or stack. The identity check's fixed result remains
visible through the framework's ordinary Unhealthy Error/status record.

The matching [ASP.NET Core 10.0.12 health service](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.12/src/HealthChecks/HealthChecks/src/DefaultHealthCheckService.cs)
copies result exception/Data into report entries and supplies the exception to Unhealthy logging.
Suppressing anonymous response detail alone cannot protect those sinks. These checks therefore
remove raw exceptions at the result source while retaining useful fixed descriptions and status.
`/health/ready` stays anonymous and returns 503/`Unhealthy` for failed checks. Known missing
model-object details and healthy schema/identity results are preserved; real identity comparison,
mismatch and missing-marker validation remain in `DatabaseIdentityValidator`.

Both direct checks keep their existing operation-cancellation classification as Unhealthy,
including when the caller token is cancelled. Framework pre-cancellation still runs no probe.
There is no change to startup identity validation, `DatabaseIdentityHostedService`, health
registration, routes, schema or authorization. [DEC-0229](docs/decisions/current/2026-10-08-DEC-0229.md)
and [working evidence](docs/readiness/work-evidence.md) own fail-first/acceptance and limits.

This boundary covers caught failures in these two checks and their framework logger/result
sinks. Registration-construction failures, arbitrary other checks, logger failure, startup
identity logs, independent EF/provider diagnostics, callbacks and the complete enabled-sink
inventory remain outside its proof. Synthetic dependency faults occur before EF/provider
logging; fake identity commands exercise the actual validator without a SQL server. No complete
logging-redaction, live readiness, deployment or independent security/regulatory claim follows.

### 1.1 Gaps this design closes

1. **Resolved 2026-09-10:** UI-thread, background-thread, unobserved-task, and startup failures are
   handled. Faults that cannot execute managed code are detected by the surviving run marker at the
   next authenticated launch. When Windows recorded an Application Error 1000, its bounded
   structured signature is correlated by the persisted process name and PID. A missing or late WER
   record remains explicitly pending/unavailable instead of being misreported as “no crash.”
2. **A record names the failure but not the situation.** There is no trail of what the user was
   doing in the seconds before, so most records are unactionable.
3. **No error codes and no plain language.** A record holds `System.InvalidOperationException` and
   an HResult. Neither the user nor a support conversation can use that.
4. **No way to send anything.** Nothing in the product tells a user where the files are, and
   nothing packages them.
5. **Partially resolved 2026-09-10:** 5 MB rolling files, 30-day expiry, a 50 MB directory cap, and
   existing dispatcher fingerprint suppression are in place. A general per-fingerprint budget and
   end-of-session suppression record remain.
6. **`Microsoft.Extensions.Logging` is referenced by `Sati.csproj:114` and never used.** Framework,
   EF Core, and `HttpClient` diagnostics are discarded.
7. **Desktop and server records cannot be joined.** The desktop reference and the API correlation
   ID are unrelated values.

---

## 2. Design principles

1. **Plain language comes from a catalog, never from `Exception.Message`.** An exception message is
   data-influenced. EF, `HttpClient`, and file-system messages routinely embed parameter values,
   URLs, and paths that can carry a client name. The existing decision to exclude exception messages
   stays; readable text is looked up by code instead.
2. **One owner for the catalog.** Codes and their text live in `Sati.Contracts.V1` so the desktop
   dialog, the log record, and the bundle summary cannot drift. A second hand-written copy of an
   error's wording is a defect, per `CLAUDE.md`.
3. **The bundle is safe by construction, not by review.** Only enumerated values are ever written.
   There is no code path that copies a narrative, a person field, a token, or a connection string
   into a log, so no reviewer has to catch one.
4. **Diagnostics must work when the application does not.** The writer and the bundle builder
   depend on the file system alone: no host, no database, no API, no theme, no session.
5. **Sati never transmits a bundle on its own.** The user saves a file and chooses to send it.
6. **Logs are operational, not a record.** `AuditEvent` remains the authority for who accessed what.
   Support logs are disposable and expire on a schedule. Keeping them free of protected health
   information is what keeps them out of the retention and legal-hold obligations in
   `OPERATIONS.md`.

---

## 3. Error codes

### 3.1 Shape

```text
SATI-<AREA>-<NNNN>
```

`AREA` is a short fixed token: `APP`, `DB`, `NET`, `AUTH`, `SAVE`, `PRINT`, `AI`, `FILE`, `UI`,
`CFG`, `BILL`. `NNNN` is a zero-padded ordinal within the area. Codes are permanent. A retired code
is never reassigned; it is marked obsolete in the catalog so an old bundle stays readable.

### 3.2 Catalog entry

`Sati.Contracts.V1.SatiErrorCatalog` holds one immutable entry per code:

| Field | Purpose |
|---|---|
| `Code` | `SATI-DB-0002` |
| `Severity` | `Critical`, `Error`, `Warning` |
| `Title` | Six words or fewer, shown as a dialog heading |
| `WhatHappened` | One or two sentences a case manager can read |
| `WorkImpact` | Which of three save outcomes applies: not saved, saved but not refreshed, or unknown |
| `WhatToDoNext` | The concrete next action, including whether retrying is safe |
| `SupportHint` | One line written for Josh, not shown in the UI |
| `IntroducedIn` | Release the code first shipped in |

`WorkImpact` reuses the three-outcome vocabulary already decided for client creation in
`DECISIONS.md`. A user's first question after a crash is whether their note survived, and the
catalog is where that answer is written once.

### 3.3 Example entries

```text
SATI-DB-0002  Error
  Title:        Sati could not reach the database
  WhatHappened: Sati lost its connection to the database while saving your work.
  WorkImpact:   Your change was not saved.
  WhatToDoNext: Check that you are connected to your agency network, then try again.
                Reopening Sati is not necessary.
  SupportHint:  SqlException, connection-level. Check the run marker for a matching
                unclean exit and the breadcrumb trail for retry counts.

SATI-SAVE-0004  Critical
  Title:        A save may or may not have completed
  WhatHappened: Sati sent your change to the server but did not receive a confirmation.
  WorkImpact:   Unknown. The change may already be saved.
  WhatToDoNext: Refresh the client record before you retry, so you do not create a
                duplicate.

SATI-UNK-0000  Error
  Title:        Sati ran into an unexpected problem
  WhatHappened: Something failed in a way Sati does not yet recognize.
  WorkImpact:   Unknown.
  WhatToDoNext: Save a support report and send it to support, then reopen Sati.
```

`SATI-UNK-0000` is the fallback for any unmapped exception. It is expected to be common at first.
Its appearance in a bundle is the signal that a new catalog entry is owed.

### 3.4 Mapping

`Sati.Contracts.V1.SatiErrorMap.Classify(exception, area)` returns a code from an ordered rule list
evaluated against exception type, HResult, inner-exception type, and the sanitized area string that
call sites already pass to `AppErrorLog.Record`. The map is pure, has no dependencies, and is
covered by table-driven tests. It never inspects message text, because message text is the thing
being excluded.

The existing 12-character reference stays. It identifies one occurrence; the code identifies the
class of problem. Both appear in the dialog, the record, and the bundle.

---

## 4. The log record

### 4.1 Format

One JSON object per line, unchanged in spirit from `AppErrorLog.BuildEntry`, with fields added:

```json
{
  "schema": 2,
  "timestampUtc": "2026-09-02T14:03:11.412Z",
  "sequence": 184,
  "sessionId": "9F2C41A8",
  "reference": "A41C90DE7B12",
  "code": "SATI-DB-0002",
  "title": "Sati could not reach the database",
  "whatHappened": "Sati lost its connection to the database while saving your work.",
  "workImpact": "NotSaved",
  "severity": "Error",
  "area": "note-entry.save",
  "environment": "Demo",
  "release": "1.2.40",
  "correlationId": "7b41d0c2e9f04a1b",
  "exceptionType": "Microsoft.Data.SqlClient.SqlException",
  "hResult": "0x80131904",
  "target": "Sati.Data.Cloud.CloudNoteService",
  "innerExceptionType": "System.Net.Sockets.SocketException",
  "fingerprint": "3F9A...",
  "stackTrace": "...",
  "breadcrumbs": []
}
```

Carrying `title`, `whatHappened`, and `workImpact` in the record itself, rather than only the code,
means an old bundle stays readable after the catalog changes. The bundle is self-describing.

`stackTrace` stays. It is a frame list, not user data, and it is the single most useful field for
diagnosing a `SATI-UNK-0000`. It never leaves the workstation except in a bundle the user chooses to
send, and it is still excluded from the aggregated incident table.

### 4.2 Breadcrumbs

A bounded in-memory ring of the last 200 events, flushed into any Error or Critical record. This is
what turns "an exception occurred" into an explanation.

```csharp
public readonly record struct Breadcrumb(
    DateTime AtUtc,
    BreadcrumbKind Kind,   // Navigation, Command, Http, Database, Session, Ai, Print
    string Name,           // from a closed set of constants, never interpolated
    int? Count,            // retry number, row count, milliseconds
    string? Outcome);      // "ok", "retry", "cancelled", an HTTP status, an error code
```

The hard rule: **`Name` and `Outcome` accept constants only.** No call site interpolates a value
into a breadcrumb. A test scans call sites for non-constant arguments, so a future
`$"opened {person.FullName}"` fails the build rather than shipping a client name to a support inbox.

A useful trail looks like this, and none of it identifies anybody:

```text
14:02:58  Navigation  shell.tab.notes
14:03:02  Command     note.draft.autosave        ok
14:03:07  Http        POST /v1/notes             503   corr=7b41d0c2
14:03:09  Http        POST /v1/notes  retry 1    503
14:03:11  Database    connection.open  retry 2   fail
```

### 4.3 What is never written

Note and assessment narratives, scratchpad or journal text, person names, dates of birth, MaineCare
or Social Security numbers, addresses, phone numbers, passwords, password hashes or salts, bearer
tokens, connection strings, request or response bodies, full URLs with identifiers in the path, and
raw exception messages. Query strings are dropped; a URL is recorded as method plus route template.

This list is enforced by a redaction test that seeds each category into an exception chain and
asserts none of it appears in the written line or in a produced bundle.

---

## 5. Capturing every failure shape

| Shape | Handler | Behavior |
|---|---|---|
| UI thread exception | `DispatcherUnhandledException` (exists) | Keep the reentrancy and fingerprint dedupe. Add code lookup and breadcrumb flush. |
| Background thread | `AppDomain.CurrentDomain.UnhandledException` (exists) | Write synchronously and flush before the process dies. No dialog; the process is already terminating. |
| Unobserved task | `TaskScheduler.UnobservedTaskException` (exists) | Mark observed, record as Warning, no dialog. |
| Startup, pre-host | Existing `try/catch` in `OnStartup` | Already writes `application.startup`; add the code and environment block. |
| Process-terminating fault | None possible in managed code | Run marker records the unclean fact; Windows Application-log readback adds a matching external signature when one exists. |

Stack overflow, an access violation in a native dependency, a power loss, or a task-manager kill
give no managed handler a chance to run. `ApplicationRunState` already detects these on the next
launch. The change is to make that detection **useful**: the run marker gains a periodically
flushed tail of the last breadcrumbs, so the replay at next launch can say what the dead session was
doing, not merely that it died. Flush the tail on navigation and on command completion, not on a
timer, to keep it cheap and meaningful.

As of 2026-09-10, the run marker also carries the exact process ID, process name, session reference,
and a 30-second heartbeat. On an unclean restart, Sati queries only the ordinary Windows
**Application** channel in a window from one minute before through thirty minutes after the last
heartbeat. An Application Error 1000 is accepted only when both its structured `AppName` and
`ProcessId` match the marker. A nearby .NET Runtime 1026 contributes only a yes/no signal after that
exact Event 1000 match and only when its System `ProcessID` also matches. Sati never calls
`FormatDescription` or reads Event 1026 `EventData`, because
that prose can contain an exception message and protected information. The bounded Event 1000
fields—application/version, module/version, exception code, fault offset, event record/time, and
PID—enter the curated incident envelope through `CrashDiagnosticRules`; application and module
paths have no contract field.

WER can finish after an impatient relaunch. Sati retries once after 750 ms, records
`PendingOrUnavailable` rather than denying the unclean exit, and retains the previous marker for a
later launch. A later match uses the same support reference, replaces a queued pending envelope,
and enriches the existing incident without another count when that reference is still the group's
retained last reference. The Application log
is readable as a standard user on supported Windows installations; Sati requests no elevation and
records `ApplicationLogUnavailable` if local policy denies access.

The replay uses one stable session reference. A matched or inherently uncorrelatable legacy marker
is deleted after reporting; a pending or temporarily unreadable marker is retained. In both
incident aggregators, an immediate replay matching the retained last reference can enrich a
higher-quality result without another occurrence. Older references are not a historical
deduplication ledger; the API execution boundary above does not enlarge that guarantee.

---

## 6. Framework logging

Wire `Microsoft.Extensions.Logging` into the WPF host with a `SatiFileLoggerProvider` that writes
the same JSONL, under two restrictions:

1. **Category allowlist.** Only `Sati.*` categories emit message text, because Sati controls what
   those templates contain. Framework categories such as `Microsoft.EntityFrameworkCore` and
   `System.Net.Http` are recorded at Warning and above as category, event ID, level, and exception
   type chain only. Their message text and template arguments are dropped.
2. **`EnableSensitiveDataLogging` is never enabled outside a Debug build**, and a test asserts it.
   EF parameter logging would put client field values straight into a file the user is being asked
   to email.

This is deliberately conservative. The value of framework logging here is knowing that EF retried
three times or that a socket was reset, and that survives without message text.

---

## 7. Files, rotation, and retention

Directory stays `%LOCALAPPDATA%\SatiLogica\Sati\Logs\`.

| Concern | Rule |
|---|---|
| File name | `sati-yyyyMMdd-<pid>.jsonl`. Per-process files remove cross-process append contention, which the current single in-process lock does not cover. |
| File size | Roll at 5 MB to `-001`, `-002`. |
| Directory size | 50 MB ceiling, oldest file deleted first. |
| Age | Delete files older than 30 days, once per launch, on a background thread. |
| Repeat storm | Per-fingerprint budget of 20 records per session, then one suppression record naming the count at session end. |
| Write failure | Never throws, never retries in a loop, degrades to `Debug.WriteLine` exactly as today. |
| Redirected AppData | Detect a OneDrive-redirected `LOCALAPPDATA` and record it in the environment block. It is a real cause of file-lock failures on managed workstations. |

Rotation runs before the first write of a session so a disk that filled overnight recovers on
restart rather than staying stuck.

---

## 8. The support bundle

### 8.1 Contents

A single zip named `Sati-Support-<release>-<yyyyMMdd-HHmm>-<reference>.zip`:

```text
README.txt            What this file is, what it contains, what it does not contain,
                      and where to send it. Written for the user.
summary.txt           Human-readable. Each error: local time, code, title, what happened,
                      work impact, what to do next, occurrence count. Newest first.
environment.txt       Release, build configuration, Demo or Production, Windows version and
                      build, .NET version, culture, RAM, free disk, display scaling and
                      monitor layout, theme, install path, elevation, AppData redirection.
runstate.txt          Recent session starts and whether each ended cleanly.
logs/*.jsonl          Raw records for the selected window.
user-description.txt  Optional. Only present if the user typed one.
manifest.json         Every entry with its SHA-256, the bundle schema version, and the
                      redaction policy version.
```

`summary.txt` is the reason for the whole design. It is the part Josh reads first and the part the
user can read before sending, which is what makes sending it feel safe.

```text
Sati support report
Release 1.2.40 (Demo)   Windows 11 26200   Generated 2026-09-02 10:14 local

1.  Sep 2, 10:03 AM   SATI-DB-0002   Sati could not reach the database   (3 times)
    Sati lost its connection to the database while saving your work.
    Your change was not saved.
    Reference A41C90DE7B12.
    Just before: notes tab, autosave ok, POST /v1/notes returned 503 twice.
```

### 8.2 Window and size

Default window is the last 7 days or the last 3 sessions, whichever is longer. A "since Sati last
started" option exists for a reproduced crash. Hard cap of 10 MB; oldest raw records are dropped
first and `summary.txt` states that they were dropped.

### 8.3 Entry points

1. **The crash dialog.** A "Save a report for support" button next to OK. After a crash the user
   may never reach Settings, and this is the moment they are motivated.
2. **Settings, a new Support tab.** Visible to every role, unlike Maintenance, which is Admin-only
   destructive tooling. Shows the log folder path, the retention rule, a plain statement of what is
   and is not collected, and the Save button.
3. **`installer/Collect-SatiSupportBundle.ps1`.** For the case Sati will not start at all. It
   follows the existing precedent of `Build-LocalDbDiagnostic.ps1`, reads the same folders, and
   produces the same zip layout minus anything requiring the running process.

### 8.4 Consent and the free-text box

Before writing, show the exact file list and a plain statement: no client names, no note text, no
passwords, and no database contents are included. Nothing is sent automatically.

The optional "What were you doing?" box is worth including, with a visible caution not to type
client names, and it is stored as its own file and labeled `user-authored` in the manifest. The
honest tradeoff: it is the one part of the bundle Sati cannot guarantee is free of protected health
information. Excluding it does not remove the risk, because a user who wants to explain will type
the same sentence into the email body instead. Keeping it inside the bundle, isolated and labeled,
at least makes it visible and disposable. `README.txt` tells Josh to treat that one file as
potentially containing protected health information and to delete the bundle on the same schedule as
any support material.

### 8.5 Delivery

Phase 1 is save-and-send by the user. No upload.

A later `POST /v1/support-bundles` upload is possible: authenticated as the actor, tenant-scoped,
size-capped, hash-verified against the manifest, and audited on receipt. It should not be built with
the first slice. It adds server storage, retention, legal hold, and access review, all of which are
already open items in `OPERATIONS.md`, and none of which are needed to answer why Sati crashed on
one workstation.

---

## 9. Joining desktop and server records

`CloudApiClient` sends `X-Correlation-ID` on every request, generated per request and recorded in
the breadcrumb with the resulting status. `Sati.Api` honors an inbound value when it is well formed,
meaning hexadecimal and 64 characters or fewer, and otherwise generates its own, then echoes it as
it does now. A Demo bundle then carries the exact identifiers needed to find the matching
server-side JSON console lines. Validating the inbound value matters: it is a caller-controlled
string that lands in server logs, so it must be constrained before it is trusted as a log field.

---

## 10. Where the code lives

```text
Sati.Contracts/V1/SatiErrorCodes.cs      Code constants
Sati.Contracts/V1/SatiErrorCatalog.cs    Text, severity, impact, next action
Sati.Contracts/V1/SatiErrorMap.cs        Exception to code, pure and table-driven
Services/Diagnostics/SatiLog.cs          Writer, replaces AppErrorLog, same folder and format
Services/Diagnostics/BreadcrumbTrail.cs  Bounded ring, thread-safe
Services/Diagnostics/LogRetention.cs     Rotation, caps, pruning
Services/Diagnostics/SupportBundleBuilder.cs
Services/Diagnostics/EnvironmentSnapshot.cs
ViewModels/SupportViewModel.cs           Settings Support tab
installer/Collect-SatiSupportBundle.ps1
```

The catalog belongs in `Sati.Contracts.V1` for the reason `CLAUDE.md` gives: both hosts need it, and
a rule enforced in two places is enforced two ways. The API returns codes from the same catalog in
its problem responses, so a server-side failure and its desktop record name the same thing.

**One acknowledged tension.** `CLAUDE.md` forbids service-locator access, and the crash path cannot
depend on dependency injection, because the container may be the thing that failed. The resolution
is that the writer stays a static infrastructure primitive in the same category as
`Debug.WriteLine`, documented as a deliberate exception, while everything with a real dependency
graph, meaning the bundle builder, the environment snapshot, and the view model, is injected
normally. The static writer takes a swappable sink so tests never touch the real folder.

---

## 11. Tests required before this ships

Per `CLAUDE.md`, each of these must be shown to fail against the unfixed code.

1. **Catalog integrity.** Every code has all fields, codes are unique, no retired code is reused,
   and every code referenced by `SatiErrorMap` exists.
2. **Redaction.** Exceptions seeded with a connection string, a bearer token, a person name, and
   note text produce a record and a bundle containing none of them.
3. **No interpolated breadcrumbs.** Call-site scan fails on a non-constant `Name` or `Outcome`.
4. **EF sensitive logging off.** Asserted for Release and Demo configurations.
5. **Rotation and retention.** Size caps, age pruning, per-fingerprint suppression, and the
   suppression-count record.
6. **Crash-handler coverage.** Background-thread and unobserved-task failures each produce exactly
   one record.
7. **Unclean-shutdown replay.** The previous session's breadcrumbs are recorded once, and a crash
   during replay does not duplicate them.
8. **Two processes writing at once** produce two readable JSONL files with no interleaved lines.
9. **Bundle without a host.** The builder produces a valid zip with no container, no database, and
   no network, and the manifest hashes match the entries.
10. **Bundle size cap.** Oldest records drop first and the summary says so.
11. **External-fault redaction.** Synthetic Event 1000 XML containing paths and PHI-like extra
    fields persists only the allowlisted structured metadata; Event 1026 prose has no read path.
12. **Exact Windows correlation and late-WER upgrade.** Both PID and full process name must match;
    pending readback upgrades under the same reference without counting another crash.

---

## 12. Phasing

| Phase | Content | Rough size |
|---|---|---|
| 1 | Error codes, catalog, map, schema-2 record, all four crash handlers, rotation and retention | Largest slice; delivers a readable log even with no UI |
| 2 | Breadcrumbs, run-marker tail, unclean-session replay | Medium; this is where crashes start being explainable |
| 3 | Support bundle, crash-dialog button, Settings Support tab, PowerShell fallback | Medium; the user-facing goal |
| 4 | Framework logger provider, correlation ID join | Small; sharpens phase 1 |

Phase 3 is the stated goal, but shipping it before phase 1 would hand Josh a zip full of
`InvalidOperationException` with no situation attached. Phases 1 and 2 are what make the bundle
worth opening.

---

## 13. Non-goals and follow-ups

- **Not an audit trail.** `AuditEvent` remains authoritative for record access.
- **Not a change to the incident pipeline.** The aggregated table keeps its curated envelope. The
  bundle stays workstation-only material, consistent with the 2026-08-13 decision.
- **Not availability or crash-free-session measurement.** Incident Health v1 explicitly does not
  claim that, and this design does not add the safe denominators it would need.
- **Windows Error Reporting local dumps** are useful for a native or stack-overflow fault, but they
  are a machine-level registry setting. If they are ever wanted, they are a documented step a user
  or their IT department performs deliberately, never something Sati configures. Phase 2 leaves
  LocalDumps disabled: memory dumps can contain PHI and require an explicit retention, access, and
  handling decision outside this diagnostic envelope.
- **`REGULATORY_CONCERNS.md` needs a line** once phase 3 ships, stating that the support bundle is
  a user-initiated export designed to contain no protected health information, and naming the one
  user-authored file as the exception.
