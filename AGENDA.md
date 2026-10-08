# Sati — Active agenda

**Consolidated October 8, 2026.** Stable IDs below are current priorities, not new implementation
authorization. Read the topic owner and [readiness registry](docs/readiness/README.md) before work.
Historical completions/releases remain in the [byte-preserved agenda](docs/archive/2026-10-08/AGENDA.md).

## Release 1.3.37 — source version reference

This heading preserves the release-preflight lookup. Exact release outcomes, commands, hashes and
remaining historical checkpoints belong to the [captured release record](docs/archive/2026-10-08/AGENDA.md),
not this active backlog. The [environment inventory](DATABASE_ENVIRONMENTS.md) records the later
October 8 firewall-removal evidence; historical unchecked release items were not silently closed.

## Current priorities

| Stable ID | Status | Work and acceptance owner |
|---|---|---|
| SATI-TEN-001 | planned | Structural tenant owners/composite constraints and shared/database-per-tenant/hybrid decision; [architecture](docs/architecture/identity.md), [structural review](SATI_STRUCTURAL_REVIEW_2026-09-28.md). Preserve current caseload/capability checks. |
| SATI-WRK-001 | partial; note failure, HTTP deadline, agency paging and daily cache rotation implemented | W8 workload isolation, agency/account fairness, API admission, global/tenant budgets and synthetic multi-host/load proof; [worker handoff](BACKGROUND_WORKERS_HANDOFF.md), [working evidence](docs/readiness/work-evidence.md). |
| SATI-OPS-001 | activation/evidence pending | Watchdog publication, named-owner notification/absence evidence and enabled-worker expectations; [operations](OPERATIONS.md), [readiness](docs/readiness/README.md). |
| SATI-REC-001 | evidence pending | Complete-service recovery, approved RPO/RTO, protected SQL/blob/key inventories, restore/cutover/later-write/external-send reconciliation; [operations](OPERATIONS.md), [readiness](docs/readiness/README.md). |
| SATI-IDEM-001 | investigation/evidence pending | End-to-end command identity: durable scoped request keys and fingerprints, replay/race conflicts, outbox/inbox intent, lease/fencing ownership, ambiguous external sends and provider reconciliation; [protocol baseline](docs/readiness/protocol-baseline.md), [readiness method](docs/readiness/readiness-method.md). Idempotency is assessed separately from source-only assertions or literal exactly-once claims. |
| SATI-BIL-001 | residual export-error repair implemented; R1/full queue/send R2 open | Assessment R1/R2 original/correction lifecycle and current compliance release gates; [billing architecture](docs/architecture/billing.md), [sandbox runbook](CLAIMMD_SANDBOX_RUNBOOK.md). Five intended regressions and focused source/synthetic acceptance are recorded in working evidence; no complete send-gate closure. |
| SATI-SEC-001 | escaping request-exception boundary eligible; other security evidence pending | Authenticated expensive-operation/per-actor/IP/validated-agency budgets, distributed login guard, deployed least privilege/redaction and independent review; [logging owner](LOGGING_DESIGN.md), [security review](SECURITY_REVIEW_2026-09-10.md), [API audit](API_SECURITY_AUDIT.md). |
| SATI-GOV-001 | source implemented/runtime gated | Governance activation, approved policy periods and complete storage/recovery adapters; [governance runbook](RECORDS_GOVERNANCE_RUNBOOK.md). Runtime retention stays PolicyOnly. |
| SATI-CLI-001 | evidence pending | External-device/accessibility/mixed-version/clean-install acceptance and supported client/server/schema compatibility; [Demo acceptance](DEMO_ACCEPTANCE.md), [readiness](docs/readiness/README.md). |
| SATI-DB-001 | planned | Incremental model/schema owner consolidation, controlled migration/rollback and bounded summary queries; [architecture](ARCHITECTURE.md), [environment procedures](DATABASE_ENVIRONMENTS.md). |
| SATI-DOC-001 | implemented; upkeep required | Canonical documentation owners, stable IDs, dated evidence and no duplicate current inventory; [governance](docs/documentation-governance.md). Validator and mutation checks are required. |

## Next eligible work

**Next eligible item:** SATI-SEC-001

