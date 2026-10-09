# Sati DATT Release Playbook

This playbook defines the bounded release workflow invoked by the exact, case-insensitive command
`invoke DATT!`. It is designed to release verified Sati changes without silently discarding work,
publishing to Production, changing a cloud database, or overwriting an existing artifact.

## Invocation and authority

A DATT release begins only when the newest user message, after trimming surrounding whitespace,
equals `invoke DATT!` using a case-insensitive comparison. For example, `INVOKE datt!` is valid.
Mentioning or quoting the phrase inside any longer message is not valid.

On a valid invocation, first reply:

> DATT received. Starting release audit.

The invocation authorizes these actions for the current Sati repository:

- inspect, edit, build, and test the in-scope release;
- fetch Git state and reconcile completed, relevant branches;
- delete only branches that satisfy every safe-deletion rule below;
- create ordinary commits and push them to the resolved default branch;
- publish the Sati Demo API to its existing Demo Azure resource;
- build and acceptance-test new Demo and Local installers;
- publish the accepted installers and checksum files to the exact distribution folders below; and
- commit and push final release evidence.

The invocation does not authorize:

- a Production API or infrastructure deployment;
- a cloud database migration or any transformation of Production data;
- copying Production data into Demo;
- opening or altering a database, network, or other security setting, including an Azure SQL
  firewall rule, even temporarily and even when the release cannot proceed without it;
- force-pushing, rewriting published history, discarding changes, or overwriting an artifact;
- deleting a protected, active, ambiguous, or uniquely valuable branch; or
- expanding the release to another product merely because it shares this repository.

Normal Codex, operating-system, Git host, and Azure approval prompts still apply.

## Success criteria

A DATT release is complete only when all applicable conditions are true:

- relevant completed work is present on the repository's resolved default branch;
- the working tree contains no unexplained or accidentally omitted changes;
- the version, Settings release tracker, builders, readiness checks, tests, and release documents
  agree on one new version;
- documentation ownership checks pass, and the Settings readiness report has a reviewed snapshot
  for that exact version, retaining previous releases and their original scoring criteria;
- the full Release build and all available automated test projects pass;
- source commits are pushed without rewriting remote history;
- the Demo API reports healthy liveness and readiness, the new release version, and the expected
  contract revision;
- the Demo and Local installers are new, non-overwritten artifacts and pass their acceptance gates;
- the accepted Local and Demo installers and checksums are present in their designated distribution
  folders with hashes identical to the accepted build artifacts;
- deployment identifiers, test results, artifact paths, sizes, and SHA-256 hashes are recorded;
- when the release changes schema, both halves are recorded: the Demo application with its
  evidence, and the known Local Production machines with the release each is on, including any
  known to be behind;
- when the release migrates `SatiDemo`, the reset baseline has been recaptured and one full reset
  afterwards has recorded `demo.reset.completed`; and
- the final evidence commit is pushed and the local default branch matches its remote.

## 1. Preflight and repository audit

1. Read `AGENTS.md`, this playbook, and the current release section of `AGENDA.md`. Read other
   project instructions required by the scope.
2. Confirm the repository root, remotes, current branch, upstream, linked worktrees, and remote
   default branch. Do not assume that the default branch is named `main`; it is currently `master`.
3. Fetch current remote state before deciding whether anything is merged, obsolete, ahead, or
   behind. Do not use a force option.
4. Inspect staged, unstaged, untracked, ignored release artifacts, and recent commit state. Preserve
   unrelated changes and changes in other worktrees.
5. Identify the releasable change set. If there are no new releasable changes, report that result
   and stop without incrementing a version, committing, deploying, or packaging.
