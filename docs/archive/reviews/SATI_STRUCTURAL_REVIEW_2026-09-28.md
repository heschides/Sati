# Sati structural review — 2026-09-28

**Repository snapshot:** `master` at `b2d249a` ("Record Sati 1.3.30 release evidence") with the
working tree's uncommitted changes present but not reviewed as part of this document.
**Purpose:** structural problems — ownership, boundaries, data model, verification and process — found
while designing Karuna (`karuna/KARUNA_DESIGN.md`). This is not a bug hunt and not a security audit;
`SECURITY_REVIEW_2026-09-10.md` and `API_SECURITY_AUDIT.md` remain the security record.

Confidence labels follow `SATI_SYSTEM_MAP.md`: **VERIFIED** (read in current source or configuration),
**INFERRED** (a strong conclusion from verified code, not an observed runtime fact), **UNVERIFIED**
(could not be established from the repository). Line numbers describe this snapshot and will drift.

Like the earlier dated reviews, this is a point-in-time reading, not a certification.

---

## Overall calibration

On a scale where 1 is "nobody would ever notice", 50 is "tweaks needed; expected midway through
development" and 100 is "irreparable time bomb", **this review lands at about 40.**

- **Nothing here requires a rewrite**, and the most dangerous-sounding findings fail closed: a person
  with a null agency is unreachable rather than exposed; the Demo-only authentication check refuses
  rather than admits.
- **The codebase is unusually disciplined for its stage**: named rule owners shared by client and
  server, append-only evidence, fail-first security tests, typed concurrency conflicts, dated reviews
  that record what was sound, and documentation that is honest about what is not deployed.
- **Two findings are more than tweaks** — S-1 (every workflow implemented twice over two entity
  models) and S-2 (tenant isolation by per-route discipline over nullable tenant columns). Both are
  normal for a desktop application midway to a hosted platform, and both become expensive if hosted
  Production or Karuna is built on top of them unchanged. Doing that would move this review to roughly
  60–65.
- The rest are small, local corrections, several of which are latent defects rather than design
  choices (S-3, S-4, S-8).

Each finding below carries its own severity on the same scale.

| # | Finding | Severity | Effort |
|---|---|---:|---|
| S-1 | Every workflow is implemented twice, over two entity models | 60 | Large |
| S-2 | Tenant isolation is procedural, over nullable tenant owners | 55 | Medium–large |
| S-5 | The restructure plan contains two boundary contradictions | 45 | Small now, large later |
| S-7 | SQL Server behaviour the guarantees rely on is not tested in CI | 45 | Small |
| S-3 | The API still reads the host clock; "the Maine date" has two owners | 40 | Small |
| S-9 | The governing documents have outgrown their readers and drifted | 40 | Medium |
| S-6 | A 10,991-line endpoint file; route inventory and audit are by hand | 35 | Medium |
| S-4 | "How many units" has four implementations and two answers | 35 | Small |
| S-8 | The root WPF project compiles every `.cs` file under the repository | 30 | Small |
| S-10 | Billing-authoritative facts stored as unconstrained JSON | 25 | Small–medium |
| S-11 | `PlatformOperator` containment grew; authentication is coupled to Demo | 25 | Small |
| S-12 | One note status, three representations | 20 | Small |
| S-13 | `Person` is anchored to one owner and carries domain logic on the entity | 20 | Medium |

---

## S-1. Every workflow is implemented twice, over two entity models — severity 60