**Eligibility:** the bounded escaping request-exception boundary is eligible under Josh's direct
request to perform remaining eligible local work in sequence. The shared billing residual-error
repair is implemented; [working evidence](docs/readiness/work-evidence.md) owns its actual
fail-first/passing results and limits. That repair leaves R1 and full queue/pre-send R2 open.
Follow [the standing workflow](AGENTS.md#standing-work-and-documentation-upkeep).

**Bounded slice:** contain exceptions escaping downstream request execution inside the real
`Sati.Api/Program.cs` pipeline before ASP.NET Core's exception handling/hosting observability can
receive the raw exception. Preserve a generic 500 `server_error` response and correlation identity
when a response can be written, bounded content-free logging and the existing safe incident
envelope. Do not pass unrestricted exception objects, messages, inner exceptions or `Data` to
general log/diagnostic sinks. A client-aborted request keeps its 499 handling; an unrelated
`OperationCanceledException` remains an unexpected failure. When a response has started or a
secondary handler/write fails, contain the failure and abort safely instead of rethrowing raw
failure data through hosting.

**Dependencies and owners:** read [the logging design](LOGGING_DESIGN.md),
[regulatory posture](REGULATORY_CONCERNS.md),
[the API audit](API_SECURITY_AUDIT.md),
[the security review](SECURITY_REVIEW_2026-09-10.md),
[the contingency owner](docs/readiness/multitenancy-contingencies.md) and
[working evidence](docs/readiness/work-evidence.md). Revalidate `Program.cs`,
`ApiIncidentRecorder`, `IncidentAggregator`, `ExceptionFingerprint`, the incident DTO/sanitization
owners and `Sati.Api.Tests/ApiExceptionRedactionTests.cs`. No schema, cloud, provider or working-data
prerequisite is needed. Preserve authentication/tenant middleware and safe staff-facing errors.

October 8 revalidated source: `UseExceptionHandler` sends the unrestricted failure to a general
logger and framework handling can report it before/around the safe response. Test-only middleware
inside the actual authenticated Program pipeline reproduces four synthetic raw-narrative
exposures through captured logger/DiagnosticListener observations. Preserve the failing-before
proof and final executed counts in the evidence ledger; do not infer redaction from response
body text or a log level alone.

**Boundaries and completion evidence:** implement only this reversible request-boundary/test
slice with synthetic disposable storage. Ordinary verified commits/pushes to the approved branch
remain authorized. No DATT, activation, release, deployment, schema, cloud/security change,
working-data access or real external call is authorized. Keep sealed readiness/history unchanged.

Acceptance covers nested and uncancelled operation failures, response-started and secondary
incident/write failures, actual downstream execution and authenticated scope. Assert generic
response/correlation where writable, safe incident identity in the isolated supported fixture,
no raw exception reference or synthetic narrative in captured log messages/state/exception trees
or diagnostic payloads, and unconditional cleanup. Retain request-aborted 499 and normal positive
request behavior; run focused existing incident/health boundary checks with honest limits.

The boundary fixtures use non-retrying synthetic SQLite to isolate this request behavior.
Production incident persistence is separately unresolved: `Program` enables SQL retry while
`IncidentAggregator.UpsertAsync` starts an explicit transaction without an outer execution scope;
`ApiIncidentRecorder` can contain a recording failure with no incident committed. A separate
fail-first one-attempt incident execution-scope repair follows this slice. Do not credit the
SQLite incident positive as evidence that production recording works. Independent EF/provider
logs, health/startup diagnostics, callback/on-completed paths and a complete log inventory remain
open; this pointer does not claim complete logging redaction. Admission/rate limits, structural
tenancy, billing lifecycle/send gates, recovery and independent review retain their owners.

## Preserved open-work inventory

[Legacy open-item registry](docs/backlog/legacy-open-items.md) contains **454** original
unchecked tasks with stable LEG IDs, original text, heading context and original line numbers.
They require revalidation: some old checkboxes conflict with later implemented/superseded records.
No item was dropped or declared fixed merely to shorten this agenda. Promote, merge, supersede or
close each legacy ID only with an explicit disposition, canonical active ID and dated evidence.

Deferred domain/product work remains in its existing canonical topic owner and the legacy registry.
Do not expand into future Karuna/OADS/mobile work just because a historical roadmap mentions it.

## Change discipline

Preserve IDs when wording/status changes. Record owner, acceptance criteria, evidence and reason
for deferral; update readiness separately when activation requires operator/cloud/vendor evidence.
Completed records move to a dated archive with a disposition link, rather than disappearing.
Never use a checked box or aggregate test count as proof of a live service or legal clearance.