6. Determine during preflight whether the change set adds or alters database schema, by checking
   for new `Sati.Persistence/Migrations/` entries and for API contracts that read columns or tables
   the deployed Demo database may lack. Schema work makes the release dependent on two things the release
   itself cannot supply, so establish both before building anything:
   - explicit authorization for the controlled Demo migration; and
   - network access to `SatiDemo`, whose SQL firewall admits only `sati-demo-api-satilogica`'s
     outbound addresses. No developer workstation has standing access. Running a migration from
     one therefore needs a temporary exact-IP rule, which is a security setting: **ask the user to
     add it and to remove it afterwards. Never add, alter, or delete a firewall rule.** Report the
     workstation's public address so the user can create the rule without hunting for it.

   Discovering either of these at the API publication step wastes a full release pass. Raise both
   in the preflight report, alongside the other findings, before any version bump or commit.

   Whenever a migration needs workstation access, provide the user with copy-and-paste
   PowerShell commands for both adding and removing the exact-IP rule; reporting an IP alone
   is insufficient. Use `scripts/Set-DemoWorkstationFirewallRule.ps1`, the freshly reported
   public IPv4 address, and one explicit release-specific rule name in both commands. For example,
   from the repository directory (replace the example address/date for the current release):

   ```powershell
   # Josh runs this before the controlled migration.
   .\scripts\Set-DemoWorkstationFirewallRule.ps1 -Ip 72.95.106.10 -RuleName datt-workstation-20261004

   # Josh runs this after migration, baseline capture and reset verification.
   .\scripts\Set-DemoWorkstationFirewallRule.ps1 -Remove -RuleName datt-workstation-20261004
   ```

   Check the helper before handing it over: restrict it to the exact Demo server/resource group,
   reject nonzero Azure CLI exits, refuse to repoint an existing rule, verify exact start/end IP
   after creation, and verify absence of the same named rule after removal. A failed listing is
   not proof of removal. The user executes both commands; assistants never execute the helper
   against Azure. Keep the rule open through the separately approved baseline capture and
   verification reset, then obtain confirmation of removal.

   A release containing the full Demo reset has an additional controlled database operation even
   though it adds no EF migration: the first reviewed baseline capture (or an intentional baseline
   replacement after schema/data changes). DATT does not authorize that data transformation. Stop
   in preflight unless the user has separately approved capturing the current `SatiDemo` contents,
   and ask the user to add and later remove the temporary exact-IP firewall rule. Run only
   `scripts/Initialize-DemoFullReset.ps1 -ReplaceBaseline` against the identity-validated Demo
   database. The rollout order is load-bearing: first publish the matching API with reset settings
   absent, so its shared mutation lock is live while the reset route remains closed; then capture
   the baseline, publish the Function, obtain its host key without printing or committing it, and
   store `DemoReset__FunctionEndpoint` and `DemoReset__FunctionKey` only in the Demo API's protected
   application settings. Verify the restarted API afterward. Never place the key in a URL, client
   configuration, release evidence, or logs.

   **Every Demo schema change also replaces the reset baseline.** `SatiResetToCanonicalBaseline`
   refuses (SQL 51002) when a `dbo` table was added or removed after capture, and a column added to
   an existing table fails the restore's inserts (SQL 207). Either way no Demo reset succeeds, nightly
   or manual, until the baseline is recaptured. Between 2026-09-10 and 2026-09-26 this went unnoticed
   for seventeen nights. So a release that migrates `SatiDemo` needs the same separate approval to
   capture its current contents, and the firewall rule must stay open through the capture in step 6.
   Ask for both in the preflight report, together with the migration authorization.

   A schema change reaches two kinds of place, never one. `SatiDemo` is a single Azure database
   migrated deliberately. Local `SatiProduction` is a separate database on *every* machine, migrated
   by the desktop at its next launch. They are therefore never applied together, and treating the
   Demo application as "the migration is done" is how three machines came to be running different
   schemas on 2026-08-30 without anyone knowing until one refused to start.

   So a schema-changing release records both halves:
   - the Demo application, with its evidence; and
   - the Local Production machines that exist, and which release each is on.

   **Never assume a machine has caught up.** The desktop applies pending migrations at launch, so a
   machine that has not been opened since the last release has not received it. List the known
   machines by name and version in the release record, including any believed to be behind. A
   machine nobody has thought about is the one that breaks.
7. If a change's ownership or intent cannot be determined safely, stop and ask the smallest
   necessary question. Never hide uncertainty by stashing, resetting, or overwriting it.

## 2. Branch reconciliation and cleanup

Inspect each local and remote branch relative to the resolved default branch using ancestry,
unique commits, diffs, upstream state, and worktree ownership.

Merge a branch only when all of these are true:

- it contains completed work relevant to the current Sati release;
- its unique commits and diff have been reviewed;
- it is not an unrelated experiment, historical setup branch, or incomplete product slice;
- the merge can be performed without discarding or concealing working-tree changes; and
- conflicts can be resolved from clear project intent and verified afterward.

