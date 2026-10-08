# Data environments — authoritative dated inventory

**Inventory owner:** this file. **Latest evidence date:** October 8, 2026. Read-only assessment
observations are dated below; this consolidation made no cloud/database call. Current release
readiness belongs to [the readiness registry](docs/readiness/README.md). Historical deployment
chronology is retained in [the original inventory](docs/archive/2026-10-08/DATABASE_ENVIRONMENTS.md).

## Observed Demo inventory — October 8, 2026

| Fact | Observed value / evidence boundary |
|---|---|
| Demo SQL | `SatiDemo`, logical server `sati-demo-satilogica-central`, Basic capacity 5, Online |
| SQL recovery configuration | Seven-day short-term retention; 12-hour differential interval; Local backup redundancy; no returned LTR, zone redundancy, replication links or named-server failover groups |
| Demo API | `sati-demo-api-satilogica`, App Service Free F1; Always On false; WebSockets false |
| Host/SQL identities | Managed identity architecture exists; current exact grants/key/log permissions were not inspected by this assessment |
| Monitoring | Planned watchdog alert rules absent in bounded October 8 reads; actual notification receipt not established |
| Temporary workstation rule | `datt-workstation-20261007` absence confirmed by October 8 follow-up after Josh removed it; this item is closed in that dated evidence |
| Future cloud Production | Not deployed; Demo observations do not establish PHI/financial Production readiness |

Source: [October 8 engineering assessment](reports/SATI_ARCHITECTURE_ENGINEERING_ASSESSMENT_2026-10-08.md),
sections 7–8 and its bounded control-plane evidence. No live record, PHI, credential or private
setting was inspected. Earlier `GP_S_Gen5_2`/free monthly allowance/auto-pause entries are historical,
not current Basic facts. D2 no wake ping and the no-idle-polling policy remain in force.
## Demo API release preflight — October 8, 2026, 23:03:45 UTC

Bounded public checks returned `/health/live` HTTP 200 (`live`), `/health/ready` HTTP 200
(`Healthy`), and `/health/version` HTTP 200 for `Sati.Api` release `1.3.37`, contract revision
`D33428799A40`. The inspected source manifest independently matched that revision (268 routes,
69 contract shapes). The existing successful deployment metadata identified active deployment
`9d53a76fb9da4b3fa9288fb34e965145`, completed October 7 at 16:09:21.2189106 UTC.

These were read-only public health and projected Azure deployment metadata checks. They establish
the pre-release API state, not database contents, current principal grants, enabled worker progress,
vendor acceptance or complete service operation. No token, application setting or protected record
was printed, and no resource, database or security setting was changed. The reviewed candidate
diff against fetched `origin/master` adds no persistence migration/model or reset-baseline change;
no Demo migration or baseline replacement is required by this release slice.

## What `SatiProduction` is


`SatiProduction` is the developer's personal working environment. It holds real PHI from daily
case-management use, run alongside the employing agency's system, which remains the official
record. It is not a deployment target and does not need production hardening. It is a PHI store:
the workstation rules in `OPERATIONS.md` apply in full, and anything that creates copies
(backups, exports, logs, crash dumps) must follow them.

"Production" as a product environment means the future cloud deployment, which does not exist yet
and will be separately approved. Server-side capabilities are built for that environment and proven
in Demo. The local EF path shares their rules through `Sati.Contracts.V1` rather than duplicating
the server's scheduling or operations.

| Startup choice | Transport | Required target | Required marker |
|---|---|---|---|
| `My work` | Local EF/SQL (`SatiProduction`) | `SatiProduction` | `Production` |
| `Demo` | HTTPS API | `https://sati-demo-api-satilogica.azurewebsites.net/` | `SatiDemo` / `Demo` |

The environment chooser is the first window shown, before the splash screen and before Sati builds
its service host or opens a database connection. User credentials never select or redirect the
database. Local Production startup fails before migrations or authentication when the configured
database name or database-resident identity marker does not match the explicit startup selection.
Demo builds no EF context and never receive an Azure SQL connection string; the API validates the
database marker during its own startup. Closing the chooser opens neither environment and exits Sati.

