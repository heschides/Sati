<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## Platform direction

### Sati is a cloud platform with a WPF client, not a cloud-attached desktop database

The existing WPF application remains a first-class staff client and the current product-development
surface. The target system, however, is an API-mediated platform that can support Windows, web, and
mobile clients. Distributed clients do not connect directly to Azure SQL.

**Rejected:** embedding a shared SQL credential in an installer. Trusted testers and synthetic data
reduce immediate harm but do not make an extractable credential a sound product boundary.

### The API is the authority

Authentication, authorization, tenant isolation, workflow transitions, transactions, audits,
migrations, and integrations are server responsibilities. UI visibility is never an authorization
control. Caller-supplied user or agency IDs are hints at most; authoritative identity comes from the
validated server session.

### Managed identity is service-to-service identity

Azure-hosted API and background-job identities receive least-privilege access to Azure SQL without
stored passwords. Managed identity does not replace a Sati user's login and is not distributed to
desktop installations.

### Tenant isolation is structural

`AgencyId` alone does not establish safe multi-tenancy. Every protected aggregate must have an
unambiguous tenant owner, and enforcement must occur centrally with automated cross-tenant tests.
Whether production ultimately uses a shared database, database-per-tenant, or a hybrid remains an
explicit design decision; no feature may assume that a forgotten query predicate is adequate
isolation.

### Network contracts use DTOs, not EF entities

EF entities are persistence models and may contain navigation graphs, internal fields, password
material, or properties callers must not set. API request and response contracts are deliberately
small, versionable DTOs. In particular, `PasswordHash` and `Salt` never leave the server.

### Submitted healthcare records are amended, not overwritten

Drafts may remain mutable. Submission, approval, signature, claim generation, or other defined
record-finalization events create immutable versions. Later corrections produce amendments or new
versions with a linked reason and actor. Audit events are append-only.

### Person lifecycle history is a separate, PHI-bearing ledger

The small `AuditEvent` envelope remains intentionally free of narratives and profile values. A
Person audit has a different evidentiary purpose, so each revision stores a compressed full snapshot
and the exact field-level before/after changes with actor, agency, timestamp, and request ID.
Application code may append a version but may not update or delete one. Admin history access is
tenant-scoped and audited, and PDF responses are non-cacheable.

**Rejected:** storing Person values in the general activity log. That would spread PHI across every
operational audit query and make retention and access control harder to reason about.

**Rejected:** inventing history for existing People. The first touch creates an explicit tracking
baseline of current state; only subsequent changes can truthfully identify who changed what and when.

### Clients do not migrate cloud databases

`Database.Migrate()` remains acceptable during local development while the transition is underway.
Production and distributed Demo schema changes run as controlled deployment operations before the
new application version is admitted.

### Demo schema and Demo seed are separate assets

Migrations define structure. A versioned canonical seed defines the synthetic superhero/sitcom
dataset and stored demonstration logins. The intended design is a scheduled Azure job restoring
that baseline nightly under a reset-specific managed identity, separate from the API identity.

**Daily caseload refresh configured 2026-09-06.** A timer-triggered Azure Function now updates the
canonical working caseload at 3:15 AM Eastern under its own managed identity and restricted SQL
role. It rolls dates forward, completes ordinary synthetic profiles, preserves six labeled teaching
exceptions and the superhero/TV humor, repairs synthetic claim prerequisites, and fails unless its
post-commit validation passes. A live run and an immediate repeat run both succeeded.

**Full-reset source implemented 2026-09-06; deployment pending.** The approved current synthetic
database becomes a protected, explicitly replaceable baseline rather than a second seed that tries
to recreate every relationship. An owner-executed procedure restores all baseline tables; the
existing versioned seed then rolls dates and validates profile and billing readiness. The Function
holds an exclusive application lock for both phases, while every Demo API mutation takes the
matching shared lock. Restoration rotates the database instance identifier carried by access
tokens, invalidating every old session instead of hoping that each client clears stale state.

The Admin command is deliberately Demo-only, requires a typed confirmation, and crosses the normal
WPF -> API -> separately authenticated Function boundary. The Function key remains an API setting,
not a query-string secret in source or a desktop setting. The reset identity cannot directly read
the protected baseline; the procedure runs as its owner. Schema drift refuses the reset and requires
a newly reviewed baseline capture after migration.

**Rejected:** implementing full reset as a chain of client API deletions; giving the desktop SQL
credentials; allowing mutations during restoration and date repair; leaving pre-reset tokens valid;
and silently adapting an old baseline to a new schema. The hosted system must still be called the
daily caseload refresh until controlled baseline capture, deployment, live acceptance, and an
approved failure-notification destination are complete.

### Automated tests are a platform prerequisite

Further feature work may continue, but production expansion requires tests for tenant isolation,
authorization, workflow transitions, concurrency, billing rules, audit completeness, migrations,
and reset/recovery. Manual verification is not sufficient evidence for a healthcare SaaS platform.

---

