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
| SATI-WRK-001 | partial; note failure and HTTP deadline slices implemented; agency discovery slice eligible | W8 workload isolation, agency/account fairness, API admission, global/tenant budgets and synthetic multi-host/load proof; [worker handoff](BACKGROUND_WORKERS_HANDOFF.md), [working evidence](docs/readiness/work-evidence.md). |
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

**Eligibility:** ready for the third bounded local implementation below. Note-worker recoverable
failure continuation and the connector HTTP deadline are implemented; [W8](BACKGROUND_WORKERS_HANDOFF.md)
and [working evidence](docs/readiness/work-evidence.md) own acceptance and limits. This is the
next-work pointer, not an invocation or permission for the entire priority. Follow the workflow in
[AGENTS.md](AGENTS.md#standing-work-and-documentation-upkeep).

**Bounded slice:** replace `NoteAbandonmentWorker.RunDueAsync`'s unbounded ordered agency-ID
`ToListAsync` with keyset discovery pages of at most a named 100 agencies. Capture an initial
nullable maximum agency ID and traverse that finite key range. Use a nullable initial cursor
with no first-page lower-bound predicate, so damaged nonpositive IDs retain existing failure
behavior rather than being silently skipped. Advance to each page's last observed ID, including
completed agencies, and preserve the distinct 100-note per-agency sweep bound.

The upper bound is a finite key range, not a frozen membership snapshot. Agencies can appear or
disappear between queries; lower-ID inserts/reseeding behind the cursor are not guaranteed that
pass. At range completion, a bounded existence check for IDs above the upper bound leaves the
day due if growth is observed, allowing the existing hourly pass to discover them. For an
initially empty range, use a bounded existence check for any agency. These checks cannot
protect within-range inserts after the last discovery query, lower-ID growth behind the cursor,
or inserts after the final check. Complete provisioning/
membership cache invalidation is deferred; do not claim all start-time/new agencies were visited.

Preserve global SQL/reset coordination, current recoverable-failure policy, cancellation,
disablement, committed-count/atomic-audit semantics, successful-agency/day skipping, next-day
behavior, default-off settings and hourly/no-idle-SQL cadence. Bound per-query agency-ID
materialization only. This slice does not bound the day cache, total turns/time or healthy-agency
wait, add parallelism or change dispatch/account scheduling.

**Dependencies and owners:** the first two local slices are complete within their evidence
limits. Read [W8](BACKGROUND_WORKERS_HANDOFF.md), [DEC-0222](docs/decisions/current/2026-10-08-DEC-0222.md),
[worker architecture](docs/architecture/workers.md), [failure contingencies](docs/readiness/multitenancy-contingencies.md),
[the protocol baseline](docs/readiness/protocol-baseline.md), [tenant boundaries](docs/architecture/identity.md)
and [the dated inventory](DATABASE_ENVIRONMENTS.md). Inspect `NoteAbandonmentWorker`,
`NoteAbandonmentSweep`, shared `NoteAbandonmentRules`, existing coordination/reset proofs and
`Sati.Api.Tests/NoteAbandonmentWorkerTests.cs`; reuse the synthetic fixture's
`DatabaseCommandInterceptor` seam. No schema, vendor or cloud prerequisite is needed.

October 8 revalidated source: `RunDueAsync` materializes all ordered IDs before the loop;
completion uses the per-agency day cache and `allCompletedOn`. These are source assertions,
not dynamic-membership or runtime evidence; revalidate before implementation.

**Boundaries and completion evidence:** implement only this reversible local worker/test slice
and synthetic tests in disposable private storage under a valid next-work invocation or direct
instruction. No activation, schema change, release, deployment, publication, cloud/security
change, working-data access or real external call.

With at least 251 stable synthetic agencies and a reader/materialization observer, prove no
page materializes more than 100 IDs and each stable agency is visited once with exact committed
counts/audits. Demonstrate the page-bound regression fails against the current unbounded query.
Put a recoverable agency fault early and healthy work in a later page: progress continues, the
failed/at-limit agencies remain due and completed agencies are skipped on the later pass.
Deterministic barriers must prove cancellation/disablement between pages stops later-page work
and leaves the day due, with unconditional test cleanup. Preserve damaged nonpositive-ID
failure behavior. Insert a higher-ID agency after the upper-bound capture: the finite range
terminates; observed growth leaves the day due, and a later hourly pass visits it while skipping
completed agencies. Record the narrower guarantee and unobserved/lower-ID insertion limits.

Retain existing worker/rule/API/SQL coordination acceptance and report actual passes/skips and
disposable instance cleanup. Update W8, ownership/decisions as needed and dated working evidence,
then advance this pointer. Sealed readiness stays unchanged. Dispatch poison isolation is
blocked on the durable known-unsent hold/backoff/reopen policy and additive schema proposal
recorded in W8; total budgets, fair lane selection, API admission, aggregate capacity and live
notification/vendor proof remain later work.

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
