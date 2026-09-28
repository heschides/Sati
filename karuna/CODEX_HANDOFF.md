# Handoff — Karuna, the SatiLogica direct-service program

**For:** Codex, or whoever implements this next, starting with no prior context.
**Written:** 2026-09-28 against `master` @ `b2d249a` (Sati release 1.3.30).
**Status:** design only; no Karuna code exists. All nine decisions in §2 were confirmed by Josh on
2026-09-28 and are recorded in `DECISIONS.md`. This handoff authorizes the prerequisite work in §3
and the steps in §4, against synthetic data only. The open questions it names (KQ-*, MQ-*) are still
open.

## 0. Read before writing code

In this order. This brief is a map, not a replacement for them.

1. `AGENTS.md` at the repository root — non-negotiable project rules. (Note: `CLAUDE.md` and
   `AGENTS.md` have diverged; `SATI_STRUCTURAL_REVIEW_2026-09-28.md` S-9. Where they differ, follow the
   stricter rule and tell Josh.)
2. `PLATFORM_DOMAIN.md` — tenancy, authority versus relationship, the person registry, sharing layers.
3. `karuna/KARUNA_REQUIREMENTS.md` — what Therap does for an agency, and the Maine and federal rules.
4. `karuna/KARUNA_DESIGN.md` — the design. §1.3 is the principle most likely to be violated by habit.
5. `karuna/KARUNA_EMAR_DESIGN.md` — before step K4.
6. `SATI_STRUCTURAL_REVIEW_2026-09-28.md` — Sati problems Karuna must not copy.
7. `PLATFORM_RESTRUCTURE_PLAN.md` — the stages the prerequisites below draw from.

Do **not** try to read `DECISIONS.md` and `AGENDA.md` in full; they are 5,600 and 8,000 lines. Search
them for the area you are touching.

## 1. What Karuna is

The provider-side program on the SatiLogica platform: the system a Maine direct-service agency
(Sections 21 and 29 first) uses instead of Therap to document care, run medication administration,
report events, manage behaviour supports, schedule and qualify staff, verify visits, and bill
MaineCare. Its tenant is the provider organization. It runs as its own API host and a Blazor
WebAssembly client over one shared SatiLogica database.

The five differences from Sati that drive the design (`KARUNA_DESIGN.md` §1): many staff per person
rather than one owner; a 24-hour clock stored as instants; clinical facts always recorded while claims
are gated; shared devices at the point of care; standalone operation first. And no local-database
mode — one application-service implementation from the first commit.

## 2. Decisions (confirmed 2026-09-28)

All nine are recorded, with reasoning and rejected alternatives, in `DECISIONS.md` under
"2026-09-28 — Karuna's foundational decisions". Treat them as fixed; if implementation shows one is
wrong, stop and tell Josh rather than working around it.

| # | Decided | What it means for implementation | Needed by |
|---|---|---|---|
| D-1 | One migration chain, owned by a new composition assembly `SatiLogica.Schema` | P5 moves the existing chain there before Karuna's first table. There is **no** separate Karuna chain. | P5, K0 |
| D-2 | Each product is its own host on a shared `SatiLogica.Hosting` library | P4 extracts the library with no behaviour change for Sati. `Sati.Api` keeps its entry assembly; Karuna gets `Karuna.Api`. Restructure stage 5 is amended to match. | P4, K0 |
| D-3 | Blazor WebAssembly PWA client, conditional on the device spike (`KARUNA_DESIGN.md` §13.1) | Run and record the spike in K0 **before** building module screens. If it fails, stop and report. | K0 |
| D-4 | Billing mechanics are platform; billing rules stay per product | The formatter, profiles, outbox, connectors, 835 ingestion and remittance move to `SatiLogica.*` before K8. | K8 |
| D-5 | UUIDv7 keys as idempotency keys; `karuna` SQL schema; non-null `OrganizationId`, composite FKs, query filters, row-level security | All in K0, with architecture tests. | K0 |
| D-6 | EVV phase 1 reconciles with the State's Sandata EVV | K7 records Sandata visit ids and gates billing on a matching verified visit. No EVV capture or aggregator transmission yet. | K7 |
| D-7 | Clinical facts are never refused for clinical-rule violations; claims are gated | Every Karuna validator is written this way (§5). A "record it anyway" confirmation is allowed; a refusal is not. | K3 onward; design K0 validators to it |
| D-8 | One tenant per product, even for an organization holding both roles | The tenant model carries a product. A person working on both sides has two accounts. The 2026-08-15 "at most one tenant" sentence is superseded. | K0 |
| D-9 | `Karuna` is fine in code; anything publicly hosted uses a neutral name | Azure resource names, web app title and manifest, and device install names must not say Karuna. Ask Josh for the public name when the first resource is needed. | First public resource |

