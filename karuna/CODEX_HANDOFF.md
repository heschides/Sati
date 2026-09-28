# Handoff — Karuna, the SatiLogica direct-service program

**For:** Codex, or whoever implements this next, starting with no prior context.
**Written:** 2026-09-28 against `master` @ `b2d249a` (Sati release 1.3.30).
**Revised:** 2026-09-28, twice, after two Codex reviews of the prerequisites. §9 lists what each
changed.
**Status:** design only; no Karuna code exists. Decisions D-1 to D-13 in §2 are confirmed by Josh and
recorded in `DECISIONS.md`. Josh approved the P0 and P1 plans in §3 on 2026-09-28, so they may start.
Later steps proceed in order, each with a review-before-build gate, against synthetic data only. The open questions it names (KQ-*, MQ-*) are
still open.

## 0. Read before writing code

In this order. This brief is a map, not a replacement for them.

1. `AGENTS.md` and `CLAUDE.md` — the project's non-negotiable rules. They have diverged
   (`SATI_STRUCTURAL_REVIEW_2026-09-28.md` S-9); where they differ, follow the stricter rule and tell
   Josh.
2. `PLATFORM_DOMAIN.md` — tenancy, authority versus relationship, the person registry, sharing layers.
3. `karuna/KARUNA_REQUIREMENTS.md` — what Therap does for an agency, and the Maine and federal rules.
4. `karuna/KARUNA_DESIGN.md` — the design. §1.3 is the principle most likely to be violated by habit.
5. `karuna/KARUNA_EMAR_DESIGN.md` — before step K4.
6. `SATI_STRUCTURAL_REVIEW_2026-09-28.md` — Sati problems Karuna must not copy.
7. `PLATFORM_RESTRUCTURE_PLAN.md` — stages 3, 4 and 5 were rewritten on 2026-09-28 to match this
   handoff.

Do **not** try to read `DECISIONS.md` and `AGENDA.md` in full. Search them for the area you are
touching; the Karuna decisions are the two 2026-09-28 entries at the end of `DECISIONS.md`.

**Line numbers** in these documents come from the snapshot they were written against. Verify each one
against current code before relying on it.

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

All are recorded, with reasoning and rejected alternatives, in `DECISIONS.md`:

- D-1 to D-9 under "Karuna's foundational decisions";
- D-10 to D-13 under "Karuna prerequisite corrections";
- four implementation refinements, approved by Josh, under "Karuna prerequisite refinements after the
  second review". The table below includes them.

Treat them as fixed; if implementation shows one is wrong, stop and tell Josh rather than working
around it.

