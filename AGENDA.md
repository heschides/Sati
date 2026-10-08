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
| SATI-BIL-001 | residual export-error slice eligible; R1/full queue/send R2 open | Assessment R1/R2 original/correction lifecycle and current compliance release gates; [billing architecture](docs/architecture/billing.md), [sandbox runbook](CLAIMMD_SANDBOX_RUNBOOK.md). Reproduce with synthetic fixtures and fail-first tests. |
| SATI-SEC-001 | planned/evidence pending | Authenticated expensive-operation/per-actor/IP/validated-agency budgets, distributed login guard, deployed least privilege/redaction and independent review; [security review](SECURITY_REVIEW_2026-09-10.md), [API audit](API_SECURITY_AUDIT.md). |
| SATI-GOV-001 | source implemented/runtime gated | Governance activation, approved policy periods and complete storage/recovery adapters; [governance runbook](RECORDS_GOVERNANCE_RUNBOOK.md). Runtime retention stays PolicyOnly. |
| SATI-CLI-001 | evidence pending | External-device/accessibility/mixed-version/clean-install acceptance and supported client/server/schema compatibility; [Demo acceptance](DEMO_ACCEPTANCE.md), [readiness](docs/readiness/README.md). |
| SATI-DB-001 | planned | Incremental model/schema owner consolidation, controlled migration/rollback and bounded summary queries; [architecture](ARCHITECTURE.md), [environment procedures](DATABASE_ENVIRONMENTS.md). |
| SATI-DOC-001 | implemented; upkeep required | Canonical documentation owners, stable IDs, dated evidence and no duplicate current inventory; [governance](docs/documentation-governance.md). Validator and mutation checks are required. |

## Next eligible work

**Next eligible item:** SATI-BIL-001

**Eligibility:** a bounded shared-rule repair is eligible under Josh's direct request to perform
remaining eligible work in sequence. The note worker's daily cache rotation is implemented and
its fail-first/main/class and updated documentation checks passed within the scope recorded in
[working evidence](docs/readiness/work-evidence.md). This billing slice is
independent of those unchanged coordination/business-rule owners. Follow
[the standing workflow](AGENTS.md#standing-work-and-documentation-upkeep).

**Bounded slice:** `Sati.Contracts.V1.BillingExportGate.Evaluate` must retain the caller's
authoritative residual `complianceErrors` even when the frozen claim and source have a complete
matching stored supervisory exception. That list already accounts for exact-obligation exceptions
and Admin recovery; the export gate validates source/exception integrity and must not discard
remaining blockers or recompute a weaker decision from today's mask. Keep this one shared rule
as the API and transitional local export/replay owner.

Preserve legitimate exact-obligation exceptions and Admin recovery when their residual error
list is empty, current actor/tenant/source/date/approval checks, invalid-configuration refusal,
generation/replay identity, immutable retained financial/file bytes, transaction/audit behavior
and typed safe refusal. Do not change exception authority, grant a blanket waiver, rewrite frozen
evidence or duplicate compliance logic in callers.

**Dependencies and owners:** read [billing architecture](docs/architecture/billing.md),
[the billing requirements](PAYER_BILLING_REQUIREMENTS.md),
[the sandbox runbook](CLAIMMD_SANDBOX_RUNBOOK.md),
[the contingency owner](docs/readiness/multitenancy-contingencies.md),
[the protocol baseline](docs/readiness/protocol-baseline.md) and
[working evidence](docs/readiness/work-evidence.md). Revalidate `BillingExportGate`,
`BillingComplianceGate`, `BillingComplianceException` and service-date policy/recovery owners;
API `ApiEndpoints.BillingExport.cs`, local `Data/Billing/IdeService.cs` and the existing
`BillingExportGateTests`, `BillingExportComplianceTests` and `LocalBillingExportComplianceTests`.
No schema, cloud, provider or working-data prerequisite is needed. Existing stored exception
semantics govern this repair; unsupported new policy cases remain decisions for their owners.

October 8 revalidated source: `BillingExportGate` adds `complianceErrors` only in the branch
without a stored exception. API and local callers already calculate authoritative remaining
errors, so a complete exception can suppress an unrelated/revoked obligation's blocker during
new generation or replay. Reproduce the gap before changing the shared rule.

**Boundaries and completion evidence:** implement only this reversible shared-rule/test slice
with synthetic fixtures in disposable private storage. Ordinary verified commits/pushes to the
approved repository/branch remain authorized. No DATT, activation, release, deployment, schema,
cloud/security change, working-data access, real provider call or financial transmission is authorized.

Retain failing-before/fixed-after evidence for the pure residual-error regression and API/local
fresh-generation/replay cases. Use a complete exact PCP exception, then revoke an independently
required Comprehensive Assessment attestation not selected by that exception. Both export and
same-key replay must refuse with the remaining blocker, commit no new generation/audit/submission
event and preserve exact frozen claim/file/name evidence. Positive complete-exception and Admin
recovery fixtures must remain releasable and replay the original bytes when no blocker remains.
Run focused shared/API/local compliance acceptance, report actual results/skips and evidence limits,
update canonical owners/decisions/evidence and advance this pointer. Sealed readiness stays unchanged.

This addresses the shared residual-error release gate only. Assessment R1 accepted-original
lifecycle and full queue/pre-send R2 compliance recheck remain open; no vendor/payer acceptance
is inferred. W8 current-day cache cardinality/churn, provisioning invalidation, total budgets,
fair lane selection, API admission, aggregate capacity and live progress proof remain open.
Dispatch known-unsent hold/backoff/reopen implementation retains its policy/schema blocker.

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
