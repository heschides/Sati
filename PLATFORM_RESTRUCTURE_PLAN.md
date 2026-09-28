# SatiLogica — platform restructure plan

*Status: stages 0 and 2 implemented; stages 1 and 3–6 remain proposed. Written 2026-09-07 against branch
`ui-legibility-and-accessibility` at commit `6e78c6d`, restaged the same day and updated 2026-09-09. Read
`PLATFORM_DOMAIN.md` first: it decides what the platform is, and this document only moves code to
match. `ARCHITECTURE.md` says what owns what today and `DECISIONS.md` says why. **Amended 2026-09-28**
by Karuna decisions D-1, D-2 and D-10 to D-13, which change the target shape and stages 3, 4 and 5;
see "Amended 2026-09-28" below.*

---

## What was asked

Abstract Sati into its own project inside a `SatiLogica` solution that holds the shared code, with
Sati, Karuna, Carika and Upekkha as projects within it.

## What the code actually looks like today

Sixteen projects in one solution, in a clean layered graph with no cycles.

```
Sati.Contracts ─┬─ Sati.Forms
                ├─ Sati.Persistence
                ├─ Sati.Signatures ── Sati.Portal
                ├─ Sati.Api
                ├─ Sati            (WPF desktop)
                └─ Carika          (Avalonia)
```

| Project | Lines | What it holds |
|---|---:|---|
| Sati.Contracts | 8,301 | 54 files: DTOs and the rules both client and API enforce |
| Sati.Api | 12,921 | Endpoints, security, infrastructure |
| Sati.Persistence | — | `SatiContext`, ~40 entity models, **175 migrations** |
| Sati.Forms | — | Six PDF generators |
| Sati.Signatures | — | Signature workflow, serves the Portal |
| Carika | — | Avalonia client, references `Sati.Contracts` only |

## The problem, stated precisely

Carika is a separate program that already depends on a project named after a different program.
When Karuna and Upekkha arrive they will do the same, and every one of them will carry Sati's
billing rules, claim readiness and caseload transfer logic into a build that has no use for them.
The names say `Sati` owns the shared code. It does not; it shares a house with it.

**The finding that shapes everything else:** the shared code is the minority of what is in the
shared projects. Classifying the 54 contract files by hand gives roughly:

| Kind | Count | Examples |
|---|---:|---|
| Platform | ~15 | `UserPermissions`, `AuditCsv`, `ChatAccess`, `IncidentHealthScoring`, `LegalHold`, `SsnMask`, `Signatures`, `EnvelopeProtection` |
| Case management | ~37 | `BillingRules`, `ServiceTimeline`, `ClaimResponseReader`, `NoteWorkflow`, `ProviderDirectoryRules`, `AnnualPacket` |
| Mixed within one file | 2 | `Contracts.cs` (103 types), `ApiSurface.cs` |

`Contracts.cs` alone holds `LoginRequest`, `UserProfileDto` and `AuditEventDto` beside `NoteDto`,
`CaseloadOwnershipDto` and `IncentiveDto`.

This inverts the obvious approach. Renaming `Sati.Contracts` to `SatiLogica.Contracts` would drag
thirty-seven files of case-management logic into a platform assembly and call the problem solved.
The correct move is the opposite: **extract the platform core out**, and leave the rest named `Sati`
because that is what it is.

## Target shape

```
SatiLogica.slnx
├── platform/
│   ├── SatiLogica.Contracts      identity, permissions, audit, chat, incidents,
│   │                             legal hold, envelope protection, signatures
│   ├── SatiLogica.Persistence    SatiContext, migration chain, platform entities
│   ├── SatiLogica.Api            hosting, auth, TenantAccess, AuditTrail, TokenIssuer,
│   │                             and the seam products register endpoints through
│   ├── SatiLogica.Forms          PDF composition primitives
│   └── SatiLogica.Signatures     signature workflow  ── SatiLogica.Portal
├── sati/
│   ├── Sati.Contracts            billing, claims, notes, caseload, compliance forms
│   ├── Sati.Api                  the case-management endpoints
│   ├── Sati.Persistence          case-management entity configurations
│   ├── Sati.Desktop              the WPF client (today's Sati.csproj)
│   └── Carika                    Avalonia client of Sati  ← still open, see below
├── karuna/                       designed in PLATFORM_DOMAIN.md, no code yet
└── upekkha/                      designed in PLATFORM_DOMAIN.md, no code yet
```

