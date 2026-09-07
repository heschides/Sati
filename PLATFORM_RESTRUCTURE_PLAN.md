# SatiLogica — platform restructure plan

*Status: proposal, nothing implemented. Written 2026-09-07 against branch
`ui-legibility-and-accessibility` at commit `6e78c6d`. Read `ARCHITECTURE.md` for what owns what
today and `DECISIONS.md` for why. Three decisions at the end have to be settled before stage 3
starts; the earlier stages are safe to begin without them.*

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
│   └── Carika                    Avalonia client of Sati  ← see decision 1
├── karuna/                       nothing yet  ← see decision 2
└── upekkha/                      nothing yet  ← see decision 2
```

## Three decisions to settle before stage 3

These are yours, not mine. The staged work below stops at stage 2 without them.

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

Each stage builds, passes the full suite, and is worth committing alone. Stages 1 and 2 are safe
before the decisions above are settled.

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

*Risk: none beyond anyone with the old solution path in muscle memory. `README.md`, the release
scripts and `RELEASE_PLAYBOOK.md` reference the solution by name and need updating together.*

### Stage 3 — extract the platform core

The real work, and the only stage with design judgement in it.

1. Create `SatiLogica.Contracts`. Move the ~15 platform files into it.
2. Split `Contracts.cs` and `ApiSurface.cs` along the same line. This is the delicate part: 103
   types in one file, and the split has to be by concern rather than by convenience.
3. Point `Sati.Contracts` at `SatiLogica.Contracts` and leave the case-management files where they
   are, correctly named at last.
4. Repoint `Carika` at both.

Namespaces move with the files. Every file that changes namespace is a `using` change everywhere it
is consumed, so this stage produces a large diff that is nearly all mechanical. Keep it mechanical:
no behaviour changes, no renames beyond the namespace, nothing else in the commit.

*Risk: moderate but contained. Compile errors are the failure mode, and the compiler finds them all.
The 1,536-test suite plus the `API_AUTHORIZATION.md` route inventory catch the rest.*

### Stage 4 — extract platform persistence

Move `SatiContext`, the migration chain, and the platform entities (`User`, `Agency`, `AuditEvent`,
and the Organization registry when it lands) into `SatiLogica.Persistence`. Case-management entities
move to `Sati.Persistence` and register through `IEntityTypeConfiguration`.

**Do not renumber, rewrite or split the migrations.** Their namespaces change; their ids, their
order and `__EFMigrationsHistory` must not. `MigrationEffectAnalyzer` and `SchemaDriftHealthCheck`
both read that chain and are the safety net that proves it.

*Risk: the highest in this plan. Controlled migration deployment is still manual and outstanding in
`AGENDA.md`, and the Demo SQL firewall is closed to workstations, so verification needs a temporary
rule only you can add. Do this stage alone, against Demo, with the drift health check green before
and after.*

### Stage 5 — split the API

`SatiLogica.Api` keeps hosting, `TokenIssuer`, `TenantAccess`, `AuditTrail`, `LoginAttemptGuard`,
`ValidatedActorFilter` and the endpoint-registration seam. `Sati.Api` becomes a library of
case-management endpoints registered through it.

**This stage changes the deployed artifact.** Release 1.3.4 shipped `SatiApi-1.3.4-fx-x86.zip` to a
live Azure app, and renaming the entry assembly changes what is deployed and what the Function's
settings point at. It needs its own release, its own acceptance run, and `DATABASE_ENVIRONMENTS.md`
updated with a matched client/API pair.

*Risk: deployment, not code. Sequence it as a release, never as a refactor.*

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
- **The migration chain.** One chain, one context. See decision 3.
- **`Sati.Portal`.** It serves signature recipients and is already product-neutral. It moves folder
  and namespace in stage 3 and nothing else.

## Effort and sequencing

| Stage | Size | Can start now |
|---|---|---|
| 1 guardrail | small | yes |
| 2 solution shape | small | yes |
| 3 platform contracts | large | needs decisions 1 and 3 |
| 4 platform persistence | large | needs decision 3 |
| 5 API split | medium, plus a release | after 4 |
| 6 desktop rename | medium, plus an installer test | any time after 2 |

Stages 1 and 2 are worth doing regardless of how the decisions land, because the guardrail is what
keeps the boundary honest afterwards and the solution folders cost nothing.