## 3. Prerequisites in the Sati repository

Independently committable, in order. P0 and P1 are small and safe and should land first. P5 is the
only one that touches a hosted database.

**P0 — Stop the root project from swallowing new code (review S-8).**
`Sati.csproj` at the repository root globs every `.cs` file beneath it. Before creating any file under
`karuna/` or `platform/`, add `Compile`, `None`, `Content` and `EmbeddedResource` removes for
`karuna\**` and `platform\**`, beside the existing list at `Sati.csproj:46-85`. Add a test that the
WPF assembly contains no type from a `Karuna` or `SatiLogica` namespace. *Done when:* the solution
builds, the test fails if the exclusion is removed (fail-first), and nothing else changed.

**P1 — The dependency-graph guardrail (restructure stage 1).**
An architecture test that records today's project references and fails on a new edge, plus the rules
in `KARUNA_DESIGN.md` §3.1: no `Karuna.*` ↔ `Sati.*` references in either direction; no
`SatiLogica.*` project references a product except `SatiLogica.Schema`. *Done when:* the test is green
now and red for a deliberately added forbidden reference.

**P2 — One clock (review S-3).** Introduce the platform `TenantClock` and `BusinessDayCalendar`
(Maine State holidays as versioned data) in a new `SatiLogica.Contracts`, move `ApiClock` and
`BillingRules.MaineBusinessDate` onto it, replace the host-clock call sites listed in S-3, and add
`Microsoft.CodeAnalysis.BannedApiAnalyzers` banning `DateTime.Now`, `DateTime.Today` and
`TimeZoneInfo.Local` in API and contract projects. This fixes a Sati latent defect and gives Karuna
its clock. *Done when:* a test pins a UTC instant of 00:30 UTC in July and every affected path uses
the previous Maine date.

**P3 — Platform contracts, first slice (restructure stage 3, subset).**
Move only what Karuna's first steps need into `SatiLogica.Contracts`: the actor and permission-set
shape (generalized to a tenant kind), the audit envelope and `AuditCsv`, `LegalHold`,
`EnvelopeProtection`, document-artifact contracts, and the P2 clock. Mechanical moves only: namespace
changes and `using` updates, no behaviour change, nothing else in the commit — exactly as stage 3
instructs.

**P4 — `SatiLogica.Hosting` (D-2).**
Extract `TokenIssuer`, `ValidatedActorFilter`, the core of `TenantAccess`, `AuditTrail` and
`LoginAttemptGuard` into a library `Sati.Api` references, with no behaviour change for Sati. Two
generalizations are required. First, a tenant carries its product (Sati or Karuna; D-8), and the
actor carries it too, so a Sati token can never be accepted by the Karuna host or the reverse. Second,
the database-instance check reads the expected environment name from startup-validated configuration
instead of the `"Demo"` literal (`TenantAccess.cs:123`; review S-11). This is restructure stage 5 as
amended on 2026-09-28. `Sati.Api` keeps its entry assembly, but authentication code moves, so it ships
as its own Sati release with an acceptance run.

**P5 — Schema composition (D-1).** Create `SatiLogica.Schema` and move the one migration chain into
it. Follow stage 4's rules exactly: do not renumber, rewrite or split migrations; ids, order and
`__EFMigrationsHistory` must not change; `MigrationEffectAnalyzer` and `SchemaDriftHealthCheck` must be
green before and after. **This step touches the Demo database and needs a temporary firewall rule only
Josh can add.** Stop and ask; do not attempt it unattended.