## Amended 2026-09-28 — changes from designing Karuna and from Codex's review

Recorded in `DECISIONS.md` under "2026-09-28 — Karuna's foundational decisions" (D-1, D-2) and
"2026-09-28 — Karuna prerequisite corrections, D-10 to D-13". Stages 3, 4 and 5 below have been
rewritten to match; the earlier wording is not preserved in place.

**The migration chain lives in a composition assembly (D-1).** Stage 4 as first written put the chain
in `SatiLogica.Persistence`, while stage 1 forbids platform projects from referencing products. EF
needs every entity type in the model that owns the chain, so both cannot hold. The chain moves instead
to `SatiLogica.Schema`, which references the platform and every product persistence assembly. It owns
a design-time composition context that is separate from every runtime context, so no client or
product host has to reference it. Stage 1's rule gains one named exception: `SatiLogica.Schema`.

**Each product keeps its own host (D-2).** `SatiLogica.Hosting` is a library holding authentication
and audit *mechanics* behind seams the host supplies: an actor store, an audit writer enlisted in the
host's own transaction, and environment identity from validated configuration. `Sati.Api` stays Sati's
host and keeps its entry assembly; Karuna gets `Karuna.Api`. Each host has its own token audience and
signing key.

**Platform contracts are introduced, not moved (D-11).** Review showed that most "platform" files in
`Sati.Contracts` are Sati-shaped. `UserPermissions` and `AgencyActor` are Sati's capability set.
`EnvelopeProtection` binds integer agency and record ids under the `sati.v1` prefix. `LegalHold` and
`DocumentArtifactDto` carry Sati's integer person and agency ids. They stay in Sati. `SatiLogica.Contracts`
starts with only product-neutral types; platform versions of the others are designed when a second
product needs them.

**Identity storage is per product for now (D-10).** Karuna keeps its own users, memberships and audit
events in the `karuna` schema. The platform owns the mechanics, not the tables. Relocating Sati's
`dbo` identity into a platform schema is not part of this plan.

**Local Production stops migrating itself (D-12).** Before the chain gains any non-Sati entity, Local
Production's migrations move to a separate runner the installer invokes; see stage 4.

The revised platform folder:

```
platform/
├── SatiLogica.Contracts      product-neutral only: tenant actor (no permission enum), audit envelope
│                             and AuditCsv, tenant clock; later the business-day calendar and
│                             billing mechanics
├── SatiLogica.Persistence    platform entities only (none yet; identity stays per product, D-10)
├── SatiLogica.Hosting        library: token issuance, actor validation, TenantAccess core, audit,
│                             LoginAttemptGuard, behind host-supplied seams
├── SatiLogica.Schema         design-time composition context and the one migration chain
├── SatiLogica.Migrator       installer-run migration runner for Local Production (D-12)
├── SatiLogica.Forms
└── SatiLogica.Signatures  ── SatiLogica.Portal
```

The prerequisite work Karuna needs, in order, is §3 of `karuna/CODEX_HANDOFF.md`. Its first item is
not a stage of this plan and must precede any stage that creates files under `platform/`: the root
`Sati.csproj` picks up every file beneath it unless excluded (`SATI_STRUCTURAL_REVIEW_2026-09-28.md`,
S-8).

## Superseded: the staging changed on 2026-09-07

This document originally put the platform extraction at stage 3 with the Karuna and Upekkha design
deferred until each shipped. That was backwards. What belongs to the platform cannot be derived from
one product, so extracting first would have encoded Sati's assumptions as "platform" and called it
done. The design now comes first and lives in `PLATFORM_DOMAIN.md`; the code boundary in stage 3 is
derived from it rather than guessed.

