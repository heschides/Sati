# Sati — Project Briefing for Coding Assistants

Read this file, `ARCHITECTURE.md`, `DECISIONS.md`, and the relevant section of `AGENDA.md`
before making architectural changes.

## What Sati is

Sati is a Maine-focused human-services case-management platform built by Josh, a social-services
case manager and software developer. It began as a WPF desktop application used for real daily
work. Its longer direction is a cloud-based, multi-tenant platform capable of supporting provider
agencies, supervisors, billing staff, reviewers, and eventually state-facing workflows.

The current client targets .NET 10 and uses WPF, CommunityToolkit.Mvvm, EF Core, SQL Server, and
Microsoft.Extensions.Hosting. The solution now contains the root WPF project, `Sati.Api`,
`Sati.Contracts`, `Sati.Persistence`, desktop/domain tests, and cross-platform API integration
tests. `Sati.Persistence` owns the platform-neutral entity model, `SatiContext`, and migration
chain. The WPF project still owns transitional local EF service implementations; that is not the
intended cloud boundary.

## Product direction

Treat the following as governing constraints:

1. Sati is becoming an API-mediated platform with WPF as one client.
2. Distributed clients must not connect directly to Azure SQL or contain database credentials.
3. The API is authoritative for identity, authorization, tenant isolation, validation,
   transactions, audit history, migrations, and integrations.
4. Azure-hosted services should use managed identities and least-privilege access.
5. Submitted clinical and financial records require immutable versions and amendments rather
   than silent overwrites.
6. Tenant isolation, auditability, concurrency, recovery, observability, and automated testing
   are platform foundations, not post-launch enhancements.
7. Demo and Production must remain separate in databases, credentials, service identities,
   deployments, logs, backups, and administrative access. The desktop bootstrap chooser may
   select either environment only through the validated, hard-coded environment mapping.

Do not solve a cloud feature by placing an Azure SQL connection string in the WPF application.
Do not add new direct EF dependencies to ViewModels or distributed-client services.

## Current product areas

- Client caseloads and demographic records
- Service notes and documentation workflow
- Service-day scheduling and prevention of overlapping billable time
- Compliance forms, annual cycles, and quarterly reviews
- Upcoming deadlines and calendar events
- Supervisor dashboards, queues, and approval workflow
- Productivity, incentives, settings, scheduling, and exempt dates
- Comprehensive Assessment authoring
- Contacts and support teams
- Provider directory and assistive-technology requests
- Early billing, claim-line, and 837P generation
- Local AI-assisted note drafting with explicit human acceptance
- Agency incident and health telemetry, with cross-tenant support on a separate identity

The long-term market direction includes Maine waiver and targeted case-management organizations.
Potential future scope includes person-centered plans, client/reportable incident management, EVV,
mobile/offline documentation, authorization/utilization, full claim and remittance lifecycle,
reporting, and state/payer/provider integrations. Do not imply those capabilities already exist.
The incident features that do exist today are operational error telemetry, not reportable-event
management for clients; do not present one as the other.

## Current architecture

Most persistence operations are behind interfaces in `Data/`, with implementations that create a
short-lived `SatiContext` from `IDbContextFactory<SatiContext>` per method. This is useful as a
transition seam. The `SatiContext` type and migration chain live in the platform-neutral
`Sati.Persistence` assembly; the local service implementations remain in the desktop project.

The target transition is:

```text
WPF ViewModel -> IFeatureService -> HttpFeatureService -> Sati.Api
                                                     -> domain/application service
                                                     -> EF Core -> Azure SQL
```

Existing EF service implementations can move behind the API. Do not expose EF entities directly
as network contracts. Introduce narrowly scoped DTOs, especially around `User`, whose persistence
model currently contains password hash and salt fields that must never leave the server.

Methods that accept `userId`, `agencyId`, or similar caller-controlled scope values must never
trust them. As of the 2026-08-14 review every such API route gates on
`TenantAccess.CanAccessUserAsync` before the value reaches a query, and `ValidatedActorFilter`
re-confirms the claimed identity, role, and agency against the database on every request. New
routes must follow that pattern; see `API_AUTHORIZATION.md` for the route inventory and
`API_SECURITY_AUDIT.md` for what has and has not been reviewed. Transitional desktop-local
services repeat the same restrictions rather than relying on the API being the only caller.