## 4. Karuna landing order

Each step builds, passes the full suite, and is worth committing alone. Every step is **synthetic
data only**. Every step updates `karuna/KARUNA_ARCHITECTURE.md`, `karuna/KARUNA_API_AUTHORIZATION.md`
and `karuna/KARUNA_AUDIT_EVENTS.md` as it lands (create them in K0), and records durable choices in
`DECISIONS.md`.

**K0 — Foundation.** Projects per `KARUNA_DESIGN.md` §3.1 (under `karuna/`, in the `karuna` solution
folder). Organization tenant (a Karuna-product tenant, D-8), sites, programs, users,
`KarunaPermissions` and `KarunaPermissionRules`,
enrolment, `CareTeamAccess` (standing, shift and organization-wide reach — shift reach can use a
minimal `Shift` stub until K7), emergency access, device enrolment and shared-device sessions, the
four isolation layers (§4.2), route metadata for tenant owner/capability/audit action with the
generated inventory, audit actions, `TenantClock` usage. Web client shell with shift and office modes
and the D-3 spike results recorded.
*Done when:* the access-matrix suite passes; every entity has a non-null `OrganizationId`, a query
filter and row-level security coverage (architecture test); a raw SQL query from the API principal
cannot read another organization's rows; every mutating route declares an audit action; cross-tenant
tests pass on SQL Server in CI.

**K1 — The person record.** `ServiceRecipient` with a registry link field (nullable; the registry
does not exist yet — do not create it), versioned profile ledger, guardians, contacts, structured
allergies with explicit "no known allergies", diagnoses, diet, equipment, advance directive, care
protocols, rights modifications with required elements, presence intervals, face sheet PDF with print
audit. Photos follow Sati's photo decision. No SSN field.

**K2 — Staff qualifications.** Requirement catalog as versioned data from Section 21.10, credentials,
training, person-specific training, background-check records without results, `StaffQualificationRules`
returning Qualified/NotQualified/Unknown with reasons, expiry queues.

**K3 — Event reports.** `EventReport` per person with `OccurrenceGroupId`, `EventReportWorkflow`,
`ReportableEventRules` with the program taxonomy **as data** (blocked on KQ-4 and KQ-16 for the real
content: build with a clearly labelled synthetic taxonomy until the OADS matrix is obtained),
notification records including APS, staff-recorded Evergreen filing, 30-day follow-up, amendments,
auto-draft entry point for other modules, escalation of unsubmitted drafts. Never name anything
`Incident`.

**K4 — eMAR.** Follow `KARUNA_EMAR_DESIGN.md` §20 internally. Steps 1–7 make a synthetic medication
pass demonstrable; 8–9 are required before any residential agency could use it. The RN clinical review
gate in the eMAR design's header applies before it is shown to an agency.

**K5 — Daily documentation and service records.** Shift notes, `ServiceRecord` with
`ServiceDocumentationGate` (the six elements of the 2026-09-21 bulletin), `ServiceRecordWorkflow` with
amendments, `StaffTimeClaimRules` checked in the same serializable transaction as the write, late-entry
rules, goal, health and behaviour observations, appointments, plan snapshots and support plans,
authorization receipts with second-person verification.

**K6 — Behaviour support.** Plan versions, `PlanApproval` evidence, `BehaviorPlanRules` activation,
per-version staff training, `RestrictiveInterventionRecord` with `RestrictiveInterventionRules` and
event-report drafts.

**K7 — Scheduling, time and EVV phase 1.** Shifts and assignments with qualification refusal and
reasoned supervisor override, `StaffingCoverageRules`, time punches, payroll CSV export, Sandata visit
reconciliation and `EvvRequirementRules` (impacted codes as data, blocked on KQ-2 for real content).

**K8 — Billing.** After D-4's platform extraction: rates, `ServiceUnitRules`,
`AuthorizationUtilizationRules`, `ServiceCompatibilityRules` (data, KQ-5), `PerDiemDayPolicy` (KQ-1),
`KarunaClaimReadiness` with exact blockers, claim creation with frozen inputs, and the platform 837P,
outbox and 835 paths. Mock clearinghouse only, as Sati's Demo does.

