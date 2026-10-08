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
| SATI-SEC-001 | request boundary implemented; incident execution scope eligible; other security evidence pending | Authenticated expensive-operation/per-actor/IP/validated-agency budgets, distributed login guard, deployed least privilege/redaction and independent review; [logging owner](LOGGING_DESIGN.md), [security review](SECURITY_REVIEW_2026-09-10.md), [API audit](API_SECURITY_AUDIT.md). |
| SATI-GOV-001 | source implemented/runtime gated | Governance activation, approved policy periods and complete storage/recovery adapters; [governance runbook](RECORDS_GOVERNANCE_RUNBOOK.md). Runtime retention stays PolicyOnly. |
| SATI-CLI-001 | evidence pending | External-device/accessibility/mixed-version/clean-install acceptance and supported client/server/schema compatibility; [Demo acceptance](DEMO_ACCEPTANCE.md), [readiness](docs/readiness/README.md). |
| SATI-DB-001 | planned | Incremental model/schema owner consolidation, controlled migration/rollback and bounded summary queries; [architecture](ARCHITECTURE.md), [environment procedures](DATABASE_ENVIRONMENTS.md). |
| SATI-DOC-001 | implemented; upkeep required | Canonical documentation owners, stable IDs, dated evidence and no duplicate current inventory; [governance](docs/documentation-governance.md). Validator and mutation checks are required. |

## Next eligible work

**Next eligible item:** SATI-SEC-001

**Eligibility:** the bounded incident execution-scope repair follows the implemented request
exception boundary under Josh's direct request to perform remaining eligible local work in
sequence. [Working evidence](docs/readiness/work-evidence.md) owns actual fail-first/passing
results and limits for both the billing residual-error repair and request boundary. Billing R1
and full queue/pre-send R2 remain open.
Follow [the standing workflow](AGENTS.md#standing-work-and-documentation-upkeep).

**Bounded slice:** make direct `IncidentAggregator.UpsertAsync` execute its complete explicit
transaction inside a named zero-retry EF execution scope. Refuse an already-active retrying
execution strategy before gate/context/transaction/write work, because a nested strategy cannot
replace an active outer scope. Preserve valid existing zero-retry request scopes, short-lived
contexts, the existing striped gate, SQL transaction/locking and immediate same-reference replay
semantics. Do not automatically replay incident transactions, including ambiguous commit failures.

**Dependencies and owners:** read [the logging design](LOGGING_DESIGN.md),
[regulatory posture](REGULATORY_CONCERNS.md),
[the API audit](API_SECURITY_AUDIT.md),
[the security review](SECURITY_REVIEW_2026-09-10.md),
[the contingency owner](docs/readiness/multitenancy-contingencies.md) and
[working evidence](docs/readiness/work-evidence.md). Revalidate `Program.cs`,
`ApiIncidentRecorder`'s private fingerprint, `IncidentAggregator`, `SingleAttemptWriteFilter`,
the incident DTO/sanitization owners and `Sati.Api.Tests/IncidentAggregationExecutionTests.cs`.
Revalidate the configured EF package's `ExecutionStrategy.Current` and transaction guard.
No schema, cloud, provider or working-data prerequisite is needed. Preserve authentication/tenant
middleware and the implemented safe request boundary.

October 8 revalidated source: `Program` enables SQL retry and `IncidentAggregator` begins an
explicit transaction without an execution scope. Direct aggregation under a configured retrying
synthetic strategy fails before a successful write; an active retrying outer scope currently
fails to refuse work. Both intended regressions failed against the unchanged aggregator. Preserve
the fail-first proof and final executed counts in the ledger; no automatic retries are permitted
as a shortcut to passing the direct case.

**Boundaries and completion evidence:** implement only this reversible request-boundary/test
slice with synthetic disposable storage. Ordinary verified commits/pushes to the approved branch
remain authorized. No DATT, activation, release, deployment, schema, cloud/security change,
working-data access or real external call is authorized. Keep sealed readiness/history unchanged.

Acceptance covers direct persistence with configured retry enabled, no-work refusal inside a
retrying outer scope, valid single-attempt outer scope, immediate same-reference/new-reference
counts, cancellation before work and after an actual write, retriable save failure with rollback,
post-commit acknowledgement failure with no automatic replay, gate release and unconditional
cleanup. Count context/transaction/query/save/commit attempts independently and inspect committed
state through a fresh context. Run focused existing incident/request-boundary tests. Synthetic
SQLite strategy/transaction observations do not establish production-engine locks or live operation.

The request-boundary fixtures intentionally remain non-retrying SQLite to isolate HTTP
containment. This repair does not establish historical occurrence deduplication: only the stored
last reference is replayed without increment. Health-check exception redaction follows next with
its own fail-first direct/framework sink proof. Independent EF/provider logs, health/startup,
callback/on-completed paths and the complete sink inventory remain open. No full logging-redaction
or deployed incident-persistence claim follows from either local slice. Admission/rate limits,
structural tenancy, billing lifecycle/send gates, recovery and independent review retain their owners.

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