Rules that decide permission, billability, approval, or record status belong in
`Sati.Contracts.V1`, which both the desktop client and `Sati.Api` reference, so a rule cannot be
enforced two different ways. Current owners: `BillingComplianceGate`, `MonthlyContactRules`, `BillingRules`,
`ServiceTimeline`, `AuditCsv`, `IncidentHealthScoring`, `JournalEntry`, `ChatAccess`,
`ProductivityForecast`. A second hand-written copy of one of these
rules is a defect, not a convenience.

Pure presentation and local concerns may stay in the client. Any calculation that controls
persistence, permission, approval, billability, or official record status belongs server-side.

## Data environments

- The bootstrap chooser runs before the splash screen or any database connection.
- `My work` maps only to `SatiProduction`; `Demo` maps only to `SatiDemo`.
- Database name and `dbo.SatiDatabaseIdentity` are checked against that selection before login.
- A selected Demo session displays a permanent Demo indicator.
- The original mixed local database is retained as an archive and is not a runtime target.

`SatiProduction` is the developer's personal working environment. It holds real PHI from daily
case-management use, run alongside the employing agency's system, which remains the official
record. It is not a deployment target and does not need production hardening. It is a PHI store:
the workstation rules in `OPERATIONS.md` apply in full, and anything that creates copies
(backups, exports, logs, crash dumps) must follow them.

"Production" as a product environment means the future cloud deployment, which does not exist
yet. Server-side work (background workers, alerting, backup verification) targets that and is
proven in Demo first. Where the local EF path needs the same behavior, it calls the same
`Sati.Contracts.V1` rule from its existing triggers rather than growing a second scheduler.

See `DATABASE_ENVIRONMENTS.md`. `SatiProduction` data must never be copied, queried,
transformed, or uploaded as part of Demo work without explicit authorization.

## Near-term priority

[AGENDA.md](AGENDA.md) owns the active backlog; [the readiness registry](docs/readiness/README.md)
owns launch/activation status and evidence. Neither source implementation nor a historical green
test count establishes deployment, service operation or compliance. The dated inventory in
[DATABASE_ENVIRONMENTS.md](DATABASE_ENVIRONMENTS.md) owns deployment facts. Feature work may
proceed when it reinforces the governing boundaries or is explicitly prioritized.
## Engineering rules

- Preserve unrelated user changes in the dirty worktree.
- Use constructor dependency injection; do not introduce service-locator access.
- Keep ViewModels unaware of Views.
- Use factories for window creation where a window remains the correct UI primitive.
- Keep database contexts short-lived and server-side as the API transition proceeds.
- Put authoritative business rules in one named owner and document cascade points.
- Do not use UI visibility as security.
- Do not log unrestricted note narratives, passwords, tokens, connection strings, or other
  sensitive content.
- Add tests with new authorization, tenancy, billing, audit, migration, and concurrency work.
- Confirm a security or concurrency regression test fails against the unfixed code before keeping
  it. A test that passes either way reports safety it never checked.
- Take a `LatestRequestTracker` identity for any load triggered by selection or navigation, and
  check it before writing to shared UI state.
- Neutralize untrusted values on the way out, not only on the way in. A quoted CSV field still
  executes in a spreadsheet; see `AuditCsv`.
- Record durable design choices in `DECISIONS.md` and deferred work in `AGENDA.md`.
- Update `ARCHITECTURE.md` when ownership or boundaries change, and `API_AUTHORIZATION.md` when a
  route is added, removed, or rescoped.

## DATT release trigger

Treat the newest user message as a DATT release invocation only when, after trimming surrounding
whitespace, the entire message equals `invoke DATT!` using a case-insensitive comparison. A quoted
phrase, a discussion of the phrase, or a longer message containing it is not an invocation.

On a valid invocation:

1. Reply with `DATT received. Starting release audit.` before taking release actions.
2. Read and follow `RELEASE_PLAYBOOK.md` completely.
3. Treat the invocation as authorization for the bounded Sati release actions listed there,
   including verified source changes, safe branch reconciliation, ordinary Git commits and pushes,
   Demo API publication, and Demo and Local installer generation, acceptance testing, and
   publication to the two exact distribution folders named in the playbook.
4. Continue to honor tool and operating-system approval prompts. The invocation does not authorize
   a Production deployment, a cloud database migration, force-pushing, overwriting a release
   artifact, discarding work, or deleting a branch that does not satisfy every safe-deletion rule in
   the playbook.