| # | Decided | What it means for implementation | Step |
|---|---|---|---|
| D-1 | One migration chain, owned by a design-time composition context in `SatiLogica.Schema` | No separate Karuna chain. Only `SatiLogica.Migrator` and test projects may reference `SatiLogica.Schema`; no product library, host or client does (enforced in P1). | P5 |
| D-2 | Each product is its own host on a shared `SatiLogica.Hosting` library | The library supplies mechanics behind host-supplied seams. `Sati.Api` keeps its entry assembly. Each host has its own token audience and signing key. | P4 |
| D-3 | Blazor WebAssembly PWA client, conditional on the device spike (`KARUNA_DESIGN.md` §13.1) | Run and record the spike in K0 before building module screens. If it fails, stop and report. | K0 |
| D-4 | Billing mechanics are platform; billing rules stay per product | Extract before K8. | K8 |
| D-5 | UUIDv7 keys as idempotency keys; `karuna` SQL schema; non-null `OrganizationId`, composite FKs, query filters, row-level security | All in K0, after the RLS lifecycle spike. | K0 |
| D-6 | EVV phase 1 reconciles with the State's Sandata EVV | Record Sandata visit ids; gate billing on a matching verified visit. No capture or transmission yet. | K7 |
| D-7 | Clinical facts are never refused for clinical-rule violations; claims are gated | Every validator is written this way (§5). A "record it anyway" confirmation is allowed; a refusal is not. | K3 onward |
| D-8 | One tenant per product, even for an organization holding both roles | A person working on both sides has two accounts. | K0 |
| D-9 | `Karuna` is fine in code; anything publicly hosted uses a neutral name | Ask Josh for the public name when the first resource is needed. | First public resource |
| D-10 | Karuna keeps its own users, memberships and audit events in the `karuna` schema | The platform owns the mechanics (password hashing format, tokens, lockout, revocation, the audit envelope), not the tables. No relocation of Sati's `dbo` identity. | K0 |
| D-11 | Platform contracts are introduced, not moved | `SatiLogica.Contracts` starts with product-neutral types only, using **opaque product-tagged identifiers** (Sati's keys are `int`, Karuna's `Guid`). Sati's `UserPermissions`, `AgencyActor`, `EnvelopeProtection`, `LegalHold`, `DocumentArtifactDto` **and `AuditCsv`** stay in Sati, unchanged; only `AuditCsv`'s spreadsheet-safe field encoding is extracted. | P3 |
| D-12 | Local Production's migrations move to an installer-run `SatiLogica.Migrator` | **P6 lands before P5.** The runner grows from `tools/SatiUpdateReport`, is built against today's chain, and ships first. P5 then moves the chain and repoints the runner in the same commit. Local Production's database will carry other products' empty tables. | P6, then P5 |
| D-13 | The Maine business-day calendar is its own step before K3 | A new State holiday dataset; not `WorkdayHelper` (agency productivity exclusions) or `ExemptDate` (a user's days off). | P7 |

## 3. Prerequisites

**Order: P0, P1, P2, P3, P4, P6, P5, P7.** P6 precedes P5, and the sections below appear in that
order. The numbers are identities, not positions, so earlier references stay valid. Each step is
independently committable in this order. **P0 and P1 are authorized to start now**, on the plans below,
which incorporate Codex's second-review proposals as Josh approved them. Stop and report after each
step.

### P0 — keep `karuna\` and `platform\` out of the Sati WPF build (review S-8)

`Sati.csproj` at the repository root picks up every file beneath it and relies on a hand-kept
exclusion list: `Compile` (lines 46–60), `EmbeddedResource` (61–75) and `None` (76–90) for each sibling
project. There are no `Content` or `Page` removes, and there is no `Directory.Build.props`.

**Evidence (Codex, 2026-09-28).** Evaluating a temporary copy of `Sati.csproj` in the Debug and Demo
configurations picked up `.cs` as `Compile`, `.resx` as `EmbeddedResource`, `.txt` as `None` and `.xaml`
as `Page`; `Content` was empty. `Page` removes are therefore needed, and `Content` removes are not.

**Change.** Beside the existing entries, add exactly:

```xml
<Compile Remove="karuna\**\*.cs" />
<Compile Remove="platform\**\*.cs" />
<EmbeddedResource Remove="karuna\**\*" />
<EmbeddedResource Remove="platform\**\*" />
<None Remove="karuna\**\*" />
<None Remove="platform\**\*" />
<Page Remove="karuna\**\*" />
<Page Remove="platform\**\*" />
```

**Test:** `RootProjectItemBoundaryTests.ExcludesKarunaAndPlatformItems` in `Sati.Tests`, a theory over
Debug and Demo. It evaluates the real items instead of looking for types, because an assembly check
passes vacuously while no Karuna types exist.

1. Copy `Sati.csproj` into a fresh temporary directory.
2. Create `Sentinel.cs`, `Sentinel.resx`, `Sentinel.xaml` and `Sentinel.txt` under each of `karuna\` and
   `platform\` there. Add a control, `RootSentinel.cs`, at the root.
3. Run `dotnet msbuild <temp>\Sati.csproj -getItem:Compile,EmbeddedResource,None,Page,Content
   -property:Configuration=<config>`. This is evaluation only: no build, no restore.
   - Locate `dotnet` through `DOTNET_HOST_PATH` (set by the test host), falling back to `PATH`.
   - Use a bounded timeout, and include MSBuild's standard error in any failure message.
4. **Parse the JSON; don't match text.** Assert that none of the eight nested sentinels appears in any
   of the five item lists. Compare both the normalized `Identity` and the `FullPath` under the
   temporary root, case-insensitively, so link metadata cannot hide one.
5. Assert that the control appears in `Compile`, so a broken evaluation cannot pass vacuously.
6. Delete the temporary directory in a `finally`.

**Fail first:** red against today's `Sati.csproj`, reporting the leaked items; green after the eight
entries. Record the leaked-item list from the red run in the commit notes. Nothing is created inside
the repository.

### P1 — the project-reference guardrail (restructure stage 1)

The solution lists 13 projects, but the repository has 18 project files. Five sit outside the solution:
`installer/Sati.LocalBootstrap` and `tools/BrochureBackdrop`, `BrochureDecompile`, `SatiComplianceSeed`
and `SatiUpdateReport`. The guardrail must cover all of them, or a new project added outside the
solution escapes it.

**Data.** `architecture/project-graph.json`, with repository-relative, forward-slash paths. Each
project has two separate fields:

- `product`: `platform`, `sati` or `karuna`;
- `kind`: `library`, `host`, `client`, `test` or `tool`.

Its `references` array is the **complete approved outgoing `ProjectReference` set**. Keeping product
and kind separate answers the question Codex raised: a test belongs to its subject's product, so
future `Karuna.Tests` is product `karuna`, kind `test`.

The initial 18 nodes. Codex's edge list matches every real `ProjectReference` exactly, and none are
conditional. All 18 are product `sati`:

| Project | kind | references |
|---|---|---|
| `Carika/Carika.csproj` | client | Sati.Contracts |
| `Carika.Tests/Carika.Tests.csproj` | test | Carika, Sati.Contracts |
| `installer/Sati.LocalBootstrap/Sati.LocalBootstrap.csproj` | tool | — |
| `Sati.csproj` | client | Sati.Contracts, Sati.Forms, Sati.Persistence |
| `Sati.Api/Sati.Api.csproj` | host | Sati.Contracts, Sati.Forms, Sati.Persistence, Sati.Signatures |
| `Sati.Api.Tests/Sati.Api.Tests.csproj` | test | Sati.Api, Sati.Contracts, Sati.Signatures |
| `Sati.Contracts/Sati.Contracts.csproj` | library | — |
| `Sati.Forms/Sati.Forms.csproj` | library | Sati.Contracts |
| `Sati.Persistence/Sati.Persistence.csproj` | library | Sati.Contracts |
| `Sati.Portal/Sati.Portal.csproj` | host | Sati.Signatures |
| `Sati.Portal.Tests/Sati.Portal.Tests.csproj` | test | Sati.Portal |
| `Sati.Signatures/Sati.Signatures.csproj` | library | Sati.Contracts, Sati.Persistence, Sati.Forms |
| `Sati.Signatures.Tests/Sati.Signatures.Tests.csproj` | test | Sati.Signatures |
| `Sati.Tests/Sati.Tests.csproj` | test | Sati.csproj, Sati.Api |
| `tools/BrochureBackdrop/BrochureBackdrop.csproj` | tool | — |
| `tools/BrochureDecompile/BrochureDecompile.csproj` | tool | — |
| `tools/SatiComplianceSeed/SatiComplianceSeed.csproj` | tool | Sati.Persistence |
| `tools/SatiUpdateReport/SatiUpdateReport.csproj` | tool | Sati.Persistence |

In the JSON, each reference is the full repository-relative path (for example
`Sati.Contracts/Sati.Contracts.csproj`).

**Rules, in code; the data file cannot override them:**

- no `sati` ↔ `karuna` reference in either direction;
- no `platform` → product reference, except from `SatiLogica.Schema`;
- only `SatiLogica.Migrator` and projects of kind `test` may reference `SatiLogica.Schema`;
- a `test` project may reference only its own product and `platform`;
- only `tool` projects may reference a `tool`;
- `Karuna.Web` references only `Karuna.Contracts` and `SatiLogica.Contracts`;
- a `SatiLogica.*` project must be product `platform`, and a `Karuna.*` project product `karuna`, so a
  misclassification cannot dodge a rule;
- a `ProjectReference` with a `Condition` is an error until the reader supports it, so a conditional
  reference cannot slip past the check.

**Tests:**

1. **`ProjectGraphRulesTests`** on synthetic graphs, each written against an initial checker that
   returns no diagnostics, so each fails for its own missing diagnostic before its rule is added:
   - `RejectsSatiToKarunaReference`
   - `RejectsKarunaToSatiReference`
   - `RejectsPlatformToProductReference`
   - `AllowsSchemaToProductReference` — fails against the initial blanket platform rule, then passes
     once the named exception is added
   - `RejectsSchemaReferenceFromProductProject`
   - `RejectsTestReferenceToOtherProduct`
   - `RejectsNonToolReferenceToTool`
   - `RejectsKarunaWebNonContractReference`
   - `RejectsUnapprovedRepositoryEdge`
   - `RejectsStaleApprovedEdge`
   - `RejectsUnlistedProject`
   - `RejectsMisclassifiedPrefixedProject`
   - `RejectsConditionalReference`
2. **`ProjectGraphReaderTests.NormalizesRelativeProjectReference`**, using two temporary project files.
   It fails before relative-path normalization exists.
3. **`RepositoryProjectGraphTests.MatchesAllProjectFilesAndReferences`** scans every project file,
   excluding `bin`, `obj`, `.codex-build`, `.vs` and `tmp`.
   - Show it red by comparing against an in-memory copy of the manifest with one existing edge removed,
     and against a synthetic project absent from the manifest.
   - Then show it green against the real manifest.

**Future projects** land with their node and edges in the manifest in the same commit; reviewing that
diff is the approval.

### P2 — one clock (review S-3)

**Scope: the clock only.** The business-day calendar is P7.

1. **One zone owner.** Add `TenantClock` in a new `SatiLogica.Contracts` (with its node in the P1 data
   file). `ApiClock` delegates to it. `BillingRules.MaineBusinessDate` keeps its signature, delegates,
   and is marked obsolete. Today the two choose the zone separately: `ApiOptions.cs:41` defaults to
   `"Eastern Standard Time"`, and `BillingRules.cs:28-32` hard-codes `America/New_York`.
2. **Shared rules stop reading the clock.** `AnnualPacket.cs:93` (`asOf ?? DateTime.Today`) makes `asOf`
   required. `RepresentativePayee.cs:197, 201` takes `today` as a parameter. `ServiceTimeline.cs:156`
   formats a time of day without today's date.
3. **Desktop callers pass `DateTime.Today` explicitly**, so Local Production's dates cannot change. A
   missed caller becomes a compile error, not a silent change.
4. **The API uses `ApiClock` everywhere:** `AnnualPacketEndpoints.cs:34, 72, 135, 177`;
   `ApiEndpoints.cs:6894, 8296, 8368, 8740, 8854`; `BillingCorrectionEndpoints.cs:312`. Search again;
   the list may have grown.
5. **Ban the host clock** with `Microsoft.CodeAnalysis.BannedApiAnalyzers` (`DateTime.Now`,
   `DateTime.Today`, `TimeZoneInfo.Local`) in `Sati.Api` and `Sati.Contracts` only. Never in the WPF
   project.

**One change Demo will see:** EDI `generatedAt` (`ApiEndpoints.cs:6894`, `BillingCorrectionEndpoints.cs:312`)
moves from UTC to Eastern, which changes the ISA/GS date and time in newly generated 837 files. Josh
accepted this. The 837 fixture-hash test injects a fixed timestamp
(`Sati.Tests/SyntheticClaimExchangeTests.cs:16`), so it is unaffected.

**Test:** pin a UTC instant of 00:30 in July and assert every changed API path uses the previous Maine
date.

### P3 — introduce platform contracts (D-11; restructure stage 3)

Add to `SatiLogica.Contracts` only:

- **opaque principal and tenant identifiers**: product plus an opaque string. Sati's user and agency
  keys are `int`, Karuna's are UUIDv7 `Guid`, so platform code must not assume either. Each product
  converts its typed keys at the boundary; platform code only compares and serializes;
- a **tenant actor** carrying those identifiers, the product and the security version, with no
  permission enum;
- the **audit envelope** contract, using the same identifiers;
- the **spreadsheet-safe CSV field encoding** — RFC 4180 quoting plus formula neutralization —
  extracted from `AuditCsv`'s field logic.

**`AuditCsv` stays in `Sati.Contracts`.** Its `AuditCsvRow` has an `int ActorUserId`
(`AuditCsv.cs:10`, written at `:67`), and its comments describe Sati's export (`:17-30`). It keeps its
row, header and export method, and calls the shared encoder. Add a golden-output test first: pin Sati's
exact export bytes for a fixed set of rows, including every formula trigger, then extract the encoder
and show the bytes unchanged.

**Do not move** `UserPermissions`, `AgencyActor`, `EnvelopeProtection`, `LegalHold` or
`DocumentArtifactDto` either. `FieldBinding` binds integer ids under the literal `sati.v1`
(`EnvelopeProtection.cs:44, 52`); changing that encoding would make existing ciphertext unreadable.

**Pin stored permissions** (regression guards; prove each red by changing one value in a scratch copy):

- flag values 1, 2, 4, 8, 16 and 32, and `AllAgencyPermissions` = 63;
- `FromLegacyRole`: CaseManager 1, Supervisor 3, Director 19, Finance 40, Admin 63;
- `IsSupported(64)` false, and every `Has*` check denies with an unknown bit;
- an EF round trip: store 19 and read back Director's set;
- the JSON value in DTOs. There is no `JsonStringEnumConverter`, so permissions travel as integers
  (`Contracts.cs:19, 28, 32`); pin that so an old client and a new server cannot disagree.

### P4 — `SatiLogica.Hosting` (D-2; restructure stage 5)

Extract `TokenIssuer`, `ValidatedActorFilter`, the core of `TenantAccess`, `AuditTrail` and
`LoginAttemptGuard`. They are written against `ApiDbContext` and `Server*` entities today, so the library
takes seams the host supplies:

- an actor store returning a user's security facts (enabled, security version, tenant, product
  permissions as an opaque integer), keyed by the P3 opaque identifiers;
- an audit writer enlisted in the host's own transaction;
- the expected environment name from startup-validated configuration, replacing the `"Demo"` literal
  in `TenantAccess.cs:123` and `ApiEndpoints.cs:1300, 1343`.

**Product binding** uses the audience the token already carries and the API already validates
(`TokenIssuer.cs:46-48`, `Program.cs:156-161`). Each host gets its own audience and signing key. No
product claim or persisted product is needed, so this step has no migration.

**Proof:** the full API suite, plus a test that a token issued by the pre-extraction code is still
accepted. That supports "no intended change for Sati"; do not claim more. It ships as its own Sati
release with an acceptance run.

### P6 — the Local migration runner (D-12), before P5

**Why first.** The desktop migrates through `SatiContext`, and nothing sets `MigrationsAssembly`
(`App.xaml.cs:580`, `SatiContextFactory.cs:30`), so EF finds migrations in `SatiContext`'s own
assembly. If P5 moved the chain first, the desktop's provisioning and updater would find nothing to
apply: fresh databases would stay empty, and later migrations would never reach Local Production. So
the runner is built against **today's** chain in `Sati.Persistence` and ships before anything moves.
Details are in stage 4 of the restructure plan.

- **Grow `SatiLogica.Migrator` from `tools/SatiUpdateReport`**, which already runs the startup
  analyzer and, with `--apply`, backs up, fingerprints protected data and migrates
  (`Program.cs:5-17, 140`). Don't write a new tool.
- Move into it both of the desktop's migration paths:
  - fresh-database provisioning (`App.xaml.cs:270-274`, `LocalDatabaseProvisioner.cs:34-51`);
  - the backed-up update of an existing database (`App.xaml.cs:289-303`).
- Keep the database identity check before any write.
- The desktop stops migrating. It records the head migration id it was built with, and refuses to open
  a database that is behind, telling the user to run the updater.
- A developer launch runs the migrator as a separate process.
- The WPF project never references `SatiLogica.Schema`, now or after P5.

Changing the Local installer is part of this step, and so is a Local release; the installer's own
acceptance applies.

### P5 — move the migration chain (D-1; restructure stage 4), after P6

Follow `PLATFORM_RESTRUCTURE_PLAN.md` stage 4 exactly. It has three parts:

1. **Preparation.** Make `SchemaComparison` and `SchemaSnapshotReader` schema-aware; today they key
   tables by name alone. Add a CI check that each runtime model is a subset of the composed model.
2. **The move, in one commit.**
   - Create the composition context in `SatiLogica.Schema`.
   - Re-point the `[DbContext]` attribute in all 122 migration and snapshot files.
   - Repoint everything that finds migrations through `SatiContext`'s assembly:
     - `SatiLogica.Migrator`;
     - `MigrationEffectAnalyzer`;
     - `PersistenceAssemblyBoundaryTests`;
     - the migration-assembly tests in `Sati.Tests/StabilizationTests.cs:1630-1756`;
     - `tools/SatiComplianceSeed/Seeder.cs:175-176`;
     - `scripts/Test-SchemaDrift.ps1:11`.
   - Mark `scripts/Apply-Release1311Migrations.ps1:309-310` as historical.

   The per-migration Demo runners (`scripts/Apply-*Migration.ps1`) apply SQL keyed by migration id and
   are unaffected.
3. **The five-part proof:**
   - identical migration ids;
   - a byte-identical idempotent script;
   - no model-differ difference and no pending model changes;
   - a scratch synthetic local database, migrated through the runner, with zero pending migrations;
   - a read-only Demo check after an ordinary deploy.

**P5 adds no migration and writes nothing to any database schema.** Its Demo check is read-only. It
does not need any change to database access.

### P7 — the Maine business-day calendar (D-13)

Before K3. Maine State holidays as versioned data in `SatiLogica.Contracts`, with the source cited in the
data file, and one `BusinessDayCalendar` owner. It is unrelated to `WorkdayHelper` and `ExemptDate`.
Neither changes.

## 4. Karuna landing order

Each step builds, passes the full suite, and is worth committing alone. Every step is **synthetic
data only**. Every step updates `karuna/KARUNA_ARCHITECTURE.md`, `karuna/KARUNA_API_AUTHORIZATION.md`
and `karuna/KARUNA_AUDIT_EVENTS.md` as it lands (create them in K0), and records durable choices in
`DECISIONS.md`.

**K0 — Foundation.**

1. **Row-level security lifecycle spike first.** Prove, on SQL Server:
   - with no organization in session context, zero rows are visible;
   - an organization's context shows only its rows;
   - a pooled connection returned by one organization's request carries nothing into the next request;
   - a read-only session context cannot be changed mid-request;
   - sign-in can find a user before any organization is set, through a narrow path that is outside the
     tenant filter;
   - a background job sets context one tenant at a time.

   Record the design (`KARUNA_DESIGN.md` §4.2) before building on it.
2. **Then the foundation itself:**
   - projects per `KARUNA_DESIGN.md` §3.1 (under `karuna/`, in the `karuna` solution folder);
   - Karuna's own `Users`, `Organizations` (the Karuna tenant, D-8), memberships and `AuditEvents` in
     the `karuna` schema (D-10);
   - sites, programs, `KarunaPermissions` and `KarunaPermissionRules`, enrolment;
   - `CareTeamAccess`: standing, shift and organization-wide reach. Shift reach can use a minimal
     `Shift` stub until K7;
   - emergency access, device enrolment and shared-device sessions;
   - the four isolation layers (§4.2);
   - route metadata for tenant owner, capability and audit action, with the generated inventory;
   - the web client shell with shift and office modes, and the D-3 spike results recorded.
3. **K0's first tables are the first migration that changes a schema.** Applying it to Demo needs an
   authorized runner and access path that Josh decides at that time. Stop and ask.

*Done when:* the access-matrix suite passes; every entity has a non-null `OrganizationId`, a query
filter and row-level security coverage (architecture test); a raw SQL query from the API principal
cannot read another organization's rows; every mutating route declares an audit action; cross-tenant
tests pass on SQL Server in CI.

**K1 — The person record.** `ServiceRecipient` with a registry link field (nullable; the registry does
not exist yet — do not create it), versioned profile ledger, guardians, contacts, structured allergies
with an explicit "no known allergies", diagnoses, diet, equipment, advance directive, care protocols,
rights modifications with the required elements, presence intervals, and a face-sheet PDF with a print
audit. Photos follow Sati's photo decision. No SSN field.

**K2 — Staff qualifications.** The requirement catalog as versioned data from Section 21.10,
credentials, training, person-specific training, background-check records without results, and
`StaffQualificationRules` returning Qualified/NotQualified/Unknown with reasons. Expiry queues.

**K3 — Event reports** (needs P7):

- `EventReport` per person with `OccurrenceGroupId`, `EventReportWorkflow`;
- `ReportableEventRules` with the program taxonomy **as data**. The real content is blocked on KQ-4
  and KQ-16; build with a clearly labelled synthetic taxonomy until the OADS matrix is obtained;
- notification records including APS, staff-recorded Evergreen filing, the 30-day follow-up and
  amendments;
- the auto-draft entry point for other modules, and escalation of unsubmitted drafts.

Never name anything `Incident`.

**K4 — eMAR.** Follow `KARUNA_EMAR_DESIGN.md` §20 internally. Steps 1–7 make a synthetic medication
pass demonstrable; 8–9 are required before any residential agency could use it. The RN clinical review
gate in the eMAR design's header applies before it is shown to an agency.

**K5 — Daily documentation and service records.**

- shift notes;
- `ServiceRecord` with `ServiceDocumentationGate` (the six elements of the 2026-09-21 bulletin);
- `ServiceRecordWorkflow` with amendments;
- `StaffTimeClaimRules`, checked in the same serializable transaction as the write;
- late-entry rules;
- goal, health and behaviour observations, and appointments;
- plan snapshots and support plans;
- authorization receipts with second-person verification.

**K6 — Behaviour support.** Plan versions, `PlanApproval` evidence, `BehaviorPlanRules` activation,
per-version staff training, and `RestrictiveInterventionRecord` with `RestrictiveInterventionRules` and
event-report drafts.

**K7 — Scheduling, time and EVV phase 1.** Shifts and assignments with qualification refusal and a
reasoned supervisor override, `StaffingCoverageRules`, time punches, payroll CSV export, Sandata visit
reconciliation, and `EvvRequirementRules` (impacted codes as data; real content blocked on KQ-2).

**K8 — Billing.** After D-4's platform extraction:

- rates, `ServiceUnitRules`, `AuthorizationUtilizationRules`;
- `ServiceCompatibilityRules` (data, KQ-5) and `PerDiemDayPolicy` (KQ-1);
- `KarunaClaimReadiness` with exact blockers;
- claim creation with frozen inputs, over the platform 837P, outbox and 835 paths.

Mock clearinghouse only, as Sati's Demo does.

**K9 — Therap import and cutover.** Blocked on KQ-11: obtain a real export format first, under a data
agreement, into a non-Production environment. Then:

- current state imported through reviewed batches;
- history kept as hash-verified artifacts;
- medication orders imported as Draft only;
- `SystemOfRecordCutover` per site and per module.

**K10 — Oversight dashboards and nurse queues.** Bounded, exact-or-labelled counts.

**K11 — Personal funds.** Append-only ledger, two-person counts, derived balances.

**Later, not in this handoff:**

- relationship sharing with Sati, which needs the platform authorization and sharing policy, and
  counsel on KQ-9;
- EVV alternate-vendor integration;
- pharmacy interfaces;
- offline capture, which needs its own design and the `AGENDA.md` mobile prerequisites;
- guardian access;
- cross-product identity, which would need platform identity tables (D-10 defers them).

## 5. Rules that are easy to break by habit

- **Never refuse a truthful clinical fact for a clinical-rule violation** (D-7). A validator that
  returns 409 because a PRN exceeded its maximum is a defect. Classify, flag, route. Refuse only for
  integrity: reach, tenant, revision, idempotency conflict, future instant. The eMAR tests 8–11 must be
  shown failing against a refusing build before they are kept.
- **Do gate claims.** Service records, claim lines, schedule assignments and order activation are claims
  and fail closed with exact reasons.
- **One owner per rule**, in `Karuna.Contracts.V1` or `SatiLogica.Contracts`. The web client calls the
  owner for previews; the server calls it to decide. A private helper that re-derives units, deadlines
  or eligibility is a defect (review S-4 is what happens otherwise).
- **No local database, no `Server*` twin entities, no second implementation of a workflow.**
- **Instants, not offsets.** No `DateTime.Now` or `DateTime.Today` in Karuna server code, the banned-API
  analyzer applies to every `Karuna.*` server project, no minutes-after-7 AM, and DST tests for every
  time rule.
- **Derived obligations are not stored.** Scheduled doses, deadlines and due items are computed with
  stable identities; only evidence is stored.
- **Append-only means append-only in the database too:** deny `UPDATE` and `DELETE` to runtime
  principals on evidence tables, and test it on SQL Server.
- **No PHI in audit metadata, logs, notifications or browser persistent storage.**
- **Honest labels.** "Filed in Evergreen — staff recorded", "Downtime paper back-entry". Never claim an
  integration that does not exist.
- **Do not decide an open question in code.** See `KARUNA_DESIGN.md` §19 (KQ-*) and
  `KARUNA_EMAR_DESIGN.md` §21 (MQ-*). Build the rule owner with the question as versioned data and a
  clearly labelled synthetic default; stop and ask before real content.
- **Do not touch the shared index while another agent may be active.** Commit only your own paths.

## 6. Tests

`AGENTS.md`'s rules apply: security, tenancy and concurrency tests must fail against the unfixed code
before they are kept. Additionally (`KARUNA_DESIGN.md` §17):

- SQL Server rather than SQLite for API integration tests from the first commit, with CI running them;
- the access matrix;
- DST suites;
- clinical-fact acceptance suites;
- claim refusal suites;
- idempotency;
- architecture tests.

## 7. Environment and release

- **Synthetic data only.** No real person, staff member, order or agency data in any environment.
- **Demo is the only hosted environment.**
  - A step that adds no migration writes nothing to `SatiDemo`'s schema. Verify it read-only.
  - A migration that changes `SatiDemo`'s schema needs an authorized runner and access path that Josh
    decides. Detect the need, report it, and wait.
  - The history-reconciliation WebJob cannot apply schema changes, and `SatiLogica.Migrator` (P6) is
    for Local Production.
  - Never change a firewall or any other security setting.
- **Releases.** `RELEASE_PLAYBOOK.md` and the DATT trigger describe Sati releases. They do not yet cover
  a Karuna host or client; a Karuna release needs a playbook section written and approved first. Do not
  treat a DATT invocation as covering Karuna.
- **No manual smoke tests as release gates.** Josh validates releases by using them.
- **No Production deployment.**

## 8. Regulatory note

`REGULATORY_CONCERNS.md` governs. Karuna adds medication administration, restrictive interventions,
reportable events, EVV and personal funds, each with its own rules; the regulatory ground and its
confidence labels are in `KARUNA_REQUIREMENTS.md` §4. Add a Karuna section to `REGULATORY_CONCERNS.md`
in K0 that lists the open counsel and clinical questions, and keep it current as steps land. Nothing
in Karuna is represented as HIPAA compliant, clinically reviewed or accepted by OADS, MaineCare or
Sandata.

## 9. What Codex's review changed (2026-09-28)

Codex reviewed the first version of this handoff against the code. All nine of its findings were
confirmed; two were refined.

1. **P0.** Now an evaluated-items test. The assembly check was vacuous.
2. **P1.** Now covers all 18 project files, not only the 13 in the solution, with approved edges kept as
   data.
3. **P2.** Now the clock only, and it includes the shared-contract call sites. `WorkdayHelper` and
   `ExemptDate` were wrongly treated as a holiday calendar.
4. **P3.** Now introduces product-neutral types instead of moving Sati-shaped ones (D-11).
5. **P4.** "No behaviour change" became host-supplied seams plus a token-compatibility test. Product
   binding uses the existing audience, so no migration is needed (the refinement).
6. **P5.** Was not buildable. It now has schema-aware tooling, a separate composition context and a
   five-part proof. `SchemaDriftHealthCheck` never compared runtime models with the chain.
7. **The blanket firewall statement** was wrong and is removed.
8. **K0** now uses Karuna-owned identity tables (D-10), and a row-level security lifecycle spike comes
   first.
9. **Superseded statements** in the design and the restructure plan are corrected.

**Second review (2026-09-28).** Codex checked the revised documents and proposed concrete P0 and P1
plans. All six findings were confirmed, and four refinements were approved by Josh.

1. **`AuditCsv` stays in Sati** (integer `ActorUserId`, Sati-specific comments); only its CSV field
   encoding is extracted.
2. **P6 precedes P5.** Moving the chain first would strand the desktop's startup migration.
3. **Only `SatiLogica.Migrator` and tests may reference `SatiLogica.Schema`**, replacing "referenced by
   no runtime project".
4. **The restructure plan's opening inventory** is labelled historical instead of recounted.
5. **Summary rules carry their exceptions inline**: the `SatiLogica.Schema` exception, and platform
   identity as mechanics only.
6. **KQ-3 moved** to a resolved list.

Claude added four items:

- platform identifiers are opaque, because `int` and `Guid` keys both cross into platform code;
- a list of everything else that finds migrations through `SatiContext`'s assembly;
- P1 splits `product` from `kind`;
- conditional references are rejected.

Codex's verification is folded into P0: `Page` removes are needed, `Content` removes are not, and the
`-getItem` syntax is confirmed on SDK 10.0.401. The 837 fixture test injects its timestamp, so P2 cannot
disturb it.
