# Sati — Current architecture index

**Reviewed documentation baseline:** October 8, 2026. This index distinguishes implemented
boundaries from planned work. Deployment observations belong only to
[DATABASE_ENVIRONMENTS.md](DATABASE_ENVIRONMENTS.md); activation/evidence states belong to
[the readiness registry](docs/readiness/README.md). Historical feature/release paragraphs are
retained in the [architecture snapshot](docs/archive/2026-10-08/ARCHITECTURE.md).

## Operating boundaries

Synthetic Demo: WPF ViewModel → feature interface → Cloud HTTP service → Sati.Api → shared
rules/application workflow → ApiDbContext → SQL. The personal local working environment uses
local EF service implementations → SatiContext → LocalDB. There is no hosted cloud Production
implementation established by this index. Distributed clients never contain cloud database
credentials or fall back to direct SQL.

`Sati.Persistence` owns the platform-neutral model, shared persistence helpers and migration
chain. `ApiDbContext` still has a distinct server entity model; consolidation has not removed
that duplication. DbContexts are short-lived units of work. `Sati.Contracts.V1` owns portable
authoritative rules and DTOs; WPF concerns remain client-side. Do not expose EF entities as
network contracts or duplicate permission/billability/status rules in a ViewModel.

Current stored account/agency/capability/security-version validation and scoped predicates enforce
ordinary API access. Selected composite tenant keys exist. General EF tenant filters, blanket
SQL RLS and a production database-tenancy strategy remain planned, not independently established.
Targeted revisions and SQL application locks protect specific decisions; they do not guarantee
all cross-record races, resource fairness or exactly-once external effects.

