# Data environments — authoritative dated inventory

**Inventory owner:** this file. **Latest evidence date:** October 9, 2026 UTC and local.
Assessment observations and subsequent bounded Demo release observations are dated below. Current release
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

## Demo API 1.3.38 publication — October 9, 2026, 01:24:01 UTC

The existing `rg-sati-demo / sati-demo-api-satilogica` App Service serves release **1.3.38**
from pushed source `a50a4df611439f2354f02426e8b5d404297b8ab4`, contract `D33428799A40`
(268 routes, 69 contract shapes). Public `/health/live`, `/health/ready` and `/health/version`
returned HTTP **200**, respectively `live`, `Healthy` and the expected product/version/contract.
Active OneDeploy deployment `d824f423082542b597a94e303981d1bc` has status **4**, complete **true**;
raw UTC start `2026-10-09T01:10:50.1523236Z`, end `2026-10-09T01:10:52.0586455Z`.

The new API ZIP is `artifacts/SatiApi-1.3.38-fx-x86.zip`, **11,831,567 bytes**, **70 entries**,
SHA-256 `D7AD47030ED82D864FB6925E153E78CDFA41EEE65C08B08A9408349CDF983CC2`.
Exactly one upload received HTTP 202. The initial verifier incorrectly followed a temporary
deployment ID and stopped on its later 404; a separate read-only verification pinned the real
completed ID, matched all 70 known deployed file lengths/hashes to the reviewed ZIP, and checked
stable deployment identity around the file and public-health checks. Original failed evidence
is retained. Full verification evidence and its limits are recorded in
[the working ledger](docs/readiness/work-evidence.md#2026-10-08--1338-demo-api-publication-and-verification).

No schema/model/reset-baseline delta was present. No database access, migration, capture, reset,
firewall or other setting change was performed. The prior known-healthy package/deployment is
retained. These observations establish this Demo API publication and known package bytes;
they do not establish worker completion, current SQL grants, recovery or independent client
acceptance. Post-packaging hosting evidence does not revise the sealed 1.3.38 readiness score.

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

## Pending source schema — October 9, 2026 worker readiness

`20261009183720_AddClearinghousePreflightReadiness` is an additive source migration authored under
Josh's explicit bounded approval. [W8](BACKGROUND_WORKERS_HANDOFF.md#october-9--known-unsent-dispatch-isolation-implementation)
owns schema/rollout/rollback behavior; [working evidence](docs/readiness/work-evidence.md#2026-10-09--missing-key-dispatch-isolation-implementation)
owns private synthetic verification. That source implementation did not migrate Demo, local
working or cloud databases. No existing database was accessed or changed during that slice.
A later authorized release must
revalidate exact schema, synthetic baseline/reset compatibility and worker versions with dispatch
paused; earlier 1.3.38 no-schema-delta observations remain dated facts for that release only.

## Demo readiness migration preflight — October 9, 2026

Josh subsequently approved the **Demo worker-readiness migration, reset-baseline replacement and
one verification reset** after adding the temporary workstation rule himself. This approval does
not invoke a new DATT release or authorize dispatch activation, Production access, or assistant-run
firewall changes. [The working record](docs/readiness/work-evidence.md#2026-10-09--readiness-migration-runner-preparation)
owns preparation checks and the remaining rollout sequence.

At **20:33:32 UTC**, a token-authenticated, read-only metadata query against the pinned
`sati-demo-satilogica-central.database.windows.net / SatiDemo` validated `DB_NAME()` and the
`dbo.SatiDatabaseIdentity` Demo marker. It observed **128 migration IDs**, latest
`20261007111016_AddAssessmentReviewCycles`; neither the readiness table nor its reset-baseline
table existed. There were **92 baseline tables**. This first read checked count/latest, not yet
the complete source-ID set or every existing schema object's semantics. It did not read clinical
or financial record contents and made no database changes.

At **20:42 UTC**, the locally verified controlled runner's `-PreflightOnly` mode passed against
that same pinned target. It validated the complete source predecessor-ID set and existing
prerequisite schema and reported `PREFLIGHT_PASSED_NO_PERSISTENT_TARGET_CHANGES`, migration
already applied **false**, changes required **true**, persisted count **128**. Guarded SQL
SHA-256 was `6E9C8AC1614FF2E9D38894786434E58E21D82484D210BA217C6C283E056FA1AA`.
The runner acquired/released reset exclusion and rolled back its inspection transaction; no
persistent target change, cloud DDL rehearsal, application or reset was performed.

Bounded control-plane reads during this preflight observed the operator-created
`datt-workstation-20261009` rule with matching start/end **72.95.106.10**, the API's expected
environment/database values `Demo`/`SatiDemo`, and neither
`Sati__EnableSyntheticClearinghouseDispatch` nor `Sati__EnableClaimMdSandboxTransport` present in
its app settings. Source defaults for both flags are false; this projection alone does not prove
every running host's effective configuration. SQL was Online with Local backup redundancy and
an earliest restore timestamp of **2026-10-02T20:37:17.432226Z**. Recovery metadata is not a tested
restore. Credentials and unrelated settings were not printed.

The cloud schema, baseline and deployed API remain unchanged by these reads. Revalidate paused
dispatch on all hosts, exact schema/history, recovery prerequisites and reset eligibility before
the approved operation. After the matching API is healthy, capture the approved baseline and
verify exactly one reset under the playbook; the user then removes the temporary rule and the
assistant verifies absence. No new release invocation or reset outcome is recorded here yet.

## 1.3.39 release preflight and Local uptake — October 9, 2026

Josh subsequently sent the exact DATT invocation. During release preflight, the guarded Demo
inspection and live/baseline reset guards passed again without persistent changes. The Demo API
had **no deployment slots**; the existing `sati-demo-refresh-satilogica` Function was Running.
The projected expected environment/database and absent dispatch flags matched the earlier
observation. No other dispatch host was identified by this bounded resource/source inventory;
this is not an exhaustive tenant host/configuration audit.

Known Local machine **LONGCHENPA** has installed Sati **1.3.38.0**, confirmed from the installed
executable's file metadata. It remains behind the 1.3.39 candidate and has not been migrated by
this release work. The desktop applies pending local migrations at its next upgraded launch;
installer distribution is not evidence that a machine was updated. Other Local machines and last
known versions have been requested from Josh and are presently **unknown**, not assumed current.
No working database or clinical record was queried. [The release working record](docs/readiness/work-evidence.md#2026-10-09--datt-1339-release-audit)
owns gates and pending deployment/reset outcomes.

## 1.3.39 controlled Demo rollout — October 9, 2026

The separately approved guarded migration passed its rollback rehearsal at 128 migrations,
committed `20261009183720_AddClearinghousePreflightReadiness`, and passed a second `-Apply`
as `IDEMPOTENCY_VERIFIED` at **129 migrations**. Exact guarded SQL SHA-256 remained
`6E9C8AC1614FF2E9D38894786434E58E21D82484D210BA217C6C283E056FA1AA`. The API's two dispatch
settings were absent immediately before application; their source defaults remain false.
No dispatch activation, cloud Production change or Local working-database migration occurred.

The matching Demo API from pushed source `6fbfeb65730697d4e0244a551fca736f5a3e82a1` completed
deployment **`943c4d4f09154180b32f33fe20a57338`** at **21:16:31.4177673 UTC**. Public liveness,
readiness and version all returned HTTP 200: `live`, `Healthy`, release **1.3.39**, contract
**`D30D44631876`**. All 70 reviewed deployed file lengths/hashes matched the accepted package,
with the same completed active deployment before/after the reads. Matching known files does
not prove absence of extra files or distinguish another publisher deploying identical bytes.
The prior healthy 1.3.38 package/deployment remains retained.

The current-source compliance dry run checked 177 synthetic clients: **zero changes**, zero
unexpected current/historical billing blockers, retained teaching exceptions and two known
missing-effective-date teaching cases. The approved capture reported
`DEMO_FULL_RESET_BASELINE_CAPTURED`; metadata observed **93 baseline tables**, anchor
**2026-10-09**, captured **21:17:49.7796694 UTC**. Exactly one verification reset was accepted
HTTP 202 at **21:18:53 UTC**, request **`d851db02-81c6-4559-ab9c-e15ea880bddd`**, actor 1006.
The exact request recorded **`demo.reset.completed` at 21:22:48.1941896 UTC**, with no matching
failed event. The same `ResetDemoWorker` operation **`91bb7a0e059b1b35b4aabb958bacbdf2`** logged
`DEMO_COMPLIANCE_HISTORY_COMPLETE` at **21:22:48.1745044 UTC**, followed by the request-specific
completion at **21:22:48.208553 UTC**. Reset metadata retained 129 migrations, 93 baseline tables
and today's anchor/application date. Public health/version was Healthy at **21:25:33 UTC**.
An early read-only metadata observation timed out during the reset. Initial log queries required
a prefix correction before correlation; the final match uses the fixed completion text, exact
request and same nonempty operation ID. No reset replay was attempted. Josh confirmed removal; a successful read-only exact-rule listing returned **zero matches**.
The temporary `datt-workstation-20261009` rule is **absent**, verified without a settings change
by the assistant. `artifacts/datt-1.3.39/firewall-removal-verification.json` records the target
and successful absence evidence.

## 1.3.39 installer publication — October 9, 2026

After Josh closed the working application, the unchanged release installers passed sequential
isolated acceptance on **LONGCHENPA**. Demo recorded five responsive 15-second launches, normal
closes, exact version **1.3.39.0** and cleanup at **21:45:34 UTC**. Local recorded exact version,
Windows integrated security, `SatiProduction` mapping, valid embedded Microsoft LocalDB signature
and cleanup at **21:46:30 UTC**; it did not launch the working Local application or query its database.
These workstation gates are not independent external-machine acceptance.

At **21:46:45 UTC**, the two accepted installers and their `.sha256` files were published without
overwrite. All four final lengths/hashes and both checksum contents were verified:

| Artifact | Exact published path | Bytes | SHA-256 |
|---|---|---:|---|
| Local | `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.3.39.exe` | 206,932,521 | `07F3850B46315537405C584770BFE8B6D7930DB14E8BFFF396AAE6A2E3BADF10` |
| Demo | `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.3.39.exe` | 104,583,168 | `B7E18B675B53C0B024E09162183DEE0E4B3A9EE9A9ECEF1FE6051D4AA99FFCEB` |

`artifacts/datt-1.3.39/distribution-evidence.json` records all four exact paths, hashes and sizes,
and binds the two successful acceptance JSON hashes. Cloud-sync receipt is unverified. Distribution
does not update the working installation: LONGCHENPA's last observed installed Local version remains
**1.3.38.0**; other machines remain unknown. Upgraded desktop next-launch migration is still the
Local owner's responsibility. [The dated working ledger](docs/readiness/work-evidence.md#2026-10-09--1339-installer-acceptance-and-distribution)
owns commands, earlier refusal and gate limits. No working database or real claim was exercised.