Decisions 1 and 3 below are settled, and decision 2 changed answer. Both are recorded in
`DECISIONS.md`.

- **Tenancy.** One model. Agency is Sati's tenant, provider organization is Karuna's, and OADS is an
  authority rather than a tenant. Membership and authority are separate concepts.
- **Person identity.** A platform registry that products link to, mirroring the Organization
  decision. Reconciliation is a link, never a swap.
- **Karuna and Upekkha.** Designed now, not deferred. Not as empty projects — as the domain design
  that decides what the platform is. Projects still wait until there is code to put in them.

## The original three decisions

*Kept for the record. Decision 2's premise was wrong: the reason to design Karuna and Upekkha now is
that they constrain Sati, which has nothing to do with when they ship.*

**1. Is Carika a product or a Sati client?** Its README calls it "a deliberately limited Avalonia
client for authenticated client-profile access and case-note entry", it reaches Azure only through
the Sati API, and it needs `SaveNoteRequest` and `CaseNoteEntryOptions` — case-management contracts,
not platform ones. On the evidence it is a second client of Sati, the way the WPF app is, and it
belongs under `sati/` rather than beside Karuna. It was listed as a peer in the request, so this may
be a deliberate roadmap intent I cannot see.

**2. Do Karuna and Upekkha get project folders now?** `AGENDA.md` records the opposite decision:
"Introduce the program names only as each ships... roadmap names for work that does not exist yet
and should stay internal until the quarter before each ships." The same section of `DECISIONS.md`
refuses to add a column pointing at a table that does not exist. Empty projects carrying those names
would contradict a decision already on the record. Creating the *solution folders* costs nothing and
commits to nothing; creating projects does. My recommendation is folders now, projects when each
ships, but the earlier decision is yours to revisit.

**3. One database or one per product?** This is the load-bearing one, and everything in stage 4
depends on it. `DECISIONS.md` already anticipates Karuna arriving as "a tenant with its own users
and data" while sharing a canonical Organization identity, which reads as one platform database with
product-specific tables. If that holds, the migration chain stays single and lives in
`SatiLogica.Persistence`, and product entities are contributed through `IEntityTypeConfiguration`
from product assemblies. If instead each product gets its own database, the 175-migration chain has
to be split, cross-product transactions become distributed, and the audit and tenancy guarantees in
`CLAUDE.md` need rethinking from scratch. **Recommendation: one database, one chain, one
`SatiContext`.** Do not split the migration chain as part of this restructure.

## Staged sequence

Each stage builds, passes the full suite, and is worth committing alone.

### Stage 0 — design the other two programs

Done for the structural decisions, in `PLATFORM_DOMAIN.md`. It settles who the tenant is in each
program, who reads across tenants and by what authority, and whether a person is one identity or
three. Those answers are what make the stage 3 split derivable instead of arbitrary.

It also produced three things Sati should do now, none of which waits on the restructure and all of
which get harder the longer they wait: do not build the OADS Resource Coordinator as an agency user,
strengthen `MaineCareId` capture before a registry has to match on it, and stop widening the
`Role != "PlatformOperator"` exclusion pattern.

Four open questions remain in that document. None blocks stages 1 and 2.

### Stage 1 — guardrail first

Add an architecture test that asserts the dependency graph: which project may reference which. It
records today's edges and fails on a new one. Written before anything moves, it protects every stage
after it, and it is the only artifact here that keeps paying after the restructure ends.

Also assert the rule that motivates the whole exercise: **no platform project may reference a
product project.** That test is what makes the boundary real rather than aspirational.

*Risk: none. Nothing moves.*

### Stage 2 — solution shape only

Rename `Sati.slnx` to `SatiLogica.slnx` and add solution folders (`platform/`, `sati/`, `karuna/`,
`upekkha/`). Solution folders are metadata; no file moves, no project renames, no csproj edits.