The API's Claim.MD connector owns the HTTP I/O deadline; client registration delegates timer
configuration to that owner. [Background execution](docs/architecture/workers.md) and the
[sandbox runbook](CLAIMMD_SANDBOX_RUNBOOK.md) describe its component boundary and evidence limits.
The [worker isolation proposal](BACKGROUND_WORKERS_HANDOFF.md#october-9--known-unsent-dispatch-isolation-proposal)
retains its reviewed design. The [implemented W8 boundary](BACKGROUND_WORKERS_HANDOFF.md#october-9--known-unsent-dispatch-isolation-implementation)
adds shared Contracts readiness rules, Persistence account state and API-owned due/hold/reopen.
WPF uses safe DTOs and an authorized HTTP command; no desktop scheduler or key resolver is added.
Source migration and local verification do not establish deployed activation or broader fairness.
The [idle/wake implementation](BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-idle-wake-implementation)
adds `ClearinghouseDispatchSchedule`, constructor-injected into the existing API worker and
validated request publishers. It owns local activity/waits; SQL retains eligibility and leases.
No route, DTO, schema, desktop scheduler or deployment boundary changes.

Task 1.3.1's [dispatch fairness design](BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-fairness-design--task-131)
was adopted in [DEC-0240](docs/decisions/current/2026-10-09-DEC-0240.md). Task 1.3.2's
[source implementation](BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-fairness-implementation--task-132)
adds Persistence-owned rotation metadata/index and a constructor-injected API selector. Its
short zero-retry SQL transaction disposes before existing send admission and external work.
Offers change scheduling position only. Known lane skips preserve normal activity pacing;
shared barriers retain cooldown. Acceptance is recorded separately from migration/deployment.

Task 1.3.3.3's [polling boundary](BACKGROUND_WORKERS_HANDOFF.md#october-9--durable-polling-selection--task-1333)
uses independent Persistence scheduling pivots and a constructor-injected short API selector.
Reset/poller/vendor admission and authoritative receipt/cursor effects retain their owners;
bounded feed offers and one oldest ERA replace full account/ERA draining. Source migration
generation and synthetic verification are separate from existing database rollout/activation.

The selected [signature-worker design](BACKGROUND_WORKERS_HANDOFF.md#october-10--signature-fairness-design--task-1335)
now has its [reset/timing/gate source boundary](BACKGROUND_WORKERS_HANDOFF.md#october-10--signature-reset-and-hosted-boundary--task-1336);
independent Persistence scheduling metadata and API phase/agency/item selection remain pending.
Clinical projection,
immutable package, outbox lease/GUID/revocation and restricted portal owners remain authoritative.
This plan supplies no deployed activation, shared capacity or safe external baseline-reset claim.

The [desktop test boundary](docs/architecture/desktop.md) explicitly suppresses production
startup while loading canonical UI resources; omitting Application.Run does not suppress the
startup callback queued by WPF construction. Fixture hosts remain explicitly supplied.

The API's constructor-injected request exception boundary owns containment and safe failure
responses before downstream middleware exceptions reach hosting. [The identity boundary](docs/architecture/identity.md)
and [logging owner](LOGGING_DESIGN.md#api-request-boundary--source-october-8-2026) describe the
scope. `IncidentAggregator` owns its full transaction inside a named zero-retry execution scope
and refuses an active retrying caller before gate/context work; the
[incident execution owner](LOGGING_DESIGN.md#api-incident-execution--source-october-8-2026)
records its immediate-reference replay limit. The
[two-check health boundary](LOGGING_DESIGN.md#api-health-failures--source-october-8-2026)
owns safe caught-failure results/logging while preserving status, cancellation and identity/schema
validation. General sink redaction, startup/provider work, SQL locking evidence and deployed
incident persistence remain separate work.

## Feature ownership

The shared Contracts `EdiReplayRules` owns retained EDI request identity for API and transitional
local replay. [The billing boundary](docs/architecture/billing.md) links its implementation and
evidence; lifecycle permission and dispatch compliance remain separate owners.
Contracts `OriginalClaimReleaseRules` owns original delivery admission; Persistence owns its
validated history projection and common agency SQL admission. The API rechecks current exact
retained-subset compliance and correction purpose before queue/Sending. Wrapping and uploads
run outside SQL; source and private synthetic proof are linked from the billing owner.

| Area | Current architecture reference | Canonical detailed policy/runbook |
|---|---|---|
| Identity, tenancy and sessions | [Identity boundary](docs/architecture/identity.md) | [Route inventory](API_AUTHORIZATION.md), [API security review](API_SECURITY_AUDIT.md) |
| Notes, forms and clinical review | [Clinical records](docs/architecture/clinical.md) | [Attestation](NOTE_FORM_ATTESTATION_DESIGN.md), [Assessment review](ASSESSMENT_REVIEW_RUNBOOK.md), [Amendments](NOTE_AMENDMENTS_RUNBOOK.md) |
| Billing and clearinghouse work | [Billing boundary](docs/architecture/billing.md) | [Payer requirements](PAYER_BILLING_REQUIREMENTS.md), [Certification](PAYER_BILLING_CERTIFICATION.md), [Claim.MD runbook](CLAIMMD_SANDBOX_RUNBOOK.md) |
| Workers and capacity isolation | [Background execution](docs/architecture/workers.md) | [Background workers](BACKGROUND_WORKERS_HANDOFF.md) |
| Signatures and communication | [Signatures and chat](docs/architecture/communication.md) | [Signature guide](SIGNATURE_PORTAL_GUIDE.md), [Chat guide](TEAM_CHAT_GUIDE.md) |
| Desktop coordination and personal tools | [Desktop ownership](docs/architecture/desktop.md) | [Display design](DISPLAY_MODES_DESIGN.md), [Logging design](LOGGING_DESIGN.md) |
| Governance and recovery | [Preservation boundary](docs/architecture/governance.md) | [Governance runbook](RECORDS_GOVERNANCE_RUNBOOK.md), [Operations](OPERATIONS.md) |
| Future platform products | [Planned platform](docs/architecture/platform.md) | [Platform domain](PLATFORM_DOMAIN.md), [Restructure plan](PLATFORM_RESTRUCTURE_PLAN.md), [Karuna handoff](karuna/CODEX_HANDOFF.md) |

Changes to an owner or boundary update this index/feature page and the affected topic owner.
Record rationale in [DECISIONS.md](DECISIONS.md), route scope in the route inventory and deferred
work under stable IDs in [AGENDA.md](AGENDA.md). Historical source line references describe their
captured revision and are not guaranteed to match current source.