If a relevant merge is ambiguous or conflicts with unexplained changes, stop for direction. Do not
autostash, reset, force a merge, or choose one side merely to make the merge complete.

A branch may be deleted only when every condition below is proven:

- its tip is fully merged into the resolved default branch;
- it has no unique commits relative to that branch;
- it is not the default branch, a protected branch, a release branch, or otherwise designated for
  retention;
- it is not checked out by any linked worktree;
- it is not associated with active or uncertain work; and
- its name and tip commit are recorded before deletion.

Use safe local deletion, never forced deletion. Delete a remote branch only when the same conditions
are true for its remote tip and the branch is clearly an ephemeral completed feature branch. If any
condition is uncertain, keep the branch and report it instead.

## 3. Version and release record

Determine the next version from the latest committed release and the repository's established
convention. Use a patch increment by default. Use a minor or major increment only when the user's
request or a recorded product decision clearly requires it.

Update all coordinated owners before the source release commit, including as applicable:

- `Sati.csproj` and `Sati.Api/Sati.Api.csproj`;
- Demo, Local, and diagnostic installer builder defaults;
- Demo readiness expectations and explicit version assertions;
- `Services/ProductReleaseNotes.cs`, which supplies the Settings version tracker and release notes;
- a reviewed, append-only snapshot in `docs/readiness/readiness.json` for the new version (see
  [Readiness reporting](#readiness-reporting) below);
- current installer and runbook examples; and
- a new current-release section in `AGENDA.md` with deployment and artifact evidence still marked
  pending.

Do not bump Carika or another product merely because it is in the same solution. Include another
product only when its own release is clearly part of the releasable change set.

Never reuse a version whose API ZIP or installer already exists. Never replace different bytes under
an existing version number.

## 4. Release validation

1. Review the complete diff and run `git diff --check`. After updating the coordinated release
   owners, run `scripts/Test-DattPreflight.ps1` before starting the full build/test gates. This
   source-only check catches stale project versions, builder/readiness defaults, release-note
   assertions, migration-count/latest-migration assertions, and installer examples without
   compiling or connecting to a database. Fix every mismatch first; it supplements the full gates.
   The preflight also runs `Test-DocumentationStructure.ps1` and `Test-ReleaseReadiness.ps1`.
   Run their negative-case checks when either gate or its format changes. CI runs these checks
   on every change. A valid report can show unfinished multitenancy work; a missing, malformed,
   stale, or rewritten report cannot pass.
2. Build the complete `SatiLogica.slnx` solution in Release configuration.
3. Run every test project in `SatiLogica.slnx`, including Sati desktop/domain tests, API integration
   tests, and Carika tests when present. Run profile-dependent DPAPI, WPF, or Avalonia checks under
   the normal signed-in Windows profile when the sandbox cannot exercise them correctly.
4. Run additional focused tests required by the changed behavior and inspect relevant packaged UI
   behavior when automated coverage alone is insufficient.
5. Treat an optional test as skipped only when its documented external prerequisite is genuinely
   absent; report the skip explicitly.

Any real build or test failure stops the release. Fix it and repeat the affected gates before
continuing. Do not publish or package a knowingly failing source state.

Reuse passing evidence only while that gate's verified source, configuration, tools, dependencies
and artifact bytes remain unchanged. Repeat a gate when its inputs changed, it failed, or a specific
unresolved concern requires it.
Evidence-only ledger edits do not require another build, test run, package, or acceptance run.
The readiness JSON is embedded in the desktop. Changing it changes the shipped UI and therefore
requires rebuilding and verifying that artifact; it is not a post-build operational ledger edit.

### Readiness reporting

The authoritative scoring method is [docs/readiness/readiness-method.md](docs/readiness/readiness-method.md).
The known risk and failure inventory is [docs/readiness/multitenancy-contingencies.md](docs/readiness/multitenancy-contingencies.md);
the protocol guarantees and limitations are in [docs/readiness/protocol-baseline.md](docs/readiness/protocol-baseline.md).
Read these during release review. The Settings Release tab presents separate multitenancy and
idempotency thermometers, overall readiness including operations, hard blockers, plain-language
current state and next steps, and the change from the preceding release. Progress is evidence-based;
a new version or more tests alone earns no points. A release with no progress must say so honestly.

1. After coordinating the new version, create a template with
   `scripts/New-ReleaseReadinessSnapshot.ps1 -Release <new-version> -SourceRevision <audited-40-character-commit> -DraftPath <review-file>`.
   This creates unknown assessments requiring review; it does not copy claims of verification.
2. Review every criterion against the release inputs. Supply precise versioned evidence references,
   the date and audited source commit, an explanation of what changed or remains unproven, and
   concrete next steps in plain language. Evidence must identify its tested inputs and results.
   Label source inspection, synthetic tests, SQL/provider tests, live hosting exercises, and
   independent review accurately. Unknown or unavailable evidence earns no verification credit.
   A source revision records the commit inspected before preparing the report; later relevant
   source changes require reassessment. Do not create self-referential commit hashes.
3. Append the reviewed file with
   `scripts/New-ReleaseReadinessSnapshot.ps1 -Release <new-version> -SnapshotPath <review-file>`.
   The helper validates the exact current source version, evidence references, complete coverage,
   and unchanged history before replacing the ledger. Repeating an identical append is harmless;
   a different snapshot under the same release is rejected.
4. Run preflight and build/test the final embedded report. If relevant inputs change before source
   commit, reassess the unreleased draft. Once committed, the report and its rubric are immutable:
   record corrections in the next release. Add a new rubric version when requirements change;
   retain old rubrics. The UI explains that scores from different rubrics cannot be compared.
5. Record any deployment/hosting observations obtained after packaging in the dated operational
   release evidence, then assess them in the next shipped snapshot. Do not overwrite an accepted
   installer to improve its thermometer.

The initial 1.3.37 report is an October 8 retrospective assessment of the inspected source,
not a claim that this feature shipped on October 7 and not a completed release in this task.
The readiness evidence gate checks report integrity; the independent cloud Production launch gate
requires every mandatory protection to be verified. A high average never clears a hard blocker.

## 5. Source commit and push

1. Fetch the remote default branch again and confirm it has not advanced unexpectedly.
2. Stage only the verified release scope. Review the staged diff and staged whitespace check.
3. Create a normal release commit that includes the coordinated version and Settings release notes.
4. Push to the resolved remote default branch without force.
5. Confirm the remote contains the exact source commit before producing deployment artifacts.

If the remote advanced, reconcile it safely and rerun affected validation. Never rewrite another
contributor's published history.

## 6. Demo API publication

Publish only the existing Sati Demo API resource. Build the API ZIP from the pushed source commit,
confirm its assembly version, confirm that no private desktop settings or reusable credentials are
packaged, and record its SHA-256 hash.

No database migration is implied by this workflow. If the release requires a cloud schema change,
stop and obtain explicit authorization for the controlled migration procedure before publishing a
dependent API.

Apply an authorized Demo migration with an existence-guarded script rather than EF's generated
idempotent script. `SatiDemo` and `SatiProduction` have acquired columns outside the migration
chain, so `__EFMigrationsHistory` and the real schema disagree in both directions, and the
generated script fails with SQL 2705 on a column that exists without its history row. Follow
`scripts/Apply-ProviderDirectoryMigrations.ps1`: fail closed on database and environment identity,
guard every statement on the actual schema, verify that anything already present has the expected
semantics rather than merely the expected name, and stay rerunnable. Run it once with a
rollback-only dry run, then for real, then once more to prove idempotency.

Reaching `SatiDemo` from a workstation requires the temporary firewall rule described in preflight.
The user adds and removes it; this workflow never does. A readiness check that returns healthy
afterwards is the real confirmation that the migration satisfied the deployed model, because
`SchemaDriftHealthCheck` compares the model's tables and columns against the database.

### Demo reset baseline after a migration

A migrated `SatiDemo` cannot be reset until its baseline matches the new schema (see preflight).
With the capture approved and the firewall rule still open, once the migrated API reports healthy:

1. Check the live data before freezing it. Run `tools/SatiComplianceSeed` with `--demo` and no
   `--apply` against `sati-demo-satilogica-central.database.windows.net`, with
   `SATI_SQL_ACCESS_TOKEN` set from `az account get-access-token --resource
   https://database.windows.net/`. It changes nothing. Stop if it reports anything other than
   what a reset would normally complete. The capture freezes whatever is live, including the
   migration's own backfills.
2. Capture: `scripts/Initialize-DemoFullReset.ps1 -ReplaceBaseline`, keeping the default anchor
   (today). Anchoring to an earlier date would roll rows recorded since then into the future. Expect
   `DEMO_FULL_RESET_BASELINE_CAPTURED`. The capture replaces the previous baseline, which cannot be
   restored against the new schema anyway.
3. Queue one full reset: the Admin **Reset Demo** button, or a POST to the Function with its host
   key held only in memory. It answers 202 at once and runs for about four minutes.
4. Confirm the outcome: a `demo.reset.completed` audit event with `ResourceId` equal to the
   request id, and `DEMO_COMPLIANCE_HISTORY_COMPLETE` in the `ResetDemoWorker` log. A
   `demo.reset.failed` event names the failing stage. Stop and report it; the reset is never
   retried automatically.
5. Ask the user to remove the firewall rule, and confirm that it is gone.

Record the capture marker, the anchor date, the reset request id, and its outcome in the release
evidence.

After publication, verify:

- deployment success and deployment identifier;
- `/health/live` and `/health/ready`;
- `/health/version` product and release version; and
- client/API contract revision parity.

For asynchronous Kudu publication, HTTP 202 records acceptance, not completed deployment.
Temporary `temp-*` and `pending` status records are not permanent deployment identifiers. Keep
polling the accepted status URI, normally `/api/deployments/latest`, until a real, completed,
active deployment is identified. Do not replay an accepted upload to recover from a polling
failure. Kudu reserves `latest` for polling through completion and deletes temporary records;
see its [async controller](https://github.com/projectkudu/kudu/blob/master/Kudu.Services/Deployment/PushDeploymentController.cs)
and [temporary-deployment owner](https://github.com/projectkudu/kudu/blob/master/Kudu.Core/Deployment/DeploymentManager.cs#L337-L365).

Preserve deployment timestamps as ISO 8601 values with their UTC marker or explicit offset.
PowerShell 7.5 or later can use `ConvertFrom-Json -DateKind String` before explicit invariant
parsing and UTC normalization. If a reader produces `DateTime` or `DateTimeOffset` objects,
handle those typed values directly rather than reparsing their display strings, which can lose
the original offset and precision. See the [PowerShell date-conversion guidance](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/convertfrom-json?view=powershell-7.5#-datekind).

If polling fails after acceptance, retain that failed evidence. A separate read-only verifier may
write new evidence with `CreateNew`, preserving the original upload record. Tie verification to
the fixed Demo resource, pushed source, reviewed ZIP hash and real deployment identifier; check
that the completed active identifier remains stable around file and public health checks. When
checking deployed bytes, GET only the reviewed package's exact known paths and compare their byte
lengths and SHA-256 hashes; [Kudu's VFS API](https://github.com/projectkudu/kudu/wiki/REST-API#vfs)
supports those file reads. Record the limits: matching known files does not establish absence of
extra files, and timestamps plus matching bytes cannot distinguish another publisher deploying
identical bytes. No additional upload or rollback is implied by read-only verification.

Retain the prior known-healthy API package and deployment information. If verification fails, stop
downstream work and report the failure. Do not improvise a Production deployment or an unapproved
database action. Ask before redeploying a rollback package unless prior instructions explicitly
authorize that rollback.

## 7. Demo and Local installers

Build installers only after the matching API and source commit pass their release gates.

- Build the Demo installer with `installer/Build-DemoInstaller.ps1`.
- Build the Local installer with `installer/Build-LocalInstaller.ps1`, using the durable repository
  prerequisite only after verifying that `SqlLocalDB.msi` has a valid Microsoft signature.
- Reject Local configuration containing SQL usernames or passwords; it must use Windows integrated
  security.
- Refuse to overwrite an installer or checksum bearing the selected version.
- Run the Demo isolated acceptance test for five responsive launches, normal closes, exact installed
  version, and cleanup.
- Run the Local isolated acceptance test for exact version, embedded LocalDB signature, integrated
  security, and cleanup.

Pass a new `-EvidencePath` to both acceptance scripts. For example:

```powershell
$releaseVersion = (& .\scripts\Test-DattPreflight.ps1).ReleaseVersion
.\scripts\Test-DemoInstaller.ps1 -InstallerPath ".\artifacts\SatiDemoInstaller\SatiDemoSetup-$releaseVersion.exe" `
    -LaunchIterations 5 -EvidencePath ".\TestResults\datt-$releaseVersion-demo-installer-acceptance.json"
.\scripts\Test-LocalInstaller.ps1 -InstallerPath ".\artifacts\SatiLocalInstaller\SatiLocalSetup-$releaseVersion.exe" `
    -EvidencePath ".\TestResults\datt-$releaseVersion-local-installer-acceptance.json"
```

Both JSON records are written only after acceptance and cleanup succeed, with `CleanupPassed=true`.
Local acceptance reads the exact installer's embedded MSI and validates its Microsoft signature;
no separate inspection or repeated acceptance run is needed. `-KeepInstalledFiles` is a diagnostic
Demo option: its evidence explicitly records retained files and `CleanupPassed=false`, and cannot
satisfy the release cleanup gate. Never overwrite evidence from a prior run.

Record each final artifact's absolute path, byte size, and SHA-256 hash. Generated installers are not
assumed to be code-signed merely because the embedded Microsoft LocalDB prerequisite is signed.

### Distribution publication

Only after both installer acceptance gates pass, publish the final installer executables and their
generated `.sha256` files as follows:

- Local installer and checksum:
  `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`
- Demo installer and checksum:
  `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`

Resolve and validate both absolute destinations before writing. They must remain within
`C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents`. Create only the two exact destination
directories when one is missing; do not create a guessed or similarly named location.

Never overwrite a published file. If a destination filename already exists, compare its SHA-256
with the accepted artifact. Treat an identical file as already published. If the hashes differ,
stop and report the collision rather than replacing either file.

For a new destination file, copy to a uniquely named temporary sibling, verify the temporary copy's
SHA-256, and then rename it to the final versioned filename. Verify the final copy and checksum file
again after publication. On failure, remove only the exact temporary file created by this run; do
not remove an existing final file. Do not publish the API ZIP, LocalDB prerequisite, private
configuration, or any additional files to these folders.

## 8. Evidence commit and final report

Update the current `AGENDA.md` release section with:

- the pushed source commit identifier (report the resulting evidence commit identifier after push);
- test totals and any legitimate skips;
- API ZIP hash, deployment identifier, health status, release version, and contract revision;
- for a Demo migration: the baseline capture marker and anchor date, plus the verification reset's
  request id and outcome; and
- Demo and Local installer names, byte sizes, hashes, acceptance results, cleanup results, and
  verified distribution paths.

Complete the operational checklist, then make **one final evidence commit** and push it to the
resolved default branch. Confirm a clean working tree and equality with the remote branch, and
report the resulting evidence commit identifier in the final response. Do not create a second
closing-ledger commit merely to insert a commit's own hash into `AGENDA.md`. A blocked-release
checkpoint is appropriate when needed to preserve an interrupted rollout's state; it does not
replace the final evidence commit after recovery.

The final user report must lead with whether the release completed. Include the version, commits,
API verification, installer links and hashes, tests, branches merged or deleted, branches retained
because of uncertainty, and any unresolved warning or manual follow-up.

## Stop conditions

Stop before the next side effect and explain the blocker when any of these occurs:

- unexplained or overlapping user changes cannot be preserved safely;
- a branch has unique or ambiguous work;
- the remote default branch cannot be identified or changed unexpectedly;
- a merge conflict lacks an evidence-backed resolution;
- the version is ambiguous or an artifact already uses it;
- a build, required test, security check, or acceptance gate fails;
- documentation structure or readiness-report validation fails, including missing current-version
  coverage or changed prior snapshots/rubrics;
- a secret, credential, private configuration, or unrestricted narrative appears in an artifact or
  log;
- a cloud database migration or Production deployment would be required;
- the deployed API is unhealthy or reports the wrong version or contract revision;
- a required Demo migration is unauthorized, or reaching SatiDemo would need a firewall or other
  security setting the user has not already put in place;
- a Demo migration is authorized but the baseline recapture that must follow it is not, or the
  verification reset records `demo.reset.failed`;
- the LocalDB prerequisite is missing or its Microsoft signature is invalid;
- a distribution path resolves outside the named documents root, cannot be written, or contains a
  same-named artifact with a different hash; or
- required authority or access is unavailable.

Do not mark a partial release complete. Report what succeeded, what did not occur, and the smallest
next action needed.