**Completed 2026-09-09.** All current projects are grouped under `sati`; the other three solution
folders are intentionally empty until code exists that belongs in them. CI, the Demo runbook,
release instructions, and source-based repository-root discovery now use `SatiLogica.slnx`.

*Risk: none beyond anyone with the old solution path in muscle memory. `README.md`, the release
scripts and `RELEASE_PLAYBOOK.md` reference the solution by name and need updating together.*

### Stage 3 — introduce the platform contracts

*Rewritten 2026-09-28 (D-11). The first version moved "~15 platform files"; review found most of them
Sati-shaped.*

1. Create `SatiLogica.Contracts` with product-neutral types only: a tenant actor carrying user id,
   tenant id, product and security version (no permission enum); the audit envelope contract; and
   `AuditCsv`, whose header is already product-neutral. The tenant clock arrives here from the clock
   step that precedes this stage.
2. Leave `UserPermissions`, `AgencyActor`, `EnvelopeProtection`, `LegalHold`, `DocumentArtifactDto` and
   the rest of `Contracts.cs` and `ApiSurface.cs` in `Sati.Contracts`, unchanged. Sati adopts the new
   types through adapters where it needs them, one call site at a time, with no change to any public
   contract or encrypted value.
3. Pin `UserPermissions`' integer values, the combined mask, the legacy role mapping, unknown-bit denial,
   the database round trip and the JSON representation, so no later change can silently reinterpret
   stored permissions.

A platform version of legal hold, document artifacts or envelope protection is designed when a second
product needs it. For envelope protection that means a new versioned prefix for new bindings, with
`sati.v1` decryption kept permanently and a golden-ciphertext test.

*Risk: small. No Sati type moves, so there is no namespace churn and no compatibility exposure.*

### Stage 4 — move the migration chain into `SatiLogica.Schema`

*Rewritten 2026-09-28 (D-1, D-12).*

**Prepare first**, each independently committable:

1. Make schema comparison schema-aware. `SchemaTable` has a name but no schema
   (`Sati.Contracts/V1/SchemaComparison.cs:7`), and the comparer and `SchemaSnapshotReader` key tables
   by name alone, so `dbo.X` and `karuna.X` would be conflated. This must land before any non-`dbo`
   table exists.
2. Add a CI check that each runtime model (today `SatiContext` and `ApiDbContext`) is a subset of the
   composed design-time model. `SchemaDriftHealthCheck` compares the API model with the live database;
   it does not compare anything with the chain.

**Then move.** Create `SatiLogica.Schema` with a design-time composition context distinct from the
runtime `SatiContext`. Move the migrations and the model snapshot into it, re-pointing the
`[DbContext]` attribute in all 122 migration and snapshot files. Update `MigrationEffectAnalyzer` and
`PersistenceAssemblyBoundaryTests`. **Do not renumber, rewrite or split migrations**: ids, order and
`__EFMigrationsHistory` must not change.

**Proof**, replacing "drift health check green":

1. The ordered list of migration ids is identical before and after (a pinned test).
2. The idempotent script from zero to head is byte-identical before and after.
3. EF's model differ finds no difference between the old and new snapshot models, and
   `has-pending-model-changes` reports none.
4. A scratch synthetic local database migrated by the old build opens under the new build with zero
   pending migrations and an unchanged history table.
5. On Demo, after an ordinary API deployment, a read-only check shows the history table equals the
   pinned list and `/health/ready` is Healthy.

This stage adds no migration and writes nothing to any database schema. Its Demo check is read-only.
The first migration that does change a schema — Karuna's first tables — needs its own authorized
runner and access path, decided by Josh at that time. The history-reconciliation WebJob cannot apply
DDL.

**Then take migration out of the desktop (D-12).** Before the chain gains its first non-Sati entity,
Local Production stops migrating itself and `SatiLogica.Migrator`, invoked by the Local installer,
takes over. It must carry everything the desktop does today:

- fresh-database provisioning (`App.xaml.cs:270-274`, `LocalDatabaseProvisioner.cs:34-51`);
- the backed-up update of an existing database (`App.xaml.cs:289-303`, `LocalDatabaseUpdater`);
- the database identity check before any write.

The desktop records the expected head migration id at build time and refuses to open a database that
is behind, telling the user to run the updater. A developer launch runs the migrator as a separate
process. WPF never references `SatiLogica.Schema`. Local Production's database will still receive
other products' empty tables, because one chain cannot be partly applied.

*Risk: moderate. The move is proven byte-for-byte, and the desktop change is guarded by refusal rather
than repair.*

### Stage 5 — extract `SatiLogica.Hosting`

*Rewritten 2026-09-28 (D-2).*

Extract `TokenIssuer`, `ValidatedActorFilter`, the core of `TenantAccess`, `AuditTrail` and
`LoginAttemptGuard` into `SatiLogica.Hosting`. Today they are written directly against `ApiDbContext`
and `Server*` entities (for example `AuditTrail.cs:5,14`), so the library needs seams the host supplies:

- an actor store returning a user's security facts (enabled, security version, tenant, product
  permissions as an opaque integer);
- an audit writer enlisted in the host's own transaction;
- the expected environment name from startup-validated configuration, replacing the `"Demo"` literal
  in `TenantAccess.cs:123` and `ApiEndpoints.cs:1300, 1343`.

`Sati.Api` stays Sati's host and keeps its entry assembly. Product binding uses the audience the token
already carries and validates (`TokenIssuer.cs:46-48`, `Program.cs:156-161`): each host has its own
audience and signing key. Existing Sati tokens are unaffected, and nothing about a product is persisted
in this stage.

The goal is no intended change for Sati, proven by the full API suite plus a test that a token issued by
the previous code is still accepted. It ships as its own Sati release with an acceptance run, because
authentication code moves.

*Risk: moderate, in authentication. Sequence it as a release, never as a refactor.*

### Stage 6 — rename the desktop project

`Sati.csproj` at the repository root becomes `sati/Sati.Desktop`. Touches the installer, the Start
Menu folder, the registry publisher and `%LOCALAPPDATA%` paths.

*Risk: installer and upgrade behaviour. `AGENDA.md` records that the `SatiLogica` casing change
needed no upgrade story because Windows paths are case-insensitive; this rename does not get that
reprieve, and an existing install's uninstall entry has to keep working.*

## What deliberately does not change

- **`SatiDemo`, `SatiProduction` and `dbo.SatiDatabaseIdentity`.** 1,479 occurrences across 162
  files, and
  `CLAUDE.md` makes the hard-coded environment mapping a safety mechanism: the bootstrap chooser
  validates the database name against the selection before login. Renaming a live database to tidy
  a namespace trades a working guard for a cosmetic gain. If they are ever renamed it is its own
  project with its own runbook, and not while Demo and Production are the only two things standing
  between the app and the wrong data.
- **The migration chain.** One chain, owned by one design-time composition context in
  `SatiLogica.Schema`; product runtime contexts map their own tables. See decision 3 and D-1.
- **`Sati.Portal`.** It serves signature recipients and is already product-neutral. It moves folder
  and namespace in stage 3 and nothing else.

## Effort and sequencing

| Stage | Size | Can start now |
|---|---|---|
| 0 design | done for the structural decisions | — |
| 1 guardrail | small | yes, after the root-project exclusions |
| 2 solution shape | done 2026-09-09 | — |
| 3 platform contracts (introduce) | small | after the clock step |
| 4 migration chain to `SatiLogica.Schema`, then the Local migrator | medium, plus an installer change | after 3; the migrator before any non-Sati entity |
| 5 `SatiLogica.Hosting` extraction | medium, plus a release | after 3; independent of 4 |
| 6 desktop rename | medium, plus an installer test | any time after 2 |

Stage 2 is complete. The exact order, with the steps that are not stages of this plan (root-project
exclusions, the clock, the business-day calendar), is §3 of `karuna/CODEX_HANDOFF.md`.