## Local environments

The provisioning script preserves the original mixed `Sati` database, creates a checked backup,
restores isolated copies, and filters them by owning user's agency:

```powershell
.\scripts\Provision-LocalDataEnvironments.ps1
```

- `SatiProduction` retains production-owned users and clients.
- `SatiDemo` retains the synthetic demonstration users and clients.
- Ownership follows `Person.UserId -> User.AgencyId`; the denormalized `Person.AgencyId` value is
  not authoritative for this split.
- The original `Sati` database remains unchanged as a source archive.
- Backups are stored under `%LOCALAPPDATA%\Sati\DatabaseSnapshots`.
- The script refuses to overwrite either target database.

During development, either build can be started normally and the data environment selected in the
first window:

```powershell
dotnet run --configuration Debug
```

The `Demo` build configuration remains available when a separately named `Sati.Demo.exe` artifact
is useful, but it also requires an explicit environment selection and does not bypass identity
validation.

## Deployed Demo cloud boundary

The Demo path now uses the required boundary:

```text
Sati.Demo client -> authenticated HTTPS API -> Azure SQL SatiDemo
```

Implemented properties:

- The client contains the HTTPS API address, never an Azure SQL password.
- The API validates Sati credentials and issues a short-lived session token.
- Password hashes and salts remain server-side.
- The API derives user, role, agency, and allowed records from the authenticated session rather
  than trusting caller-supplied IDs.
- The Azure API connects to SQL using managed identity.
- Azure SQL accepts connections from the hosted service boundary, not arbitrary tester devices.
- The Demo client does not register an EF context and does not run database migrations.
- The database retains the `dbo.SatiDatabaseIdentity` marker as an additional environment guard.

Historical initial HTTP-backed Demo workflows included authentication, caseload and person summaries, journals,
case notes, settings reads, scratchpads, exempt dates, incentives, and form state updates. Features
that have not yet received an authorized API endpoint fail explicitly in Demo; they do not fall back
to LocalDB or direct Azure SQL.

## Canonical reset and environment safety

Schema migrations and versioned synthetic seed/baseline are separate assets. Reset excludes
concurrent mutations, validates the Demo marker/ownership/inventory, records outcome and invalidates
prior sessions through instance identity rotation. Admin reset requests use the single-delivery
queue; an interrupted outcome may leave poison evidence. Never replay poison or infer success
from queue acceptance. External clearinghouse linkage can intentionally block reset; preserve it.

[OPERATIONS.md](OPERATIONS.md) owns reset/watchdog/uncertainty response procedures.
[CLAIMMD_SANDBOX_RUNBOOK.md](CLAIMMD_SANDBOX_RUNBOOK.md) owns sandbox windows and reset pause.
[RELEASE_PLAYBOOK.md](RELEASE_PLAYBOOK.md) owns release authorization and evidence gates.

## Migration and recovery requirements

Cloud migrations require separate authorization, identity validation, reviewed additive/rollback
behavior, backup/rehearsal evidence and the controlled runner. Installed Demo clients never migrate
cloud schemas. Temporary firewall changes remain operator-run; no release or generic “proceed”
authorizes the assistant to alter them. Personal working data is never queried/copied/transformed
for Demo without explicit authorization. Future cloud Production requires separate approval.

Configuration describes available backups, not tested restoration. A restore must preserve SQL,
object/key evidence, immutable lineage and later/external effects under the operations/readiness
criteria. Local backup creation/pruning remains PHI-copy work governed by workstation policy.

## Updating this inventory

Record environment, observation time, bounded source, observed value and what was not verified.
Replace the current table with dated evidence while retaining the previous observation in history.
Other current documents link here; do not repeat SKU, quota, pause, grants or alert-deployment facts.
Never refresh observations by querying a private database or changing infrastructure as doc upkeep.
