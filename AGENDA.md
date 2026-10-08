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
| SATI-WRK-001 | partial; note failure, HTTP deadline and agency paging implemented; stale-day cache slice eligible | W8 workload isolation, agency/account fairness, API admission, global/tenant budgets and synthetic multi-host/load proof; [worker handoff](BACKGROUND_WORKERS_HANDOFF.md), [working evidence](docs/readiness/work-evidence.md). |
| SATI-OPS-001 | activation/evidence pending | Watchdog publication, named-owner notification/absence evidence and enabled-worker expectations; [operations](OPERATIONS.md), [readiness](docs/readiness/README.md). |
| SATI-REC-001 | evidence pending | Complete-service recovery, approved RPO/RTO, protected SQL/blob/key inventories, restore/cutover/later-write/external-send reconciliation; [operations](OPERATIONS.md), [readiness](docs/readiness/README.md). |
| SATI-IDEM-001 | investigation/evidence pending | End-to-end command identity: durable scoped request keys and fingerprints, replay/race conflicts, outbox/inbox intent, lease/fencing ownership, ambiguous external sends and provider reconciliation; [protocol baseline](docs/readiness/protocol-baseline.md), [readiness method](docs/readiness/readiness-method.md). Idempotency is assessed separately from source-only assertions or literal exactly-once claims. |
| SATI-BIL-001 | investigation/repair pending | Assessment R1/R2 original/correction lifecycle and current compliance recheck before send; [billing architecture](docs/architecture/billing.md), [sandbox runbook](CLAIMMD_SANDBOX_RUNBOOK.md). Reproduce with synthetic fixtures and fail-first tests. |
| SATI-SEC-001 | planned/evidence pending | Authenticated expensive-operation/per-actor/IP/validated-agency budgets, distributed login guard, deployed least privilege/redaction and independent review; [security review](SECURITY_REVIEW_2026-09-10.md), [API audit](API_SECURITY_AUDIT.md). |
| SATI-GOV-001 | source implemented/runtime gated | Governance activation, approved policy periods and complete storage/recovery adapters; [governance runbook](RECORDS_GOVERNANCE_RUNBOOK.md). Runtime retention stays PolicyOnly. |
| SATI-CLI-001 | evidence pending | External-device/accessibility/mixed-version/clean-install acceptance and supported client/server/schema compatibility; [Demo acceptance](DEMO_ACCEPTANCE.md), [readiness](docs/readiness/README.md). |
| SATI-DB-001 | planned | Incremental model/schema owner consolidation, controlled migration/rollback and bounded summary queries; [architecture](ARCHITECTURE.md), [environment procedures](DATABASE_ENVIRONMENTS.md). |
| SATI-DOC-001 | implemented; upkeep required | Canonical documentation owners, stable IDs, dated evidence and no duplicate current inventory; [governance](docs/documentation-governance.md). Validator and mutation checks are required. |

## Next eligible work

**Next eligible item:** SATI-WRK-001

**Eligibility:** the fourth bounded local source slice is eligible under Josh's direct request
to perform remaining eligible work in sequence. Note-worker failure continuation, the connector
HTTP deadline and 100-agency keyset discovery are implemented; [W8](BACKGROUND_WORKERS_HANDOFF.md)
and [working evidence](docs/readiness/work-evidence.md) own their actual verification and limits.
The paging chunk's portable acceptance, two guarded SQL preservation proofs and updated
documentation checks passed within their recorded scope. This pointer does not authorize the whole priority or a release.
Follow [the standing workflow](AGENTS.md#standing-work-and-documentation-upkeep).

**Bounded slice:** in `NoteAbandonmentWorker.RunDueAsync`, retire `completedByAgency` entries
whose completion date is not the current `ApiClock.Today`, while holding the existing `runGate`
and before a due pass uses the cache. Preserve current-day successful entries and the existing
`allCompletedOn` no-idle-SQL check. Removed agencies from earlier dates must not remain retained
indefinitely. This bounds retention to current-day completion records after the enabled due check;
it does not impose a 100-agency cache cap or bound current-day agency churn/cardinality.

Preserve the separate 100-agency discovery and 100-note sweep limits, captured finite key range,
observed-growth due behavior and documented membership limits. Preserve narrowly recoverable
failure continuation, at-limit retries, committed counts/atomic audits, cancellation/disablement,
global SQL/reset coordination, successful-agency/day skipping, next-local-day catch-up and
default-off/hourly/no-idle-SQL cadence. Do not add a scheduler, parallelism, durable state,
provisioning invalidation, dispatch/account policy or total-pass/healthy-agency wait claim.

**Dependencies and owners:** read [W8](BACKGROUND_WORKERS_HANDOFF.md),
[DEC-0222](docs/decisions/current/2026-10-08-DEC-0222.md),
[DEC-0224](docs/decisions/current/2026-10-08-DEC-0224.md),
[worker architecture](docs/architecture/workers.md),
[failure contingencies](docs/readiness/multitenancy-contingencies.md),
[the protocol baseline](docs/readiness/protocol-baseline.md) and
[tenant boundaries](docs/architecture/identity.md). Revalidate `NoteAbandonmentWorker`, its
`runGate`, `completedByAgency` and `allCompletedOn`, `NoteAbandonmentSweep`, shared
`NoteAbandonmentRules` and `Sati.Api.Tests/NoteAbandonmentWorkerTests.cs`. No schema,
cloud, provider or working-data prerequisite is needed. The committed documentation/HTTP
foundation and paging's local source acceptance do not establish activation or capacity.

October 8 revalidated source: discovery pages are bounded, but the dictionary retains completion
dates for every previously successful agency and has no stale-date removal. This is a source
assertion to reproduce, not a measured process-memory guarantee.

**Boundaries and completion evidence:** implement only this reversible local cache/test slice
with synthetic fixtures in disposable private storage. Ordinary verified commits/pushes to the
already approved repository/branch are authorized by Josh's sequential-work request. No DATT,
activation, release, deployment, publication of a service/installer, schema or cloud/security
change, working-data access or real external call is authorized.

Use deterministic dates and a large synthetic day-one population, then remove earlier agencies
and change the day. Prove the stale-retention regression fails against the current cache and
passes after repair; record retained cache cardinality and exact agency attempts/committed
counts/audits. Current-day failed/at-limit agencies must remain due while successful agencies
are skipped on later same-day passes. Prove next-day catch-up and successful-day idle checks
still avoid SQL/coordination, including enablement/cancellation controls and unconditional
barrier cleanup. Verify both discovery and note-batch bounds remain distinct. Describe current-day
retention honestly; do not infer an absolute memory, pass-duration or fairness bound.

Retain the existing worker/rule/API and guarded SQL coordination acceptance, report actual
passes/skips and disposable instance cleanup, update W8/decisions/evidence and then advance
this pointer. Sealed readiness stays unchanged. Dispatch known-unsent hold/backoff/reopen
implementation remains blocked on its recovery policy and additive schema proposal under W8.
Total budgets, fair lane selection, API admission, aggregate capacity, provisioning invalidation
and live notification/vendor proof remain later work.

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