5. The invocation never authorizes changing a security setting, including an Azure SQL firewall
   rule, even temporarily and even when the release cannot proceed without it. `SatiDemo`'s firewall
   admits only the Demo API's outbound addresses, so a migration run from a workstation needs a
   temporary exact-IP rule that the user adds and removes. Detect that need during preflight, report
   the workstation's public address, and wait. Do not treat a later "proceed" or "I trust you" as
   permission to make the change instead.

If the playbook is missing, ambiguous, or cannot be read completely, stop without making release
changes and tell the user what is blocking the invocation.

## Healthcare and regulatory posture

Sati is not currently represented as HIPAA compliant or production-ready. Azure hosting and use of
HIPAA-eligible services do not themselves establish compliance. Consult `REGULATORY_CONCERNS.md`
before work involving real PHI, OADS review, MaineCare claims, signatures, records retention,
cross-agency access, AI, exports, or external integrations.

The platform must be designed to produce evidence of access control, integrity, auditability,
availability, risk management, and incident response. Regulatory conclusions require review by
qualified counsel, agency stakeholders, and the appropriate Maine authorities.

## Working with Josh

- Lead with the architectural reason and then explain the implementation.
- Be direct when an assumption is unsafe or conceptually incorrect.
- Avoid flattering language and avoid overstating readiness.
- Match complexity to the real risk, but do not treat healthcare security as optional simplicity.
- Preserve accessible design: meaningful automation names, keyboard navigation, screen-reader
  support, non-color status cues, and sensible focus order.
- Track deferred items instead of silently dropping them.

Sati is simultaneously a working tool and the seed of a much larger product. Protect the working
system while deliberately moving the platform boundary in the intended direction.

## Documentation governance

Read [docs/documentation-governance.md](docs/documentation-governance.md) before changing docs.
Update one canonical owner; link it instead of copying its state. Register new root Markdown in
`docs/documentation-index.json`, use stable backlog/decision IDs, and record explicit supersession.
Preserve historical snapshots. Run `scripts/Test-DocumentationStructure.ps1` and its negative
mutation checks before completing documentation or release work. These rules do not expand user
authorization, DATT, migration, infrastructure or real-data permissions.

## Standing work and documentation upkeep

After each significant portion of work, update the canonical documentation thoroughly before
finishing: changed behavior and ownership, status, decisions and reasons, tests and their actual
results, evidence limits, remaining risks/blockers, dependencies, and the concrete next slice.
Read [the documentation governance](docs/documentation-governance.md) and the relevant owners;
update their content in place and link it from indices instead of copying policy or current facts.
Record dated working evidence in [the working evidence ledger](docs/readiness/work-evidence.md).
Preserve all sealed release snapshots and rubric versions; working evidence does not change a
sealed release score. A later release assessment requires its own reviewed evidence snapshot.

Treat the newest user message as a next-work invocation only when its entire trimmed text equals
`Perform the next thing on the list` using a case-insensitive comparison. Discussion, quotation
or a longer message containing the phrase is not an invocation. Adding these standing rules
does not invoke the phrase.

On a valid invocation:

1. Read [AGENDA.md](AGENDA.md), its single **Next eligible item** pointer, the listed dependencies,
   this briefing, and the canonical topic/acceptance/evidence owners. Revalidate the pointer
   against current source and recorded blockers; do not choose an old checkbox from the archive.
2. Announce the concrete stable ID, bounded slice, expected result and verification before work.
   Execute the eligible reversible local slice within the authority stated there and existing
   user constraints. If that slice is blocked, record the blocker and select the next eligible
   slice with satisfied dependencies in the current agenda; continue independent authorized work.
   Ask for a missing decision only when no eligible bounded work can proceed without it.
3. Keep release, deployment, publication, cloud settings, database changes, real-data access and
   external actions subject to their existing separate authorization. The next-work phrase does
   not invoke DATT or broaden those permissions. Do not silently implement a larger project.
4. Before the final response, update the canonical owner, agenda status/dependencies and next
   eligible pointer, durable decisions when needed, and dated working readiness evidence.
   Record what passed, failed, was not run, and remains unverified. Run the relevant checks and
   documentation checks; report the completed slice and the next concrete step.

*Consolidated October 8, 2026. AGENTS.md is the authoritative assistant briefing.*
