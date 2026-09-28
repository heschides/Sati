# Karuna — design

*Status: design only. No Karuna code exists and none is authorized by this document. Written
2026-09-28 against `master` @ `b2d249a` (Sati release 1.3.30). Nothing here has been reviewed by
counsel, an RN clinical advisor, OADS, or an agency.*

**Read first, in this order:** `PLATFORM_DOMAIN.md` (tenancy, authority vs relationship, the person
registry, sharing layers — all decided 2026-09-07), `KARUNA_REQUIREMENTS.md` (what Therap does for an
agency and the rules behind it), this document, then `KARUNA_EMAR_DESIGN.md`. `CODEX_HANDOFF.md` is
the implementation map. `SATI_STRUCTURAL_REVIEW_2026-09-28.md` at the repository root lists Sati
problems that Karuna must not inherit; several of this document's choices are the corrections.

**What this document decides:** Karuna's shape inside SatiLogica, its access model, its time model,
the principle that separates clinical facts from claims, the module boundaries and their rule owners,
and the landing order. Its foundational decisions were **confirmed by Josh on 2026-09-28** and are
recorded in `DECISIONS.md`:

- D-1 to D-9 under "Karuna's foundational decisions";
- D-10 to D-13 under "Karuna prerequisite corrections", which followed Codex's review of the
  prerequisites against the code.

They are marked where they appear below.
**What it deliberately does not decide:** anything listed in §19. Do not settle those in code.

---

## 1. Karuna is not "Sati for providers"

Five differences from Sati drive nearly every choice below. Each one is a place where copying Sati
faithfully would produce the wrong system.

### 1.1 Many hands, not one owner

Sati anchors a consumer to exactly one case manager (`Person.UserId`, non-null), and access follows
that assignment through `TenantAccess.OwnedPeople` and `CaseloadTransferRules`. In a group home, a
person is supported by fifteen staff across three shifts, a floating relief pool, a nurse who covers
six houses and a behaviour specialist who visits monthly. **There is no owner.** Access is derived
from where staff work and when, not from who a person "belongs to" (§4).

### 1.2 A 24-hour clock, not a 7:00 AM to 7:00 PM day

`ServiceTimeline` defines the loggable day as 7:00 AM to 7:00 PM and stores `Note.StartTime` as
minutes after 7:00 AM. Direct support runs overnight, across midnight and across daylight-saving
changes. A minute offset from a wall-clock origin cannot represent a 10:00 PM to 6:00 AM shift, and
cannot tell the 1:30 AM that happens twice in November apart. Karuna stores instants and derives
dates (§5).

### 1.3 The record says what happened; rules decide what happens next

This is the one genuinely new principle, and it needs to be stated plainly because it inverts a Sati
reflex.

Sati refuses writes that would break a rule: an overlapping service time is rejected with 409,
because the note *is* a claim on billable time. Karuna has two kinds of record, and they must be
treated oppositely:

- **Clinical facts** — a dose given, a dose refused, a restraint used, a seizure observed, a fall.
  These are records of events that already happened in the physical world. **Karuna never refuses
  a truthful clinical fact because it violates a clinical rule.** A second dose recorded for a dose
  already given, a PRN given past its 24-hour maximum, a medication given by someone whose CRMA
  lapsed yesterday, a restraint that lasted 20 minutes — each of these is exactly the record an
  auditor, a nurse and the State most need to exist. Refusing it does not undo the event; it only
  hides it, or pushes it onto paper, or teaches staff to document something else. Karuna accepts
  the fact, classifies it, and routes it: an exception flag, a notification, a draft event report.
- **Claims** — on money (a claim line), on staff time (a billable service interval), on a schedule
  (assigning a person to a shift), on an order becoming active. These are assertions about the
  future or about entitlement, and Karuna gates them exactly as Sati does: fail closed, typed
  refusals, revalidated at the server.

Refusals for **integrity** remain for both kinds: no authority, wrong tenant, stale revision, a
duplicate idempotency key, an instant in the future. Those are not clinical rules.

Prevention still matters and belongs in the interface *before* the fact: the MAR shows "given by
Dana at 8:02" in the dose row before anyone can record it again. What the system refuses to do is
turn prevention into concealment after the fact.

**Rejected:** blocking the save of a clinical record that violates policy. It is how paper-backup
workarounds and falsified entries start, and it destroys the evidence that the violation happened.

**Confirmed 2026-09-28 as a governing rule (D-7).** An explicit "record it anyway" confirmation is
acceptable prevention; refusing the truthful entry is not.

### 1.4 The point of care is a shared tablet, not a desk

DSPs document on a house tablet that five people share, or on their own phone in a parking lot.
Karuna's primary client is therefore not WPF (§13). Shared-device use needs its own identity design:
device enrolment, fast staff switching, idle lock, and attribution to the authenticated person, never
to the device (§4.6).

### 1.5 Standalone first

Most people a Karuna agency supports will have case managers at agencies that do not use Sati. The
relationship flow in `PLATFORM_DOMAIN.md` — provider documentation reaching the case manager through
the authorization — is an enhancement Karuna must support when both sides are on the platform. It
can never be a dependency. **Every Karuna workflow must work for an agency whose case managers,
State reviewers and pharmacies are all outside SatiLogica.**

### 1.6 And one thing Karuna does not get: a local database

Sati has two runtime architectures — Local Production on per-user LocalDB, and hosted Demo behind the
API — and pays for that twice in every feature (see the structural review, S-1). Karuna is
multi-user, multi-site and 24/7 from its first day. **Karuna has no local-database mode, no
desktop-applied migrations and no transitional EF services in a client.** One application-service
implementation, behind one API, from the first commit.

---

## 2. Principles carried from Sati unchanged

Each of these is already a governing rule in `CLAUDE.md` or `DECISIONS.md`. The right-hand column is
how it lands in Karuna.

| Sati principle | In Karuna |
|---|---|
| The API is the authority; clients never hold database credentials. | Same. Karuna has only API clients. |
| Rules deciding permission, billability, approval or record status have one owner in shared contracts. | `Karuna.Contracts.V1` owns Karuna's rules (§8 lists them). A second copy is a defect. |
| Submitted clinical and financial records are amended, not overwritten. | Same, and extended to append-only clinical facts (§11.3). |
| Tenant isolation is structural. | Actually structural this time: non-null tenant owner on every row, composite keys, query filters and row-level security (§4.2). Sati currently relies on per-route predicates (structural review S-2). |
| Snapshot semantics for documents of record. | PCP snapshots, authorization snapshots, claim inputs, event-report filings. |
| Compute, don't store, derived obligations; derived items have stable identity. | Scheduled doses, due follow-ups, filing deadlines, credential expiries. Only evidence is stored. |
| Generation creates obligations, never evidence. | Auto-drafted event reports are drafts, not reports. A generated downtime MAR is not administration evidence. |
| An attestation is not a signature; staff-recorded provenance is labelled as such. | "Filed in Evergreen — staff recorded"; "Paper MAR back-entry — staff recorded". |
| Append-only policy, selected by the occurrence date, with enforcement dates. | Every Karuna rule set (§6). |
| Audit events are a minimal activity index without PHI; mutation and audit commit together. | Same envelope, `karuna.`-prefixed actions (§14). |
| Optimistic concurrency with typed 409s. | Same on every mutable draft. |
| Durable outbox for anything external; never retry an uncertain send. | Clearinghouse, EVV aggregator, notifications. |
| Demo and Production are separate in everything. | Same; Karuna starts synthetic-only. |
| Honest status language. | Karuna never claims Evergreen, Sandata, pharmacy or payer integration it does not have. |
| Accessibility is measured through the automation tree. | Measured through the browser accessibility tree and real screen readers (§13.4). |
| A security or concurrency test must fail against the unfixed code before it is kept. | Same. |

---

## 3. Where Karuna sits in SatiLogica

### 3.1 Projects