**What.** Sati has two runtime architectures (`SATI_SYSTEM_MAP.md` §"Architecture in one
paragraph"). Local Production runs WPF → local EF service → `SatiContext` → LocalDB. Demo runs WPF →
`Cloud*` HTTP adapter → `Sati.Api` endpoint → `ApiDbContext` → Azure SQL. The two paths do not share an
application-service implementation, and they do not share an entity model.

**Evidence (VERIFIED).**

- `Data/` holds 117 source files of local service implementations; `Data/Cloud/` holds 29 files
  declaring 51 `Cloud*` classes.
- `Sati.Api/Data/ApiDbContext.cs` is 1,733 lines and declares at least 46 hand-written `Server*`
  entity classes (`ServerPerson`, `ServerNote`, `ServerClaimLine`, … `ServerChatReadMarker`) that map the
  same tables `Sati.Persistence` maps with its own entity types. `ApiDbContext` exposes 76 `DbSet`s;
  `SatiContext` 75; `SignatureDbContext` a further 10 over the same database.
- `ARCHITECTURE.md` already names the fix: "make server persistence/migrations authoritative so
  `SatiContext` and `ApiDbContext` cannot drift", and `SATI_SYSTEM_MAP.md` §3 records that "there are
  still two application-service implementations to keep aligned".

**Why it matters.** Shared rule owners in `Sati.Contracts.V1` keep the *rules* aligned. They do not
keep the *orchestration* aligned: which rows load, in what transaction, with what lock, what is
audited. Every feature costs roughly twice, every fix must be found twice, and `SchemaDriftHealthCheck`
exists because two models of one schema can disagree. S-3 and S-4 below are examples of the drift this
structure produces.

**Recommendation.** Decide the future of Local Production explicitly; there are two sound options.

1. **Run the API in-process for Local Production.** The desktop hosts `Sati.Api`'s application layer
   on loopback (or in-process through the same endpoint pipeline) against LocalDB, and the WPF client
   uses the `Cloud*` adapters everywhere. One application service, one entity model, and Local
   Production keeps its data on the workstation. This can be done before any hosted Production exists.
2. **Set a retirement date for Local Production** once hosted Production is authorized, and stop
   building new local implementations now — new features ship API-only, and Local Production gets them
   when it moves.

Either way, stop adding `Server*` twins: new server entities should come from `Sati.Persistence`.
**Karuna must not repeat this** — it has no local mode (`karuna/KARUNA_DESIGN.md` §1.6).

---

## S-2. Tenant isolation is procedural, over nullable tenant owners — severity 55

**What.** `DECISIONS.md` ("Tenant isolation is structural") says every protected aggregate must have an
unambiguous tenant owner and "no feature may assume that a forgotten query predicate is adequate
isolation". The code relies on exactly that predicate, carefully applied.

**Evidence (VERIFIED).**

- There is no `HasQueryFilter` anywhere in the repository (0 occurrences) and no row-level security.
- `Person.AgencyId` is `int?` (`Sati.Persistence/Models/Person.cs:63`), as is `Note.AgencyId`
  (`Note.cs:41`); the API twins match (`ApiDbContext.cs:913`, `:1088`). A person's tenancy is
  effectively derived twice — from `Person.AgencyId` and from the owning user's agency
  (`TenantAccess.OwnedPeople`, `TenantAccess.cs:80-90`).
- `TenantAccess.CanAccessPersonAsync` requires `person.AgencyId == actor.AgencyId`
  (`TenantAccess.cs:63-66`), so a null agency is unreachable — **fail-closed, which is why this is not
  higher**. But each route must call it; `API_AUTHORIZATION.md` is the hand-maintained evidence that
  they do, backed by cross-tenant tests.

**Why it matters.** The current model works because one careful developer applies it everywhere and
tests it. It does not survive the arrival of deliberate cross-tenant reads (Karuna's relationship
reads, OADS authority), because the first exception to a convention is where conventions break, and a
null tenant owner cannot be enforced by a foreign key or a filter.

**Recommendation.**

1. Backfill and make `AgencyId` non-null on `Person`, `Note` and every other protected aggregate; add
   composite `(AgencyId, Id)` foreign keys where child rows reference tenant-owned parents (the
   clearinghouse tables already do this).
2. Add EF global query filters in `ApiDbContext` keyed to the validated actor, with a test that fails
   if any tenant-owned entity lacks one. Keep `TenantAccess` for caseload and supervisory rules; the
   filter only guarantees the tenant boundary.
3. Consider SQL Server row-level security as a second layer before hosted Production.
4. Do this before building any cross-tenant read, so the exceptions are visible against a structural
   default.

---

## S-3. The API still reads the host clock, and "the Maine date" has two owners — severity 40

**What.** The 2026-08-17 decision ("Review and billing read the same clock") moved supervisory review
onto `ApiClock`. Other API paths still use the host's date.

**Evidence (VERIFIED).** `DateTime.Today`/`DateTime.Now` in the API:
`AnnualPacketEndpoints.cs:34, 72, 135, 177`; `ApiEndpoints.cs:6894` (EDI `generatedAt`), `:8296`,
`:8368` (form completion future-date check), `:8740` (`PersonSaveRules.Validate`), `:8854`;
`BillingCorrectionEndpoints.cs:312`. Separately, `ApiClock` converts with the configured
`SatiApiOptions.TimeZoneId` (default `"Eastern Standard Time"`, `ApiOptions.cs:41`) while
`BillingRules.MaineBusinessDate` hard-codes `"America/New_York"` (`BillingRules.cs:28-32`).

**Consequence (INFERRED).** Azure App Service runs in UTC unless `WEBSITE_TIME_ZONE` is set; the
repository sets it only for the Demo Refresh Function (`scripts/Publish-DemoRefresh.ps1:68`), and the
API's app settings are not in source (UNVERIFIED for the deployed API). If the API host is UTC, then
from 8:00 PM EDT (7:00 PM EST) until midnight, `FormCompletionRules.Validate(completedOn,
DateTime.Today)` accepts tomorrow's date as not-in-the-future, annual-packet windows shift a day, and
EDI timestamps are UTC.

**Recommendation.** One platform clock owner (`TenantClock`), injected everywhere; delete
`MaineBusinessDate` or make it delegate. Add `Microsoft.CodeAnalysis.BannedApiAnalyzers` with
`DateTime.Now`, `DateTime.Today` and `TimeZoneInfo.Local` banned in `Sati.Api` and `Sati.Contracts`,
so the rule is enforced by the build rather than remembered.

---

## S-4. "How many units" has four implementations and two answers — severity 35

**Evidence (VERIFIED).**

| Implementation | 20 minutes = |
|---|---|
| `BillingRules.CalculateSection13Units` (`BillingRules.cs:12-23`) — the billing owner | **1.33** (partial units after the first 15 minutes) |
| `Note.CalculateUnits` (`Note.cs:92-96`) | **2** (ceiling) |
| `ProductivityForecast.CalculateUnits`, private (`ProductivityForecast.cs:417-418`) | **2** |
| `ApiEndpoints.CalculateUnits`, private (`ApiEndpoints.cs:9796-9797`) | **2** |

The ceiling version feeds the note-entry label ("MINUTES — 2 UNITS", `NoteEntryViewModel.cs:440-442`),
productivity (`ProductivityReportService.cs:54`, `ApiEndpoints.cs:5828`) and the consumer billing-loss
percentage (`ConsumerBillingLossReportService.cs:192`, `ApiEndpoints.cs:5955`). Claims use the partial
rule.

**Why it matters.** `CLAUDE.md`: "A second hand-written copy of one of these rules is a defect, not a
convenience." Here there are four copies and they disagree. A case manager is told a note is two units
when it bills 1.33, and a report named "billing loss" is computed on units that billing does not use.
If whole-unit productivity is intentional, it is a different rule and needs its own name and owner.

**Recommendation.** Decide whether productivity counts whole or billed units; give each an owner in
`Sati.Contracts.V1`; delete the private copies; add a test that `Note` exposes no unit arithmetic of
its own.

---

## S-5. The restructure plan contains two boundary contradictions — severity 45

Both are found by designing Karuna, which is what `PLATFORM_DOMAIN.md` said a second product would do.
Neither is in code yet, which is why they are cheap now.

**Decided 2026-09-28** as Karuna decisions D-1 (composition assembly) and D-4 (billing mechanics are
platform); see `DECISIONS.md`. `PLATFORM_DOMAIN.md` and `PLATFORM_RESTRUCTURE_PLAN.md` are amended.
Implementation is steps P5 and K8 in `karuna/CODEX_HANDOFF.md`.

**(a) The migration chain cannot live where stage 4 puts it (VERIFIED against the plan's text).** Stage
1 asserts "no platform project may reference a product project". Stage 4 moves `SatiContext` and the
single migration chain into `SatiLogica.Persistence`, with product entities registered "through
`IEntityTypeConfiguration`" from product assemblies. EF's migration model snapshot needs every entity
type at design time, so the assembly that owns the chain must see every product's types. All three
statements cannot hold. **Recommendation:** a composition assembly (`SatiLogica.Schema`) references the
platform and every product persistence assembly and owns the single chain; `SatiLogica.Persistence`
stays product-agnostic. Decide before stage 4.

**(b) Billing mechanics are classified as Sati's, and Karuna needs them.** `PLATFORM_DOMAIN.md` places
"Billing, claims and remittance" in the Sati column, and the restructure plan lists
`ClaimResponseReader` among case-management contracts. Karuna bills MaineCare with the same 837P and
835 transaction sets. **Recommendation:** classify the formatter, trading-partner profiles,
clearinghouse outbox and connectors, response/835 ingestion, remittance and deposit reconciliation as
platform; keep eligibility and readiness rules per product. Otherwise stage 3 leaves Karuna needing a
reference to `Sati.Contracts` to bill.

---

## S-6. A 10,991-line endpoint file; the route inventory and audit are by hand — severity 35

**Evidence (VERIFIED).** `Sati.Api/Endpoints/ApiEndpoints.cs` is 10,991 lines (the next-largest
source file in the repository is 2,827). Endpoint lambdas contain orchestration, not only transport.
`API_AUTHORIZATION.md` (353 lines) is the hand-maintained inventory of each route's tenant owner.
Auditing is written per route; `AGENDA.md` records at least one mutation path without an audit event
(the overdue abandonment sweep).

**Why it matters.** Review, merge and audit are all harder in one file; a route's security properties
are a document that can drift from the code; and "every protected mutation is audited" is a
convention, not a check.

**Recommendation.** Split by feature (the `ApiEndpoints.*.cs` partials have started this) behind
application services. Declare tenant owner, required capability and audit action as endpoint metadata;
generate the authorization inventory from it; add a test that fails any mutating route without an
audit action. That converts two documents into enforced invariants.

---

## S-7. SQL Server behaviour the guarantees rely on is not tested in CI — severity 45

**Evidence (VERIFIED).** The API integration factory uses SQLite (`Sati.Api.Tests/SatiApiFactory.cs:95`).
SQL Server suites are opt-in through `SATI_RUN_SQLSERVER_TESTS=1`
(`Sati.Api.Tests/SyntheticPipelineFactory.cs:45, 284`), and `.github/workflows/build.yml` does not set
it. `SECURITY_REVIEW_2026-09-10.md` item 6 already says "SQLite tests and EF guards do not establish
production SQL behavior".

**Why it matters.** Several of Sati's documented guarantees are SQL Server behaviour: application locks
for Claim.MD and the billing-period write scope, serializable transactions for policy application,
filtered unique indexes, and concurrency tokens. CI proves none of them on every change.

**Recommendation.** Add a CI job with SQL Server (a service container on `ubuntu-latest`, or LocalDB on
`windows-latest`) that runs the opt-in suites on every push. Karuna's API tests should target SQL
Server from the first commit.

---

## S-8. The root WPF project compiles every `.cs` file under the repository — severity 30

**Evidence (VERIFIED).** `Sati.csproj` sits at the repository root and relies on SDK default globbing,
with a hand-maintained exclusion list for every sibling project (`Sati.csproj:46-60` `Compile Remove`,
`:76-85` `None Remove`). There is no entry for `karuna\` or `platform\`.

**Why it matters.** The first `.cs` file created under `karuna/` or `platform/` — exactly what the
restructure and Karuna will do — compiles into the Sati WPF application, silently. Every new project
requires remembering to edit `Sati.csproj`.

**Recommendation.** Immediately: add `Compile`/`None`/`Content`/`EmbeddedResource` removes for
`karuna\**` and `platform\**`, and a test asserting the WPF assembly contains no types from those
namespaces. Structurally: restructure stage 6 (move the WPF project into `sati/Sati.Desktop`) removes
the problem; consider doing it before stage 3 rather than last.

---

## S-9. The governing documents have outgrown their readers and drifted — severity 40

**Evidence (VERIFIED).**

- `CLAUDE.md` requires reading `CLAUDE.md`, `ARCHITECTURE.md` (3,019 lines), `DECISIONS.md` (5,613)
  and `AGENDA.md` (8,033) before architectural changes: about 16,700 lines and 1.2 MB. That exceeds any
  coding agent's working context, so the instruction cannot be followed as written; agents will skim.
  There are 49 Markdown files at the repository root.
- `CLAUDE.md` and `AGENTS.md` have diverged while both say "Last updated: August 15, 2026, against
  release 1.2.17": `CLAUDE.md` lists nine rule owners and no DATT section; `AGENTS.md` lists five and
  alone contains the DATT release trigger.
- `README.md` states release 1.2.17 and "136 desktop and 65 API tests"; the latest handoff reports
  3,548 passing tests and the latest release is 1.3.30.
- The migration count is stated as 175 (`PLATFORM_RESTRUCTURE_PLAN.md`), 108 (`SATI_SYSTEM_MAP.md`),
  and is 120 migration files today.
- `DECISIONS.md` says "Newest sections at the bottom" and "Last updated: 2026-09-14", but entries are
  out of order (2026-09-06 → 2026-09-27 → 2026-09-06 around lines 3174–3318), and top-level `#`
  headings appear mid-file (`DECISIONS.md:2923`, `ARCHITECTURE.md:2911`, `REGULATORY_CONCERNS.md:374`).

**Why it matters.** The documents are one of Sati's real strengths — the reasoning is recorded and
honest. But documentation that must be read completely and cannot be, and that disagrees with itself,
stops governing. For a codebase built largely with coding agents, that is a structural control
failing quietly.

**Recommendation.**

1. One agent briefing: make `AGENTS.md` the file and have `CLAUDE.md` contain only a pointer to it (or
   the reverse), so they cannot diverge.
2. Split `DECISIONS.md` into dated decision records (`docs/decisions/2026-09-07-membership-and-authority.md`)
   with a generated index carrying status (accepted, superseded by …). Supersession becomes a field,
   not a paragraph.
3. Split `AGENDA.md` into open items and an archive of closed ones; agents read only the open file.
4. Replace hand-written counts (tests, migrations, rule owners) with generated values or remove them.
5. Rewrite the reading instruction to name the index plus the decisions relevant to the area, not
   every file in full.
6. Group documents by product (`docs/platform`, `docs/sati`, `karuna/`).

---

## S-10. Billing-authoritative facts stored as unconstrained JSON — severity 25

**Evidence (VERIFIED).** `Note.OverrideObligationIdsJson` (`Note.cs:70-81`) holds the exact obligation
IDs a supervisory billing exception covers, and it is read by billing, supervision and both contract
mappers. `DECISIONS.md` makes those exact IDs the scope of what may be billed ("an unselected, stale,
or newly discovered blocker still prevents claim creation").

**Why it matters.** A fact that decides billability is unqueryable in SQL, unconstrained by any
foreign key, and invisible to schema-drift and migration analysis. JSON is right for frozen snapshots
(the claim snapshot, visit documentation); it is wrong for authoritative sets.

**Recommendation.** Normalize exception scope into rows (`NoteBillingExceptionObligation`) with foreign
keys, inside the existing revision boundary. Audit other JSON columns against the same test: does a
rule read it, or does a report filter on it?

---

## S-11. `PlatformOperator` containment grew; authentication is coupled to Demo — severity 25

**Evidence (VERIFIED).**

- The 2026-09-07 decision was to stop widening the exclusion pattern, when `PLATFORM_DOMAIN.md` counted
  "five queries". Today there are six `!= "PlatformOperator"` comparisons in the API
  (`AccountLifecycleEndpoints.cs`, `ApiEndpoints.cs` ×3, `ChatEndpoints.cs` ×2) and six
  `!= UserRole.PlatformOperator` comparisons in the local services and model (`UserService.cs` ×3,
  `AdminService.cs`, `LocalPlatformHealthService.cs`, `User.cs`). Two commits since 2026-09-07 changed
  `PlatformOperator` references (`f1db749`, `09ccdd1`).
- `ValidatedActorFilter` requires `DatabaseIdentities.EnvironmentName == "Demo"` to validate any token's
  instance claim (`TenantAccess.cs:121-128`; also `ApiEndpoints.cs:1300, 1343`). The API as written
  rejects every token against any non-Demo database.
- Actor validation still compares `user.Role == actor.Role` (`TenantAccess.cs:18, 87, 119`) although
  `Role` is documented as non-authoritative compatibility metadata.

**Why it matters.** None of this is unsafe today — each check fails closed. But the exclusion list is
the shape `PLATFORM_DOMAIN.md` said must not grow, and the Demo literal is an undocumented coupling of
authentication to the Demo reset's instance rotation that hosted Production will trip over.

**Recommendation.** Implement authority grants before adding any further exclusion; derive the
expected environment name from startup-validated configuration rather than a literal; decide whether
`Role` remains part of token integrity and record it.

---

## S-12. One note status, three representations — severity 20

**Evidence (VERIFIED).** `NoteStatus` is an enum in `Sati.Persistence/Models/Enums.cs:114-126`;
`NoteWorkflow` mirrors its ordinals as `int` constants "without coupling contracts to the desktop
persistence model" (`NoteWorkflow.cs:3-7, 28-37`); `ServiceTimeline` matches status **names** as
strings (`ServiceTimeline.cs:59-60`). But `Sati.Persistence` already references `Sati.Contracts`
(`Note.cs:4`), so the coupling the mirror avoids already exists in the other direction.

**Why it matters.** A renamed or reordered status breaks one of the three silently.

**Recommendation.** Move the enum into `Sati.Contracts.V1`, reference it from persistence, and delete
the ordinal mirror and the string set.

---

## S-13. `Person` is anchored to one owner and carries domain logic on the entity — severity 20

**Evidence.** `Person.UserId` is non-null (`Person.cs:15`, VERIFIED); `ARCHITECTURE.md` describes
`Person` as the "central domain entity" that "owns compliance logic, form generation, billing window
evaluation", and `Person.cs` is 809 lines (VERIFIED). The API works with `ServerPerson`, which carries
none of that logic (VERIFIED), so the API must obtain it from contracts or re-express it (INFERRED).

**Why it matters.** For Sati, one case manager per consumer is a true fact about the work. It is not
true for the platform person registry or for Karuna, where many staff serve one person. And logic on
the persistence entity is invisible to the server model that is supposed to become authoritative.

**Recommendation.** Continue moving `Person`'s decision logic into contract rule owners (the pattern
already used for `BillingComplianceGate` and `FormAttestationRules`). Keep single ownership as a Sati
caseload fact — an assignment row — rather than as the identity of the person, before the registry
arrives.

---

## What was found sound

Recorded because a review that lists only problems misrepresents a codebase:

- **Rule ownership** in `Sati.Contracts.V1` is real and widely used; most rules have exactly one owner.
- **Evidence modelling** — append-only attestations, supersession instead of overwrite, claim input
  snapshots, append-only policy versions selected by service date — is consistent and well reasoned.
- **External boundaries** — the clearinghouse outbox, never retrying an uncertain upload, separate
  status and ERA cursors, fail-closed storage configuration — are designed the way healthcare
  integrations should be.
- **Honesty about status** is systematic: "staff verified", "synthetic-only", "not deployed", and
  dated reviews that do not claim compliance.
- **Tests** are extensive and the fail-first rule for security tests is the right discipline.

These are why the score is 40 and not higher.

---

## Suggested order

1. **S-8** now — it will fire the moment Karuna or platform code is created.
2. **S-3**, **S-4**, **S-12** — small, local, and they remove live inconsistencies.
3. **S-7** — so the next structural work is verified against the real database engine.
4. **S-5** — decided 2026-09-28; implement before restructure stage 3 moves any files.
5. **S-2** — before any cross-tenant read is built.
6. **S-1** decision (in-process API or Local Production retirement) — before hosted Production.
7. **S-9**, **S-6**, **S-10**, **S-11**, **S-13** — as the areas are next touched.