**K9 — Therap import and cutover.** Blocked on KQ-11: obtain a real export format first, under a data
agreement, into a non-Production environment. Current state imported through reviewed batches;
history as hash-verified artifacts; medication orders import as Draft only; `SystemOfRecordCutover`
per site and per module.

**K10 — Oversight dashboards and nurse queues.** Bounded, exact-or-labelled counts.

**K11 — Personal funds.** Append-only ledger, two-person counts, derived balances.

**Later, not in this handoff:** relationship sharing with Sati (needs the platform authorization and
sharing policy, and counsel on KQ-9), EVV alternate-vendor integration, pharmacy interfaces, offline
capture (needs its own design and the `AGENDA.md` mobile prerequisites), guardian access.

## 5. Rules that are easy to break by habit

- **Never refuse a truthful clinical fact for a clinical-rule violation** (D-7). A validator that
  returns 409 because a PRN exceeded its maximum is a defect. Classify, flag, route. Refuse only for
  integrity: reach, tenant, revision, idempotency conflict, future instant. The eMAR tests 8–11 must be
  shown failing against a refusing build before they are kept.
- **Do gate claims.** Service records, claim lines, schedule assignments and order activation are
  claims and fail closed with exact reasons.
- **One owner per rule**, in `Karuna.Contracts.V1` or `SatiLogica.Contracts`. The web client calls the
  owner for previews; the server calls it to decide. A private helper that re-derives units, deadlines
  or eligibility is a defect (review S-4 is what happens otherwise).
- **No local database, no `Server*` twin entities, no second implementation of a workflow.**
- **Instants, not offsets.** No `DateTime.Now`/`Today`; no minutes-after-7 AM; DST tests for every time
  rule.
- **Derived obligations are not stored.** Scheduled doses, deadlines and due items are computed with
  stable identities; only evidence is stored.
- **Append-only means append-only in the database too**: deny `UPDATE`/`DELETE` to runtime principals
  on evidence tables, and test it on SQL Server.
- **No PHI in audit metadata, logs, notifications or browser persistent storage.**
- **Honest labels.** "Filed in Evergreen — staff recorded", "Downtime paper back-entry". Never claim an
  integration that does not exist.
- **Do not decide an open question in code.** `KARUNA_DESIGN.md` §19 (KQ-*) and
  `KARUNA_EMAR_DESIGN.md` §21 (MQ-*). Build the rule owner with the question as versioned data and a
  clearly labelled synthetic default; stop and ask before real content.

## 6. Tests

`AGENTS.md`'s rules apply: security, tenancy and concurrency tests must fail against the unfixed code
before they are kept. Additionally (`KARUNA_DESIGN.md` §17): SQL Server rather than SQLite for API
integration tests from the first commit, with CI running them; the access matrix; DST suites;
clinical-fact acceptance suites; claim refusal suites; idempotency; architecture tests.

## 7. Environment and release

- Synthetic data only. No real person, staff member, order or agency data in any environment.
- Demo is the only hosted environment. Any schema change to `SatiDemo` needs a temporary exact-IP
  firewall rule that only Josh adds and removes; detect the need, report it, and wait.
- `RELEASE_PLAYBOOK.md` and the DATT trigger describe Sati releases. They do not yet cover a Karuna
  host or client; a Karuna release needs a playbook section written and approved first. Do not treat a
  DATT invocation as covering Karuna.
- Josh validates releases by using them; do not add manual smoke tests as release gates.
- No Production deployment, no cloud migration without Josh, no security or firewall changes.

## 8. Regulatory note

`REGULATORY_CONCERNS.md` governs. Karuna adds medication administration, restrictive interventions,
reportable events, EVV and personal funds, each with its own rules; the regulatory ground and its
confidence labels are in `KARUNA_REQUIREMENTS.md` §4. Add a Karuna section to
`REGULATORY_CONCERNS.md` in K0 that lists the open counsel and clinical questions, and keep it current
as steps land. Nothing in Karuna is represented as HIPAA compliant, clinically reviewed or accepted by
OADS, MaineCare or Sandata.