Following the target shape in `PLATFORM_RESTRUCTURE_PLAN.md`:

```
SatiLogica.slnx
├── platform/
│   ├── SatiLogica.Contracts      product-neutral only (D-11): tenant actor with no permission
│   │                             enum, audit envelope and AuditCsv, tenant clock, business-day
│   │                             calendar; later billing mechanics, authorization, sharing policy
│   ├── SatiLogica.Persistence    platform entities only (no product types; none yet, D-10)
│   ├── SatiLogica.Hosting        token issuance, actor validation, TenantAccess core, AuditTrail,
│   │                             LoginAttemptGuard behind host-supplied seams; outbox and workers
│   ├── SatiLogica.Schema         design-time composition context and the single migration chain
│   │                             (§3.3); referenced by no runtime project
│   └── SatiLogica.Migrator       installer-run migration runner for Local Production (D-12)
├── sati/                         unchanged in this design
└── karuna/
    ├── Karuna.Contracts          V1 DTOs and Karuna rule owners; references SatiLogica.Contracts only
    ├── Karuna.Persistence        Karuna entity types and IEntityTypeConfiguration; `karuna` SQL schema
    ├── Karuna.Application        application services (the only implementation of each workflow)
    ├── Karuna.Api                ASP.NET Core host: feature endpoint modules, thin
    ├── Karuna.Worker             background host: deadlines, notifications, outbox dispatch
    ├── Karuna.Web                Blazor WebAssembly client (PWA), references Karuna.Contracts
    ├── Karuna.Tests              rules, application services, DST, access matrix
    └── Karuna.Api.Tests          HTTP + JWT pipeline against SQL Server (not SQLite; §17)
```

**Dependency rules, enforced by the stage-1 architecture test before any Karuna code exists:**

1. No `Karuna.*` project references a `Sati.*` project, or the reverse.
2. No `SatiLogica.*` project references a product project, except `SatiLogica.Schema`.
3. `Karuna.Web` references `Karuna.Contracts` and `SatiLogica.Contracts` only — no persistence, no
   application services.
4. Endpoint code in `Karuna.Api` calls `Karuna.Application`; it contains no workflow logic.

### 3.2 Why a separate API host

**Decided (D-2): `Karuna.Api` is its own deployable, hosted through `SatiLogica.Hosting`, sharing the
database.** Sati likewise keeps `Sati.Api` as its own host on the same library, which amends
restructure stage 5 from "one platform host" to "a shared hosting library". Reasons:

- **Availability class.** A morning medication pass across every house in an agency happens at 8:00
  AM whether or not Sati is being redeployed. Karuna needs a higher availability target and its own
  deployment cadence; a shared host couples them.
- **Blast radius.** A Sati endpoint defect, memory leak or runaway request cannot take down a MAR.
- **Least privilege.** The Karuna host's managed identity gets rights on the `karuna` schema only,
  nothing in Sati's tables (§11.1). That is possible because Karuna keeps its own identity and audit
  tables (D-10).
- **Separate trust.** Each host has its own token audience and signing key, so neither accepts the
  other's tokens under the JWT validation Sati already performs.
- **It forces the platform seam to be real.** If Karuna can be hosted without referencing Sati, the
  platform extraction worked.

**Rejected:** registering Karuna endpoints inside `Sati.Api` before the platform split. It would make
Karuna depend on the Sati host and repeat the coupling the restructure exists to remove. **Rejected:**
a Karuna host that copies `TokenIssuer`, `ValidatedActorFilter` and `AuditTrail`. Two copies of
authentication are two ways to be wrong.

### 3.3 One database, one migration chain — and where the chain lives

`PLATFORM_RESTRUCTURE_PLAN.md` decision 3 (one database, one chain, one context) stands. But its stage
4 as written puts the chain in `SatiLogica.Persistence` while product entities register from product
assemblies, and stage 1 forbids a platform project from referencing a product. Those cannot all hold:
the model snapshot that EF migrations compare against needs every entity type at design time.

**Decided (D-1):** a composition assembly, `SatiLogica.Schema`, references the platform and all
product persistence assemblies. It owns the one chain through a design-time composition context that
is **separate from every runtime context**, so no client or product host references it.
`SatiLogica.Persistence` stays product-agnostic. Runtime contexts per product (`KarunaDbContext`,
`SatiContext`, `ApiDbContext`) map only their own tables. A separate Karuna migration chain was
considered and rejected. The contradiction is also recorded as a structural finding (S-5), because it
blocked Sati's stage 4 as much as Karuna.

*Corrected 2026-09-28 after Codex's review.* An earlier version said `SchemaDriftHealthCheck` compares
each runtime model against the chain. It does not: it compares the API model with the live database
(`SchemaDriftHealthCheck.cs:56-59`). Two additions are therefore required before a second schema
exists:

- schema-aware comparison — `SchemaComparison` and `SchemaSnapshotReader` key tables by name alone
  today, so `dbo.X` and `karuna.X` would be conflated;
- a CI check that each runtime model is a subset of the composed model.

Both are part of restructure stage 4 (handoff P5). Because Local Production still migrates itself at
startup, the desktop stops doing so before the chain gains any Karuna entity (D-12, handoff P6).

### 3.4 What becomes platform because Karuna exists

`PLATFORM_DOMAIN.md` predicted that a second product would move things out of Sati. Karuna moves
these, and each needs to be in `SatiLogica.*` before Karuna depends on it:

| Concern | Today | Why it is platform |
|---|---|---|
| Service authorization | Designed as platform; not built | The join for relationship reads. Karuna bills against it. |
| Sharing policy | Designed as platform; not built | One owner for what crosses a tenant boundary. |
| Tenant clock | `ApiClock` and `BillingRules.MaineBusinessDate`, which choose the zone separately | Two owners of "the Maine date" today (S-3). |
| Maine business-day calendar | Does not exist (D-13) | Karuna's filing deadlines are business-day arithmetic. A new State holiday dataset; `WorkdayHelper` (agency productivity exclusions) and `ExemptDate` (a user's days off) are unrelated and unchanged. |
| 837P formatting, trading-partner profiles, clearinghouse outbox and connectors, 835 ingestion, remittance and deposit reconciliation, claim correction mechanics | `Sati.Contracts`, `Sati.Api` | Karuna bills MaineCare on the same transaction sets. Eligibility and readiness rules stay per product. |
| Documents and hash-verified artifacts, signatures, envelope protection, legal hold | Sati-shaped types in `Sati.Contracts` | Platform in principle, but today's types carry Sati's integer person and agency ids, and envelope protection binds them under `sati.v1`. They stay in Sati (D-11). Platform versions are designed when Karuna first needs each one; Karuna's early steps do not. |
| Notifications (contentless) and chat | Chat in Sati | Karuna's shift handoff and alerts. |
| Organization and person registries | Designed; not built | Karuna's tenant is an organization; its recipients link to the registry. |

The billing row corrects `PLATFORM_DOMAIN.md` and the restructure plan, which put claims and remittance
in Sati's column. **Decided (D-4)**, and `PLATFORM_DOMAIN.md` is amended to match. The mechanics move
before Karuna's billing step, so Karuna never needs a reference to `Sati.Contracts` to bill.

---

## 4. Tenancy, identity and access

### 4.1 The tenant

The tenant is the **provider organization** (`PLATFORM_DOMAIN.md`). Every Karuna row carries a
non-null `OrganizationId`. Sites (homes, day programs, offices) and programs (a service line such as
"Section 21 residential") belong to the organization.

**Tenancy is per product (D-8).** An organization that provides case management to some people and
direct services to others holds a Sati tenant and a separate Karuna tenant. A token issued by one
product's host is never accepted by the other's, because each host has its own audience and signing
key. Membership stays single, so a staff member who works on both sides holds two accounts. This
supersedes "at most one tenant" in the 2026-08-15 provider-directory decision.

**Karuna owns its identity tables (D-10).** Karuna's users, organizations (its tenants), memberships
and audit events live in the `karuna` schema. The platform supplies the mechanics through
`SatiLogica.Hosting`: password hashing format, token issuance and validation, lockout, session
revocation through a security version, and the audit envelope. It does not supply the tables. Sati's
`dbo` identity is not relocated. A platform identity store becomes necessary only when something must
span products, such as `PlatformOperator` or the OADS authority grants in `PLATFORM_DOMAIN.md`. It is
designed then.

### 4.2 Isolation that does not depend on remembering a predicate

`DECISIONS.md` says "no feature may assume that a forgotten query predicate is adequate isolation".
Sati today has no query filters and nullable tenant columns on `Person` and `Note` (S-2). Karuna
starts with four layers:

1. **Schema.** `OrganizationId` is non-null on every table. Every foreign key between Karuna
   aggregates is composite — `(OrganizationId, Id)` — so a row in one organization *cannot* reference
   a row in another. Sati's clearinghouse tables already use this pattern for account/generation.
2. **EF global query filters** on `KarunaDbContext` keyed to the validated actor's organization,
   applied to every Karuna entity by convention; a test fails if an entity type lacks one.
3. **SQL Server row-level security** on the `karuna` schema, keyed to `SESSION_CONTEXT` set by the
   connection interceptor from the validated actor. Defence in depth: a raw SQL query or a filter
   accidentally ignored with `IgnoreQueryFilters()` still cannot cross. Its lifecycle is proven by a
   spike before K0 builds on it:
   - **Unset means empty.** The predicate returns no rows when no organization is set.
   - **Read-only per request.** The organization is set when the connection opens for a request, with
     `@read_only = 1`, so nothing can change it mid-request.
   - **No pooled leakage.** A pooled connection must not carry one request's organization into the
     next. That a connection reset clears session context is believed but not yet proven here; the
     spike tests it.
   - **Sign-in bootstrap.** Sign-in runs before any organization is known. The user table is outside
     the tenant predicate and reachable only through a narrow sign-in path.
   - **Background jobs** set the organization explicitly, one tenant at a time.
4. **Cross-tenant tests** on every route, as Sati already does.

Relationship reads (a case manager reading documentation) and authority reads (OADS) are the only
deliberate crossings. They run through one platform sharing service with its own context, its own
audit, and no ability to write. Everything else runs filtered.

**Rejected:** relying on route-level checks alone. It works until the first route that forgets, and
Karuna will have more routes than Sati.

### 4.3 Capabilities versus qualifications

Sati separated capability from job title (`UserPermissions`). Karuna needs one more separation:
**capability** (what the organization authorizes a user to do in the software) versus
**qualification** (what the State recognizes the person as competent to do). A house manager may be
authorized to use the MAR but not be CRMA-certified; a nurse may be licensed but not granted billing.
Both are checked; neither implies the other.

Karuna capabilities, a persisted `[Flags]` set interpreted only by `KarunaPermissionRules`:

| Capability | Grants |
|---|---|
| `DirectSupport` | Read care information and record documentation for people within reach (§4.4). |
| `SiteSupervision` | Review and approve documentation and events at supervised sites. |
| `Nursing` | Order entry and verification workflows, medication oversight queues, health review. Still subject to the verifier *qualification*. |
| `BehaviorSupport` | Author behaviour plans; review restrictive-intervention records. |
| `EventCoordination` | Reportability determination, State filing records, 30-day follow-ups. |
| `Scheduling` | Shifts and assignments. |
| `StaffRecords` | Credentials and training evidence. No clinical reach. |
| `Billing` | Authorizations, rates, claims, remittance. No clinical narrative by default. |
| `FundsManagement` | Personal-funds ledgers. |
| `Administration` | Organization configuration, users, devices, audit export, rule-set versions. |
| `OrganizationWideReach` | Reach every site rather than assigned ones (Sati's `AgencyWideSupervision` lesson). |

Qualifications live in staff records (§9.7) and are evaluated by `StaffQualificationRules`.

### 4.4 Care-team reach

`CareTeamAccess` (in `Karuna.Contracts`, evaluated server-side) answers: *may this user act on this
person's record at this instant, for this kind of record?*

A user is **in reach** of a person at instant *t* when any of these holds:

1. **Standing membership** — the user is a member of site *S* at *t*, and the person has an active
   enrolment at *S* at *t*.
2. **Shift reach** — the user is assigned to a shift at *S* whose interval, widened by the
   organization's documentation grace period (default proposed: 12 hours after shift end), contains
   *t*, and the person is enrolled at *S*. Floating staff get exactly the reach their schedule
   justifies, and lose it when the grace period ends.
3. **Organization-wide reach** — `OrganizationWideReach` or `Administration`.
4. **Emergency access** — an active break-glass grant (§4.5).

Reach then combines with capability and record kind: `DirectSupport` reach reads the face sheet,
protocols, today's MAR and plans; it does not read personal funds or staff records. `Billing` reads
service records and authorizations without narrative fields unless the organization enables it.

**The join is enrolment, not ownership.** Transferring a person between homes ends one enrolment and
starts another; reach follows automatically, exactly as `PLATFORM_DOMAIN.md` wants authorization to
carry case-manager visibility.

**Rejected:** giving every DSP the whole organization. Minimum necessary, and the relief-pool staff
member who worked one shift at a house two years ago should not be able to read its residents today.
**Rejected:** schedule-only access with no standing membership. When a schedule is wrong at 3:00 AM,
the person who showed up still needs the MAR; that is what standing membership and emergency access
are for.

### 4.5 Emergency access

HIPAA contemplates emergency access procedures and `REGULATORY_CONCERNS.md` lists them as required
evidence. A user without reach may open a person's record by stating a reason. The grant:

- is scoped to one person and one site, and expires after a short period (proposed four hours);
- records reason, user, device, time and authority "emergency access" in the audit;
- immediately notifies the site's supervisor and the event coordinator (contentless notice);
- appears on a review queue until someone with `SiteSupervision` acknowledges it.

It is never a silent bypass, and it is never available to `StaffRecords`- or `Billing`-only users.

### 4.6 Shared devices

A house tablet is **enrolled** by an administrator to one site. Enrolment issues a device credential
bound to that site and stored in the platform key store of the device; the device is a possession
factor.

- A staff member starts a session on an enrolled device with their own credentials. On an enrolled
  device, device + staff password (or passkey) satisfies two factors; on an unenrolled personal
  device, the staff member uses their own second factor.
- Within the shift, the session re-locks after an idle period (organization setting, proposed five
  minutes) and unlocks with the staff member's short PIN. Switching to another staff member is a
  full sign-in and an opaque privacy boundary, following Sati's two-phase account-switch decision.
- **A device session's reach is the intersection of the user's reach and the device's site.** A
  nurse who covers six houses, signed in on House A's tablet, sees House A.
- Every record is attributed to the authenticated user. The device identifier is recorded as
  context, never as the actor.
- **Witness sign-in** for two-person acts (controlled-substance counts, some verifications): the
  second person authenticates on the same device for that one act, without ending the first session.
- A lost device is revoked centrally; its credential stops working on the next request.

### 4.7 Shared living providers and contractors

A shared living home provider is a user of the organization with `DirectSupport` reach limited to the
site that is their home. Contractors carry the same qualification requirements (§9.7). Karuna does
not model employment status beyond what qualification and payroll export need.

### 4.8 Across tenants

Unchanged from `PLATFORM_DOMAIN.md`: relationship (case manager, via the authorization) and authority
(OADS, `PlatformOperator`) are separate mechanisms, and specially protected categories travel only on
a recorded release. Karuna's proposed defaults are in §12. Provider-to-provider visibility for a
shared person is **off**: nothing joins two providers (PLATFORM_DOMAIN open question 2).

### 4.9 Conflict of interest

A Maine organization may hold both roles for different people. If one organization is both a Sati
tenant and a Karuna tenant, the platform must be able to detect a registry person served by both its
case-management and its direct-service tenants and require a recorded exception, because federal
conflict-free case management forbids the combination for the same person except under an approved
exception (`REGULATORY_CONCERNS.md`). Karuna has no write path into any person-centred plan,
authorization or OADS decision; it can only receive them. That is the structural half of separation
of duties; the detection is a platform report.

---

## 5. Time

### 5.1 Instants, and the organization's zone

Every time-bearing Karuna fact stores UTC instants as `DateTimeOffset`. Each organization has one IANA
time zone. Dates and wall-clock times are derived through **one** platform owner, `TenantClock` —
not `DateTime.Today`, not a second hard-coded zone (structural review S-3). A banned-API analyzer
fails the build if `DateTime.Now` or `DateTime.Today` appears in `Karuna.*` server code.

### 5.2 Three times on every clinical record

| Field | Meaning | Who sets it |
|---|---|---|
| `OccurredAt` (or `StartedAt`/`EndedAt`) | When the event happened, as asserted by the person documenting it | Staff, once |
| `RecordedAt` | When the server accepted the record | Server |
| `DeviceCapturedAt` | The device's clock when the entry was made; informational | Client |

`LateEntryRules` classifies a record as late when `RecordedAt − OccurredAt` exceeds the threshold for
its kind (proposed defaults: medication administration 60 minutes after the dose window closes; shift
note or service record, end of the documentation grace period; event report, 24 hours). A late entry
requires a reason and is labelled "late entry" everywhere it is displayed and printed. The
2026-09-21 bulletin's "at the time services are provided, or as close to the time of service as
practical" is what this measures.

`OccurredAt` is never edited after submission. A correction is an amendment carrying the original,
the new value, the reason and the actor. A large skew between `DeviceCapturedAt` and `RecordedAt` is
flagged for review, because device clocks are trivially changed.

### 5.3 Service dates

A service record's service date is derived, per service, by a versioned rule:

- **Time-based services** (quarter-hour, community support): the organization-local date of the
  start instant; a session that crosses midnight is split into two service records at local midnight
  by `ServiceTimeRules`, because claims are per date of service.
- **Per diem services** (Agency Per Diem, Shared Living): a billable day is defined by the
  organization's `PerDiemDayPolicy` — whether admission and discharge days count, how hospital and
  leave-of-absence days are treated. **Karuna does not know the MaineCare answer**; the policy is
  append-only and selected by service date, and the default is chosen by the agency (KQ-1).

### 5.4 Daylight saving

Durations are computed from instants: a 10:00 PM to 6:00 AM shift is seven or nine hours on the two
transition nights, and billing, payroll export and staff time all use the true duration. Scheduled
wall-clock events (medication times) resolve per local date, with the nonexistent spring hour moved
forward and the repeated autumn hour taking its first occurrence; `KARUNA_EMAR_DESIGN.md` §4 owns the
detail. DST tests are required, not optional.

### 5.5 Business days

State filing deadlines are business-day arithmetic in Maine. `BusinessDayCalendar` (platform) owns
the Maine State holiday list as versioned data. Karuna's deadline rules call it; nothing else counts
weekdays.

---

## 6. Rule sets are per program, per site, per service — and versioned

Karuna must serve different regulators without branching code. Three axes select the applicable rules:

| Axis | Selects | Example |
|---|---|---|
| **Program authority** of the enrolment (OADS adult services; later OCFS, Section 19) | Reportable-event taxonomy, deadlines, notification duties, follow-up rules | OADS: within one business day; OCFS: within 72 hours, some by phone within 4 hours. |
| **Site medication rule set** | Administration window, PRN follow-up interval, count cadence, order renewal and phone-order confirmation periods, witness requirements | A site licensed under 10-144 ch. 113 vs a shared living home. |
| **Service code** (with modifier) | Unit rule, rate, EVV requirement, documentation gate additions, compatibility with concurrent services, ratio | Home Support Quarter Hour vs Agency Per Diem. |

Every rule set is an **append-only version with an enforcement date**, selected by the occurrence or
service date — the `BillingCompliancePolicyVersion` pattern. Past-dated versions are disabled by
default, require an explicit Admin enablement and an explanation, and never rewrite a submitted
record; they raise review flags. Rule-set *content* ships as reviewed data, not code, so that a new
State matrix is a data release.

**Rejected:** one global rule set with `if (program == ...)` branches, and settings that mutate the
active rules in place. Both make it impossible to answer "which rule applied to this record on that
date?", which is the question an auditor asks.

---

## 7. Domain model

Tenant owner is `OrganizationId` for every aggregate. "Evidence" means append-only after creation.

| Aggregate | Mutability | Purpose |
|---|---|---|
| `Site`, `Program` | Mutable config, versioned | Homes, day programs, service lines. |
| `ServiceRecipient` | Mutable profile, versioned ledger | Karuna's local record of a person; links to the platform registry (never swapped). UI says "person supported". |
| `Enrolment` | Effective-dated | A recipient in a program at a site, from/to. Drives reach. |
| `Guardian`/`Representative` | Effective-dated | Authority basis, as in Sati's signer model. |
| `HealthProfile` items (allergy, diagnosis, diet, equipment, advance directive) | Versioned | Face sheet sources. |
| `CareProtocol` | Versioned, approved | Person-specific instructions requiring training. |
| `RightsModification` | Versioned; originates in PCP | HCBS settings-rule elements and review dates. |
| `PlanSnapshot` | Immutable | The PCP as received, with source and version. |
| `SupportPlan` / `SupportGoal` | Versioned | Provider implementation of PCP outcomes. |
| `AuthorizationReceipt` | Immutable snapshot | Authorization lines as received from MIHMS/OADS; links to the platform authorization when it exists. |
| `ShiftNote` | Draft → submitted → approved; amendments | Narrative documentation of a shift. |
| `ServiceRecord` | Draft → submitted → approved; amendments | The billable fact (§9.3). |
| `GoalObservation`, `HealthObservation`, `BehaviorObservation` | Evidence | Structured data points. |
| `Appointment` | Mutable until completed | Medical appointments and outcomes. |
| `EventReport` | Workflowed; versions | Internal event report per person (§9.5). |
| `StateFilingRecord`, `NotificationRecord`, `FollowUpReview` | Evidence | Filing, notification and 30-day follow-up facts. |
| `BehaviorSupportPlan` + `PlanApproval` | Versioned; approvals are evidence | §9.6. |
| `RestrictiveInterventionRecord` | Evidence | §9.6. |
| `StaffProfile`, `Credential`, `TrainingCompletion`, `PersonSpecificTraining`, `BackgroundCheckRecord` | Evidence with expiry | §9.7. |
| `Shift`, `ShiftAssignment` | Mutable until started; changes audited | §9.8. |
| `TimePunch`, `EvvVisit`, `EvvVisitMaintenance` | Evidence | §9.8. |
| `FundsAccount`, `FundsLedgerEntry`, `CashCount` | Evidence | §9.9. |
| Medication aggregates | See `KARUNA_EMAR_DESIGN.md` | |
| Billing: `ServiceRate`, `ClaimLine` (Karuna readiness, platform mechanics) | Rates versioned; claims frozen | §10. |
| `Device`, `DeviceEnrolment` | Revocable | §4.6. |
| `EmergencyAccessGrant` | Evidence | §4.5. |
| `SystemOfRecordCutover` | Evidence | §16. |

**Naming:** Karuna never uses "Incident" for client events. Sati's `IncidentGroup` is operational
error telemetry, and `CLAUDE.md` forbids presenting one as the other. Client events are
`EventReport`s; State submissions are `StateFilingRecord`s.

---

## 8. Rule owners (`Karuna.Contracts.V1`)

The Karuna equivalent of Sati's shared-rule-owner table. Each is referenced by the API and, where a
preview helps, by the web client. None may be re-implemented anywhere else.

| Owner | Rule |
|---|---|
| `KarunaPermissionRules` | Interpretation of the capability set; unknown bits deny. |
| `CareTeamAccess` | Reach at an instant (§4.4); combination with capability and record kind. |
| `EmergencyAccessRules` | Eligibility, scope, duration and required notifications of a break-glass grant. |
| `LateEntryRules` | Late thresholds per record kind; reason requirement. |
| `ServiceTimeRules` | Service-date derivation, midnight splitting, DST-true durations. |
| `ServiceRecordWorkflow` | Transition table for service records and shift notes (§9.3). |
| `ServiceDocumentationGate` | The bulletin's six required elements, plus per-service additions, before submission. |
| `StaffTimeClaimRules` | Ratio-aware double-claim prevention for billable staff time (§9.3). |
| `ServiceUnitRules` | Units from actual time or days, per versioned service rule; never written back over actual time. |
| `AuthorizationUtilizationRules` | Units used, pending and remaining against an authorization line and period caps. |
| `ServiceCompatibilityRules` | Which services may not be billed for the same person in overlapping time or on the same day. |
| `KarunaClaimReadiness` | Exact blocker set for a service record becoming a claim line (§10.2). |
| `EvvRequirementRules` | Whether a service record requires EVV (service code, live-in, remote, date) and whether a visit verifies it. |
| `EventReportWorkflow` | Transition table for event reports (§9.5). |
| `ReportableEventRules` | Program taxonomy, reportability criteria, filing deadline, notification duties, follow-up due date. |
| `CriticalIncidentMapping` | Mapping from program taxonomy to the CMS minimum critical-incident definition. |
| `BehaviorPlanRules` | Required approvals per level, activation, review cadence, expiry. |
| `RestrictiveInterventionRules` | Planned vs emergency vs prohibited classification; reportability. |
| `StaffQualificationRules` | Qualified / not qualified / unknown for a requirement at an instant, with reasons. |
| `StaffingCoverageRules` | Awake coverage, ratio coverage and medication-pass coverage gaps. |
| `PerDiemDayPolicy` | Billable-day definition per organization and service date. |
| `FundsLedgerRules` | Entry validity, two-person counts, reconciliation. |
| Medication owners | Listed in `KARUNA_EMAR_DESIGN.md` §11. |

`ARCHITECTURE.md`'s shared-owner table gets a Karuna twin in `karuna/KARUNA_ARCHITECTURE.md` when the
first owner lands.

---

## 9. Modules

### 9.1 The person record

Profile, identifiers, photo, guardians and contacts follow Sati's patterns (versioned person ledger,
photos on a person-scoped non-cacheable route, SSN not stored at all — Karuna has no use for it).
Additions:

- **Allergies are structured.** Each allergy records an allergen as a coded ingredient when possible
  (RxNorm ingredient, see the eMAR design) and as text otherwise, with reaction and severity.
  "No known allergies" is an explicit recorded fact with a date, not an empty list.
- **Face sheet** is a pure function of current profile, active orders and protocols, generated on
  demand with a print audit. It is a downtime artifact.
- **Rights modifications** carry the settings-rule elements as required structured fields and a
  review date; they originate in the PCP and cannot be created as free text.

### 9.2 Plans, goals and authorizations

Karuna **implements** the person-centred plan; it never authors or approves it.

- A `PlanSnapshot` is created from an uploaded PCP document (standalone agencies) or, later, from the
  platform relationship with a Sati case manager. It is immutable; a new PCP is a new snapshot.
- A `SupportPlan` is the provider's implementation: goals linked to PCP outcomes, support strategies,
  data-collection method and schedule. Goals without a PCP link are allowed but labelled, because the
  bulletin requires notes to tie to PCP goals.
- An `AuthorizationReceipt` records service code, modifier, units, period, rate basis and source
  document as received. Staff enter it from the MIHMS prior-authorization notice and a second user
  verifies it before billing may rely on it. When the platform authorization exists, receipts link
  to it; the link is never a swap.

### 9.3 Shift documentation and the service record

Two records, deliberately separate:

- A **shift note** is narrative: what the shift was like for this person. It can be long, it can be
  shared with the case manager, and it is not a claim.
- A **service record** is the billable fact. It carries exactly the 2026-09-21 bulletin's elements:
  service (code/modifier), the PCP goal(s) addressed, date and **actual** start and end instants (or
  the per diem day), what the staff member did (structured activities plus text), who provided it,
  and location when relevant. `ServiceDocumentationGate` refuses submission without them.

**`ServiceRecordWorkflow`** (author = the staff member who provided the service):

| From | Author may move to | Supervisor may move to | System |
|---|---|---|---|
| Draft | Submitted, Withdrawn | — | — |
| Submitted | — | Approved, Returned | — |
| Returned | Draft, Submitted, Withdrawn | — | — |
| Withdrawn | Draft | — | — |
| Approved | — (amendment only) | — (amendment only) | — |

- A supervisor may not approve a record they authored or appear on as a provider.
- **Approved is terminal.** Corrections are amendments: a new version linked to the original, with
  reason and actor, re-entering review. If the original already reached a claim line, the amendment
  raises a claim-correction flag; it never edits the claim (Sati's "a sent claim is corrected by
  frequency code").
- Sati's own outstanding gap — no amendment path for an approved note — is the reason this is designed
  in from the start rather than retrofitted.
- Entering a service record on behalf of another staff member is not supported in the first release;
  "who provided the service" has to be the author.

**`StaffTimeClaimRules`** replaces `ServiceTimeline`'s "no minute claimed twice" with the rule direct
support actually needs:

- A staff member's minute may be claimed by **several** billable service records only if all of them
  belong to the same `GroupSession` and the number of people in it does not exceed the smallest ratio
  their services allow (Community Support Group at 1:3 allows three).
- A one-to-one service may not overlap any other billable service by the same staff member.
- Per diem services do not claim staff minutes; staffing for them is a coverage question
  (`StaffingCoverageRules`), not a billing one.
- Intervals are half-open, as in Sati. Service records in Draft, Withdrawn or Returned hold no time;
  Submitted and Approved do.

Unlike a clinical fact, a service record is a claim, so this rule **refuses** the save with a typed
409, checked in the same serializable transaction as the write (Sati's create-time race in
`AGENDA.md` is the lesson; do not check and save in separate steps).

### 9.4 Health observations and appointments

Typed observations (vitals, weight, bowel, seizure, sleep, fluids, menses, pain, skin, blood glucose)
are evidence records with `OccurredAt`, unit and method. A seizure observation with a rescue
medication links to the PRN administration. Out-of-range values are flagged against person-specific
parameters where a protocol or order defines them; Karuna does not ship clinical reference ranges.
An emergency-department visit or admission recorded here creates an event-report **draft**.

### 9.5 Event reports

**One report per person.** When an altercation involves two residents, each gets their own report,
linked by an `OccurrenceGroupId`. A person's record never contains another person's narrative. The
OCFS matrix requires separate reports per client, and privacy requires it anyway.

**`EventReportWorkflow`:**

| State | Who moves it | To |
|---|---|---|
| Draft | Author (any staff with reach, including witnesses) | Submitted |
| Submitted | Reviewer with `SiteSupervision` (not the author) | Reviewed, Returned |
| Returned | Author | Submitted |
| Reviewed | Coordinator with `EventCoordination` | FilingRequired or NotReportable |
| FilingRequired | Coordinator | Filed (with `StateFilingRecord`) |
| Filed / NotReportable | Coordinator | FollowUpOpen (if required) or Closed |
| FollowUpOpen | Coordinator | Closed (with `FollowUpReview`) |

- **Reportability is a human determination**, shown against the program's criteria by
  `ReportableEventRules`, recorded with its basis. The rule computes deadlines and duties; it does not
  file.
- **Deadlines run from the event and from awareness**, and both are recorded. OADS: "as soon as
  possible within one business day of the Reportable Event". The exact reading for an event at 4:30 PM
  Friday is KQ-4; the rule owner encodes whichever reading is confirmed, once.
- **State filing is staff-recorded provenance**: the Evergreen reference, filing time and filer, with
  the label "Filed in Evergreen — staff recorded". Karuna produces a filing view laid out in
  Evergreen's field order so the coordinator can transcribe it; it does not claim to submit.
- **Mandated-reporter duty is separate**: an APS report is a `NotificationRecord` with time, method,
  reference and reporter, and the rule surfaces it whenever abuse, neglect or exploitation is alleged,
  independent of reportability.
- **Notifications** to guardian, case manager, physician and law enforcement are recorded facts with
  times. If the case manager is on SatiLogica and the relationship exists, Karuna can also deliver a
  contentless notice through the platform (§12).
- **Auto-drafts** are created, never submitted, by: a medication error (eMAR), an emergency or
  unapproved restrictive intervention (§9.6), an emergency-department visit or admission (§9.4), a
  missing-person or death record. An unsubmitted auto-draft escalates after a set time.
- **Amendments**: after filing, corrections are versions with reason; the filed version stays exactly
  as filed.
- **Taxonomy** is versioned data per program authority, mapped by `CriticalIncidentMapping` to the
  CMS minimum definition for future State reporting.

### 9.6 Behaviour support and restrictive interventions

- A `BehaviorSupportPlan` version records its level, target behaviours with measurement definitions,
  approved interventions (restrictive ones with exact technique and limits), effective and review
  dates, and the qualified professional who authored it.
- **Activation requires evidence**, not a status click: `BehaviorPlanRules` lists the approvals the
  level requires under 14-197 ch. 5 — Planning Team approval date, guardian consent (signature
  evidence through the platform, or external evidence staff-verified), Review Team approval where
  restraint exceeds 15 minutes or is mechanical or chemical, physician evaluation within 30 days
  before implementation — and a version becomes Active only when all are present. Approvals are
  append-only `PlanApproval` rows, like `FormAttestation`.
- **Staff are trained per version.** A new version requires new training attestations; scheduling and
  the shift view show which on-shift staff are not trained on the current version.
- A `RestrictiveInterventionRecord` is a clinical fact and is always accepted. `RestrictiveInterventionRules`
  classifies it: **Planned** only if an Active version on that date authorizes that exact technique for
  this person, within its limits, by staff trained on that version; otherwise **Emergency** or
  **Prohibited**. Anything not Planned creates an event-report draft.
- Behaviour plans also live in Evergreen; Karuna records the filing the same way as event reports.

### 9.7 Staff qualifications

- The **requirement catalog** is versioned data per program: for example, *work independently*
  (the four College of Direct Support modules, CPR and First Aid current, background check within 24
  months), *DSP beyond six months* (full curriculum), *administer medication* (CNA-M, CRMA or RN, or a
  person-specific delegation), *crisis intervention* (behavioural-intervention training), *support
  this person* (current behaviour-plan version and protocol training).
- `StaffQualificationRules.Evaluate(staff facts, requirement, instant)` returns **Qualified**,
  **NotQualified** with reasons, or **Unknown** (evidence missing). Gates treat Unknown as not
  qualified; screens show it differently, because "we have no record" and "it expired" need
  different fixes.
- Evidence is a `Credential` or `TrainingCompletion` row with dates and an optional document.
  **Background check results are not stored** — only type, date, cleared/not-cleared attestation,
  attestor and next due date. Minimum necessary for a record staff can be asked to show.
- Three consumers: scheduling (warn and require a reasoned supervisor override, §9.8), medication
  administration (eligibility, eMAR design §7), billing (a policy-selectable blocker, KQ-6).

### 9.8 Scheduling, time and EVV

- **Shifts** belong to a site, carry a role and its qualification requirements (awake overnight,
  medication pass), and have instants. Assignments are audited changes until the shift starts.
- Assigning someone who does not meet a shift's requirements is **refused** unless a supervisor
  records an override with a reason. Scheduling is planning, so it is gated — but an agency must not
  be pushed into understaffing a house because a CPR card lapsed yesterday, so the override exists,
  is visible in QA, and never extends to medication eligibility at the moment of administration.
- `StaffingCoverageRules` shows gaps: awake coverage when people are home at a per diem site, group
  ratios, and medication-pass coverage (for each scheduled dose, at least one on-shift staff member
  eligible for that person).
- **Time punches** are evidence: in/out instants, device, site, and location only where EVV needs it.
  Payroll is an export (CSV per payroll vendor), not a payroll system.
- **EVV, phase 1 — reconcile, don't integrate (D-6).** Agencies keep using the State-offered Sandata EVV
  for in-home services. Karuna records the Sandata visit identifier and verification state against
  the service record (entered or imported from a Sandata export), and `EvvRequirementRules` makes a
  verified, matching visit a billing blocker where required. This matches the State's "pend for up to
  30 days and then deny" behaviour without Karuna pretending to be an EVV vendor.
- **EVV, phase 2 — alternate vendor.** Karuna captures the six elements itself (check-in/out with
  location, visit maintenance with reason codes as append-only rows) and transmits through a durable
  outbox to the Sandata aggregator after the State's certification process (at least eight weeks).
  Live-in caregiver and remote-support exemptions are rule data.

### 9.9 Personal funds

Required for residential agencies holding people's money. An append-only ledger per person —
deposits, withdrawals with a receipt artifact, transfers, adjustments with an explanation — and
two-person cash counts. Balance is derived. Karuna records money movements that humans make; it
never initiates a payment. Sati's representative-payee ledger is the pattern, and the ledger
primitive should become platform when Karuna needs it.

### 9.10 Oversight

Dashboards are bounded, organization- and reach-scoped projections: medication passes not
documented or late, PRN follow-ups missing, event filings approaching deadline, follow-ups due,
restrictive-intervention trends, credentials expiring in 30/60/90 days, untrained staff on current
plan versions, approved-unbilled service records, units remaining on authorizations, late-entry
rates by site. Counts are exact or labelled as bounded — Sati's rule against silently truncating
financial counts applies to safety counts too.

### 9.11 Notifications

Push, email and in-app notices carry **no PHI**: "Two items need your review in Karuna." Detail
appears only after authentication. Escalation chains are organization configuration (site supervisor,
then on-call, then coordinator) for the time-critical items: undocumented doses past their window,
controlled-count discrepancies, event filings within hours of deadline, emergency-access grants.

---

## 10. Billing

### 10.1 Platform mechanics, Karuna rules

The 837P formatter, trading-partner profiles, clearinghouse outbox and connectors, response and 835
ingestion, remittance, deposit reconciliation and correction mechanics become platform (§3.4). What
stays in Karuna is what decides whether a service record may become a claim, and how many units it
is.

### 10.2 Claim readiness

`KarunaClaimReadiness` returns the exact blocker set for a service record (Sati's
`BillingComplianceGate` discipline — exact blockers, never a single "not ready"):

- service record Approved and not already on a claim line (claim lines unique per service record and
  service date);
- an `AuthorizationReceipt` (verified) covering the service code, modifier and date, with units
  remaining after pending claims (`AuthorizationUtilizationRules`), and monthly caps such as the
  Employment Specialist 10 hours;
- EVV verified and matching where `EvvRequirementRules` requires it;
- no incompatible concurrent service for the person (`ServiceCompatibilityRules`, rule data, KQ-5);
- staff qualified on the date of service, if the organization's billing policy version enables that
  blocker (KQ-6);
- recipient eligibility verified for the month (manual attestation until 270/271 exists);
- identifiers, rates and trading-partner configuration complete.

Claim creation revalidates immediately before persistence in the same transaction, freezes the claim
inputs (Sati's claim-snapshot decision), and links the claim line to the service record and the
authorization receipt it relied on.

### 10.3 Units

`ServiceUnitRules` derives units from actual time or days using the versioned rule for the service
code: quarter-hour rounding under MaineCare's partial-unit rule (confirm exact rule, KQ-7), per diem
per `PerDiemDayPolicy`, and caps. **Actual minutes stay on the service record; units are derived and
frozen only on the claim line.** One rule owner answers "how many units" everywhere — screens,
utilization, claims, reports. (Sati currently has two answers to that question; structural review
S-4.)

### 10.4 Rates

Rates are append-only versions per service code and modifier with an effective date, administered by
`Billing` + `Administration`. The claim freezes the rate it used.

---

## 11. Persistence

### 11.1 Schema and principals

- All Karuna tables, including its users, organizations, memberships and audit events (D-10), live in
  a `karuna` SQL schema. Sati's tables stay in `dbo`, and nothing moves out of `dbo` for Karuna. A
  `platform` schema is created only when a genuinely cross-product table is first needed (the person
  or organization registry, authority grants).
- `Karuna.Api`'s managed identity gets DML on `karuna` and no rights on `dbo`. This is achievable
  because Karuna does not read Sati's identity tables. `Karuna.Worker` gets its own identity. The
  migration identity is separate from both, following `OPERATIONS.md`.
- Sign-in reads the user table before any organization is known, so that table is outside the
  row-level security tenant predicate and reachable only through the narrow sign-in path (§4.2).

### 11.2 Keys

Decided (D-5), together with the `karuna` schema (§11.1) and the isolation layers (§4.2).

- Karuna aggregates use `Guid` keys generated as time-ordered UUIDv7, so a client can generate the
  identity of a record it is creating and retry safely: **the identity is the idempotency key**. A
  retried "record administration" with the same id returns the original result rather than creating a
  second dose (Sati's "explicit API writes are not automatically replayed" decision, made safe by
  construction).
- Every cross-aggregate foreign key is composite with `OrganizationId` (§4.2).

### 11.3 Append-only facts

Administration records, observations, punches, EVV maintenance, approvals, filings, notifications,
ledger entries, counts and emergency-access grants are insert-only. The application context refuses
updates and deletes to them (Sati's audit pattern), **and** the database denies `UPDATE`/`DELETE` to
the runtime principals on those tables. Corrections are new rows referencing the original.

### 11.4 Drafts and concurrency

Mutable drafts carry a `rowversion` revision and return typed 409s. **Drafts are server-side records**
(status Draft), autosaved; the web client keeps no PHI in persistent browser storage in the first
release (§13.3).

### 11.5 JSON

JSON columns hold only frozen snapshots (a received PCP's structure, a claim's frozen inputs). Any
fact a rule reads or a report filters on is a column or a row. (Sati's `OverrideObligationIdsJson`
decides billability from an unconstrained JSON array; structural review S-10.)

### 11.6 Documents

Uploaded and generated documents use the platform artifact store: private, write-once blob storage,
SHA-256 and byte length in SQL, no bytes in SQL, fail closed when storage is not configured —
exactly Sati's signature-evidence decision of 2026-09-27.

---

## 12. Sharing with Sati and OADS

Through the platform sharing policy only. Proposed defaults, all subject to counsel (KQ-9):

| Record | Case manager (relationship via authorization) | OADS (authority) | Layer |
|---|---|---|---|
| Drafts of anything | No | No | Operational: not yet shareable |
| Approved service records, progress summaries | Yes | Only within an open review in scope | Ordinary, default open |
| Reviewed event reports involving the person | Yes, with notice | Via Evergreen, not Karuna | Ordinary |
| Active behaviour plan, restrictive-intervention records | Yes | Via Evergreen | Ordinary |
| Active medication list | Yes | No | Ordinary, **but** see protected categories |
| MAR administration detail, health observations | On request, not by default | No | Proposed closed |
| Anything in a specially protected category (substance use, mental health, HIV) | Only if a signed release names the category | Counsel question | Regulatory: default closed, no override |
| Personal funds, staff records | Never | Never | Not shareable |

**Tagging protected content is the hard part.** A medication list can disclose substance-use
treatment; a diagnosis list can disclose mental-health treatment. Karuna cannot infer the category
reliably from codes, and `DECISIONS.md` already refuses to manufacture consent from stored data. The
conservative first release withholds medication lists and diagnoses across the boundary unless a
release covers them, shows the reader that something is withheld (PLATFORM_DOMAIN open question 5
applies), and asks counsel before relaxing it.

After an authorization ends, the case manager keeps read access to documentation written under it
and gets nothing new (PLATFORM_DOMAIN open question 4; confirm retention with counsel).

---

## 13. Clients

### 13.1 One web client

**Decided (D-3), conditional on the spike below: `Karuna.Web`, a Blazor WebAssembly application
installable as a PWA, is Karuna's only client in the first release** — for DSPs on house tablets and phones and for managers, nurses,
schedulers and billing staff at desks.

- It runs on the devices DSPs actually have, with no installer.
- It references `Karuna.Contracts`, so previews use the same C# rule owners as the server — the Sati
  principle of one rule, two callers, kept across a new client technology.
- One client codebase is sustainable for a one-person engineering team; a WPF back office plus a
  mobile front line would be two.

**Rejected:** WPF (wrong devices, installation per user, and it would carry Sati's desktop coupling).
**Rejected for now:** Blazor Server (needs a live connection for every click; a dropped connection
mid-med-pass is a safety problem). **Deferred:** a native Avalonia or MAUI client, only if the offline
spike (§13.3) shows the web cannot meet field needs. `AGENDA.md`'s mobile strategy already requires
that spike before any mobile client.

The required spike: on a low-cost Android tablet over LTE, a cold load within five seconds, a
six-person medication pass completed with TalkBack and with a keyboard, and idle lock and staff switch
working. If the spike fails, the decision is revisited before any module is built on it.

### 13.2 Two modes

- **Shift mode** (the default on an enrolled device): who is here, what is due now, the medication
  pass, what to document before the shift ends, protocols for each person. Large targets, plain
  language, photo on every person-specific screen.
- **Office mode**: queues, reviews, schedules, billing, configuration.

Both are views over the same routes and rules.

### 13.3 Connectivity

**First release: online-first, with a designed downtime procedure, and no PHI in persistent browser
storage.** Drafts autosave to the server. When the connection drops, the client says so and stops
accepting entries rather than queueing them silently. The downtime procedure — printed MAR, face
sheets and schedules, then back-entry with "downtime" provenance — is part of the product, not a
policy memo.

Offline capture is the most requested and most dangerous feature. Two offline devices at one house
can both record the 8:00 AM dose. It needs its own design: device-loss rules, encrypted storage with
non-extractable keys, conflict rules that surface double administration rather than merging it, and
server-side reconciliation. `AGENDA.md` already says mobile security, offline storage, synchronization
and device-loss rules must be defined "before storing any protected data on a phone". That design is
a later step, not an implementation detail.

### 13.4 Accessibility and language

WCAG 2.2 AA as the floor, tested through the browser accessibility tree and with NVDA and TalkBack,
not only by markup review — Sati's measured-accessibility decision applied to a new platform.
Status is never colour alone. Many Maine DSPs work in a second language; UI localization is a real
question for adoption (KQ-12), and the design keeps all user-facing strings in resources from the
first screen so the answer can be yes later.

---

## 14. Audit

The platform envelope (`AUDIT_EVENTS.md`) with `karuna.` actions. No narratives, names, drug names,
reasons or funds amounts in metadata.

- Every successful protected write records an action in the same transaction.
- **Reads that leave the system are audited**: face-sheet and downtime-MAR prints, record-set
  exports, document downloads, audit exports.
- **Emergency access is audited with the authority recorded**, not only the user.
- Routine on-screen reads are recorded in structured logs with person identifier and correlation id,
  without content, and are not duplicated into `AuditEvent` (KQ-10 asks whether per-read audit is
  required).
- Route metadata declares each route's tenant owner, capability and audit action; a test fails any
  mutating route without them, and the Karuna route inventory is generated from that metadata rather
  than maintained by hand (the lesson of `API_AUTHORIZATION.md`, structural review S-6).

---

## 15. Environments and operations

- **Public names are neutral (D-9).** `Karuna` may appear in code and project names, but not in
  anything publicly hosted until Josh says otherwise. That covers Azure resource names (they become DNS
  names), the web app's title and manifest, and device install names.
- **Synthetic only** until the gates in `REGULATORY_CONCERNS.md` and a Karuna-specific clinical review
  are met. Demo gets a synthetic Karuna organization with houses, residents, orders and staff, reset
  with the Demo baseline.
- **Availability.** Karuna's hosting cannot be the Demo's App Service Free F1. Production needs a tier
  with zone redundancy, health-checked deployment slots, and an agreed recovery objective that
  reflects medication passes.
- **Background work** runs in `Karuna.Worker` with SQL application-lock singletons (Sati's Claim.MD
  coordination precedent): deadline notices, escalation, outbox dispatch, nightly downtime-readiness
  check.
- **Monitoring** with named alert owners before any real use; the incident-telemetry pattern
  (`IncidentHealthScoring`) extends to Karuna for operational errors only.
- **Downtime drills** are an operational requirement: an agency practises the paper MAR and back-entry
  before cutover.

---

## 16. Moving an agency from Therap

Following Sati's Credible import design — explicit, reviewed, never silent:

- **Current state is imported; history is retained, not reconstructed.** People, contacts, guardians,
  allergies, diagnoses, protocols, staff and credentials import through reviewed batches. Historical
  T-Logs, GERs, MARs and plans arrive as archived PDFs or exports stored as hash-verified artifacts
  with source and date range. Karuna does not invent structured records from them (Sati refused to
  invent history for existing people for the same reason).
- **Medication orders never import as active.** They arrive as Draft orders and require the normal
  verification against current pharmacy records before the first scheduled dose.
- **Cutover is per site, at a boundary**: a `SystemOfRecordCutover` records the instant a site's
  documentation, and separately its MAR, moves to Karuna. Before it, Karuna refuses entries for that
  site except as imported artifacts; after it, Therap is read-only by agency procedure. The MAR cuts
  over at a medication-pass boundary, never mid-pass.
- **Therap's export formats were not examined in this review** (KQ-11). The import design must start
  by obtaining a real export from a consenting agency, under a data agreement, into a non-Production
  environment.

---

## 17. Testing

In addition to Sati's rules (fail-first for security and concurrency, cross-tenant tests on every
route):

- **SQL Server, not SQLite, for API integration tests from the first commit.** Row-level security,
  `rowversion`, serializable transactions, application locks and filtered indexes are what Karuna's
  guarantees rest on; SQLite has none of them. CI runs them (structural review S-7).
- An **access matrix** test suite for `CareTeamAccess`: standing, shift, grace expiry, transfer,
  device intersection, emergency access, each capability and record kind.
- **DST suites** for every time rule: shift duration, service-date split, scheduled doses on both
  transition days.
- **Clinical-fact acceptance** tests: every clinical rule violation is accepted, flagged and routed;
  none returns a refusal.
- **Claim refusal** tests: every blocker in `KarunaClaimReadiness` refuses independently.
- **Idempotency** tests: a retried create with the same id never produces a second record.
- **Architecture** tests: dependency rules (§3.1), every entity has a tenant filter and non-null
  `OrganizationId`, every mutating route declares audit metadata, no `DateTime.Now/Today` in server
  code.

---

## 18. Landing order

Detailed with acceptance criteria in `CODEX_HANDOFF.md`. In short:

- **Prerequisites** (in Sati's repository, before any Karuna code):
  - **P0** root-project exclusions;
  - **P1** the dependency-graph guardrail;
  - **P2** one clock;
  - **P3** product-neutral platform contracts;
  - **P4** the hosting library;
  - **P5** schema-aware tooling and the chain's move to `SatiLogica.Schema`;
  - **P6** the Local migration runner, which lands before any Karuna entity joins the chain;
  - **P7** the Maine business-day calendar, which lands before K3.
- **K0** foundation: a row-level security lifecycle spike first; then Karuna-owned identity and audit
  tables, organization tenant, sites, programs, capabilities, devices, enrolment, care-team access,
  emergency access and isolation layers.
- **K1** person record and face sheet. **K2** staff qualifications. **K3** event reports.
- **K4** eMAR (its own internal order in the eMAR design). **K5** daily documentation and service
  records. **K6** behaviour support. **K7** scheduling, time, EVV phase 1.
- **K8** billing on the extracted platform mechanics. **K9** Therap import and cutover.
- **K10** oversight dashboards. **K11** personal funds.
- **Later:** relationship sharing with Sati (needs the platform authorization), EVV alternate vendor,
  pharmacy interfaces, offline capture, guardian access.

Event reports precede the eMAR because medication errors must have somewhere to go on the eMAR's
first day. Qualifications precede both because eligibility is checked on every administration.

---

## 19. Open questions

These need Josh, an agency, an RN clinical advisor, OADS or counsel. **Do not decide them in code.**
Where a default is proposed, it is a placeholder that must be confirmed before real use.

- **KQ-1** Per diem billable day: admission, discharge, hospital and leave-of-absence days.
- **KQ-2** Which Section 21/29 procedure codes require EVV — obtain the State's impacted-services
  spreadsheet.
- **KQ-3** *Resolved 2026-09-28 by D-6:* EVV phase 1 reconciles with the State's Sandata EVV;
  alternate-vendor certification comes later.
- **KQ-4** The exact reading of "within one (1) business day of the Reportable Event", including events
  late on a Friday or before a holiday, and whether the clock runs from event or awareness when they
  differ.
- **KQ-5** Which services may not be billed concurrently or on the same day for one person.
- **KQ-6** Whether an unqualified staff member on the date of service makes a service unbillable.
- **KQ-7** MaineCare's partial-unit and rounding rule for Section 21/29 quarter-hour services.
- **KQ-8** Which medication rule governs each site type (10-144 ch. 113 or otherwise) — see the eMAR
  design's MQ list for the medication-specific questions.
- **KQ-9** Sharing defaults in §12, including whether provider-to-case-manager flow is a disclosure,
  and how protected categories are identified.
- **KQ-10** Whether routine reads must be audited per record, beyond structured access logs.
- **KQ-11** Therap export formats and contract terms for data return.
- **KQ-12** UI languages needed by Maine DSP workforces.
- **KQ-13** Shared living providers: are they users of the organization tenant, and what device and
  qualification rules apply in a private home?
- **KQ-14** Guardian and family access: scope, identity proofing, and whether it belongs to Karuna or
  the platform.
- **KQ-15** Retention periods for MAR, event report, service documentation and funds records.
- **KQ-16** Whether the OADS adult reportable-events matrix differs from the rule text in categories or
  timing; obtain it and Evergreen's field list before building the taxonomy.

---

## 20. Documents to create when steps land

- `karuna/KARUNA_ARCHITECTURE.md` — ownership map and rule-owner table (the `ARCHITECTURE.md` twin).
- `karuna/KARUNA_API_AUTHORIZATION.md` — generated route inventory with tenant owner, capability,
  reach rule and audit action.
- `karuna/KARUNA_AUDIT_EVENTS.md` — the `karuna.` action catalog.
- Further `DECISIONS.md` entries as each step settles a durable choice. D-1 to D-9 are already
  recorded there, and `PLATFORM_DOMAIN.md` and `PLATFORM_RESTRUCTURE_PLAN.md` were amended to match
  on 2026-09-28.
- `REGULATORY_CONCERNS.md` — a Karuna section: eMAR, restrictive interventions, event reporting,
  EVV, funds, and the open counsel questions above.
