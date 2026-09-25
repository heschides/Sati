# Sati — Refactor Agenda

## Release 1.3.28 — 2026-09-25

“A visible start and room to come back.” Ships the two unreleased slices below: bounded,
visible workspace preparation after sign-in (with the Billing Overview aggregate and the narrow
billing candidate projections), and the close-time choice to keep unfinished work on today.

**No migration and no schema change.** No `Sati.Persistence/Migrations/` entry; the only
persistence-assembly change adds `Id` to the unmapped `PersonSummary` projection. New queries read
existing `Notes`, `ClaimLines`, `BillingPeriods`, and `Users` columns only. No Demo migration,
database access, or firewall rule is needed. **Contract change:** the new route
`GET /api/v1/billing/overview-periods/{year}/{month}` and the
`person-caseload-scheduled-notes-and-contact-facts-v1` shape change `ApiSurface.Revision` from
`A28B7894E416`, so the 1.3.28 Demo client requires the 1.3.28 Demo API; the API ships first.

- [x] Preflight: fetched `origin/master`; local `master` and `origin/master` both resolved to
      `831b3f7` with a clean tree and one linked worktree. No branch merge is needed and no branch
      is proven safe to delete. Retain the merged but uncertain `origin/codex/annual-compliance`
      and `origin/codex/clarify-project-notes`, and the divergent `origin/second-machine-setup`,
      `origin/team-chat-design`, and `origin/claude/local-vs-github-workflow-dlcqpb`. The separate
      SatiLogica-profile clone has no unpushed `master` work; its only commits absent from `origin`
      are on the retained `video-conferencing-design` branch.
- [x] Demo API healthy at 1.3.27, contract `A28B7894E416`, in `rg-sati-demo` under the expected
      subscription. No 1.3.28 API ZIP, installer, or checksum exists in `artifacts` or either
      distribution directory. Select patch version 1.3.28 and coordinate desktop/API versions,
      installer builders, readiness expectations, Settings release notes, runbook examples, and
      exact-version tests.
- [x] Full `--no-incremental` Release solution build: 0 errors, 17 analyzer warnings, all in
      files unchanged since 1.3.27 (EF1002 in `SqlLocalDatabaseMaintenance.cs`; xUnit2031,
      xUnit2013, and CS8604 in existing tests). 1.3.27's zero-warning record did not force a full
      rebuild. All solution test projects pass under the signed-in profile: desktop/domain 2,520
      passed with 6 documented external-prerequisite skips; API 901 passed with 5 skips;
      signatures 119, portal 8, and Carika 4 passed, for 3,552 passed and 11 skipped overall.
- [x] Source commit `a7e0f421ed3a332eb6e885361718fe003ec3dd4f` was pushed normally to
      `origin/master`.
- [x] Published only the matching Demo API. The 10,313,950-byte
      `artifacts/SatiApi-1.3.28-fx-x86.zip` has SHA-256
      `0B3DDE8F80D9AE3EC3F5130D277E0D597564568C988B98F1073DC7E415EEDF0E`, the same 68-file set
      as 1.3.27 (70 entries with the two `App_Data/jobs` folder entries, no backslash names), no
      settings files or secret-like text, and product version
      `1.3.28+a7e0f421ed3a332eb6e885361718fe003ec3dd4f`. OneDeploy deployment
      `02e6437e013142a1a52c5b6207b4a053` published it only to existing `sati-demo-api-satilogica`
      in `rg-sati-demo`; the prior active deployment `5a9aa2a077de4c8d9182e46211e9fbea` and the
      1.3.27 ZIP are retained. Liveness and readiness are healthy; `/health/version` reports
      Sati.Api 1.3.28 and contract revision `F52354A957F6`, equal to this build's
      `ApiSurface.Revision`. The new overview route and `/api/v1/settings` return 401
      anonymously. Health-only evidence is in `artifacts/release-1.3.28-demo-readiness.json`;
      authenticated checks remain skipped because synthetic Admin credentials are not configured.
- [x] Built and accepted both installers without overwrite. The 103,120,896-byte
      `SatiDemoSetup-1.3.28.exe` has SHA-256
      `A1A5F71F5053F81C14F3491E6B61954B2AD51CE31F4226DA4BC1F9E4E1781306`; five responsive
      15-second launches, exact version 1.3.28.0, graceful closes, and cleanup passed. Evidence is
      in `artifacts/release-1.3.28-demo-installer-acceptance.json`; this is a build-workstation
      test, not an external-machine attestation. The 205,165,609-byte `SatiLocalSetup-1.3.28.exe`
      has SHA-256 `F7853AB011C16928DE0AE1F3646E736834335AE8E5957BC21D83CC13DB31519F`; exact
      version, `SatiProduction`, Windows integrated security, and cleanup passed. Its embedded
      prerequisite is the durable Valid Microsoft-signed `SqlLocalDB.msi`, 63,508,480 bytes,
      SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`, read from
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\Prerequisites`. The four-case
      packaged matrix proved that each installer refuses both `Sati` process names with inner exit
      code 2 and leaves its isolated destination unchanged (the Demo IExpress wrapper reports 0).
      The acceptance run began with no Sati process open; none was closed by the workflow.
- [x] Published only those accepted installers and their checksum files by verified temporary copy
      and rename. Final hashes and checksum contents match in
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop` and
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`; no
      temporary files remain.
- [x] This release-evidence commit was pushed normally, and local `master` was confirmed equal to
      `origin/master` with a clean working tree.
- [ ] Manual smoke test of the handoff checklist on Demo with real sign-ins (preparation window
      after login, first render, mixed and Billing-only landings, first-use loads, account
      switching) — now possible because the Demo API and client are both 1.3.28.

### Local Production machines

No schema change, so no machine is newly behind on migrations because of this release. The
1.3.27 records still stand: each machine's real `SatiProduction` receives migration 117 only when
1.3.27 or later is installed and launched.

- [ ] **Joshu workstation.** Observed 2026-09-25: installed Local client is
      `1.3.27+0abd021` and was running from 11:43 that day, so it has launched at 1.3.27 at least
      once. Migration 117's application to its `SatiProduction` was not independently inspected.
      Installing 1.3.28 adds no migration.
- [ ] **SatiLogica workstation / colleague installation.** Latest durable record is 1.3.2; treat
      it as behind.

## Unreleased — visible, bounded workspace preparation (2026-09-24)

- [x] Replace the blank post-login interval with an accessible themed preparation surface carrying
      the exact status “We are preparing the Sati workspace.” Keep it visible through the shell's
      first render and close it on every success/failure path.
- [x] Remove the artificial three-second pre-login splash delay.
- [x] Ship 100 original, nonrepeating Sati reflections under a narrowly scoped CC0-1.0 dedication;
      rotate slowly without making the prose a screen-reader live region or using a network feed.
- [x] Remove constructor-triggered Billing work. Load Billing Overview once on first Billing entry,
      including for billing-only accounts, and clear the cache at the account boundary.
- [x] Replace the Local billing Forms × Releases join and the Local/API whole-note candidate reads
      with narrow scalar candidate projections plus independent compliance graphs. Keep the shared
      billing rules authoritative. No migration is required.
- [x] Replace Billing Overview's lifetime period/claim-line graph with a tenant-scoped aggregate:
      all draft charge value plus exactly six monthly totals. Keep full period history behind the
      detailed Billing tabs and cover Local/API scope, payload, and query shape.
- [x] Defer Supervisor team-summary work unless Supervisor is the landing workspace or is opened;
      coalesce first use, reject late account results, and rebuild cached charts without refetching.
- [x] Prepare one caseload at startup, project note summaries without narrative or visit JSON, and
      publish that snapshot to Overview, Clients, and Notes Log instead of issuing three caseload
      loads plus the Notes Log's per-consumer fanout before the shell appears.
- [ ] Replace the first-use Notes Log sequential per-consumer reads with a bounded server-side page
      contract. Use a request body for clinical search text, stable `EventDate DESC, Id DESC`
      ordering, cancellation/latest-request protection, tenant-derived scope, aggregate attention
      counts, and Load More. First navigation is lazy now, but its full result remains unbounded.
- [ ] Bound Billing Overview's exact ready/blocked analytics for agencies with unusually large
      approved-unbilled backlogs, and share that result with the Queue so first Queue navigation
      does not repeat candidate discovery. Preserve authoritative per-note validation and expose
      whether the Overview count is complete rather than silently truncating financial totals.
- [ ] Batch provider-assignment and release-reconciliation inputs once per caseload instead of
      querying them for each person/target pair. Preserve the current pure reconciliation rules,
      exact provider identity, transaction boundary, and audit output.
- [ ] Project the remaining caseload profile row so Local startup does not select the unbounded
      `Person.Journal` column and the API does not materialize encrypted SSN envelope fields. Bio and
      full editable profile data should move behind an explicit selected-client/detail boundary.
- [ ] Capture representative cold/warm Local and hosted Demo timings with a production-shaped
      synthetic caseload before setting a startup service-level target. Query-shape regressions now
      prevent the known overfetch, but a workstation-sized test is not an Azure latency benchmark.

## Unreleased — keep unfinished work on today at shutdown (2026-09-25)

- [x] Add a third close-time choice for leftover Scheduled work: keep it on today, unfinished, for
      a case manager who closes Sati and returns the same day. Today's items are left untouched;
      a past day's item is brought forward to today with its old start time cleared. "Move to the
      next workday" stays the default, and "Keep all on today" sets every row. Writes still go
      through `INoteService`. No migration, route, or contract change. See `DECISIONS.md`
      (2026-09-25).
- [x] Add a Settings release-notes entry for this choice when the next release is cut (1.3.28).

## Release 1.3.27 — 2026-09-24

“Safer updates, cleaner agendas, and larger calendars.” This patch carries forward
the exact-form agenda repair, safer form-date attestation, streaming calendar
import, and four-level Legacy Dark palette staged in 1.3.26. It also makes both
installers fail closed before changing application files or prerequisites when
either Sati edition is running: close Sati, then run the installer again.

**Data-only migration; no schema expansion.**
`20260923180000_ReconcileDuplicateScheduledAgendaNotes` retains and cancels only
proved redundant Scheduled rows and appends minimized audit events. Because the
1.3.25 Demo client can still submit the old retry shape, the fixed API and accepted
1.3.27 Demo installer must be published before the one-time Demo repair; all known
old Demo sessions must then be closed. The repair uses a hash-pinned, identity-
checked runner with rollback rehearsal and a no-op rerun. Local `SatiProduction`
receives the migration only when each machine first launches the new Local client.

- [x] Preflight: fetched `origin/master`; local `master` and `origin/master` both
      resolved to `37859ef` before the 1.3.27 source changes. The current worktree is
      the only linked worktree and
      contains the reviewed release scope. No branch merge is needed and no branch
      is proven safe to delete. Retain the merged but uncertain Codex branches and
      the divergent setup, chat-design, and Claude branches. The previously recorded
      video-conferencing branch is absent after fetch.
- [x] Retain the already built 1.3.26 artifacts without overwrite and select the
      collision-free patch version 1.3.27. Coordinate desktop/API
      versions, installer builders, readiness expectations, Settings release notes,
      runbook examples, and exact-version tests. The two distribution directories
      exist under the required documents root and contain no 1.3.27 collision.
- [x] Verify the current Demo API is healthy at the staged 1.3.26 and the Azure
      identity is the expected subscription, tenant, SQL server, database
      `SatiDemo`, and `Demo` database marker. Migration history is at the exact
      reviewed predecessor: 116 rows ending with
      `20260922191918_SupportReleaseAttestationReviewFlags`.
- [x] Verify the shared running-process guard detects both `Sati` and `Sati.Demo`,
      fails closed on inspection errors, runs before LocalDB and again immediately
      before file changes, never terminates the application, and has a four-case
      isolated packaged-refusal harness ready for the new installers.
- [x] Complete the full Release solution build with zero warnings and errors. All
      solution test projects pass under the signed-in profile: desktop/domain 2,495
      passed with 6 documented external-prerequisite skips; API 898 passed with 5
      skips; signatures 119, portal 8, and Carika 4 passed, for 3,524 passed and 11
      skipped overall.
- [x] Source commit `0abd02123600787df27d0e2d26194b5242721fe3` was pushed
      normally to `origin/master`. The 10,445,021-byte API package
      `artifacts/SatiApi-1.3.27-fx-x86.zip` has SHA-256
      `758E37A2BEED5304C32BBEE15124D715AEF0764C1FF7381DD5BEC3CCE52593F0`,
      68 intended files, no private settings or secret-like text, and product
      version `1.3.27+0abd02123600787df27d0e2d26194b5242721fe3`. OneDeploy
      operation `5a9aa2a077de4c8d9182e46211e9fbea` published it only to the
      existing Demo API. Liveness and readiness are healthy; `/health/version`
      reports Sati.Api 1.3.27 and contract revision `A28B7894E416`, equal to the
      desktop contract. Health-only evidence is retained in
      `artifacts/release-1.3.27-demo-readiness.json`; authenticated checks remain
      skipped because designated synthetic Admin credentials are not configured.
- [x] Build and accept both installers without overwrite. The 103,104,512-byte
      `SatiDemoSetup-1.3.27.exe` has SHA-256
      `DC06EEB7292367CAF5381435FCF18612A720008993A7B6BC004CB84FBD5A98B3`;
      five responsive 15-second launches, exact version 1.3.27.0, normal closes,
      and cleanup passed. Evidence is in
      `artifacts/release-1.3.27-demo-installer-acceptance.json`; this is a build-
      workstation test, not an external-machine attestation. The 205,143,593-byte
      `SatiLocalSetup-1.3.27.exe` has SHA-256
      `1122A36E4AE036C41EF6E59030C5526DBFAFC555E2790572AF0BBBA3602811C5`;
      exact version, `SatiProduction`, Windows integrated security, and cleanup
      passed. Its Microsoft-signed LocalDB prerequisite remains 63,508,480 bytes,
      SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      The four-case packaged matrix proved that each installer refuses both `Sati`
      process names with exit code 2 and leaves its isolated destination unchanged.
- [x] Publish only those accepted installers and their checksum files by verified
      temporary copy and rename. Final hashes and checksum contents match in
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`
      and
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
- [x] With every Sati client closed and the user-added exact-IP rule, the controlled
      SatiDemo runner passed its rollback rehearsal, committed migration history row
      117, and passed the no-op rerun. All three runs found zero eligible groups,
      cancellations, or audit additions. Post-migration API liveness and readiness
      remain healthy at 1.3.27; health-only evidence is retained in
      `artifacts/release-1.3.27-post-migration-readiness.json`. The user removed
      `datt-workstation-temp`; a read-only verification found zero temporary or
      unexpected rules, with only 3 dedicated Demo API and 30 dedicated Demo refresh
      exact-IP rules remaining. The release workflow did not create, change, or
      delete that security rule.
- [x] Release evidence commit `92d099e1e5a7f7ec1fd03b1f425b1e7ba6c91e91`
      was pushed normally. The checklist-closing commit was then pushed and final
      local `master` confirmed equal to `origin/master` with a clean working tree.

### Local Production machines

- [ ] **Joshu workstation.** Installed Local client is 1.3.25. Treat its real
      `SatiProduction` as pending migration 117 until 1.3.27 is installed and a
      successful backed-up startup is verified.
- [ ] **SatiLogica workstation / colleague installation.** Latest durable record is
      1.3.2 and has not been rechecked during this release. Treat both its installed
      version and Local migration state as behind rather than assuming it caught up.

## Superseded staging build 1.3.26 — 2026-09-24 (not distributed)

Release 1.3.26 was staged from pushed source commit
`37859ef081e97a71713530443db88d5f64ab5a26`. Its Release build completed with
zero warnings and errors. Tests passed: desktop/domain 2,493 with 6 documented
external-prerequisite skips; API 898 with 5 skips; signatures 119; portal 8; and
Carika 4, for 3,522 passed and 11 skipped overall.

The 10,445,086-byte API package `SatiApi-1.3.26-fx-x86.zip` has SHA-256
`ED051AF690F0201974F59941622933FC0F62B1E9B77A06B409591F8642AFB724` and was
deployed only to the existing Demo API by OneDeploy operation
`cdb402d3123949d38a9971372badac3f`. Liveness, readiness, version 1.3.26, and
contract revision `A28B7894E416` parity passed; health evidence is retained in
`artifacts/release-1.3.26-demo-readiness.json`. The 103,088,128-byte Demo
installer has SHA-256
`45AB6B9C65203F4E1306C47E533415D77CBC3E73A51D5E92AD31ABAEE0705732`; the
205,141,545-byte Local installer has SHA-256
`9F976C7AD61E63099EB3AB60735D7AB8AC5ED7B260DB5B44DC153D076A5BE352`.
Local acceptance passed, but Demo acceptance could not complete while an older
Demo client remained open. Neither installer nor checksum was copied to either
distribution folder, and the data repair was not applied. Those artifacts are
retained unchanged as evidence and must never be rebuilt or overwritten under
version 1.3.26; 1.3.27 supersedes them because its installer behavior is a source
change.

## Carried into 1.3.27 — Work Agenda exact-form duplicate repair (2026-09-23)

The 1.3.23 exact-form-link transition exposed a compatibility gap: older
Scheduled Work Agenda notes intentionally retained `FormId = NULL`, but a
later sign-in retry looked only for the newly available exact `FormId`. That
made a second Scheduled row for some carried-forward Reclassifications and
Reviews. This work is not part of the already published 1.3.25 release.

- [x] Diagnose Local Production read-only. Forms retain their unique index and
      have no duplicate obligation groups. Affected Scheduled-note groups share
      the transition signature: exactly one unlinked carried-forward row and one
      or more exact-linked rows for the same Form, with identical person,
      date, form type, generated narrative, and untouched workflow state. The
      observed rows are not claim, recovery, review-flag, or attestation evidence.
- [x] Make exact `FormId` the durable retry identity, independent of generated
      agenda wording. Keep a narrow full-narrative compatibility match for an
      older null-`FormId` row; null is not a wildcard, and a different exact
      form or a release-linked row remains separate. Reject an impossible item
      that carries both form and release identity before writing anything.
- [x] Add data-only migration
      `20260923180000_ReconcileDuplicateScheduledAgendaNotes`. It cancels only
      redundant rows in an unambiguous default 15-minute fan-out: one legacy
      null-link and one or more byte-identical rows linked to the same exact Form.
      The lowest-ID exact-linked row remains Scheduled; the legacy row and all
      other exact-linked copies become Cancelled, increment revision, and each
      receive a PHI-minimized system audit event. The repair does not depend on a
      particular prior revision, never deletes or invents a historical `FormId`,
      and skips multiple-form, multiple-legacy, referenced, evidenced, or
      meaningfully audited groups. Candidate discovery is independent of row-ID
      order so an interleaved extra legacy row or different exact Form cannot be
      hidden from the ambiguity checks.
- [x] Add regression coverage for retry after the nullable-link upgrade,
      narrative changes on an exact form, different exact forms, unrelated
      legacy/release rows, invalid mixed identity, migration guards, migration
      registration, and generated SQL.
- [x] Keep the retained Cancelled rows from becoming a new manual-attestation
      ambiguity. Only a Cancelled exact-linked note carrying this migration's
      dedicated audit marker is excluded from evidence resolution; every ordinary
      cancelled or live linked note remains a conservative conflict, and an
      explicit attempt to cite a retired duplicate is refused.
- [x] Normalize the seeded Privacy Practices template to LF in both its shared
      source and historical seed migration. EF fingerprints seed values before
      applying pending migrations; platform-dependent source line endings made
      this Windows clone report a phantom model change and would have blocked
      the otherwise valid data repair. The wording is unchanged.
- [x] Rehearse the repair and the full startup updater against disposable
      LocalDB databases. The repair cancels only the proved redundant members,
      skips ambiguous and evidenced groups, writes one minimized audit event per
      cancellation, and the complete pending chain applies without a model-drift
      refusal.
- [ ] Ship through the normal release path, allow the identity-checked Local
      Production startup updater to apply the migration, compare the preflight
      candidate and cancellation counts, and verify that each group retains only
      its lowest-ID exact-linked Scheduled row. No production row has been changed
      during this source fix.
- [ ] Replace the remaining read-before-add boundary with an authoritative
      atomic ensure operation before claiming cross-process uniqueness. The
      current matcher makes ordinary retries idempotent but cannot close a race
      between simultaneous writers.
- [ ] Carry one durable recipient-release identity through the agenda and Note
      boundaries. The agenda currently exposes a GUID while `Note` stores a
      different long key, so release items retain conservative narrative
      matching until that contract is deliberately reconciled.

## Carried into 1.3.27 — Exact-form completion, calendar imports, and Legacy Dark (2026-09-24)

- [x] Let an exact Logged non-release Form activity satisfy its own ordinary
      service-date compliance preflight while preserving every unrelated blocker;
      local/API persistence writes the exact attestation before repeating the gate
      in the same transaction.
- [x] Require explicit cycle disambiguation when an older renewal-overlap target
      is selected after a later incomplete same-type renewal becomes available.
      Manual attestation refuses; the exact linked-note path requires written
      justification, remains nonbillable, and records a minimized old/new Form
      audit without moving evidence.
- [x] Replace the eager whole-file `.ics` parser and its 20 MiB guard with a
      streaming parser. Unsupported bulk fields such as `DESCRIPTION` and
      `ATTACH` are discarded; bounded supported properties, retained event text,
      a 50,000-event output ceiling, and a 512 MiB total-input safety ceiling
      remain. This is client-local and requires no database migration.
- [x] Re-layer Legacy Dark into the requested four visual depths: Bone editable
      fields, lighter Sienna shell/navigation chrome, darker Black Bean cards,
      and the deepest Black Bean inset editors. The primary shell navigation now
      consumes `NavBackgroundBrush` rather than sharing the scratchpad's
      `SurfaceAltBrush`; automated checks enforce both that binding and the
      complete light-to-dark luminance order while retaining the WCAG AA sweep.

## Release 1.3.25 — 2026-09-23

“Clearer form dates and Legacy Dark controls.” This patch distinguishes a
Scheduled note's planned date from the date form work was completed. A short
confirmation reuses that exact note as a Pending draft and records the form
attestation. It also refreshes the selected client's form state without a
restart and gives Legacy Dark light input controls with dark text.

**No schema migration.** The four migrations from 1.3.24 are recorded as
applied to Demo. This patch adds no migration or dependency on a new column;
no temporary SQL firewall rule is needed. Local database migration status
still depends on the version each Windows login has installed and launched.

- [x] Preflight: fetched `origin/master` at `52fc218`, confirmed it is the
      default and local master matches. Reviewed in-scope changes and found no
      1.3.25 artifact collision. Retained the active Claude worktree and older
      or separate branches; none was merged or deleted. The Microsoft-signed
      LocalDB prerequisite remains valid.
- [x] Coordinate version 1.3.25 in desktop/API projects, installer builders,
      readiness checks, Settings release notes, and installer examples.
- [x] Full Release build passed with 0 warnings and 0 errors. Under the signed-in
      Windows profile, all solution test projects passed: desktop 2,463 passed,
      4 skipped; API 894 passed, 5 skipped; signatures 119 passed; portal 8
      passed; Carika 4 passed. The skips are existing external-prerequisite
      tests. The restricted sandbox account cannot run DPAPI or write the
      Local billing export folder, so it was not used for the release gate.
- [x] Source commit `d85b0f2` pushed normally to `origin/master`. No branch was
      merged or deleted. Retained active `claude/cool-jang-f6b3c4` and the
      separate or uncertain `second-machine-setup`, `team-chat-design`,
      `video-conferencing-design`, `origin/claude/local-vs-github-workflow-dlcqpb`,
      `origin/codex/annual-compliance`, and
      `origin/codex/clarify-project-notes` branches.
- [x] Published only the existing Demo API resource `sati-demo-api-satilogica`
      in `rg-sati-demo`. `artifacts/SatiApi-1.3.25-fx-x86.zip` was built from
      `d85b0f2`, is 10,428,274 bytes, has SHA-256
      `AF91851334B4B98F095063A11A656C06FD212A0560D6F2178855C85EB89718DB`,
      and contains 68 intended files without private settings. Its API file
      version is 1.3.25.0. OneDeploy deployment
      `7c99095ff63d47308e72df5f43b49437` succeeded. `/health/live` and
      `/health/ready` returned HTTP 200; `/health/version` reports Sati.Api
      1.3.25 and contract revision `A28B7894E416`, equal to the desktop
      `ApiSurface.Revision`. Health-only evidence is in
      `artifacts/release-1.3.25-demo-readiness.json`; authenticated checks
      were skipped because synthetic Admin credentials were not configured.
      The 1.3.24 package and deployment information remain retained.
- [x] Built, accepted, and published both new installers without overwriting.
      `SatiDemoSetup-1.3.25.exe` is 103,071,744 bytes, SHA-256
      `697B8F5EF735E80A0368D8E67B161B8B1596486BDAB3B3B3985D3FB0BD0B4AE5`;
      five responsive 15-second launches, graceful closes, exact installed
      version 1.3.25.0, and cleanup passed. Evidence is in
      `artifacts/release-1.3.25-demo-installer-acceptance.json`; this is a
      build-workstation test, not an external-machine attestation.
      `SatiLocalSetup-1.3.25.exe` is 205,123,113 bytes, SHA-256
      `988819C45225A59C4D2DF1AC1982623E36847E323CF76E9F47BF1B74782B0CBA`;
      version 1.3.25.0, `SatiProduction`, integrated security, Microsoft-signed
      embedded LocalDB, and cleanup passed. The signed LocalDB prerequisite's
      SHA-256 is `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      Hash-verified installers and `.sha256` files are in the exact
      `SatiLogica Demo Files` and `Sati Desktop` distribution folders under
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents`.
- [x] Final evidence commit pushed normally; its identifier and clean
      local/remote equality are confirmed in the DATT handoff.
- [ ] Joshu LocalDB: verify the actual PCP and Safety Plan note links and
      attestation state after installing this build. That database is under a
      different Windows login, so this release uses synthetic local/API
      reproductions and does not claim record-specific verification.
- [ ] Local Production install tracking: SatiLogica's client was last observed
      at 1.3.2.0; Joshu reported 1.3.24.0. Recheck each login after install
      before claiming its local database has caught up.

## Included in 1.3.25 — PCP attestation feedback (2026-09-22)

- [x] Joshu's 1.3.24 record has a Scheduled PCP note dated 09/22 and a Pending
      PCP note dated 09/21. The Pending note refuses Logged status because it
      lacks the exact form-obligation selection. In 1.3.24 that selection is an
      unlabeled second dropdown below Form Type, and the note panel converts the
      validation exception into a generic Save Error. Label the dropdown,
      explain the exact selection in the validation message, and surface expected
      save errors in the note panel. Preserve the existing nonbillable hold path;
      a full desktop regression run passed (2,457 passed, 4 existing skips).
- [x] Reviewed the Joshu screenshot: the 09/21/26 PCP and Safety Plan remain
      unattested while Privacy Practices was attested for that same plan year.
      Safety Plan has no Evergreen confirmation or separate prerequisite. A
      synthetic Logged, unlinked Safety Plan note reproduces the same refusal
      in the newer note-link workflow (introduced in 1.3.23) and now produces
      visible correction guidance. Joshu confirms 1.3.24 is installed. The
      record-specific cause remains unverified until the note dates, statuses,
      and exact form links are known. A clean-database reproduction shows that
      a Privacy Practices attestation itself does not block PCP or Safety Plan.
- [x] Traced the later Safety Plan note date: a selected sign-in agenda item
      creates a persisted Scheduled form note, and the shutdown leftover-work
      prompt defaults to moving Scheduled notes to the next workday when the
      user applies it. This explains how a note can reach 09/23 without the user
      writing a clinical note. Moving the plan does not attest completion.
      The user changed the scheduled note's date to 09/21 and logged it; the
      client panel then needed a restart to display the new form state. The
      actual Joshu note's creation and move history is inaccessible here.
- [x] Reproduced a PCP attestation refusal with a synthetic Logged note that has
      the right form type but no exact `FormId`. The duplicate-note guard correctly
      leaves the form unattested; the shared PCP/assessment UI had hidden its error
      because the error text was inside the Reclassification-only panel.
- [x] Show attestation errors for every form, explain when the Evergreen confirmation
      is still required, clear stale error text before retrying, and show expected
      API and validation errors. Give a concrete correction route for older unlinked
      form notes without silently assigning them to an obligation.
- [x] Correct the effective-date boundary in desktop and API duplicate-note checks,
      pending note-to-form matching, and the attestation's note evidence. A PCP note
      dated exactly on the effective date now belongs to the obligation due that day;
      a manual attestation cites its linked draft, and an older unlinked note cannot
      silently produce a second draft. The full desktop suite passed again after
      the note-editor changes (2,457 passed, 4 existing skips); the full API suite
      passed (893 passed, 5 existing skips).
      No client data or Joshu LocalDB was accessed from the SatiLogica login.
- [x] Josh confirmed PCP and Safety Plan work were actually completed on 09/21;
      the Safety Plan note was unwritten when its Scheduled date moved forward.
      A Scheduled date is a plan, not completion evidence. Give a distinct
      explanation when its later planned date meets a 09/21 checkmark.
- [ ] On the Joshu login, confirm the Evergreen checkbox state and the notes'
      exact form links before treating the specific record as corrected. The
      synthetic regression tests establish the general behavior only.

## Release 1.3.24 — 2026-09-22

“Notes and attestations agree.” This release lets one note describe several activities,
ties manual form and release checkmarks to exact note evidence, gives Admin an audited
source-date correction path after a claim line exists, and adds the Legacy Dark theme.

**Schema and API contract change.** The four September 22 migrations are already applied
to identity-checked `SatiDemo` under separate explicit authorization. The rollback rehearsal,
live apply, idempotency rerun, and existing API readiness check passed. The temporary
workstation firewall rule was removed and verified absent. Local Production receives these
migrations only when its new client is installed and launched. On this login the
SatiLogica workstation's installed Local client is 1.3.2.0; the Joshu workstation was
last known to be on 1.3.14.0 and has not been inspected from this login. Neither
Local Production database is claimed to have received this release.

- [x] Preflight: fetched `origin/master`, confirmed local `master` equals it at `68b8aee`,
      reviewed the in-scope dirty worktree, and found no 1.3.24 artifact collision.
      No branch was merged or deleted: the Claude worktree is active and the other
      branches contain separate or uncertain work.
- [x] Coordinate version 1.3.24 in desktop/API projects, installer defaults,
      readiness expectations, Settings release notes, and installer examples.
- [x] Full Release solution build passed with no warnings. Tests passed: Sati 2,449
      (4 external opt-in skips), API 891 (5 external opt-in skips), Signatures 119,
      Portal 8, Carika 4; 3,471 passed overall and 9 documented skips.
- [x] Source commit `cf052df` pushed to `origin/master` normally. No branch was merged
      or deleted. Retained `claude/cool-jang-f6b3c4` (active worktree),
      `second-machine-setup`, `team-chat-design`, `video-conferencing-design`,
      `origin/claude/local-vs-github-workflow-dlcqpb`, `origin/codex/annual-compliance`,
      and `origin/codex/clarify-project-notes` (separate or uncertain work).
- [x] Published only the matching Demo API. `artifacts/SatiApi-1.3.24-fx-x86.zip`
      is 10,425,655 bytes, SHA-256
      `715C5905A6B3D752E6166E7A91183CB5BC39E2F5C15BEFC9364ABE7646B6D14E`,
      with 68 intended files, no private settings, and file version 1.3.24.0.
      OneDeploy to existing `sati-demo-api-satilogica` in `rg-sati-demo` succeeded
      (deployment `4d0edf3743e1446180b066da745cbe5c`). Live and ready returned
      HTTP 200; `/health/version` reports Sati.Api 1.3.24 and contract revision
      `A28B7894E416`, equal to this source's desktop `ApiSurface.Revision`.
      Health-only readiness passed and wrote `artifacts/release-1.3.24-demo-readiness.json`;
      authenticated checks were skipped because synthetic Admin credentials were not
      configured. The 1.3.23 API package and deployment information remain retained.
- [x] Built, accepted, and distributed new installers without overwriting. The embedded
      `SqlLocalDB.msi` source has a Valid Microsoft signature and SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      `SatiDemoSetup-1.3.24.exe` is 103,067,648 bytes, SHA-256
      `34F5ACC0D6FFE7C98ADB566E442FF62EB6FAAD429F9F70202852CC76CF7F4700`;
      five responsive 15-second launches, graceful closes, exact installed version
      1.3.24.0, and cleanup passed. Acceptance evidence is in
      `artifacts/release-1.3.24-demo-installer-acceptance.json`.
      `SatiLocalSetup-1.3.24.exe` is 205,402,153 bytes, SHA-256
      `20AEE2C3226BCC0AF6B64874A358288B52149BFDAA04103E546A387167019B24`;
      version 1.3.24.0, `SatiProduction`, integrated security, signed embedded
      LocalDB, and cleanup passed. These are build-workstation acceptance runs.
      The installers and their `.sha256` files were published by verified temporary
      copy and rename to `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`
      and `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`,
      respectively. Win32 resolved-path checks confirmed both cloud-placeholder
      directories remain within the named documents root; final hashes match.
- [x] This final evidence commit was pushed normally; the commit identifier and
      clean local/remote equality are confirmed in the DATT handoff.
- [ ] Local Production: the SatiLogica login has installed client 1.3.2.0; the Joshu
      login was last observed on 1.3.14.0. Install and launch 1.3.24 on each machine
      when its operator chooses, then verify its local migration and readiness state.

## Included in 1.3.24 — Multi-activity notes (2026-09-22)

- [x] Replace work-type radio buttons with independent Visit, Phone, Email, Form, and Other checkboxes; keep Reminder exclusive.
- [x] Preserve historical note types while storing new activity flags in a nullable column and API contract.
- [x] Apply exact-form attestation and late-form billing holds to an entire mixed note; count mixed contact notes once.
- [x] Applied `20260922154932_AddMultiActivityNotes` to identity-checked `SatiDemo`
      through the separately authorized, rehearsed release migration process.

## Included in 1.3.24 — Checkmark attestation and existing-note reconciliation (2026-09-22)

- [x] Before a manual form or recipient-specific release attestation, search for an exact linked note. A matching activity date links that note as evidence and creates no extra note; a date conflict blocks the checkmark and shows the correction route.
- [x] If no note exists, save the manual attestation and a linked, nonbillable draft note atomically. A draft is not submitted to a supervisor or billing until the user finishes it.
- [x] Preserve append-only correction history: revoke an incorrect attestation with a reason rather than deleting it. Never guess among multiple candidate notes.
- [x] Permit a supervisor to return a submitted or approved note for correction only while no billing claim line exists; require an audited Admin/claim-correction path once a claim line exists.
- [x] Add exact note-to-release-obligation identity and a release-attestation correction path. The existing withdrawal action records authorization ending; it is not a correction to an erroneous completion date.
- [x] Cover local and API paths with matching, conflict, authorization, concurrency, audit, and billing tests before release.

## Release 1.3.23 — 2026-09-22

“Form dates and billing follow the work recorded.” This patch makes annual assessment renewals
identify the correct plan year and binds a Logged form-work note to one exact obligation. The note's
activity date becomes its attested completion date. Late form work remains clinically reviewable but
is not billable; an Admin technical-error correction records a reason and source evidence. Changed
attestations flag downstream supervisor and billing review. Saved forms and their note links remain
retained, with a visible explanation in the form screen.

**Schema and API contract change.** `SatiDemo` received the two September 21 additive migrations
under separate user authorization: rollback rehearsal, real apply, and idempotency rerun passed.
The temporary exact-IP firewall rule was removed and verified absent. The existing Demo API remained
ready after migration. Each Local Production installation receives these migrations only when that
machine launches the new Local client; the Joshu workstation is still known to be on 1.3.14.

- [x] Preflight: fetched `origin/master`, confirmed local `master` equals the remote at `b2d0b54`,
      reviewed the pending compliance change set, and found no 1.3.23 artifact collision. No branch
      merge or deletion: the Claude branch has an active worktree; setup, chat, video, and remote
      Claude branches have separate work; the merged remote Codex branches are not proven disposable.
- [x] Coordinate 1.3.23 desktop/API versions, installer and readiness defaults, Settings notes,
      examples, and release expectations.
- [x] Complete the full Release solution build and all solution test projects. The final build has
      zero warnings and zero errors. Desktop/domain: 2,440 passed, four documented opt-in skips;
      API: 886 passed, five documented opt-in skips; signatures: 119 passed; portal: eight passed;
      Carika: four passed. All 27 focused migration-boundary/theme legibility checks passed.
      The first full run found stale test expectations, two dark-theme labels, and a shared API
      fixture collision; these were corrected and the affected complete suites passed.
- [x] Commit and push verified source normally. Source commit `f4afb63` is present on
      `origin/master`; no force or history rewrite was used.
- [x] Publish only the matching Demo API. `artifacts/SatiApi-1.3.23-fx-x86.zip` is 10,358,612
      bytes, SHA-256 `5A75A2D7A995D3D78B67668482A91ECEC58876D0378E11A3FF0950E63557B400`,
      with 68 intended files, no backslash entry names or private settings, and file version
      1.3.23.0. OneDeploy to the existing `sati-demo-api-satilogica` in `rg-sati-demo` succeeded
      (deployment `80a08c63e9154f1f99665bb615be4849`). Live and ready return HTTP 200;
      `/health/version` reports Sati.Api 1.3.23 and contract revision `B62F9B778B49`, which
      equals the API publish and desktop client's `ApiSurface.Revision`. Health-only readiness
      passed and wrote `artifacts/release-1.3.23-demo-readiness.json`; authenticated checks were
      skipped because synthetic Admin credentials were not configured. The 1.3.22 package and
      deployment remain the prior known-healthy release.
- [x] Build, accept, and distribute both installers without overwriting. The embedded
      `SqlLocalDB.msi` source is Valid Microsoft-signed, SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      `SatiDemoSetup-1.3.23.exe` is 103,010,304 bytes, SHA-256
      `813D90E7119A85C8C6D8DAA8931D752DDB1B0A2C82FA96975CFC9952F03775FD`;
      five responsive 15-second launches, graceful closes, version 1.3.23.0, and cleanup passed.
      `SatiLocalSetup-1.3.23.exe` is 205,058,601 bytes, SHA-256
      `C25021F0959546D40146243D93073C284AF40751C00AEE9D9A7CFA4302C4B8F9`;
      version 1.3.23.0, `SatiProduction`, integrated security, signed embedded LocalDB, and
      cleanup passed. These are build-workstation acceptance runs, not clean external-machine
      attestations. Both accepted installers and their `.sha256` files were published with
      temporary-copy and final SHA-256 checks to the exact `SatiLogica Demo Files` and
      `Sati Desktop` distribution folders, respectively. No destination was overwritten.
- [x] Commit and push final release evidence normally; report the evidence commit in the final
      handoff and verify clean local/remote equality after the push.
- [ ] Local Production (Joshu login): install and launch the new Local installer when the operator
      chooses; until then its version and pending local schema changes remain unverified.

## Unreleased — annual compliance and billing correction (2026-09-14)

- [x] Prepare `scripts/Apply-FormNoteAttestationMigrations.ps1` for the two September 21
      migrations. The first rollback-only `SatiDemo` rehearsal exposed a SQL Server multiple-
      cascade-path conflict in the proposed Notes-to-Forms `SET NULL` foreign key; the transaction
      rolled back, and read-only inspection confirmed no new column, table, or history row. The
      relationship now uses `NO ACTION`, preserving exact note evidence. Its Demo identity and
      schema checks, rollback-only path, real apply, idempotency rerun, and mismatched-index
      refusal then passed on an isolated scratch LocalDB with the relevant person cascade paths;
      the scratch instance and files were removed. Under separate user authorization on
      2026-09-22, the corrected `SatiDemo` rollback rehearsal, real apply, and idempotency
      rerun succeeded. Migration history has 112 rows, both new migrations are recorded,
      and the review-flag table has zero rows. The operator removed the temporary exact-IP
      firewall rule; its absence was verified. The existing Demo API's `/health/ready`
      still returned HTTP 200 after the additive schema change. The matching API/client
      shipped with 1.3.23.
- [ ] Deferred: design an Admin-only, audited source-schedule correction for an erroneous
      `Person.EffectiveDate`, `Form.TargetEffectiveDate`, or stored `Form.DueDate`. It must preview
      all affected annual forms and recipient-specific releases; preserve form, note, claim,
      attestation, and audit history; detect conflicting annual identities; and flag supervisor
      and billing review for submitted or finalized work. A form's target is its stable identity,
      while its stored due date controls reminders and billing. The ordinary client edit now
      refuses an effective-date change when obligations exist. The September 2026 example is not
      evidence of a wrong default deadline: December 16 minus 90 calendar days is September 17.

This is the active compliance work list. It supersedes the incompatible due-date-inferred cycle,
born-complete generation, artifact prerequisite, mutable billing-mask, due-day-blocked, and
signature-never-attests items retained later as history.

- [x] Add `Form.TargetEffectiveDate` as the stable annual identity; keep current and upcoming
      targets separate, make missing obligations visible, and key new uniqueness by person + type
      + target rather than by a mutable due date.
- [x] Make generation create outstanding obligations only. Remove effective-date/`InForceSince`
      completion inference and keep due-date inference only as a rolling legacy compatibility path.
- [x] Correct calendar-day defaults: CA available target−120/due target−90; PCP available
      target−90/due target; Reclass available target−90/due target−30; Safety, Privacy, and annual
      releases available target−90/due target; reviews due target+90/+180/+270/+360 and available
      ten days before by default.
- [x] Give opening a dedicated audited actual-date workflow. Keep `OpenedOn` and `CompletedOn`
      occurrence dates separate from immutable UTC recording time; reject future and pre-window
      dates; refuse changes through generic form update.
- [x] Make attestation sufficient without an artifact. Keep form notes/PDFs/receipts as evidence,
      and replace the old Supervisor prerequisite bypass with the one semantic rule: Reclass
      implies a same-target CA on or before it. If CA is missing, capture its actual date and commit
      two separate attestations atomically. Sati does not store the Evergreen Reclass packet.
- [x] Make the default billing blockers exactly PCP completion, CA, and all reviews. Add PCP
      opening as a separate optional gate, with Reclass, Safety, Privacy, Agency release, DHHS
      release, and Medical release independently selectable.
- [x] Replace ordinary mutable-mask semantics with append-only effective-dated agency policy.
      Require an enforcement date for every change; resolve by service date; keep past dates off by
      default and require an explanation when the separate rare-correction switch permits one; show
      the affected service-date/draft-versus-finalized impact before apply.
- [x] Correct the billing interval to start the day after due and end before completion. The due
      day and completion day are billable; ordinary late completion does not silently release
      notes inside the historical gap.
- [x] Narrow a Supervisor exception to one note and the exact current blocker IDs. Require reason,
      explicit attestation, expected revision, and persisted actor/time; revalidate the same IDs at
      billing so an unrelated or newly appearing blocker still blocks.
- [x] Add recipient-specific `ReleaseObligation` rules, persistence, local/cloud services, API
      routes, billing projection, and artifact identity. Annual DHHS is universal; Medical derives
      from active healthcare-provider assignments; Agency derives from active service/waiver
      assignments; a mid-cycle assignment is due the day before service begins; retirement and
      withdrawal are prospective and historical completion is retained.
- [x] Enforce guardian-only release signing when a guardian exists, consumer signing otherwise,
      and never authorized-representative signing. Add the idempotent API-only signature-compliance
      projection using the agency date of `SignedAtUtc`, without overwriting an earlier manual
      attestation. This remains synthetic-only; Local Production electronic signing remains off.
- [x] Keep a non-tab-specific billing status banner above the consumer profile and invalidate its
      profile/sidebar projections together after a compliance change.
- [x] Replace the remaining due-date-range PCP/Medical Release lookups in local and API annual
      packet status/rendering with exact `TargetEffectiveDate` identity plus the bounded legacy
      fallback. They are the last known live cycle-selection exception found in this review.
- [x] Add the shared post-compliance recovery planner and immutable decision persistence shape:
      only resolved obligations and otherwise billing-valid approved, unclaimed, unrecovered notes,
      all selected by default, with Admin omissions and exact frozen obligation/evidence facts.
- [x] Add agency-scoped Admin recovery GET/POST paths through the local and cloud service seams,
      a serializable immutable write, `billing-compliance-recovery.recorded`, and exact-evidence
      revalidation in candidate and claim-line construction. Focused recovery API and shared-core
      runs passed 3/3 and 37/37; the complete integrated suites remain below.
- [x] Add the WPF Admin recovery checklist and its accessibility coverage before describing
      recovery as an ordinary staff workflow.
- [x] Add durable append-only unresolved review flags and tenant-scoped Admin/Billing queues for
      submitted/finalized billing affected by a policy correction. Exact prior/new blocker IDs are
      retained and no submitted/finalized record is rewritten; an explicit resolution workflow is
      deferred rather than inferred.
- [x] Finish the WPF release-obligation workflow so staff can reconcile recipient assignments,
      select the exact obligation when generating/recording a release artifact, attest/withdraw it,
      and see missing assignment dates without falling back to the legacy category-wide forms.
- [x] Generate and inspect controlled migration
      `20260915004541_CorrectAnnualComplianceAndBillingPolicy` for target identities, policy
      versions, release obligations, signature projections, exact note exceptions, and recovery
      records, plus follow-on `20260915013852_AddBillingCompliancePolicyReviewFlags`. The target
      conversion corrects the legacy annual one-year shift, accepts the documented 120/60-day CA
      generations, requires a distinguishing post-backfill Q4 witness, aborts on ambiguous source
      shapes, and audits every assigned target and changed deadline without altering completion,
      opening, or attestation evidence. Downgrade refuses when that conversion cannot be reversed
      losslessly. The migrations have not been applied to an operational environment.
- [x] Remove the remaining parallel fixed release `Form` generation/presentation for reconciled
      targets; new consumers and provider assignments must atomically produce only exact recipient
      obligations, while legacy rows remain historical compatibility records. Verified 2026-09-15:
      every generator (local/API creation, caseload load, `ExpectedBillingComplianceObligations`)
      enumerates `PersonSaveRules.FormTypes`, which has no release; local and API creation and
      provider create/update/end/remove reconcile or retire exact obligations inside the same
      transaction with an audit event; matrix, board, agenda, and events read exact facts and
      filter superseded legacy rows. The last presenter, three unbound `NewClientViewModel`
      `Release*DueDate` properties, was removed and `ReleaseUiStructureTests` now guards against a
      current-cycle lookup of a legacy release form. The note-entry form-type picker still offers
      the three release categories as note subjects; that tags documentation, not an obligation.
- [x] Make Safety Plan, DHHS, Annual Documents, and annual-packet artifact workflows select and
      persist the exact annual target/release obligation instead of defaulting from today's cycle or
      a separate 30-day packet window. Verified 2026-09-15: Safety Plan suggests its target from its
      own configured availability, DHHS artifacts bind to the selected `ReleaseObligationId` and
      target, and packet status/persistence read the exact `TargetEffectiveDate`. Annual Documents
      no longer holds a hard-coded 30-day suggestion before the agency setting loads; a failed load
      leaves no cycle selected. The window's default remains a separate, deferred item below.
- [ ] Deferred by Josh 2026-09-15: reconcile the Annual Documents packet window with obligation
      availability. With defaults, next-target PCP/Safety/Privacy/release obligations open at
      target−90, but `AnnualPacketOpenDaysBefore` (30) keeps Annual Documents on the current
      target and `CanSavePacket` false until target−30. This is packet-assembly friction, not a
      clinical deadline or billing gate. Options considered: a 90-day default for new agencies,
      deriving the window from included obligations and retiring the setting, or explicit
      current/upcoming target selection. No stored agency value may change silently.
- [x] Retire the old Local Production due-date backfill UI/service/DI path. Make current duplicate
      repair key only on explicit `TargetEffectiveDate`; matching deadlines across distinct targets
      never merge, and differing deadlines inside one target are left conflicted. Preserve the old
      due-date key only in a raw, schema-gated startup repair staged immediately before the 2026-09-01
      legacy unique-index migration, where the target column does not yet exist.
- [x] Carry exact `FormId` + `TargetEffectiveDate` or `ReleaseObligationId` through upcoming events,
      the sign-in agenda, dashboard actions, and client-profile preview. Exact navigation fails
      closed instead of redirecting to today's cycle/category; unfinished explicit historical
      targets remain visible, and release actions open the exact recipient row.
- [ ] Replace the remaining matrix/new-client fixed release compatibility projections with 0/N
      exact recipient obligations after the rollout no longer needs their targetless fallback.
- [ ] Rehearse migration and reconciliation only on an approved non-production copy, including
      leap-day targets, historical gaps, duplicate/legacy forms, mid-cycle provider changes, and
      policies on both sides of an enforcement date. Do not query or alter Production PHI for this
      development task.
- [x] Complete synthetic verification of the integrated compliance correction: API 739 passed;
      desktop/domain 2,044 passed and one optional native-AI test skipped; signatures 119 passed;
      portal 8 passed. Correct the recovery migration snapshot while retaining claim-line uniqueness.
- [x] Revalidate draft claims at submission; make policy append/impact/pending-state refresh atomic;
      derive missing annual/provider obligations for billing and allow a second immutable recovery
      after corrected facts. Keep clinical approval separate from billing eligibility.
- [ ] Complete separate legal/accessibility/operations review and an explicitly authorized release.
      No source-level result here is a deployment or Production-readiness claim.

## Unreleased — the pace counts days still to write up (2026-09-17)

See the 2026-09-17 decision on capacity and zero days. No schema change; it reuses the
`ServiceDayInclusion` table from the section below.

- [x] `ProductivityForecast` divides the pace by future eligible workdays plus past eligible
      workdays whose window is open and whose notes are unwritten. On the reported month that
      moved the divisor from 10 to 14 and the projection from 26.7 to 19.1.
- [x] A workday with nothing billable can be marked from its calendar square: zero in the average,
      out of the capacity set. Marked days appear in the average basis line.
- [x] `UndocumentedDayPromptLauncher` and `UndocumentedDayPromptWindow` warn at sign-in and
      shutdown about days whose window closes today or tomorrow. Dates only, no client
      information; shutdown offers to stay open and write them.
- [x] "FUTURE DAYS" becomes "DAYS TO CAPTURE", with "10 ahead · 4 to write up" beneath it.
- [x] Rule, service, and calendar regressions; 2,288 desktop tests pass.
- [x] Surface a day marked with no billable work to the supervisor once its documentation window
      has closed, with the dates behind the count. Reading crosses users through
      `TenantAccess.CanAccessUserAsync`; writing stays the case manager's own. See the
      2026-09-17 supervisor-visibility decision.
- [ ] Verify the two modals in the running app, including the shutdown path that cancels the close.
- [ ] Decide whether the supervisor should also see days that are still open, as a workload
      signal rather than a question about a settled day. Deliberately not built yet.

## Unreleased — a day counts once it is finished (2026-09-17)

See the 2026-09-17 decision on when a day joins the daily average. **Adds a schema change**: the
first since 1.3.10, so the next release needs the controlled Demo migration and a temporary
exact-IP firewall rule the user adds and removes.

- [x] `ProductivityForecast` decides counted, open, or settled, and owns the case manager's
      choice; a day documented one note at a time no longer drags the average down while its
      other work is still scheduled.
- [x] `ServiceDayInclusion` (migration `20260917203349_AddServiceDayInclusions`, one new table,
      unique per user per day), `IServiceDayInclusionService` locally, `CloudServiceDayInclusionService`,
      and three API routes gated on the validated actor with no user id in the request.
- [x] The month square carries a tick for any still-open day; ticking counts it, clearing it holds
      it, and returning it to Sati's own reading deletes the row. Open days draw plainly and say
      why, and the panel shows what the average divided by.
- [x] Right-click now schedules time off from the Month view as well as the Year view.
- [x] Rule, service, calendar, rendered-view, and API regressions; 2,286 desktop and 831 API tests
      pass.
- [ ] Verify against real data on the Joshu workstation, including a dark theme.
- [ ] Decide whether the supervisor's monthly productivity view should read the same way.

## Unreleased — productivity days on the calendar (2026-09-17)

See the 2026-09-17 daily-average decision.

- [x] Move the documented daily average into `ProductivityForecast` (`DailyAverageDays`,
      `DailyAverageUnits`, `ClassifyDay`) and delete the dashboard's copy. A future day with a
      scheduled pending note no longer enlarges the divisor, so the reported average can change
      for a case manager who schedules ahead.
- [x] Tint counted days in the month and year calendars: green with logged or approved units,
      paler green with a pink border when counted through pending notes only. Each square lists
      its units by status and says which case it is in; accessible names say the same.
- [x] Add a read-only month thumbnail to the Overview productivity panel, named for screen
      readers with the counts it shows.
- [x] Three new theme tokens, overridden in the five dark themes, with the pending border
      measured against 3:1 non-text contrast in every theme.
- [x] Rule, calendar, rendered-view, and thumbnail regressions; 2,274 desktop tests pass.
- [ ] Verify the tint in the running app against real data, including a dark theme.
- [ ] Decide whether the supervisor's monthly productivity view should use the same tint.

## 1.3.14 production failures (2026-09-17)

Three workstation logs from the Joshu login, all 1.3.14.

- [x] `NullReferenceException` inside WPF layout (reference C00DD4583B60): the annual-form row
      template gave `AutomationProperties.Name` a `{x:Null}` fallback, which that property rejects.
      Fallbacks are now empty strings; `AnnualFormRowRenderTests` renders the real client profile
      and fails against the old template. Under the old template every later layout pass threw
      too, so the Clients panel stopped responding without closing (log 32440, 15:53 local).
- [x] Unobserved `NotSupportedException` from the assessment and PCP workspaces on every client
      selection when the agency has those workflows off: the hidden tabs still loaded. They now
      load only while the workflow is enabled, and a failed load is recorded and shown on the
      workspace instead of escaping. `ClientDocumentLoadFailureTests` fails against the old code.
- [x] `ArgumentException` crash dialog when marking a pending note without goal progress Logged
      from the notes log (reference D47EFC73EBA0). Any refusal is now shown on the notes log, and
      the note's status (and justification, for Send to Supervisor and Hold) is put back.
      `NotesLogStatusRefusalTests` fails against the old code.
- [x] Note-grid context menus (Mark Note Logged in the notes log, Delete Note on the dashboard)
      opened over the column header, and stayed open when keeping an unsaved draft put the old
      selection back; both then acted on a note the case manager did not point at.
      `RowContextMenu.SelectsRow` opens the menu only when the row under the pointer is the
      selected note. The stock grid does select the right-clicked row, so this was not why
      repeated right-clicks "did nothing" on 2026-09-17; that was the refused note's repeat
      exception being suppressed, fixed above. `NoteGridContextMenuTests` fails without it.
- [ ] A note was refused as not billable for a review whose attestation was dated before its
      due date; revoking and re-attesting the same form let the note log. Unexplained. The two
      screenshots first examined were from different clients, so the "off-cycle row" theory
      rested on a mix-up; the first caseload-wide run of
      `scripts/Diagnose-OffCycleComplianceForms.sql` found no off-cycle rows at all. Next time,
      capture that client's forms (result 3) before revoking.
- [ ] Run the updated diagnostic's result 1b once to confirm no client is missing current
      plan-year rows (billing projects each missing row as incomplete).
- [ ] `scripts/Diagnose-BillingGateDisagreement.sql` still reads `Forms.IsCompliant`, which
      no longer exists.
- [ ] Delete Note on the dashboard deletes without asking, and a refused delete (a submitted
      note) reaches the crash dialog.
- [ ] Stack overflow (`0xC00000FD`, 11:13 local): cause unknown. Enable dump capture on that
      workstation with `scripts/Set-SatiCrashDumpCapture.ps1 -Enable`, analyze the next dump
      there, then `-Disable -RemoveDumps`. See the 2026-09-17 decision.
- [ ] Give Sati's own refusals catalog codes (`LOGGING_DESIGN.md` §3). The notes crash record could
      not say which of five validation messages fired, because messages are rightly never logged;
      a code would have said so without the text.
- [ ] Implement the breadcrumb tail on the run marker (`LOGGING_DESIGN.md` §5). The stack-overflow
      session recorded only that it died, not which screen or command was active.
- [ ] `NoteEntryViewModel.SubmitNote` still shows "Sati encountered an error saving your note" with
      no reason and no support reference for any unexpected failure.

## Local client edits refused since 1.3.11 (2026-09-16)

Saving any client whose release rows were loaded failed in local Production with "Client Save
Status Unconfirmed" (reported after adding a shared living provider; the provider itself was saved
by its own panel). `PersonService.EditPersonAsync` called `People.Update(person)`, which marks the
whole loaded graph — notes, forms, release rows — as modified, and the 1.3.11 release-history guard
correctly refused the rewrite. The transaction rolled back, so the client changes were not saved.

- [x] Write only the client row and newly generated forms; regressions for editing after adding a
      waiver provider, not rewriting notes changed elsewhere, and still saving a new waiver's forms.
      The first two fail against the old code.
- [x] Report a client changed elsewhere (`PersonConcurrencyException`) and a refused write
      (`PersonPersistenceException`) as "not saved" instead of "status unconfirmed", and undo the
      in-memory revision bump when a save fails so a retry is not reported as its own conflict.
- [x] Audit for the same class of failure. Every other local service that accepts an entity from
      the screen loads a fresh copy by id or clears navigations first; no other graph-wide
      `Update`/`Attach` exists; the API builds entities from DTOs. Each 1.3.11-era append-only
      guard was checked against its writers (release retirement, note overrides, published check
      requests, form attestations) without finding another violation. `LocalWorkflowSweepTests`
      runs everyday sequences on a client with release history, attestations, providers, and
      notes, and fails against the old client save.

## Local Production compliance seed (2026-09-16)

See the 2026-09-16 seeding decision. Not part of any release.

- [x] Build `tools/SatiComplianceSeed` and the `scripts/remote-compliance-seed` bundle; extract
      `ExpectedBillingComplianceObligations.MissingReleasePlans` so the billing safety net and the
      tool share one list.
- [x] Rehearse end to end on a synthetic Production-marked LocalDB: row creation, form, opening, and
      release seeding, older-year releases with and without a provider recipient, a Reclass left
      alone by the prerequisite rule, refusals for a wrong total and a non-Production name, and
      Sati's own gate passing afterwards for every seeded client.
- [x] Publish the single-file tool to `Sati Desktop\compliance-seed` (SHA-256
      `bf01c7c414f818b01f8b6bb4454e1fc5885d403328650707b2288d85bffe3143`).
- [ ] Run Step 1 on the Joshu login and review the counts, then Step 2.
- [ ] Before any real legal use, start from a clean database; seeded rows are marked but are not
      records.

## Monthly contact requirement (2026-09-16, released in 1.3.14)

See the 2026-09-16 monthly-contact decision. No migration; deploy the API with or before the desktop.

- [x] Add `MonthlyContactRules` (rolling 30 days from each contact, first clock from the effective
      date) and the off-by-default `MonthlyContact` policy option in Settings.
- [x] Supply contact history to every desktop-local, cloud-client, and API billing decision, with a
      named block when it was not loaded; count a note being saved as its own contact.
- [x] Show the last contact date under each name in the client list, red with "overdue" when
      overdue; align the profile's "Last contact" with the same rule.
- [x] Rule, desktop, local-service, and API regressions, each confirmed to fail against a reverted
      rule or wiring.
- [ ] Verify the client-list line in the running app, including the dark theme's red.
- [ ] Decide whether the dashboard matrix or daily agenda should surface overdue contact.
- [ ] Confirm the 30-day interval and which note types qualify against the agency's program
      rules; they come from the agency's request, not a cited OADS rule.

## Annual forms show the renewal being prepared (2026-09-16, released in 1.3.14)

See the 2026-09-16 decision. The API must be deployed with or before this desktop build; no
migration is required.

- [x] Show the plan in force and, from its availability window until its target date, the renewal
      for PCP, Comprehensive Assessment, Reclass, Safety Plan, and Privacy Practices. Reviews and
      releases stay single.
- [x] Route a renewal checkbox to that exact row's attestation; show a missing renewal row as
      missing.
- [x] Word late opening (PCP) and late start (assessment) from the gate's own deadline.
- [x] Add the agency-selectable assessment-start billing gate (target − 120 days), off by default,
      with a Settings option and API acceptance.
- [x] Remove the dashboard view model's unbound annual `…Compliant` copies.
- [x] Regressions for the overdue-renewal case, window edges, early completion, target-date
      collapse, missing rows, February 29, command identity, and the new gate, each confirmed to
      fail against the old behavior.
- [x] Run `scripts/audit-annual-assessment-targets.sql` read-only against identity-verified My work
      Production with authorized database access. On 2026-09-24 it flagged 12 rows across 11
      clients and made no writes; those candidates are not proof of an incorrect attestation.
- [ ] Review the flagged form IDs against source evidence, correct only confirmed wrong-year
      attestations through the app's retained-history workflow, and verify billing status
      afterward. This source change alone does not certify or rewrite existing Production rows.
- [x] The caseload matrix (`FormCellViewModel`) now shows the renewal after the current annual
      form is satisfied, and marks an overdue renewal instead of showing the old completion as green.
      An unfinished current form remains the visible obligation. The cell names renewals explicitly.
- [x] Stop computing the next annual target as `currentTarget.AddYears(1)`. For a February 29
      admission that is February 28 of a leap year, which no generated row carries: upcoming
      events and the task board missed the leap-year renewal, and the desktop and API release
      reconcilers would have created an extra, never-attested release cycle on February 28
      that blocks billing. The first affected date is February 28, 2027, so no such rows exist
      yet. All callers now use `ComplianceScheduleRules.CurrentAndUpcomingTargetEffectiveDates`
      or `UpcomingTargetEffectiveDate`; a source guard refuses the old expression.
- [ ] Cycle-end arithmetic that has only a target, not the admission date, is still one day off
      for February 29 admissions: `FormAttestationRules.ResolveCycleForForm` and the Reclass
      prerequisite window, and the overlap end in `ReleaseObligationRules.GenerateCycle` and
      `ReleaseAssignmentResolution.Resolve`. Affects only a provider link or attestation dated
      on the leap-year boundary day.
- [ ] Confirm the 120-day assessment start with OADS guidance; it currently comes from the
      agency workbook.
- [ ] Verify the renewal layout in the running app at the smallest supported profile width.

## Unreleased — locked client-profile form attestations

- [x] Make the client-profile Forms matrix lock per selected person. Selecting or switching a
      person always relocks it, and relocking closes any unfinished attestation panel.
- [x] Refuse the profile form command while locked, even if invoked outside the visual control;
      the lock icon is the only profile path that enables form editing.
- [x] Render all twelve form checkboxes as command-only attestation indicators. Activation opens
      the shared blank-date attestation/revocation workflow without changing the checkmark;
      `Form.CompletedDate` remains the only source of the checked state.
- [x] Add view-model, command-only checkbox, and XAML-structure regressions for the lock,
      client-switch reset, blank-date refusal, and post-attestation checked state.

## Unreleased — exact-date billing compliance and follow-up labels

> Superseded in part on 2026-09-14. The original implementation below blocked the due day because
> that was the then-recorded assumption. The active rule above begins blocking the following day.

- [x] Name scheduled form follow-up suggestions from their recorded `FormType`; label legacy rows
      honestly when no type was stored instead of presenting a bare "Form" suggestion.
- [x] Key note submission, supervisor review/approval, and claim creation to the note's service
      date only. A later overdue requirement must not retroactively block earlier compliant work.
- [x] Initial due-day-inclusive interval implemented and tested; superseded by the confirmed
      day-after-due rule in the active September 14 section.
- [x] Add shared-rule, desktop-pipeline, API-pipeline, and real suggestion-source regressions for
      the September 4 service / September 6 due-date case and specific scheduled form labels.

## Release 1.3.22 — 2026-09-20

“Today's Work no longer looks like an error.” This patch removes the misleading red whole-panel
focus/validation outline from Today's Work while preserving a clear accent outline and bold label
on the individual tab reached by keyboard. Real load/save failures remain explicit labeled messages
with recovery actions.

**No schema or API contract change.** The release changes one WPF style and its regression coverage.
It does not require a Demo migration, firewall access, or Local Production schema tracking beyond
the existing workstation-version note.

- [x] Preflight: `master` matched `origin/master` after fetch; only the two reviewed Today's Work
      files were modified; 1.3.22 had no local or distributed artifacts; no migration, contract,
      database, or security-setting change was present. Retain every non-master branch: the Claude
      branch owns an active linked worktree, the setup/chat/video and remote Claude branches have
      unique or historical/design work, and the two remote Codex branches are merged but remote-only
      and not proven disposable. No branch was merged or deleted.
- [x] Coordinate 1.3.22 versions, installer/readiness defaults, Settings release notes, examples,
      and this release record.
- [x] Complete Release build and every solution test project under the signed-in Windows profile.
      The final build has zero warnings and zero errors. Desktop: 2,378 passed, with one documented
      opt-in local-AI skip and three documented opt-in SQL Server skips; API: 874 passed with five
      documented opt-in SQL Server skips; signatures: 119 passed; portal: 8 passed; Carika: 4
      passed. Focused Today's Work theme/render coverage passed 5/5, and the generated QA image was
      inspected with no whole-panel error outline, clipping, or layout regression.
- [x] Commit and push the verified source normally. Source commit `2d7433c` is present on
      `origin/master`; no force or history rewrite was used.
- [x] Publish only the matching Demo API. `artifacts/SatiApi-1.3.22-fx-x86.zip` is 10,303,357
      bytes, SHA-256 `9ADE4C3A7A6D133368AE55F625F07E61FC39978ACDF21540CA71286F073B1916`,
      with file/assembly version 1.3.22.0, the same 68-file inventory as 1.3.21, no settings files,
      and no secret-like configuration. OneDeploy to `sati-demo-api-satilogica` in `rg-sati-demo`
      succeeded (deployment `04cd1ecf165c4bae95b24ebc463548de`). Live and ready return 200;
      `/health/version` reports Sati.Api 1.3.22 with deployed/local contract revision
      `08C5B7028546`. `Test-DemoReadiness.ps1 -HealthOnly` passed and wrote
      `artifacts/release-1.3.22-demo-readiness.json`; authenticated checks were not run. The
      1.3.21 package and deployment remain the prior known-healthy release.
- [x] Build, accept, and publish both installers without overwriting. The embedded prerequisite
      source `SqlLocalDB.msi` is Valid Microsoft-signed, SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      `SatiDemoSetup-1.3.22.exe` is 102,952,960 bytes, SHA-256
      `9009EC38DB585ED019B8A2D27F4B72944AD2E647BD3FB1A0FA512BB78990A8DB`; five responsive
      15-second launches, graceful closes, version 1.3.22.0, and cleanup passed; it and its
      checksum were verified in `SatiLogica Demo Files`. `SatiLocalSetup-1.3.22.exe` is
      205,000,233 bytes, SHA-256
      `13B821860ED58C6A8727D35F7A74E2F058ECA5ADDEFB5477BA16B7D7C18AC79F`; version
      1.3.22.0, `SatiProduction`, integrated security, and cleanup passed; it and its checksum were
      verified in `Sati Desktop`. These were build-workstation acceptance runs, not a clean
      external-machine attestation.
- [x] Commit and push this final evidence normally; the resulting evidence commit is reported in
      the release handoff, and the clean local/remote equality check follows the push.
- [ ] Production workstation (Joshu login): known to remain on 1.3.14 until a newer Local installer
      is installed and launched. This release has no new migration; do not claim the workstation has
      received prior migrations until its launch is actually observed.

## Release 1.3.21 — 2026-09-20

“Annual Forms follow one clear path.” This release brings the related annual-form workflows under
one profile tab, makes directions visually distinct from explanatory detail, and removes WPF
facsimiles that could be mistaken for the official external document. It also carries profile-photo
cropping/preparation, paged formatted journals with checked-item reminders, correct handling of
lapsed Scheduled work, and the already-merged Demo billing deposit/claim-correction slice.

**Additive Demo migration and API contract change.** Migration
`20260919212417_AddEftDepositsAndClaimCorrections` adds four tables plus
`RemittanceClaimOutcomes.PayerClaimControlNumber` and `EdiGenerations.IsCorrection`. The billing
correction routes/DTOs change the client/API contract revision. Claim replacement/void acceptance
by MaineCare/Office Ally remains a companion-guide question and is not presented as Production-ready.

- [x] Coordinated 1.3.21 versions, installer/readiness defaults, Settings release notes, and
      installer examples. Candidate API and installer artifact paths were confirmed absent before
      the version was claimed.
- [x] Apply the controlled Demo migration with explicit user authorization. The guarded runner
      confirmed database `SatiDemo` and environment `Demo`; its rollback-only dry run, real apply,
      and second real/idempotency pass all succeeded. Each pass reported the six expected schema
      objects already present with the expected shape, migration count 110, zero deposit-entry rows,
      and zero correction rows. The temporary exact-IP rule for `72.95.106.10` was user-created and
      user-removed; a post-removal Azure listing contains only Demo API and reset-service outbound
      rules.
- [x] Branch audit: safely deleted local `billing-deposits-and-corrections` at `28e87e3` and
      `journal-pages-and-lapsed-scheduled` at `fc4ad90` only after proving both had zero unique
      commits, were contained by `origin/master`, and owned no worktree. Retained
      `claude/cool-jang-f6b3c4` (active linked worktree), `second-machine-setup`,
      `team-chat-design`, `video-conferencing-design`, and the three non-master remote-only branches
      because they are active, unique, design/history, or otherwise not proven disposable.
- [x] Complete Release build and every solution test project under the signed-in Windows profile.
      The final build has zero errors. Desktop: 2,377 passed and four documented opt-in SQL Server
      skips; API: 874 passed and five documented opt-in SQL Server skips; signatures: 119 passed;
      portal: 8 passed; Carika: 4 passed. The first API pass caught one stale 1.3.20 expectation in
      the version-contract test; after coordinating it to 1.3.21, the whole API project passed.
      Focused annual-workflow/theme coverage had already passed 48/48 and 26/26, respectively.
      The complete diff and staged scope were reviewed, and staged whitespace validation passed.
- [x] Source release commit `db7b956` pushed normally to `origin/master`; the remote contained the
      exact commit before deployment artifacts were produced.
- [x] Publish only the Demo API. `artifacts/SatiApi-1.3.21-fx-x86.zip` is 10,303,378 bytes,
      SHA-256 `9350E512588CD83D0AAD1133F051DBFFCDD3A524369CF79AD696158B38368B8A`, with the same
      68-file inventory as 1.3.20, file version 1.3.21.0, no settings files, and no secret-like
      configuration values. OneDeploy to `sati-demo-api-satilogica` in `rg-sati-demo` succeeded
      (deployment `19a1fe57b0fe4837a4156ed125650e6e`). Live and ready return 200;
      `/health/version` reports Sati.Api 1.3.21 with contract `08C5B7028546`; anonymous Settings
      returns 401. `Test-DemoReadiness.ps1 -HealthOnly` passed; authenticated checks were not run.
      The 1.3.20 ZIP and deployment remain the prior known-healthy release.
- [x] Build, accept, and publish both installers without overwriting. `SqlLocalDB.msi` is Valid
      Microsoft-signed, SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      `SatiDemoSetup-1.3.21.exe` is 102,948,864 bytes, SHA-256
      `D46354F3CA28133027966FE8F032430F27DC9829A1DD192B74124596A8F98E0E`; five responsive
      15-second launches with graceful closes, version 1.3.21.0, and cleanup passed; published with
      its checksum to `SatiLogica Demo Files`. `SatiLocalSetup-1.3.21.exe` is 205,280,809 bytes,
      SHA-256 `6E93C8A642C80AD7032A6554EAB5949A9A0457C96ABA91CA8A39EEE0FC26E3CD`;
      version 1.3.21.0, `SatiProduction`, integrated security, and cleanup passed; published with
      its checksum to `Sati Desktop`. Final distribution hashes match the accepted artifacts. These
      were build-workstation acceptance runs, not a clean external-machine attestation.
- [ ] Production workstation (Joshu login): known to remain on 1.3.14 until its next Local install
      and launch. Installing 1.3.21 will also carry all intervening migrations, including
      `AddServiceDayInclusions` and `AddEftDepositsAndClaimCorrections`; do not mark that machine
      migrated until the launch is actually observed.

## Release 1.3.20 — 2026-09-18

"Unfinished plans move on at the end of the day." Closing Sati now resolves Scheduled notes dated
today or earlier, item by item: move to the next workday (the default) or delete, through the
existing note update and delete paths. Adds the Legacy theme, and makes the leaf image, window
icon, and interface font theme resources. Both decisions are in `DECISIONS.md` (2026-09-18).
Supersedes 1.3.19 below, whose Demo installer failed acceptance, and carries its fix.

**No migration and no contract change.** No new route or DTO; the contract revision should remain
`256F0E82D4B8`. The Demo API ships only to keep its version equal to the desktop's.

- [x] Coordinated 1.3.20 version. Settings release notes unchanged from 1.3.19, which never reached
      anyone.
- [x] Fix: `Themes/States.xaml` and `Themes/Legacy.xaml` load the leaf and icon by relative
      `/images/...` URIs, which resolve against whichever assembly compiled the dictionary.
      `ThemeResourcesNeverNameTheDesktopAssembly` fails against the 1.3.19 files and passes now.
      A Demo-configuration build reached "Sati — DEMO — Sign in" under both Sunlit Shell and
      Legacy before commit.
- [x] Release build: 0 errors. Desktop 2,301, API 827, signatures 119, portal 8, and Carika 4 pass.
      Nine opt-in skips.
- [x] Source release commit `e60969a` pushed to `origin/master`.
- [x] Publish the Demo API. `artifacts/SatiApi-1.3.20-fx-x86.zip` is 10,097,811 bytes, SHA-256
      `A33DB3E22208512F033C4761ACF495D6DA9A8A6BBBA312FF2CC5CF02771E4ADE`, the same 68 files as
      1.3.19, file version 1.3.20.0, no settings files, and no secret-like values. OneDeploy to
      `sati-demo-api-satilogica` in `rg-sati-demo` succeeded (deployment
      `636239e68b1a4b1e9d9af0791c00124c`); the 1.3.19 and 1.3.18 packages are retained. Live and
      ready return 200, `/health/version` reports Sati.Api 1.3.20 with contract `256F0E82D4B8`,
      unchanged since 1.3.18 because no contract source changed. Anonymous Settings returns 401.
      `Test-DemoReadiness.ps1 -HealthOnly` passed; authenticated checks were not run. No migration
      and no firewall rule.
- [x] Build, accept, and publish both installers without overwriting. `SqlLocalDB.msi` is Valid
      Microsoft-signed, SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      `SatiDemoSetup-1.3.20.exe` is 102,907,904 bytes, SHA-256
      `440DE5EEF310E9DD0E47277FA34AB0021F02E38D49C1D54CD0A6D5C3BC2BB826`; five responsive
      15-second launches with graceful closes, version 1.3.20.0, and cleanup passed; published to
      `SatiLogica Demo Files`. `SatiLocalSetup-1.3.20.exe` is 204,943,401 bytes, SHA-256
      `043C547573ECECD1B57174F2F6E2B581A391359C6BCBA91901E646422161C6EB`; version 1.3.20.0,
      `SatiProduction`, integrated security, and cleanup passed; published to `Sati Desktop`. Both
      copies and checksum files were verified after publication, on the build workstation rather
      than a clean machine.
- [x] Branches: none created for this work. Retained the same branches as 1.3.18, for the same
      reasons.
- [ ] Production workstation (Joshu login): on 1.3.14. Install `SatiLocalSetup-1.3.20.exe`, which
      carries 1.3.15 through 1.3.18 as well, including the `AddServiceDayInclusions` migration the
      desktop applies at that first launch.

## Release 1.3.19 — 2026-09-18 — failed Demo acceptance, not distributed

Source commit `5cb2e00` was pushed and the Demo API was deployed, but the Demo installer never
reached anyone. Superseded by 1.3.20.

- [x] Release build and every test project passed: desktop 2,300, API 827, signatures 119,
      portal 8, Carika 4; nine opt-in skips.
- [x] Demo API published. `artifacts/SatiApi-1.3.19-fx-x86.zip` is 10,097,851 bytes, SHA-256
      `DA6076FDD4F1D40F2A162802CD22281C843CE7993822F4EF9D767310AB975B0C`, 68 files as in 1.3.18,
      file version 1.3.19.0, no settings files or secret-like values. OneDeploy deployment
      `0f74221be58f49a096268a8bc29b6566` succeeded; live and ready 200, version 1.3.19, contract
      `256F0E82D4B8`, anonymous Settings 401, `Test-DemoReadiness.ps1 -HealthOnly` passed. It was
      healthy; the fault was desktop-only. Replaced by the 1.3.20 deployment.
- [x] `SatiDemoSetup-1.3.19.exe` (102,912,000 bytes, SHA-256
      `10297B31C8A96A7AAB12214BFD0C9A604E9F6F73789A32BF49307B6ED1933222`) **failed acceptance** on
      the first launch with "Sati Could Not Start". The startup log (reference `B3DC5A0D3FCD`) shows
      `XamlParseException` in `SplashScreenWindow` with an inner `FileNotFoundException`: the new
      theme resources named `/Sati;component/...`, and the Demo assembly is `Sati.Demo`. The test
      suite runs as `Sati`, which is why it passed there. The installer is kept in `artifacts` as
      evidence and was never copied to a distribution folder. No Local installer was built.

## Release 1.3.18 — 2026-09-18

"The pace counts the days you still have to write up." Releases the two September 17 sections
above: the corrected pace divisor, days marked as having no billable work, the sign-in and
shutdown reminders, and supervisor visibility of settled zero days.

**No migration.** The marks reuse `ServiceDayInclusions` from 1.3.17. The contract gains
`service-day-inclusion-review-v1`, because the read now sends a `userId` an older server would
ignore while answering with the actor's own rows, so the fingerprint changes and the Demo API
ships with this desktop build.

- [x] Merge `capture-days-and-zero-days` into `master` (fast-forward to `ddfb085`).
- [x] Coordinated 1.3.18 version and Settings release notes, including the supervisor-visible
      change said plainly to the case managers it describes.
- [x] Release build: 0 errors. Desktop 2,290, API 827, signatures 119, portal 8, and Carika 4 pass.
      Nine opt-in skips.
- [x] Source release commit `f1a2930` pushed to `origin/master`.
- [x] Publish the Demo API. `artifacts/SatiApi-1.3.18-fx-x86.zip` is 10,099,185 bytes, SHA-256
      `E1EC5D7F1C135B0FCB58E0E38D6FF044E325E1B4A26A2D2EF50DC551EFD88476`, the same 68 files as
      1.3.17, file version 1.3.18.0, no settings files, and no secret-like values. OneDeploy to
      `sati-demo-api-satilogica` in `rg-sati-demo` succeeded (deployment
      `3c8eac7510354ba3921bad428626dd20`); the 1.3.17 package is retained. Live and ready return
      200, `/health/version` reports Sati.Api 1.3.18 with contract `256F0E82D4B8` — changed from
      `3A4B6F084C12` for the new contract shape — equal to `ApiSurface.Revision` from this build.
      The inclusions route refuses anonymous callers. `Test-DemoReadiness.ps1 -HealthOnly` passed;
      authenticated checks were not run. No migration and no firewall rule.
- [x] Build, accept, and publish both installers without overwriting. `SqlLocalDB.msi` is Valid
      Microsoft-signed. `SatiDemoSetup-1.3.18.exe` is 102,866,944 bytes, SHA-256
      `D63C891B5A75193B659911D133CE58EF92641C734408AAB5323C7BECC5AC8F70`; five responsive
      15-second launches with graceful closes, version 1.3.18.0, and cleanup passed; published to
      `SatiLogica Demo Files`. `SatiLocalSetup-1.3.18.exe` is 204,908,073 bytes, SHA-256
      `C3BE48091390DEFE869CBBFF426DBEDE5895D6983ACE87A02221171B171D8D39`; version 1.3.18.0,
      `SatiProduction`, integrated security, and cleanup passed; published to `Sati Desktop`.
      The Local build failed twice with a transient lock on its own staged `Sati.exe`
      (`CompressArchiveUnauthorizedAccessError`) and succeeded unchanged on the third run; no
      stale build directory and no Sati process of this login was involved. Both copies and
      checksum files were verified after publication, on the build workstation rather than a
      clean machine.
- [x] Branches: deleted local `capture-days-and-zero-days` (ddfb085, fully merged, never pushed).
      Retained the same branches as 1.3.17, for the same reasons.
- [ ] Production workstation (Joshu login): on 1.3.14. Install `SatiLocalSetup-1.3.18.exe`, which
      carries 1.3.15 through 1.3.17 as well, including the `AddServiceDayInclusions` migration the
      desktop applies at that first launch.

## Release 1.3.17 — 2026-09-17

"A day counts once you have finished it." Releases the counted-service-days section above: a day
joins the documented daily average when it has documented work and nothing left on its schedule,
the calendar tick that decides an open day, and right-click time off in the Month view.

**Schema change.** `20260917203349_AddServiceDayInclusions` creates one table and its unique
index and alters nothing existing. The contract gains three routes, so the fingerprint changes and
the Demo API ships with this desktop build.

- [x] Merge `counted-service-days` into `master` (fast-forward to `6b0197e`).
- [x] Coordinated 1.3.17 version and Settings release notes.
- [x] Apply the Demo migration. The user authorized it and added the temporary exact-IP rule
      `temp-workstation-1317` for 72.95.106.10, then removed it when told the migration was done;
      this workflow never touched a firewall rule. `scripts/Apply-ServiceDayInclusionsMigration.ps1`
      fails closed on database name and the Demo identity marker, guards every statement on the
      real schema, and verifies column types, the cascading foreign key, and index uniqueness
      rather than names alone. Rollback-only dry run predicted the table, the index, and history
      row 109; the real run created all three; the third run changed nothing (TableCreated,
      IndexCreated, HistoryWritten all false, MigrationCount 109).
- [x] Release build: 0 errors. Desktop 2,282, API 826, signatures 119, portal 8, and Carika 4 pass.
      Nine opt-in skips. The API sign-in timing guard passed in the solution-wide run this time.
- [x] Source release commit `f86bd51` pushed to `origin/master`.
- [x] Publish the Demo API. `artifacts/SatiApi-1.3.17-fx-x86.zip` is 10,097,676 bytes, SHA-256
      `5542A55507C880D2708B8466BBACB00FAA4C1A00E0FABBDD65D180C06F6DB20B`, the same 68 files as
      1.3.16, file version 1.3.17.0, no settings files, and no secret-like values. OneDeploy to
      `sati-demo-api-satilogica` in `rg-sati-demo` succeeded (deployment
      `a9b54379a3f84c02a43241390fc27dc7`); the 1.3.16 package is retained. Live and ready return
      200, and readiness is the real confirmation that the migration satisfied the deployed model,
      because `SchemaDriftHealthCheck` compares the model against the database.
      `/health/version` reports Sati.Api 1.3.17 with contract `3A4B6F084C12` — changed from
      `E18157FBC2D9`, as expected for three new routes — equal to `ApiSurface.Revision` from this
      build. Anonymous Settings and the new inclusions route both return 401.
      `Test-DemoReadiness.ps1 -HealthOnly` passed; authenticated checks were not run.
- [x] Build, accept, and publish both installers without overwriting. `SqlLocalDB.msi` is Valid
      Microsoft-signed, SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      `SatiDemoSetup-1.3.17.exe` is 102,871,040 bytes, SHA-256
      `5991E4D3268EC791BFF301A73645F3826750AF6E4D1697678D5FFB8D054CEB70`; five responsive
      15-second launches with graceful closes, version 1.3.17.0, and cleanup passed; published to
      `SatiLogica Demo Files`. `SatiLocalSetup-1.3.17.exe` is 204,899,369 bytes, SHA-256
      `A2EC984576D48713D2128888BDBC66C14E48E223547637AAACA1EE73A5A062F3`; version 1.3.17.0,
      `SatiProduction`, integrated security, and cleanup passed; published to `Sati Desktop`. Both
      copies and checksum files were verified after publication, on the build workstation rather
      than a clean machine.
- [x] Branches: deleted local `counted-service-days` (6b0197e, fully merged, never pushed).
      Retained the same branches as 1.3.16, for the same reasons.
- [ ] Production workstation (Joshu login): on 1.3.14. Install `SatiLocalSetup-1.3.17.exe`, which
      carries 1.3.15 and 1.3.16 as well. The desktop applies `AddServiceDayInclusions` to local
      `SatiProduction` at that first launch; no other database work is pending for it.

## Release 1.3.16 — 2026-09-17

"See the days your average counts." Releases the productivity-days section above: the calendar
tint, the per-day unit breakdown, the Overview thumbnail, and the corrected documented daily
average. No migration, no schema change, and no route change; `Sati.Contracts` gains
`ProductivityDayKind` and three `ProductivityForecast` members, so the Demo API is published with
this release.

- [x] Merge `calendar-productivity-days` into `master` (fast-forward to `6db19bf`).
- [x] Coordinated 1.3.16 version and Settings release notes, including the plain-words warning
      that Documented Avg reads higher for anyone who schedules notes ahead.
- [x] Release build: 0 errors. Desktop 2,274, API 824, signatures 119, portal 8, and Carika 4 pass.
      Nine opt-in skips. The API timing guard `SignInSpendsTheSameWorkWhetherOrNotTheAccountExists`
      again failed once under solution-wide load and passed in a full API-project rerun; the only
      API change in this release is the version.
- [x] Source release commit `fcaad88` pushed to `origin/master`.
- [x] Publish the Demo API. `artifacts/SatiApi-1.3.16-fx-x86.zip` is 10,079,959 bytes, SHA-256
      `5790BBD4A867CA713B04C6C70739D741A6DB9A6B3408CCE419CE0700F70B7063`, the same 68 files as
      1.3.15, file version 1.3.16.0, no settings files, and no secret-like values. OneDeploy to
      `sati-demo-api-satilogica` in `rg-sati-demo` succeeded (deployment
      `18abb1e6fbf34f5caef38fef5ee611d1`); the 1.3.15 package is retained. Live and ready return
      200, `/health/version` reports Sati.Api 1.3.16 with contract `E18157FBC2D9`, equal to
      `ApiSurface.Revision` from this build and unchanged because no route changed, and anonymous
      Settings returns 401. `Test-DemoReadiness.ps1 -HealthOnly` passed; authenticated checks were
      not run. No migration and no firewall rule.
- [x] Build, accept, and publish both installers without overwriting. `SqlLocalDB.msi` is Valid
      Microsoft-signed, SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      `SatiDemoSetup-1.3.16.exe` is 102,854,656 bytes, SHA-256
      `7FBC65151493E0F167714513794C03AE30A554E6688ED91980F02C832DCF9D46`; five responsive
      15-second launches with graceful closes, version 1.3.16.0, and cleanup passed; published to
      `SatiLogica Demo Files`. `SatiLocalSetup-1.3.16.exe` is 204,878,889 bytes, SHA-256
      `7676E1654E154081C8FF888131E331ADB13AB1D37BA70B0A3187C85916561B03`; version 1.3.16.0,
      `SatiProduction`, integrated security, and cleanup passed; published to `Sati Desktop`. Both
      copies and checksum files were verified after publication, on the build workstation rather
      than a clean machine.
- [x] Branches: deleted local `calendar-productivity-days` (6db19bf, fully merged, never pushed).
      Retained the same branches as 1.3.15, for the same reasons.
- [ ] Production workstation (Joshu login): on 1.3.14. Install `SatiLocalSetup-1.3.16.exe`, which
      carries the 1.3.15 fixes as well. No database update is pending for it.
- [ ] Verify the calendar tint against real data on that workstation, including a dark theme.
      Only synthetic notes and rendered previews have been seen so far.

## Release 1.3.15 — 2026-09-17

"The client profile and notes log keep working." Releases the 1.3.14 production failures
section above: the Clients panel layout crash, notes-log refusals, hidden-tab loads, and note-grid
context menus, plus the crash-dump capture script and off-cycle diagnostic (not shipped in the
app). No migration, no schema change, and no API or contract change; the Demo API is published
only so its reported version matches.

- [x] Merge `fix/1.3.14-production-failures` into `master` (fast-forward to `47e1749`).
- [x] Coordinated 1.3.15 version and Settings release notes.
- [x] Release build: 0 errors. Desktop 2,237, API 824, signatures 119, portal 8, and Carika 4 pass.
      Nine opt-in skips (SQL Server concurrency and pipeline, live-schema analyzer, local AI
      model). In the solution-wide run the API timing guard
      `SignInSpendsTheSameWorkWhetherOrNotTheAccountExists` failed once under concurrent load;
      it passed three times alone and in a full API-project rerun, and no API code changed.
- [x] Source release commit `f8867c4` pushed to `origin/master`.
- [x] Publish the Demo API. `artifacts/SatiApi-1.3.15-fx-x86.zip` is 10,079,137 bytes, SHA-256
      `A518E67D25072D93F847C05528F4B195C32677818A61ACE364111A50F4F26909`, with the same 68 files
      as 1.3.14, file version 1.3.15.0, no settings files, and no secret-like values. Unlike the
      1.3.14 zip it uses forward-slash entry names with explicit folder entries (85 entries).
      OneDeploy to `sati-demo-api-satilogica` in `rg-sati-demo` succeeded (deployment
      `2c03bbb3a9164bcd827842f881d48108`); the prior 1.3.14 package is retained. Live and ready
      return 200, `/health/version` reports Sati.Api 1.3.15 with contract `E18157FBC2D9`, equal to
      `ApiSurface.Revision` from this build, and anonymous Settings returns 401.
      `Test-DemoReadiness.ps1 -HealthOnly` passed; authenticated checks were not run. No migration
      and no firewall rule.
- [x] Build, accept, and publish both installers without overwriting. `SqlLocalDB.msi` is Valid
      Microsoft-signed, SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      `SatiDemoSetup-1.3.15.exe` is 102,830,080 bytes, SHA-256
      `6258C6D4A8D8825330F288D79FE051A84B35262C09B31EE201AE14D9370A380A`; five responsive
      15-second launches with graceful closes, version 1.3.15.0, and cleanup passed; published to
      `SatiLogica Demo Files`. `SatiLocalSetup-1.3.15.exe` is 204,875,305 bytes, SHA-256
      `BF594ADB6347EAA11ACDC432A56DB7C4A90385C6BB5CE845C7F7FF373A776688`; version 1.3.15.0,
      `SatiProduction`, integrated security, and cleanup passed; published to `Sati Desktop`. Both
      copies and checksum files were verified after publication. Both acceptance runs were on the
      build workstation, not a clean machine.
- [x] Branches: deleted local `fix/1.3.14-production-failures` (47e1749, fully merged, never
      pushed). Retained the same branches as 1.3.14, for the same reasons.
- [ ] Production workstation (Joshu login): on 1.3.14 (every 2026-09-17 log reports 1.3.14.0).
      Install `SatiLocalSetup-1.3.15.exe`. No database update is pending for it.

## Release 1.3.14 — 2026-09-16

"Renewals in view and client edits that save." Releases the September 16 work below: the
client-edit fix, renewal controls, monthly contact and assessment-start billing options, and the
February 29 renewal lookup. No migration and no schema change; the contract gains the optional
`NoteSummaryDto.Id` and two requirement bits, so the Demo API is published with this release.

- [x] Merge `annual-renewal-controls` into `master` (fast-forward to `bb021ee`); source release
      commit `3022d29`.
- [x] Coordinated 1.3.14 version and Settings release notes. `Test-DemoReadiness.ps1` still
      defaulted to 1.3.12 (1.3.13 skipped it) and was corrected in `5838610`.
- [x] Release build: 0 errors. Desktop 2,228, API 824, signatures 119, portal 8, and Carika 4
      pass. Nine skips, all opt-in: SQL Server concurrency and pipeline cases (five API, one
      desktop), the two live-schema analyzer cases, and the local AI model check.
- [x] Publish the Demo API. `artifacts/SatiApi-1.3.14-fx-x86.zip` is 10,077,747 bytes with 70
      entries (the same file set as 1.3.11) and SHA-256
      `805E087E7CE770CCBA739F3789A13378F63E88385476EBB0D3E3A4CD97A635E6`, file version 1.3.14.0,
      no settings files, and no secret-like values. OneDeploy to `sati-demo-api-satilogica` in
      `rg-sati-demo` succeeded (deployment `920668c5263f4f89a32648ccff1e5cf7`); the prior live API
      was 1.3.11, whose package `SatiApi-1.3.11-fx-x86.zip` is retained. Live and ready return
      200, `/health/version` reports Sati.Api 1.3.14 with contract `E18157FBC2D9` (unchanged,
      because no route or listed contract shape changed), and anonymous Settings and billing
      routes return 401. `Test-DemoReadiness.ps1 -HealthOnly` passed; its authenticated checks
      need the synthetic Admin credential and were not run. No migration and no firewall rule.
- [x] Build, accept, and publish both installers without overwriting. `SqlLocalDB.msi` carries a
      Valid Microsoft signature, SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
      `SatiDemoSetup-1.3.14.exe` is 102,830,080 bytes, SHA-256
      `28719CFD6AAF68F9AF6B0AC04D3B28EF06E9A07014FB0AD243E2F95F67FF7FBF`; five responsive
      15-second launches with graceful closes, version 1.3.14.0, and cleanup passed; published to
      `SatiLogica Demo Files`. `SatiLocalSetup-1.3.14.exe` is 204,871,209 bytes, SHA-256
      `E664C3218355B033A20A7E3C38E0B01A8064A967F2740F7C4EBA5034B0260891`; version 1.3.14.0,
      `SatiProduction`, integrated security, and cleanup passed; published to `Sati Desktop`. Both
      copies and checksum files were verified after publication.
- [x] Branches: deleted local `codex/clarify-project-notes` (d60f4d0) and local and remote
      `annual-renewal-controls` (bb021ee), both fully merged. Retained `second-machine-setup`,
      `team-chat-design`, `video-conferencing-design`, and remote
      `claude/local-vs-github-workflow-dlcqpb` (unmerged, unrelated work);
      `claude/cool-jang-f6b3c4` (checked out in a worktree); and remote `codex/annual-compliance`
      and `codex/clarify-project-notes` (merged, but possibly tracked by the separate Codex clone).
- [x] Production workstation (Joshu login): installed `SatiLocalSetup-1.3.14.exe` (its 2026-09-17 logs report 1.3.14.0)
      so client edits save again. No database update is pending for it; `SatiComplianceSeed` can
      run before or after.

## Release 1.3.13 — 2026-09-16

"Startup reads a rebuilt index correctly." The second false refusal on the same production
workstation, from the same startup check, on a different premise.

- [x] Fix `MigrationEffectAnalyzer`. `20260915004541` drops
      `IX_DocumentArtifacts_OneLivePerCycle` and recreates it on the same three columns with a
      narrower filter. The analyzer inspects indexes by table and column, so the surviving old
      index satisfied the create. One present effect among eighty missing ones is
      `PartiallyPresent`, and 1.3.12 refused to start. An index a migration drops and recreates
      under its own name looks identical before and after, so its presence is now unverifiable.
      Absence still counts: without that, `20260915153000`, whose only structural step is such a
      rebuild, read as `Indeterminate` against a database missing the table entirely.
- [x] Prove the regression against the real migration chain, not a hand-built schema. The
      existing analyzer tests could not catch either false refusal, because both only appear
      against the actual operations these migrations declare.
      `MigrationEffectAnalyzerAgainstLiveSchemaTests` builds a database at the workstation's
      exact state — every migration through `20260914030703`, nothing after — and asserts the
      release reads as `NotApplied`. It reproduces `PartiallyPresent` against the unfixed
      analyzer.
- [x] Source fix committed as `cae5cdf` before this release pass began.
- [x] Complete coordinated 1.3.13 versioning and the full Release build: zero errors. Desktop
      2,163 and API 820 pass, with one opt-in SQL Server case skipped in the desktop suite and
      five skipped in the API suite.
- [ ] Signature, portal, and Carika suites were **not** run in this pass, and the Demo installer
      was **not** built. This release changes one desktop startup-path file and touches no
      schema, contract, or API surface, and the workstation needed the Local installer tonight.
      Both remain outstanding for the next release that can afford a full pass.
- [x] Deliberately skip the Demo API publication again, on the same reasoning as 1.3.12: no
      schema and no contract change, so the deployed API stays at 1.3.11 with contract
      `E18157FBC2D9`. `/health/version` continues to report 1.3.11 against newer clients until an
      API-affecting release catches it up. No Azure SQL firewall rule was needed or requested.
- [x] Build and publish the Local installer without overwriting.
      `SatiLocalSetup-1.3.13.exe` is 204,859,433 bytes with SHA-256
      `46E18AE2D6DA147628ABDE663CB666F4EF1E1EB5DA89A2B4C33F19CF9969F42C`, verified again after
      the copy into `Sati Desktop`. The embedded `SqlLocalDB.msi` signature check is built into
      the build script and passed.
- [x] The production workstation is converted and running 1.3.13 (2026-09-16). The update was
      applied with `SatiUpdateReport --apply` after the repair also removed forms whose cycle
      began before the consumer's effective date — a migration guard the readiness check had not
      covered. The check now covers every data guard in the pending migrations.
- [ ] Make the next conversion a dry run instead of a prediction: restore the workstation's own
      backup to a scratch database on that login and apply the update there first. Every data
      blocker this release hit was found by the migration, not by the hand-written check.
- [ ] **Unexplained**: 1.3.13 still refuses on the production workstation with the same
      `PartiallyPresent` dialog. Established, not inferred: the installed `Sati.exe` is
      byte-identical to the shipped build (SHA-256 `0DAAFE5D…A21F`); that exact binary, pointed at
      a database at the workstation's migration state, converts cleanly on the development
      machine; the real analyzer, run on the workstation against its own database
      (`SatiUpdateReport`), finds nothing present; the installed connection string names the same
      server and database; there is no single-instance handoff. No cause has been found, and
      none should be written down until one is. As a first-principles workaround the update is
      being applied directly with `SatiUpdateReport --apply` (Step 7), which re-runs the gate's
      check, refuses on anything but a clean `NotApplied`, backs up and verifies, migrates, and
      compares fingerprints of the client list, notes since 2026-09-01, and recent scratchpad.
- [ ] Log the analyzer's findings when startup returns `NeedsRepair`. The dialog names the
      migration but not the effects, and `RecordAndReport` is only called for exceptions, so a
      refusal in the field leaves nothing to diagnose from. This is why the refusal above could
      not be read directly.

## Release 1.3.12 — 2026-09-15

"Startup reads the update correctly." This patch corrects the startup check that refused to
open 1.3.11 against a database with the annual compliance update still pending.

- [x] Fix `MigrationEffectAnalyzer`. `20260915004541` alters
      `Settings.BillingComplianceRequirements` only to change its default from 31 to 7, leaving
      type, nullability and bound identical. The analyzer judged alters by nullability and
      length, so that operation read as already applied on a database that had had none of the
      migration; one present effect among many missing ones is `PartiallyPresent`, and startup
      refused. An alter that changes neither nullability nor a bound is now unverifiable,
      because it looks identical before and after. A live column that still disagrees with the
      target remains proof the alter has not run.
- [x] Prove the regression against the unfixed analyzer: it reports exactly the
      `PartiallyPresent` verdict a workstation reported in the field. The first attempted fix
      broke `NullabilityRemainsTheSignalWhenNoLengthIsDeclared`; the committed rule compares the
      live column against both the target and the prior definition, and all analyzer and
      updater tests pass.
- [x] Source fix committed as `98c176b` before this release pass began.
- [x] Complete coordinated 1.3.12 versioning, the full Release build, and all five test projects.
      The Release build has zero errors. Desktop 2,157, API 820, signatures 119, portal 8, and
      Carika 4 pass, with five opt-in SQL Server cases and two external checks skipped. Source
      release `6446ca9` was pushed to `origin/master` before any artifact was produced.
- [x] Deliberately skip the Demo API publication. This release changes no schema and no
      contract: the deployed API stays at 1.3.11 with contract `E18157FBC2D9`, which the 1.3.12
      client matches exactly. `/health/version` therefore reports 1.3.11 against 1.3.12 clients
      until the next API-affecting release catches it up. This is a decision, not a failed
      deployment. No Azure SQL firewall rule was needed or requested.
- [x] Build, accept, and publish both installers without overwriting. The embedded
      `SqlLocalDB.msi` still carries a Valid Microsoft Authenticode signature with SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`. Demo passed five
      responsive 15-second launches, graceful closes, exact version 1.3.12.0, and cleanup;
      `SatiDemoSetup-1.3.12.exe` is 102,834,176 bytes with SHA-256
      `5BC9B040769564436440EE6F29C1DB93C94BC752A9E59FBA4C8E0C7C9EE55B0C`. Local passed exact
      version, `SatiProduction` agreement, integrated security, and cleanup;
      `SatiLocalSetup-1.3.12.exe` is 204,856,361 bytes with SHA-256
      `74B13B1977776C259DE333BC38D5E720AD23D479EBA521C22B6ECC4E809D39AB`. Both executables and
      their checksum files were staged, hash-verified, and renamed into `Sati Desktop` and
      `SatiLogica Demo Files`; no staging file remains. Acceptance ran on the development
      workstation, not an external clean machine.
- [x] Record deployment, test, artifact, and workstation evidence here.

### Local Production machines

This release adds no migration. Databases still holding the pre-1.3.11 shape apply migrations
106 through 108 when they first launch this client, after its verified backup.

- [ ] SatiLogica workstation: its `SatiProduction` scaffold is already converted to 108.
- [ ] Joshu workstation: holds the real working data, still at 105 migrations. Its readiness
      check reported 15 unattributable deadlines across 2 consumers and 1 legacy blanket note
      override, which are independent of this bug and must be reconciled through
      `scripts/remote-preflight-1311/` before the conversion can succeed.

## Release 1.3.11 — 2026-09-15

"Annual work by its own date." This release delivers the annual compliance and service-date
billing correction: each obligation is identified by its annual effective date, completion is an
explicit dated attestation, billing is judged by the note's own service date under append-only
agency policy, and exceptions and administrative recovery are exact and immutable.

- [x] Reconcile the correction branch with releases 1.3.8 through 1.3.10. Merge commit
      `d60f4d0d8ddb1748b3d9cc6fc67e7257cf15bed2` records both parents; `master` fast-forwards to it
      with no discarded work. No branch was deleted; the worktree-owned and divergent design
      branches are retained.
- [x] Resolve the overlapping rules in favor of the clarified 2026-09-14 decisions while keeping the
      release-safeguard structure from 1.3.8: the note submission gate reads the service date only
      and admits a justified submission into clinical review without releasing billing; the 837P
      export gate re-checks the same service-date decision as claim creation, including exact
      exceptions and recovery; supervisory approval keeps the service-time lock and overlap check.
- [x] Correct the signature catalog for the two document kinds added in 1.3.9 so an authorized
      representative cannot sign their own appointment or a referral packet's authorizations.
- [x] Combine both migration chains in timestamp order: 108 migrations. A generated check migration
      was empty, so the merged model and snapshot agree. None of the three 2026-09-15 migrations
      references the scratchpad tables.
- [x] Restore the model snapshot and give `20260915153000_AllowSupersedingBillingComplianceRecovery`
      the `.Designer.cs` it was missing. Because that hand-written migration carried no target
      model, `dotnet ef migrations remove` silently reverted the snapshot to an older model during
      the merge check, dropping the recovery, policy, and release-obligation entities.
      `StabilizationTests.EfModelMatchesLatestMigrationSnapshot` caught it before deployment; a
      regenerated check migration is empty again and the chain is now self-describing.
- [x] Complete coordinated 1.3.11 versioning, the full Release build, and all five test projects.
      The Release build has zero errors and fifteen pre-existing analyzer warnings. Desktop 2,156,
      API 820, signatures 119, portal 8, and Carika 4 pass; the default run skips five opt-in SQL
      Server cases and two external checks. One API run failed once with an unidentified test and
      has not reproduced in seven consecutive runs; it is recorded here rather than treated as
      resolved, because the failing run carried no logger.
- [x] Apply the three 2026-09-15 migrations to identity-validated Azure `SatiDemo`. The conversion
      aborted twice on its own guards before any change: once for a consumer with annual forms and
      no effective date, and once for deadlines that do not match the documented legacy calculator.
      Both aborts rolled back and left the database at 105 migrations.
- [x] Reconcile the Demo data the rehearsal exposed, through
      `scripts/Reconcile-Release1311LegacyDeadlines.ps1`. Consumer 1012's effective date was set to
      2025-12-18, derived from its own review cadence at target+90/180/270/360 and its annual rows on
      the following anniversary. 27 deadlines in the mechanical one-day family were corrected, then,
      behind a separate switch, 62 deadlines the pre-repair generator had stamped identically for
      every form type were recomputed from the documented calculator across 11 consumers. 60 of
      those rows carry completion or opening evidence; that evidence was never read or written, only
      the deadline moved. This is a deliberate synthetic-data correction, not a rule change.
- [x] Apply through `scripts/Apply-Release1311Migrations.ps1`. A single generated script cannot
      apply this range: the correction migration adds `Forms.TargetEffectiveDate` and then reads it
      from its own hand-written SQL, SQL Server compiles a batch before running it, and no GO may
      appear inside the generated guarded blocks. The runner keeps the identity, chain-position, and
      scratchpad guards and delegates the apply to EF, which sends each operation separately. Demo
      moved from 105 to 108 migrations; all 4,124 forms have annual targets, none precedes its
      consumer's admission, and the scratchpad held 135 rows before and after. A second real run
      changed nothing.
- [x] Publish the Demo API. `artifacts/SatiApi-1.3.11-fx-x86.zip` is 10,068,671 bytes with 70
      entries and SHA-256
      `C34D74492CED89A40D49B29E9A093472020C5A0256F3950A0CF84F106400A870`, built from pushed source
      `ca0db6c` with file version 1.3.11.0, no settings files, and no secret-like values. OneDeploy
      to `sati-demo-api-satilogica` in `rg-sati-demo` reported Succeeded. Live and ready returned
      200, `/health/version` reports Sati.Api 1.3.11 and contract `E18157FBC2D9`, which equals the
      compiled client's revision, and anonymous Admin access returned 401.
- [x] Build, accept, and publish both installers without overwriting. The embedded
      `artifacts\Prerequisites\SqlLocalDB.msi` still carries a Valid Microsoft Authenticode
      signature with SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`. Demo passed five
      responsive 15-second launches, graceful closes, exact version 1.3.11.0, and cleanup;
      `SatiDemoSetup-1.3.11.exe` is 102,842,368 bytes with SHA-256
      `8E920DC2CAE1A367EB6D7FAABDA0944127B206EF2F4E5BF6F0DCA65510D3C137`. Local passed exact
      version, `SatiProduction` agreement, integrated security, and cleanup;
      `SatiLocalSetup-1.3.11.exe` is 204,858,409 bytes with SHA-256
      `BA5F9C25042B6F52BA0405E92E2B565607A2DD7D49E1181E7ED2CCD986183369`. Both executables and their
      checksum files were staged, hash-verified, and renamed into `Sati Desktop` and
      `SatiLogica Demo Files`; no staging file remains. Acceptance ran on the development
      workstation, not an external clean machine.
- [ ] Confirm the temporary exact-IP Azure SQL firewall rule for the workstation is closed. The
      release workflow never added, altered, or removed a firewall rule.

### Local Production machines

This release adds migrations 106 through 108. Every Local `SatiProduction` database applies them
only when that machine first launches the 1.3.11 Local client, which takes a verified backup first.

- [ ] SatiLogica workstation: source is current; the installed Local client and its `SatiProduction`
      migration state remain unverified for this release.
- [ ] Joshu workstation: installed version and Local migration state unverified; treat it as behind
      until 1.3.11 is installed and a successful Local startup is confirmed.

## Release 1.3.10 — 2026-09-14

"Safe Local startup restored." This patch corrects the Local package configuration that made 1.3.9
stop before database access, and makes the installer build and acceptance gates prove the complete
startup configuration rather than checking only its private connection string.

- [x] Diagnose Joshu's two 1.3.9 startup refusals from the payload-free diagnostic records. Both
      stopped in `DataEnvironmentResolver` before provisioning, identity validation, migration, or
      application queries, so the Local database and consumer records were not touched.
- [x] Restore the tracked, non-secret Production expected database name in
      `appsettings.Public.json`. The Local private configuration retains only its integrated-security
      connection mapping; the resolver can now cross-check both halves before opening the database.
- [x] Add one shared Local configuration validator to installer construction and isolated installer
      acceptance. Both now require the public and private files, exact `SatiProduction` agreement,
      Windows integrated security, and no SQL username/password before an artifact can pass.
- [x] Add direct resolver regression coverage for the exact missing-setting failure shipped in
      1.3.9 and structural coverage binding both installer gates to the shared validator. Focused
      validation passes 3/3; the complete desktop suite passes 1,990 with the external local-AI and
      opt-in Local SQL concurrency tests skipped as expected.
- [x] Complete DATT preflight against GitHub `master` at
      `3cc20f2c0083b5db51f8bb4ef217b9add9798493`. No branch was merged or deleted. Retain the
      divergent setup/design branches, stale tracking refs, and the separate heavily modified user
      checkout untouched.
- [x] Confirm there is no schema or API-contract change, no 1.3.10 artifact collision, and no need
      for a database, baseline, or firewall operation. The existing Microsoft LocalDB prerequisite
      remains Authenticode-valid with SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`.
- [x] Complete coordinated 1.3.10 versioning and validation. The full Release build passed with
      zero warnings and zero errors. All five standard test projects passed 2,917 tests and reported
      seven skipped entries: five opt-in API SQL tests, one opt-in Local SQL theory, and the external
      local-AI model check. Enabling the SQL tests exercised all seven underlying SQL cases (five API
      cases plus two Local theory rows), and all seven passed against disposable LocalDB databases.
      Focused resolver/shared-installer validation passed 3/3 and directly rejected the exact
      missing-Production-name configuration from 1.3.9. Contract revision remains `66A9F0D2949E`.
- [x] Commit source release `f594e672740ac066fa7ea4b9ba23969acf2854cf` and push it normally to
      `https://github.com/heschides/Sati`, branch `master`, without rewriting history. GitHub
      contained that exact commit before packaging began.
- [x] Publish only the existing Demo API. The 9,679,338-byte framework-dependent x86 package at
      `artifacts/SatiApi-1.3.10-fx-x86.zip` has SHA-256
      `E699C6F4FF7B4FC2A043563DABC6912D5F30ECF7E87FDFA326DBFDE5D76FBE6C`, file version 1.3.10.0,
      and 65 entries. It contains no malformed paths, private settings, forbidden files,
      credentials, or secret-like values. OneDeploy deployment `88e5f219c0c444179afca374571dfab3`
      succeeded; live and ready returned Healthy, `/health/version` reported Sati.Api 1.3.10 and
      contract `66A9F0D2949E`, and anonymous Admin access returned HTTP 401. The optional encrypted
      synthetic global Admin credential was absent, so the authenticated probe was not run.
- [x] Build and pass isolated acceptance for both new installers. Demo completed five responsive
      15-second launches with graceful exits and cleanup; its 102,572,032-byte
      `SatiDemoSetup-1.3.10.exe` has SHA-256
      `F5B06D2C46A86A401B201BB0CE17FA16A310A96F17B1D9B1C6B40055C70ED7D3`. Its 92-byte checksum file
      has SHA-256 `63A7412DA8E5A078A02D360189B28FB4FF7BA9BF3C3E9220D59CA8F44C882755`. Local acceptance proved
      version 1.3.10.0, exact `SatiProduction` public/private agreement, integrated security,
      embedded prerequisite, and cleanup; its 204,896,297-byte `SatiLocalSetup-1.3.10.exe` has
      SHA-256 `7BA61D990ED585E786F306DB4928353EEE78336767A9E2CE8B831FE9677A8174`. Its 93-byte checksum file has
      SHA-256 `C1CCE1E897A73EF0FFB0425DAE75D483B854C8FC565A50266319642C3AC13811`.
- [x] Atomically publish the accepted Demo and Local installer/checksum pairs without overwrite to
      the designated `SatiLogica Demo Files` and `Sati Desktop` distribution folders. Final hashes
      and checksum records match the build artifacts, and no staging file remains.
- [x] Record deployment, test, artifact, distribution, and known workstation evidence; push the
      final evidence commit; and confirm local/GitHub equality with a clean tracked tree.

### Local Production machines

Release 1.3.10 adds no migration. Database state therefore does not change merely by installing
this hotfix; the Local client still applies any older pending migrations safely during startup.

- [ ] SatiLogica workstation: Local `SatiProduction` already contains all 105 migrations, while its
      normally installed client remains version 1.3.2.0 until 1.3.10 is installed outside isolated
      acceptance.
- [ ] Joshu workstation: diagnostics prove 1.3.9 is installed and that both reported launches
      stopped in configuration resolution before database access. Its Local migration state remains
      unverified. Install 1.3.10 and confirm that it reaches Local sign-in before treating this
      workstation as current.

## Release 1.3.9 — 2026-09-14

"Financial workflows, clearer records, timely prompts." This release adds a PHI-minimized Finance
role and Representative Payee ledger/check workflow, weekly check-request drafts and reminders,
required case-note goal progress, clearer annual-document work, live form previews, two new public-
program packet builders, Evergreen completion attestations while OADS authoring remains off, and a
date-aware Demo baseline.

- [x] Confirm the releasable work is based on GitHub `master` at release 1.3.8, with no relevant
      completed branches to merge or safely delete. Retain the active, divergent, historical, and
      uniquely valuable branches and leave the separate dirty user checkout untouched.
- [x] Apply and verify the three additive migrations on identity-checked Local `SatiProduction` and
      Azure `SatiDemo`, including rollback rehearsals, a verified Local backup, and an idempotent
      second pass. Both databases contain 105 migration-history rows.
- [x] Capture and verify the authorized 56-table canonical SatiDemo reset baseline with timeline
      anchor 2026-09-14, and verify a complete reset inside a rolled-back outer transaction.
- [x] Verify the temporary exact-IP Azure SQL firewall rule is closed. Only the permanent Demo API
      outbound rules and refresh-function outbound rules remain.
- [x] Complete the coordinated 1.3.9 Release build and all five automated test projects. The final
      build has zero errors; 2,914 standard tests pass. The default run skips one external local-AI
      model check and seven opt-in SQL Server cases; all seven SQL Server cases pass separately
      against disposable LocalDB databases. The release gate also fixed a billing-period insertion/
      submission race, Local service-time create/move races, a sliding WebSocket revalidation delay,
      and a noisy authentication-timing measurement.
- [x] Complete API and installer package security checks and focused installed-app acceptance. The
      API ZIP contains 70 entries with no bad path separators, forbidden filenames, private
      configuration, credentials, or secret-like values. The Demo installer contains only public
      endpoint configuration; the Local installer uses integrated security and embeds the
      Authenticode-valid Microsoft SQL LocalDB prerequisite.
- [x] Commit source release `04689f486a9500a83317804a185ab1adf11e6925` and push it normally to
      `https://github.com/heschides/Sati`, branch `master`, without rewriting history. GitHub
      contained that exact commit before packaging began.
- [x] Publish only the existing Demo API. The 9,973,829-byte framework-dependent x86 package at
      `artifacts/SatiApi-1.3.9-fx-x86.zip` has SHA-256
      `E91BF8CE3BD0628AA62F66D2A328FD46F328E95406DEEADB2EED801888542EA0` and file version 1.3.9.0.
      OneDeploy deployment `84cab975b62a4160a90811c13b2ee66c` succeeded; live and ready returned
      HTTP 200, `/health/version` reported Sati.Api 1.3.9 and contract revision `66A9F0D2949E`, and
      anonymous Admin access returned HTTP 401. The optional encrypted synthetic global Admin
      credential was absent, so the authenticated probe was not run.
- [x] Build new non-overwritten installers and pass isolated acceptance. Demo completed five
      responsive 15-second launches with graceful exits and cleanup; its 102,584,320-byte
      `SatiDemoSetup-1.3.9.exe` has SHA-256
      `60D1437919B9F043C37E14D1F48F2244710D37AC3B1C31A952B1CA0527B5B349`. Local passed exact-version,
      integrated-security, embedded-prerequisite, and cleanup checks; its 204,900,391-byte
      `SatiLocalSetup-1.3.9.exe` has SHA-256
      `F96E33C12A1B2CB026DEB055E1C197C6867E8E0D26BE4AB0494428F64667CA16`. Each installer and its
      matching checksum were atomically published without overwrite to the designated
      `SatiLogica Demo Files` and `Sati Desktop` distribution folders; final hashes matched and no
      staging file remained. Acceptance ran on the build workstation, not an external clean machine.
- [x] Record deployment identifiers, package paths, sizes, SHA-256 hashes, test totals, distribution
      verification, and known Local Production machine release status; then push final evidence.

### Local Production machines

This release adds migrations 103 through 105. Azure `SatiDemo` and the development workstation's
Local `SatiProduction` both contain all 105 migration-history rows, but database migration and
desktop installation remain separate facts.

- [ ] SatiLogica workstation: Local `SatiProduction` is current, but the normally installed Local
      client is still version 1.3.2.0. Install 1.3.9 outside isolated acceptance before treating the
      installed client as current.
- [ ] Joshu workstation: its installation folder is not readable from this Windows profile, so its
      installed version and Local migration state remain unverified. The user plans to install 1.3.9;
      treat the workstation as behind until that installation starts successfully.

## Unreleased — 2026-09-14 controlled migration application

- [x] Add a guarded, transactional, rerunnable runner for the Representative Payee workflow,
      weekly check-request automation, and case-note goal-progress migrations. It refuses a wrong
      database/environment marker and verifies the new tables, columns, indexes, and foreign keys.
- [x] Rehearse all three changes with rollback on identity-checked Local `SatiProduction` and Azure
      `SatiDemo`, then apply and rerun idempotently. Both databases moved from 102 to 105 migration
      history rows; the second pass made zero schema, permission, or history changes.
- [x] Back up Local Production before application to
      `SatiProduction-2026-09-14-063854.bak` and verify it with SQL Server checksum restore
      verification. Azure retains its configured point-in-time recovery.
- [x] Verify the deployed Demo API remains live and ready after the additive schema change. The
      currently deployed API remains release 1.3.8/contract `5B9D8ED7F252`; publishing the new
      compatible API and desktop builds is still a separate release step.
- [x] After separate explicit authorization, recapture the canonical SatiDemo reset baseline with
      timeline anchor 2026-09-14. All 56 live resettable tables match 56 baseline tables with zero
      missing/extra tables and zero row-count differences; the three new workflow tables are
      present as empty canonical tables.
- [x] Recreate and verify the reset procedure and managed-identity permissions, then exercise the
      complete 56-table reset inside an outer transaction. It produced zero row-count differences,
      left every foreign key/check constraint trusted and enabled, retained 177 People and all 105
      migration rows, rotated identity/reset state inside the rehearsal, and restored the exact
      pre-rehearsal live state when the outer transaction rolled back.
- [x] Verify the temporary exact-IP Azure SQL firewall rule is closed after the authorized database
      work. The remaining rules are the permanent Demo API outbound rules and refresh-function
      outbound rules; the migration runner did not alter firewall settings.

## Superseded 2026-09-19 — live form draft previews

This completed experiment is retained as history. The later Annual Forms/document-display decision
removes separately drawn previews for externally owned forms and retains only faithful previews of
Sati-owned output.

- [x] Keep a document-shaped live preview beside every currently editable form workspace: AT
      Request, Check Request, Agency Release, DHHS forms, Safety Plan, Privacy Practices, CWIC
      packet, and Housing Support Funds application.
- [x] Send bound text into the in-memory draft while the user types. Numeric AT Request and weekly
      check-default fields use a short typing delay, so valid values reach the draft without making
      the user leave the field and incomplete numeric text does not replace the last valid value.
- [x] Keep disabled OADS authoring out of this scope. PCP and Comprehensive Assessment remain
      Evergreen attestations, and Classification remains unavailable unless their separately gated
      Sati authoring features are enabled in the future.
- [x] Keep preview changes non-destructive: they update only the open draft. Published documents,
      generated artifacts, and immutable template versions are not rewritten.
- [x] Expand the UI contract and WPF render tests across all eight editable workspaces; the focused
      preview suite passes 5/5.

## Unreleased — required case-note goal progress

- [x] Add a case-note Goal Progress combobox with the required state choices: None, Minimal,
      Moderate, and Substantial. Blank remains distinct from None so the UI cannot silently answer
      the question for the case manager.
- [x] Require a deliberate choice when a note enters Logged/submitted-for-review status in both the
      local service and API. Pending drafts may remain incomplete, and future Scheduled work and
      Reminders clear the field because no service outcome exists yet.
- [x] Preserve the choice through local/cloud save, read, edit, concurrency comparison, supervisor
      review, and historical display. Existing historical notes remain readable without invented data.
- [x] Keep goal progress as an independent historical observation. A future PCP-goal reference can
      be added alongside it when PCP authoring goes live without changing these recorded choices.
- [x] Add compatibility shape `case-note-goal-progress-v1`, migration
      `20260914030703_AddGoalProgressToCaseNotes`, and focused local/API/UI tests.
- [x] Apply the migration to identity-checked Local Production and SatiDemo with backup/rehearsal/
      idempotency evidence through the controlled 2026-09-14 runner.
- [x] Deploy compatible API/desktop builds through the normal approved 1.3.9 release process.

## Unreleased — weekly check-request drafts and reminders

- [x] Add one revisioned weekly default per Representative Payee consumer. The assigned case
      manager chooses the weekday, needed-by offset, payee, mailing address, amount, and reason;
      changes affect future drafts only.
- [x] Generate at most one draft for each scheduled occurrence, preserve template and schedule
      provenance, and keep every generated request as an ordinary frozen draft requiring review,
      PDF preparation, submission, supervisor approval, and Finance release.
- [x] Prompt the case manager at sign-in, after an open workstation crosses into a new day, and
      again during shutdown until the generated request has been submitted. Defer changes no data.
- [x] Add a per-user, per-environment personal opt-out in Settings. Turning it off stops generation
      and reminders on that computer without deleting defaults or existing drafts.
- [x] Treat a newly scheduled calendar exemption as time off: when its weekday matches an enabled
      consumer default, prompt the case manager immediately with Prepare now or Later. Prepare now
      creates or opens the matching draft early without submitting it.
- [x] Check tomorrow's scheduled time off at sign-in and calendar-day rollover and prompt again for
      any matching request that has not been submitted. Removing time off does not create a prompt.
- [x] Add local/cloud services, tenant-scoped API routes, compatibility shape, narrative-free audit
      events, migration `20260914023645_AddWeeklyCheckRequestAutomation`, and focused schedule,
      persistence, API, authorization, preference, and UI-contract tests. Current focused runs:
      desktop/domain/UI 26/26 and API/route surface 12/12; the broader related regression sets also
      passed 52/52 desktop and 77/77 API before the time-off extension.
- [x] Apply the migration to identity-checked Local Production and SatiDemo with backup/rehearsal/
      idempotency evidence through the controlled 2026-09-14 runner.
- [x] Deploy compatible API/desktop builds through the normal approved 1.3.9 release process.

## Unreleased — Finance and Representative Payee workflow

- [x] Add an independently assignable Representative Payee permission. The legacy Finance role
      maps to Billing plus Representative Payee, while administrators can grant either capability
      separately without granting case-management access.
- [x] Add a Finance Representative Payee workspace with an agency-scoped consumer ledger, signed
      manual deposits/expenses, current balance, approved-check queue, check-release control, and
      receipt acknowledgement.
- [x] Add a server-authoritative workflow over each frozen check request: the assigned case manager
      submits, the assigned/agency-wide supervisor approves or returns it, and Finance records
      release and receipt. Each checkpoint is an append-only, actor- and UTC-stamped event; return
      requires a reason, and duplicate/concurrent checkpoint writes are refused.
- [x] Post each released check to the consumer ledger atomically with the release event. Ledger rows
      are append-only, one release can post only once, and ordinary consumer deletion is refused
      when financial ledger history exists.
- [x] Remove consumer identity from Finance-only Billing grids and replace the Billing candidate
      network contract with minimum-necessary service facts that cannot serialize note narrative,
      visit documentation, exception text, or consumer names.
- [x] Add supervisor Check Request review UI, case-manager submission UI, user-management controls,
      local/cloud services, API authorization, audit actions, compatibility inventory, and migration
      `20260914015314_AddRepresentativePayeeWorkflow`.
- [x] Focused validation passed: API surface/privacy/workflow tests 11/11 and relevant desktop,
      domain, annual-document, permission, and UI tests 55/55. Five separate DPAPI tests require a
      normal signed-in Windows profile and cannot run inside the restricted test sandbox.
- [x] Apply the migration to identity-checked Local Production and SatiDemo with backup/rehearsal/
      idempotency evidence through the controlled 2026-09-14 runner.
- [x] Deploy compatible API/desktop builds through the normal approved 1.3.9 release process.

## Unreleased — clearer annual-document workflow

- [x] Replace the unexplained “Load cycle” wording in Safety Plan and Annual Documents with
      “View selected year,” explain that the period is the consumer's service year beginning on
      the effective-date anniversary, and state that opening it does not change historical records.
- [x] Give the date selectors and open-period controls specific accessible names and retain the
      existing read-only reload behavior behind the clearer wording.
- [x] Present separate workflow rows for DHHS release, Medical Provider Release, Agency Release,
      Safety Plan, Privacy Practices, and the once-only DHHS Authorized Representative form, with
      plain-language preparation status and the next workspace/action for each.
- [x] Give the Authorized Representative form its own artifact kind instead of conflating it with
      the annual DHHS release. A generated draft does not count as on file; the assigned case manager
      can record a verified signed physical copy with a required protected note and audit event.
      Once recorded, the form carries across annual periods and is no longer shown as annual work.
- [x] Rename the e-signature action to “Submit to consumer or guardian for review” and present signer
      request status as Pending, Denied, Signed, or the narrower terminal state when applicable.
- [x] Focused validation passed: 32 desktop/domain/UI tests and 8 API authorization/artifact tests,
      including cross-caseload refusal, duplicate once-only refusal, status carry-forward, accessible
      controls, and distinct DHHS document identities.

## Unreleased — Housing Support Funds application

- [x] Embed the exact fillable three-page Maine DHHS OADS application dated June 30, 2025,
      preserve the form fields, and expose one entry workspace with exact PDF generation from Clients and
      Documents navigation.
- [x] Prefill authoritative consumer, waiver, Shared Living, guardian, assigned case manager and
      provider facts while keeping application-specific contact, landlord, payee, amount and
      narrative entries in the user's control.
- [x] Enforce the form's $3,000 ceiling and closed choices; prominently flag subsidy, Shared Living,
      missing proof, and missing-signature review needs rather than silently deciding eligibility.
- [x] Leave consumer/guardian signatures and dates blank and leave the entire DHHS Staff Only page
      untouched; keep electronic signing unavailable pending program and agency confirmation.
- [x] Return a non-cacheable PDF, audit generation, and record a versioned Draft artifact tied to
      the controlled OADS source without rewriting prior draft history.
- [x] Revalidated for release 1.3.8 on 2026-09-13: the official Housing Services page still links
      the exact June 30, 2025 source embedded here (SHA-256
      `F44074E0FFE5096369BDCB32B30DE645155108B8A96093A75B62E9DDD8588190`).
- [ ] Define any future electronic submission, receipt, and OADS decision-status workflow
      separately. Generating this draft does not submit it or represent OADS approval.

## Unreleased — CWIC / Benefits Counseling referral packet

- [x] Reproduce MaineHealth's currently linked ten-page BCS referral packet from the exact
      supplied PDF, with one clear entry workspace and one packet workflow in both Clients and
      Documents navigation.
- [x] Prefill identity, age, address/contact, guardian, employment, MaineCare and VR facts that
      Sati already knows while keeping every answer editable and leaving signatures blank.
- [x] Keep SSN plaintext inside the authorized local/API generation process, return only the
      finished no-store PDF, and audit each decryption and packet generation.
- [x] Record each generated packet as a versioned Draft artifact tied to the fixed MaineHealth
      source revision; regeneration supersedes the prior live draft without rewriting history.
- [x] Validate closed choice sets, bounded text and the Maine DOL one-year release window; block
      electronic signature routing pending explicit MaineHealth/program/agency confirmation.
- [x] Add the separately versioned Housing Support Funds application workflow described above.
- [x] Revalidated for release 1.3.8 on 2026-09-13: MaineHealth still links the exact embedded packet
      (SHA-256 `07450B91D9756AD4EC26D0B7F570FCE9DEBE152722A2D981ACE4CB195D3A1013`),
      whose component forms retain their 2013/2019/2020 revision labels.

## Unreleased — account disablement and session revocation

- [x] Add retained `IsEnabled` and monotonic `SecurityVersion` account state, with an EF
      concurrency token; share lifecycle policy between API and transitional local services.
- [x] Require an enabled account and matching positive version for authenticated access and
      renewal. Password change/reset, explicit session revocation and enabled-state changes
      invalidate old sign-ins; re-enabling never revives an old credential.
- [x] Add same-agency administrator controls without deleting users or their records; refuse
      self-disable and platform-operator management. Keep disabled caseloads reviewable/transferable.
- [x] Check local template administration and EDI generation/replay before protected reads;
      EDI additionally requires the independently granted Billing capability.
- [x] Close the related assigned-privileged-user takeover: supervisor profile changes and
      password resets require a stored case-management-only target, not merely that capability bit.
- [x] Require explicit enabled-state JSON; an empty object must not silently mean disable.
- [x] Pause protected cloud requests while candidate sign-in credentials await workspace
      acceptance; preserve same-access drafts and require confirmation before changed-access reload.
- [x] Preserve contact drafts and show inline feedback for expired, denied or unconfirmed saves;
      discard late completion effects after a person/session change.
- [x] Verify all five test projects: 2,719 passed, one optional native-AI test skipped; preserve
      red-before-fix evidence and unchanged-record assertions.
- [ ] Rehearse the `20260911120000_AddAccountSessionLifecycle` migration and coordinated
      server/client rollout on an approved non-production copy; do not run it implicitly at startup.
- [ ] Deploy only with separate authorization, require fresh sign-in after upgrade, and retire
      old direct-SQL desktop clients. Source-level local checks cannot constrain a database client
      that bypasses them.
- [ ] Exercise concurrent account administration and ordinary in-flight clinical writes using
      SQL Server. SQLite regression coverage is not production-engine concurrency certification.
- [ ] Define a broader last-administrator recovery/provisioning policy, including legacy
      permission-demotion paths; self-disable prevention is not complete account-recovery policy.

## Unreleased — consumer-record permission revocation

- [x] Make retained assignments insufficient for own casework after CaseManagement is removed;
      cover API journals, notes, form opening, consumer status and own-casework reports.
- [x] Check person/owner agency consistently across accessible records and explicit note agency
      on reads, transitions, review and billing candidates, using the existing migration invariant.
- [x] Preserve persisted permissions in local login without copying password verifiers; enforce
      live actor facts in local consumer, document, review, AT/PCP, Admin and AI-context services.
- [x] Preserve separately granted Billing, Supervision and Administration; reject forged AT
      publication identities and ordinary-save attestations; retain published AT requests.
- [x] Skip own-casework startup/scheduled-work loads for accounts without CaseManagement while
      preserving their personal scratchpads and existing Billing/Supervision/Admin navigation.
- [x] Add synthetic red-before-fix regressions with retained assignments, stale/fresh sessions,
      denied-write state checks and legitimate workflow controls.
- [x] Implement password-change/session revocation and account disablement/offboarding (B05);
      see the unreleased lifecycle section above for rollout and remaining limits.
- [ ] Retire or secure the separately recorded globally scoped local maintenance tools (B04).
- [ ] Complete local/API AT publication completeness and audit parity, and review billing
      candidate narrative disclosure against the biller's actual minimum necessary workflow.
- [ ] Align supervisory target-user SQL bit filters with the full supported-mask rule; the actor
      already fails closed, but corrupt target-user masks should not appear in reviewer lists.
- [ ] Prove and harden local detached-graph persistence: a validated Person root must not let
      caller-supplied nested Forms/Notes bypass their own ownership or workflow rules.
- [ ] Rehearse deployment and investigate any already-inconsistent tenant markers through approved
      backup/reconciliation procedures; these source fixes do not deploy or repair real records.

## Unreleased — close standalone form-deletion billing bypass

- [x] Retire destructive standalone form deletion in both the API and transitional local service.
      Every persisted form is retained, including unattested, future, completed and currently
      optional forms. Refusal makes no write; mixed batches cannot partially delete.
- [x] Revalidate current case-management permission and exact tenant/caseload ownership, including
      empty requests; return a shared retention conflict for authorized nonempty requests.
- [x] Reproduce the old bypass with failing API and local regressions before changing the guards.
      Keep stored due/completion dates and billing-window semantics unchanged.
- [ ] Reconcile already-missing cloud obligations and review any claims created while the bypass
      existed through a separately authorized evidence-preserving process. This fix does not
      invent historical completion dates, reconstruct deleted rows, or change existing claims.
- [ ] Complete authoritative API cycle rollover and pre-EDI compliance revalidation; those are
      adjacent integrity gaps, not solved by refusing standalone form deletion.

## Unreleased — clearinghouse response intake and launch security review

- [x] Add Billing Submissions file import for a single original 999, 277CA, or 835; match retained
      outbound evidence automatically within the current agency and test environment.
- [x] Preserve encrypted immutable raw receipts, generation/claim matches, and atomic financial
      observations; replay exact or semantically duplicate responses without duplicate effects.
- [x] Give new submissions generation-specific claim references, validate envelope controls and
      amounts strictly, and keep late acknowledgments from replacing financial progress.
- [x] Protect file selection/upload/result publication across account switches; use safe errors
      and distinguish committed receipts from a subsequent screen-refresh failure.
- [x] Independently review the implementation and audit repository security with synthetic proofs.
      See SECURITY_REVIEW_2026-09-10.md; this is not clearance to launch with real consumer data.
- [ ] Apply AddClearinghouseResponseIntake only through reviewed migration/backup procedures.
      No deployment or real database migration is authorized by this development work.
- [ ] Clear the security review's remaining launch blockers: approved session-lifecycle rollout and
      privileged maintenance boundaries; resolve service-time concurrency with SQL Server tests.
      Ordinary consumer-record capability checks and local login projection are addressed by the
      September 11 follow-up above; this is not general launch clearance.
- [ ] Complete actual clearinghouse sandbox acceptance, supported variant/companion-guide checks,
      authorized raw receipt readback, rejected/corrected/void claim workflows, bank reconciliation,
      and approved API-mediated Production activation. Production import remains disabled.

## Unreleased — Overview scratchpad history and Umber Facets

- [x] Replace the scratchpad-history search modal with a History tab beside Today's Work and
      Tomorrow's Agenda in the Overview's centered Work Agenda.
- [x] Keep History out of the narrow side dock, preserve one live agenda instance, load history
      only when selected, and prevent an old account's delayed load from publishing after a switch.
- [x] Add a short direction-aware sliding transition, preserve reduced-motion behavior, and stack
      the date picker above results when the centered panel is compact.
- [x] Remove the obsolete history window, event, factory, and dependency registrations.
- [x] Add Umber Facets from the supplied brown intersecting-shapes palette as a complete vector
      theme with frosted work surfaces and measured dark-theme contrast.

## Unreleased — recoverable productivity forecast

- [x] Stop counting blank past weekdays as future productivity capacity; count only eligible days
      from today through month-end, with the post-deadline consequence using days after today.
- [x] Add the narrative-free shared `ProductivityForecast` rule for secured units, recoverable
      Pending-note units, units due today, expired Pending units, and unknown-duration warnings.
- [x] Show both the conservative secured pace and the projected pace after recoverable notes, with
      a non-color-only deadline warning that explains how the pace changes if today's units expire.
- [x] Keep Scheduled work out of performed backlog and never invent units for an activity or note
      whose duration is unknown.
- [x] Add focused calculation and UI-structure regressions.

## Unreleased — managed crash capture and diagnostic retention

- [x] Keep workstation diagnostics in the stable
      `%LOCALAPPDATA%\SatiLogica\Sati\Logs` folder and expose that exact path on the Admin screen.
- [x] Capture WPF dispatcher, background-thread, unobserved-task, and startup failures without
      copying exception messages or business payloads into the Admin incident envelope.
- [x] Retain the unclean-run marker as the fallback for native faults, forced termination, and
      power loss that cannot execute managed crash code; report it at the next authenticated launch.
- [x] Use per-process JSONL files, 5 MB rolling files, 30-day expiry, and a 50 MB folder ceiling.
- [x] Persist the authenticated Sati process name, PID, stable crash reference, and 30-second
      heartbeat; after an unclean restart, query Windows Application Error 1000 and .NET Runtime
      1026 in a bounded window. Require both process name and PID, retain late-WER work for a future
      launch, and show only allowlisted structured metadata in Admin. LocalDumps remain disabled.
- [x] Apply `20260910102153_AddCrashDiagnosticReadback` only through the controlled migration
      process before deploying this API build. On 2026-09-10 the identity-validated Demo run added
      one nullable, bounded metadata column to `IncidentGroups`; Local Production remains unchanged
      until each workstation launches the matching Local release.
- [ ] Complete the remaining `LOGGING_DESIGN.md` phases: shared plain-language error catalog,
      breadcrumbs and run-marker tail, user-created support bundle, framework logger provider, and
      desktop/API correlation-ID join. These remain documented work; raw logs must not be uploaded
      or copied into the agency incident table as a shortcut.

## Unreleased — what the platform design changes in Sati

From `PLATFORM_DOMAIN.md` and the two decisions of 2026-09-07. None of these waits on the
restructure, and all three get harder the longer they wait.

- [x] Rename the repository solution to `SatiLogica.slnx`. The current Sati projects are grouped
      under the `sati` solution folder, with empty `platform`, `karuna`, and `upekkha` folders
      reserving the agreed platform shape. CI, operational instructions, and source-based repository
      discovery use the new filename. Product assemblies, namespaces, executable names, database
      identities, and physical project folders deliberately remain unchanged in this stage.
- [ ] **Do not build the OADS Resource Coordinator as an agency user.** It is the first *authority*,
      not the second excluded role. Membership and authority are separate concepts now; building it
      the old way and migrating later means rewriting the exclusion filters and the audit records
      both. Supersedes the wording in "Classification and future OADS access" below.
- [ ] **Strengthen `MaineCareId` capture before a registry has to match on it.** Format validation,
      a filtered unique index per agency, and a decision about the rows that already disagree.
      `Person.BirthDate` is non-nullable while `FirstName` and `LastName` are not, which is the wrong
      way round for a matching key. This is the `Provider.Npi` lesson: match quality later is
      entirely a function of capture quality now, and capture cannot be done retroactively.
- [ ] **Stop widening the `Role != "PlatformOperator"` exclusion pattern.** Five queries carry it
      today and ten places branch on the role. Containment by subtraction does not survive a second
      cross-tenant identity; the number should not grow before authority lands.
- [ ] Answer the four open questions at the end of `PLATFORM_DOMAIN.md`. They need domain knowledge
      the repository does not contain, and none of them blocks restructure stages 1 and 2.

## Unreleased — Outlook calendar import

- [x] Add a one-time `.ics` import to the existing Calendar workspace and show Outlook subjects,
      times, and locations on the month, year, selected-day, and focused-day surfaces.
- [x] Parse the export locally, including basic daily/weekly/monthly/yearly recurrence rules,
      weekly day lists, exclusions, all-day entries, UTC values, and Windows time-zone identifiers; skip cancelled,
      duplicate, and unsupported entries with a visible summary.
- [x] Treat the import as a replaceable integration overlay rather than a Sati clinical record.
      Keep it separate by Windows profile, Sati user, and Demo/Production, and protect the local
      cache with the current Windows user's DPAPI key.
- [x] Cover parsing, recurrence, replacement, user/environment isolation, ViewModel projection,
      accessible labels, and the rendered UI surfaces with focused tests.
- [ ] If automatic synchronization is later required, design Microsoft Graph authorization,
      tenant administration, revocation, audit, retention, and BAA/responsibility boundaries before
      adding account tokens or server-side calendar data.

## Unreleased — measured theme legibility

Release 1.3.4 closed the contrast gaps that were visible to review. This work replaces review with
measurement: `Helpers/ThemeContrast.cs` owns the WCAG arithmetic and `ThemeLegibilityTests` holds
all twenty-five palettes to AA, both as token pairs and as rendered views. See `DECISIONS.md`.

- [x] Score every text role against every surface role, and every fill against the ink named for
      it, in all twenty-five themes. Correct the palettes by luminance only, never by hue.
- [x] Render every view under every theme and measure the resolved brushes, finding each run's
      background by hit test rather than by ancestor walk.
- [x] Fail on any theme key a view names that no dictionary defines. `WarningSoftBrush` was
      referenced by two workspaces and defined nowhere, which nothing had ever reported.
- [x] Give `CheckBox`, `RadioButton`, `TabItem`, `TabControl` and `Expander` application-wide
      styles. The framework paints their labels black and their chrome from system brushes no
      theme dictionary can reach, which is what put black labels on dark panels.
- [x] Stop using surface, border and text tokens outside their role — `SurfaceBrush` as ink,
      `BorderBrush` and `TextSecondaryBrush` as fills, `AccentPressedBrush` as type.
- [x] Take the destructive confirmation dialogs off literal light-theme colours; both left their
      confirm label at about 1.3:1 in every dark theme.
- [x] Add `PatternScrimBrush` and layer blur, scrim and controls in Settings so an illustrated
      theme stops competing with the densest screen in the application.
- [x] Add Walnut Linen, Deep Current, Redwood Blush, and Bodhi Watercolor as richer light themes:
      deeper brown, blue, and muted red families without night-mode light text or the yellow cast of
      the cream palettes. Bodhi Watercolor follows the attached Sati leaf's turquoise, blue, violet,
      and restrained coral sequence. All four pass the token and rendered-view AA gates.
- [ ] Retire the remaining hard-coded literal colours in `ShellWindow`, `AdminDashboardView` and
      `PlatformHealthView`. Each is a self-consistent light pair, so it stays legible and the audit
      does not flag it, but it does not follow the theme and looks wrong on a dark palette.
- [ ] Extend the rendered pass beyond the views that parse standalone. A handful cannot be built
      without their code-behind; the coverage floor is asserted so the gap cannot widen silently.
- [ ] Consider whether the scrim belongs on other control-dense screens. Only Settings was reported
      and only Settings was changed.

## Unreleased — one door into settings

- [x] Fold Profile and Password & Security into Settings ahead of the agency tabs, delete
      `MyAccountWindow`, and make the greeting badge the only header entry point. The gear is gone.
- [x] Keep `MyAccountViewModel` as a constructor-injected child of `SettingsViewModel`.
- [x] Keep switching accounts outside the window: it closes, then the shell runs the flow. Asserted,
      because reversing the order leaves a live window bound to the outgoing user over a new session.
- [x] Give the greeting badge focus, tab stop and Enter/Space bindings. It is a `Border`, so none of
      that came free once it became the only way in.
- [ ] Add a rendered test of the merged window. `SettingsViewModel` takes twelve dependencies, so
      the merge is currently covered by structure tests and by the theme legibility pass.
- [x] Decide what happens when Switch User is pressed with unsaved profile edits or a half-typed
      password change. `MyAccountViewModel.UnsavedWorkWarning` names what is lost and Settings
      confirms before closing.

## Unreleased — measured accessibility

`AccessibilityAuditTests` asks the automation peers what a screen reader would be told, using the
same view loader as the theme legibility audit. See `DECISIONS.md`.

- [x] Give every control a screen reader stops on a name to announce, and fail when a control is
      announced only as an icon glyph from the private use area.
- [x] Add `Helpers.ClickableSurface`, an attached command that supplies focus, the tab stop, Enter
      and Space, and a themed focus ring together. Convert the twelve navigation tabs and three
      list rows that were mouse-only.
- [x] Name the inner entry controls of `PasswordRevealBox`. A name does not travel down a visual
      tree, so the sign-in password field on three windows announced nothing.
- [x] Hold `TabIndex` at zero across the application and keep the focus visual switched on.
- [ ] Hands-on testing with Narrator and JAWS, including browse mode, reading order as a sentence,
      and whether live regions fire at useful moments. The automated pass is a floor, not a
      certificate, and none of those can be judged from the automation tree alone.
- [ ] Decide whether the converted surfaces should report a Button control type to UI Automation.
      They are operable and named, but a screen reader still announces them by their default type.
- [ ] Audit `Sati.Portal` separately. It is a web surface, so it needs semantic markup, labels and
      ARIA rather than UI Automation, and that is where TalkBack and mobile browsers would apply.
- [ ] Check keyboard operation at the two Easy Eyes scales and under Windows high-contrast themes,
      neither of which this pass exercises.

## Release 1.3.8 — 2026-09-13

"Safer access, living forms, current demonstrations." This release closes account/session and
consumer-record authorization gaps, preserves compliance and clearinghouse evidence, adds live
CWIC and Housing Support Funds packet builders, and keeps the canonical Demo calendar current.

- [x] Preflight resolved GitHub's default branch as `master`, found it equal to the reviewed
      `d9ac8724ee44c3ca7b0e2ab724f4fe2f6c633f70`, and found a clean tracked working tree before
      release coordination. No branch was merged or deleted. Unique or uncertain work remains on
      `claude/local-vs-github-workflow-dlcqpb`, `second-machine-setup`, `team-chat-design`, the
      local-origin-only `video-conferencing-design`, and worktree-owned
      `claude/cool-jang-f6b3c4`.
- [x] Confirm the releasable scope: retained account disablement/session revocation, current
      permission enforcement, non-destructive form retention, stricter note/billing concurrency,
      immutable clearinghouse response intake, consumer photos, live document previews, explicit
      Evergreen PCP/Comprehensive Assessment attestations, agency authoring gates, CWIC and Housing
      Support Funds packet builders, and rolling Demo seed dates.
- [x] Revalidate both publisher sources immediately before release. MaineHealth and Maine OADS
      still link bytes identical to Sati's embedded resources; hashes are recorded in the two
      unreleased sections above.
- [x] Apply the four new EF migrations to identity-validated Local `SatiProduction` and Azure
      `SatiDemo`: `20260911022820_AddClearinghouseResponseIntake`,
      `20260911120000_AddAccountSessionLifecycle`, `20260912053013_AddPersonPhotos`, and
      `20260913164040_AddOadsAuthoringSettings`. Both targets reported zero pending afterward;
      Local backup `SatiProduction-2026-09-13-194940.bak` was taken first. The Demo run was
      explicitly authorized, verified, and rerun as already current.
- [x] After separate explicit authorization, replace the synthetic `SatiDemo` canonical baseline
      with timeline anchor 2026-09-13. The reset route was closed during capture, all 53 resettable
      tables matched 53 baseline tables, the reset procedure was recreated, and the route was
      restored with the current protected host key. Updated refresh Function deployment
      `fb27d8750f6d4ef9a5d338cb0cff39b6` used a 51,209-byte package with SHA-256
      `AB21706936AF7D82CC74966FAB0DBDE65099D7C1BAEA85FF3C1F64290B1EC58C`.
- [x] The user removed `datt-workstation-temp` immediately after the controlled work; a read-only
      Azure listing confirmed it absent before release editing continued.
- [x] Complete coordinated 1.3.8 version/release notes and validation. The full Release build
      passed with 0 errors and 13 existing analyzer/nullability/NuGet-audit warnings. All five test
      projects passed: 2,885 passed, 7 skipped, 0 failed (790/5 API, 1,965/2 desktop/domain,
      118 Signatures, 8 Portal, 4 Carika). The skips were the documented local-AI competence test
      and six SQL Server checks whose external test connection was not configured. The symbolic
      replay covered all 102 migrations with 0 problems, and every package-bearing solution
      project reported no known vulnerable direct or transitive dependency from the current NuGet
      sources.
- [x] Commit source release `d209c367034586a4479ee8fee53dd7b7fe9af965` and push it normally to
      `https://github.com/heschides/Sati`, branch `master`. GitHub contained that exact commit before
      packaging began.
- [x] Publish only the existing Demo API. The 9,753,567-byte framework-dependent x86 package had
      SHA-256 `9C75C0929855F8CE266D7C9F00661E130A98B9238789D5B3A7B3B3EAA8CD3F60`, file
      version 1.3.8.0, 70 entries, and no private configuration or secret-like content. OneDeploy
      deployment `5e93037e1cf24738b70d442485bd641a` succeeded; live and ready returned HTTP
      200, `/health/version` reported Sati.Api 1.3.8 and contract revision `5B9D8ED7F252`, exactly
      matching the compiled client, and anonymous Admin access returned HTTP 401. The optional
      encrypted synthetic global Admin credential was absent, so the authenticated probe was not run.
- [x] Build new non-overwritten installers and pass isolated acceptance. Demo completed five
      responsive 15-second launches with graceful exits and cleanup; its 102,457,344-byte installer
      has SHA-256 `E842CBAAA016592046FF3677322113870B9487BACF450EFDA16C04BA3924174A`.
      Local passed version, integrated-security, embedded-prerequisite, and cleanup checks; its
      204,523,047-byte installer has SHA-256
      `2F35D39607E6ACF5944FE62E7228A82C2A4F0489F70B4A1F35059FEC15E3E0C8`.
      Each installer and its matching checksum were atomically published without overwrite to the
      designated `SatiLogica Demo Files` and `Sati Desktop` distribution folders, respectively;
      final hashes matched and no staging file remained.
- [x] Record and push final release evidence, confirm the tracked working tree is clean, and confirm
      local `master` equals GitHub `master`.

### Local Production machines

This release adds migrations 99 through 102. Demo and the development workstation's Local
`SatiProduction` are current, but installing a client and migrating a machine are separate facts.

- [ ] SatiLogica workstation: Local `SatiProduction` has all four migrations, but the installed
      Local client remains on its prior package until 1.3.8 is installed outside isolated acceptance.
- [ ] Joshu workstation: installed version and migration state remain unverified; treat it as
      behind until its operator installs 1.3.8 and confirms a successful Local startup.

## Release 1.3.7 — 2026-09-10

"Honest pace, richer color, safer recovery." This release separates completed productivity from
recoverable case-note backlog, adds four richer light themes, and carries safe Windows crash
diagnostics into the existing agency incident workflow after restart.

- [x] Implement and mutation-prove the shared seven-calendar-day productivity forecast, including
      secured units, recoverable units, today's deadline consequence, unknown-duration disclosure,
      and an old-server compatibility floor that refuses to let a zero-day payload break the UI or
      age notes prematurely.
- [x] Add Walnut Linen, Deep Current, Redwood Blush, and Bodhi Watercolor. All retain dark text on
      light surfaces and pass the measured WCAG AA token and rendered-view checks.
- [x] Add bounded local crash logs, managed fatal handlers, an authenticated PID/name/heartbeat
      run marker, exact Windows event correlation after restart, late-report retry, safe incident
      enrichment, and Admin readback. Raw Event 1026 text and LocalDumps remain excluded.
- [x] Apply `20260910102153_AddCrashDiagnosticReadback` to identity-validated Azure `SatiDemo`
      through `scripts/Apply-CrashDiagnosticReadbackMigration.ps1`: rollback-only dry run predicted
      one nullable column and history row 98; the real run created both; the second real run made
      no changes. The user removed the temporary exact-IP firewall rule immediately afterward and
      a read-only Azure listing confirmed it absent.
- [x] Release validation passed: the complete Release build finished with 0 errors and 9 existing
      warnings; all 2,216 required tests passed, with only the optional local-AI competence test
      skipped. Migration 98 matches the EF model, the symbolic migration replay found 0 problems,
      and NuGet reported no known vulnerable direct or transitive package.
- [x] Source commit `ad33bfa37c67b8b96d2dcbc391c2451007d23730` was pushed normally to
      `origin/master`; the remote contained that exact commit before packaging began. No branch was
      merged or deleted. `claude/cool-jang-f6b3c4` remains worktree-owned, and
      `second-machine-setup`, `team-chat-design`, `video-conferencing-design`, and remote
      `claude/local-vs-github-workflow-dlcqpb` remain because they contain unique work.
- [x] The clean framework-dependent x86 Demo API package
      `artifacts/SatiApi-1.3.7-fx-x86.zip` was built from that pushed source: 8,832,338 bytes,
      file version 1.3.7.0, no private settings or scratch output, and SHA-256
      `3DE023E9788F8F9767C9409B764069C4090EE49289625C2B0DD5C88EE6320E42`.
      OneDeploy deployment `16d13dd59cc44e0888a61c3487769072` succeeded to the existing Demo API
      only. Live and ready returned HTTP 200, `/health/version` reports Sati.Api 1.3.7 and contract
      revision `AF5889A2C7E5`, exactly matching the compiled client, and an anonymous Admin request
      returned HTTP 401. The optional synthetic Admin credential was absent, so the authenticated
      platform probe was not run. The prior 1.3.6 package and deployment evidence remain retained.
- [x] Both installers passed isolated acceptance and were published with matching checksum files,
      without overwriting an existing artifact. Demo completed five 15-second responsive sign-in
      launches, normal closes, exact version, and cleanup; `SatiDemoSetup-1.3.7.exe` is 101,421,056
      bytes with SHA-256 `AF648C66A0456A369919D776B6FBC9A7C39247A80BB70206D66FA3745409E763`
      in `SatiLogica Demo Files`. Local passed exact version, integrated security, the valid
      Microsoft-signed LocalDB prerequisite, and cleanup; `SatiLocalSetup-1.3.7.exe` is 203,721,767
      bytes with SHA-256 `63785180CDE724CBDED970B4A682B9DA8F36586B04BD97105DFC84BF5DD0F066`
      in `Sati Desktop`. The source artifacts, published executables, and checksum contents match.
      Demo acceptance was isolated on the development workstation, not attested on an external
      clean machine.
- [x] Final release evidence is recorded here; the evidence commit is pushed and local/remote
      equality is confirmed immediately after this ledger change.

### Local Production machines

This release adds migration 98. `SatiDemo` is migrated above; every Local Production database
applies the migration only when that machine first launches the 1.3.7 Local client.

- [ ] SatiLogica workstation: source builds are current, but the installed Local package and its
      `SatiProduction` launch state have not yet been rechecked; treat the installed copy as behind.
- [ ] Joshu workstation: installed version and migration state remain unverified; treat it as behind
      until its operator installs 1.3.7 and confirms a successful Local startup.

## Release 1.3.6 — 2026-09-09 (Demo API alignment only)

"Check requests, prepared and preserved." This source/API alignment makes the new representative-
payee check-request workflow reachable from the current debugger build. It is not an installer
release and does not deploy anything to Production.

- [x] Add the Check Requests workspace, immutable financial snapshot, shared publication rules,
      narrowly scoped DTOs and routes, tenant/caseload authorization, audit event, optimistic
      concurrency, deterministic PDF rendering, and focused desktop/API tests.
- [x] Apply `20260909153255_AddCheckRequests` to identity-validated Azure `SatiDemo` with the
      existence-guarded `scripts/Apply-CheckRequestMigration.ps1`: rollback-only dry run succeeded,
      the real run created the table and migration-history row 97, and a second real run made no
      changes. The user removed the temporary exact-IP firewall rule immediately afterward; a
      read-only Azure listing confirmed it absent.
- [x] Release validation: the full Release solution build succeeded with zero errors and nine
      existing warnings. All five test projects passed: 2,184 passed and one documented optional
      local-AI evaluation skipped. The first post-version test run caught the intentionally changed
      release name's stale expected value; after that ledger assertion was corrected, the complete
      affected desktop suite passed 1,553 with the same one skip.
- [x] Source commits `c4cdb0665dfd6557978310dd3b270392926e8e7c` and
      `b0261aa5525da9b279b9926fac73f4f1b65379e3` are pushed on `master`. The second explicitly
      excludes ignored API test/publish scratch directories after pre-deploy ZIP inspection found
      that the Web SDK's default content glob would otherwise include them.
- [x] Clean API package `artifacts/SatiApi-1.3.6-fx-x86.zip` was built from pushed commit `b0261aa`:
      8,819,873 bytes, 68 intended entries, file version 1.3.6.0, no private configuration or scratch
      output, and SHA-256 `7CF6F2509B3BACF83E82F6B1220E028CB416F8FE093083D753F2DE504622B4F8`.
      OneDeploy deployment `1d06555ef0b54b81830e643b5f8244c3` succeeded to the existing Demo
      API only. Live and ready returned HTTP 200, `/health/version` reports Sati.Api 1.3.6 and
      contract revision `AF5889A2C7E5`, exactly matching the compiled client, and the new protected
      route returned HTTP 401 anonymously. The encrypted Global Admin credential was not present on
      this workstation, so that optional authenticated platform probe was not run.
- [x] No 1.3.6 installer was authorized or produced by this alignment. The published 1.3.5 Demo
      installer expects the prior contract and will correctly refuse the newer API; use the current
      debugger build until a separately invoked full release produces and accepts matching 1.3.6
      installers.

## Release 1.3.5 — 2026-09-07

"A clearer calendar, softly framed." This release brings a protected, one-time Outlook calendar
export into the existing calendar, makes month/year navigation more direct, measures theme and
automation accessibility across the application, consolidates account settings, and applies the
frosted-content rule consistently to auxiliary windows under illustrated themes.

- [x] Add a local-only `.ics` overlay separated by Windows profile, Sati user, and data environment;
      protect its replaceable cache with DPAPI and keep imported items out of notes, billing,
      compliance, exemptions, the API, and the database.
- [x] Present Outlook subjects, times, and locations across year, month, selected-day, and
      focused-day calendar surfaces with bounded recurrence expansion, visible import results,
      keyboard controls, and non-color accessible labels.
- [x] Hold all twenty themes to measured WCAG AA text/surface contrast and use one shared attached
      behavior for keyboard-accessible, named interactive surfaces.
- [x] Keep decorative artwork crisp only on open shell canvas; use frosted theme surfaces behind
      navigation, working panels, splash, sign-in, settings, account switching, confirmations, and
      the other auxiliary windows.
- [x] Fold Profile and Password & Security into Settings and preserve the account-switch ordering
      that closes the outgoing user's window before replacing the session.
- [x] Add the platform-domain/restructure design records and a synthetic-demo functionality
      voiceover script without representing planned Karuna, Upekkha, OADS, signature, chat, or
      compliance capabilities as deployed.

**No EF migration.** The model matches the 96-entry migration chain, the Local Production database
on this release workstation reports every migration applied, and hosted Demo readiness was healthy
before release. Outlook calendar data is an encrypted client-local overlay, not a database table.

- [x] Preflight: `origin/master` remained at release 1.3.4; version 1.3.5 and its API, Demo installer,
      Local installer, checksum, and distribution names were unused. The active branch contains four
      completed commits ahead of `master`; other branches retain unique or worktree-owned material
      and are not eligible for deletion. No cloud migration, Production deployment, baseline capture,
      or firewall change is required or authorized.
- [x] Release validation: the full Release solution build succeeded with zero errors and nine known
      warnings; all five test projects passed (2,175 passed, one documented optional local-AI
      evaluation skipped); eight release/installer PowerShell entry points parsed cleanly; the
      online NuGet advisory audit found no vulnerable direct or transitive packages; the 96-entry
      migration replay found no defects; EF found no model changes after the latest migration; and
      the rendered theme/calendar/accessibility suites exercised the packaged visual behavior.
- [x] Source commit `8ee618f7fd28073f4d9d012e5c5036fe89dc6c71` was fast-forwarded onto
      `master` and pushed normally; local and remote `master` matched afterward. The completed
      `ui-legibility-and-accessibility` branch was deleted locally only after its tip was fully
      contained in `master`. Other branches were retained because they have unique commits or are
      checked out by another worktree.
- [x] Demo API package `artifacts/SatiApi-1.3.5-fx-x86.zip` was built from the pushed source commit:
      8,907,368 bytes, SHA-256 `E3290D0B5FF49F4C768F0058B14643525F4E3C97EA3F3EBF2F2E4367377758A7`,
      file version 1.3.5.0, with no private configuration. OneDeploy deployment
      `658f05f6a17d442f932659fc3c64e148` succeeded to the existing Demo API only. Live and ready are
      healthy; `/health/version` reports 1.3.5 and contract revision `CC236B20EF18`; authenticated
      Admin readiness passed. No SQL, firewall, Function, baseline, or Production operation ran.
- [x] Both installers passed isolated acceptance and were published with matching checksum files
      without overwriting earlier artifacts. Demo completed five responsive launches, normal closes,
      exact version, and cleanup; `SatiDemoSetup-1.3.5.exe` is 101,339,136 bytes with SHA-256
      `D4852283DD4CF12AC419B88372BA09592152D61E35B76C2D10307447DDD845FB` in
      `SatiLogica Demo Files`. Local passed exact version, integrated security, the Microsoft-signed
      LocalDB prerequisite, and cleanup; `SatiLocalSetup-1.3.5.exe` is 203,659,016 bytes with SHA-256
      `B0D09BED961296644DF08CB0EE7A2543FEEEB10D280B187351AF419C5201B02C` in
      `Sati Desktop`. Source artifacts, published executables, and checksum contents match, and no
      temporary distribution file remains.
- [x] Evidence commit `cac1046da794cb2621c5284262933abe6b1526f5` is pushed; this final ledger
      entry indexes it. Final verification confirmed a clean tracked worktree and equality of local
      `master` with `origin/master`.

## Release 1.3.4 — 2026-09-07

"A clean slate, clearly seen." This release makes the complete synthetic Demo recoverable on
demand, closes the remaining dark-theme contrast gaps, and softens decorative patterns behind
working content.

- [x] Fix remaining theme-owned text pairs in menus, selected states, action labels, and the shell
      greeting so dark palettes do not render dark-on-dark text.
- [x] Keep the five decorative themes crisp on open canvas and move their blurred, faint motif into
      frosted content and navigation surfaces so labels and calendar text never sit directly on the
      pattern; align the calendar header within a consistently padded glass panel.
- [x] Enlarge and vary the Mid-Century Modern repeat with modest shape rotation and scale changes;
      redraw Paisley with layered hooked boteh, inner curls, foliage, and multiple orientations.
- [x] Add Vanilla Bean as a complete creamy ivory, caramel, and brown theme, with subtle cream folds
      and irregular vanilla-seed flecks that recede beneath frosted text-bearing surfaces.
- [x] Add a Demo-only Admin command with an explicit `RESET DEMO` typed confirmation. Route it
      through the Administration-authorized API and a separately authenticated reset Function;
      Local Production has no reset implementation and the API route is absent outside Demo.
- [x] Add controlled capture of the approved current superhero/TV database as a protected baseline,
      a schema-drift-refusing owner-executed restore procedure, full-table restoration, rolling-date
      validation, mutation exclusion, and database-instance token invalidation.
- [x] Add API authorization/session invalidation tests, WPF structure/contrast tests, and reset
      asset safety checks. Debug build completed with zero errors and nine known warnings; 2,098
      tests passed across all five projects, with the one explicitly optional local-AI evaluation
      skipped, and all nine release/reset PowerShell entry points passed parser validation.
- [x] During the separately authorized release, capture the reviewed cloud baseline, publish the
      Function and API, store its endpoint and Function key only in API settings, and run a live
      destructive acceptance test. A workstation capture requires the user-managed exact-IP SQL
      firewall rule; the user removed it immediately afterward and the release verified it absent.
- [ ] Configure an approved external destination for reset-failure alerts and prove one test alert.
      The existing Smart Detection action group has no receiver and there are no metric alerts, so
      this remains an explicit operational follow-up rather than a claimed release capability.

**No EF migration.** The baseline schema and reset procedure are controlled Demo-only operational
objects, deliberately outside the application migration chain. Nothing in this work applies to or
reads Local Production.

- [x] Preflight: `master` equals `origin/master`; version 1.3.4 and its API/installer artifact names
      are unused. The current synthetic Demo was separately approved as the canonical baseline.
      The user-created exact-IP SQL rule matches this workstation at `72.95.106.10`; the release
      workflow will not alter it and the user must remove it immediately after baseline capture.
- [x] The first 1.3.3 API package was a failed partial deployment only: live acceptance proved that
      Azure's PowerShell Function drops an unbuffered .NET JSON body before invocation. No 1.3.3
      installer was built or distributed. The completed release advances to 1.3.4 and buffers the
      two-field API request before sending it; an exact buffered probe completed the reset.
- [x] Release validation: full Release build succeeded with zero errors and nine known warnings;
      all five test projects passed (2,098 passed, one documented optional local-AI test skipped);
      all nine release/reset PowerShell entry points parsed cleanly; and the dependency audit found
      no vulnerable direct or transitive NuGet packages. Final 1.3.4 source commit
      `d0c8e07ded2b621e966d4a82e92e9a381dc7ed28` is pushed to `master` and `origin/master`.
- [x] The identity-checked baseline initializer completed after the locking API was live. Final
      Function deployment `dd1d2081845448189b2232d0da20b785` used a 9-entry, 45,462-byte package
      with SHA-256 `9EDD059C3315893B936745627A4DD5E00A1226E4E6227E57BD692844E0403DFB`.
      Its default host key and HTTPS endpoint are stored only in protected API settings and match
      the Function without being printed or committed. Final API deployment
      `e1084094d4d1490b94ce70d28d84cb04` used
      `artifacts/SatiApi-1.3.4-fx-x86.zip` (8,797,215 bytes, SHA-256
      `A761646F26228208AD0F2995CA35896F50BDC25706C5AF72459DC8F776591FB4`). Live and
      ready are healthy, `/health/version` reports 1.3.4 and contract revision `CC236B20EF18`,
      equal to the desktop contract. Authenticated readiness passed. The live Admin reset returned
      completion, rejected the pre-reset token, restored the canonical Admin login, and restored
      the Admin overview. Temporary diagnostic logging is Off.
- [x] Both installers passed acceptance and were published without overwriting prior artifacts.
      Demo passed five responsive launches, graceful closes, exact version, and cleanup;
      `SatiDemoSetup-1.3.4.exe` is 101,335,040 bytes with SHA-256
      `286324C3DD61CE7921749D9C9B95496C31466A20D8A518190E9C62EA07A67262` in
      `SatiLogica Demo Files`. Local passed exact version, Windows integrated security,
      Microsoft-signed embedded LocalDB, and cleanup; `SatiLocalSetup-1.3.4.exe` is 203,360,520
      bytes with SHA-256 `85F0249C92BBC69AB1602F84281905F82FE61B98144AD7BA2EB808E2E914724A`
      in `Sati Desktop`. Both published executables and checksum files exactly match their accepted
      artifacts, and no temporary distribution file remains.
- [x] Evidence commit `9e48cafd1acbcf1185271fe35cb6e391b6ae7bf6` is pushed; this checked
      ledger entry indexes that commit. Final verification confirms a clean working tree and local
      `master` equal to `origin/master`.

**Branches.** No branch is eligible for merge or deletion. `video-conferencing-design` contains
the unrelated ACS design work identified by the user; `team-chat-design`, `second-machine-setup`,
and `origin/claude/local-vs-github-workflow-dlcqpb` retain unique work; the fully merged
`claude/cool-jang-f6b3c4` remains checked out by another worktree, whose untracked `AGENTS.md` is
untouched.

## Completed 2026-09-06 — billing-safe Demo diagnoses

- [x] Give every synthetic Demo client a deterministic, valid fictional diagnosis from a varied
      ICD-10 teaching set. Diagnosis is a hard billing gate and is no longer permitted as an
      intentionally missing teaching exception.
- [x] Keep the second teaching profile as a diagnosis-verification lesson without clearing its
      billable code, and validate every refreshed Demo client after commit so a missing or
      unexpected diagnosis makes the refresh fail visibly.

**No schema change.** This updates synthetic Demo seed values and validation only.

- [x] Source commit `ffd09c87107e5d0769eca6592468c7781a4c1bf2` is pushed to `master` and
      `origin/master`. The complete desktop/domain suite passed 1,461 tests with the one optional
      local-AI evaluation skipped because its explicit on-device authorization flag is absent.
- [x] Existing Demo Refresh Function deployment `51f4da260ef6466ba9b4195f438d87a0` succeeded
      from a six-entry, 22,168-byte package with SHA-256
      `9D5F0F52CCF40CEE396FC35C92BD73CE83E7302C1703D0F40D2A14B7D1B5A55C` and no
      private settings or key files. No API, installer, infrastructure, identity, settings,
      firewall, schema, or Production change was made.
- [x] The authorized one-time invocation `4b4da0f5-d23e-4986-ab15-fe3065f08329` completed
      successfully in 30.6 seconds. The canonical refresh's post-write validation passed, proving
      that no Demo client remained without an approved fictional diagnosis.

## Release 1.3.2 — 2026-09-06

"Claims in the right lane." This release makes Submit & Lock visibly move work from the draft
queue into 837 staging, quarantines invalid historical claims before generation, and repairs the
daily synthetic Demo claim snapshots that exposed the missing-diagnosis problem.

- [x] Replace the implicit submitted-date-range behavior with a visible 837 staging grid. Submit
      and Lock removes a period from the Draft selector and places it in staging; generation uses
      the checked staging rows, removes successful rows immediately, and announces that they were
      captured in the generated files.
- [x] Derive staging from exact frozen-claim readiness and absence of exchange history. Historical
      Submitted rows that fail today's gate are shown separately as Blocked before 837 and can
      never be offered to the generator as ready.
- [x] Add an audited Return to Draft transition for pre-exchange periods only. The API and Local
      paths share `BillingPeriodWorkflow`, preserve agency boundaries, and serialize the return
      against 837 generation. Periods with any 837 or clearinghouse history cannot be rewound.
- [x] Repair every synthetic Demo claim's direct identifiers and frozen snapshot during the
      canonical daily refresh, including a fictional F89 diagnosis fallback for the deliberately
      incomplete diagnosis teaching profile. Expand the post-refresh assertion beyond units,
      charge, and snapshot presence so this seed defect cannot recur silently.

**No schema change.** Staging is derived from BillingPeriod, exact readiness, and existing exchange
events. The new API route requires a matching Demo API and desktop build before distribution.

- [x] Preflight: `master` equals `origin/master`; the release scope is the reviewed billing
      staging, guarded return-to-draft workflow, and synthetic Demo claim repair. Version 1.3.2 and
      all intended artifact names are unused. No persistence schema changed, so no migration or
      workstation firewall rule is required. The user separately authorized publishing the
      existing Demo Refresh Function code without changing its identity, settings, network rules,
      or database schema.
- [x] Release build succeeded with zero errors. All five automated test projects passed: 1,461
      desktop/domain, 494 API integration/authorization, 118 signature, 8 portal, and 4 Carika
      tests (2,085 total). The optional local-AI competence test was skipped because its explicit
      on-device authorization flag is absent. The Demo seed script also passed PowerShell parsing.
      The 13-project dependency audit found no known vulnerable packages, and the changed-source
      credential scan found no credential or private-key pattern.
- [x] Source commit `224d6a17e4dc5aa222faeb5ba27ffe8fb3cead60` is pushed to `master` and
      `origin/master`. A concurrent design task switched the shared checkout immediately before
      the first commit, so the release patch was safely cherry-picked onto `master` without its
      unrelated ACS design document; no force operation or history rewrite was used.
- [x] Demo API deployment `a5a903c9faf842a68954b205a5c8b830` succeeded from the pushed source
      commit. `artifacts/SatiApi-1.3.2-fx-x86.zip` is 8,790,055 bytes with SHA-256
      `5603D8D4811B15954203DE0CCDD63594013E2F95E6934208710218B5009AF318`, contains
      68 forward-slash entries, no private settings or key files, and both reconciliation WebJob
      files. Packaged `Sati.Api.dll` reports file version 1.3.2.0 and product version
      `1.3.2+224d6a17e4dc5aa222faeb5ba27ffe8fb3cead60`. Live and ready are healthy;
      `/health/version` reports Sati.Api 1.3.2 and contract revision `6426D2D85A15`, equal to the
      desktop contract. Authenticated readiness was skipped because designated synthetic Admin
      credentials were not supplied to the release process.
- [x] Existing Demo Refresh Function deployment `7fdbcdb7b9b947bb94f8fa69b6ca55e3` succeeded
      from the same source commit. Its six-entry package is 21,864 bytes with SHA-256
      `687C850923016B64AEC782D6C0B792F375E88F68C86DD4E90C1F13FC7E0E4850`; it contains
      the repaired seed script and no private settings or key files. The Function App remains
      running, its managed-identity ID is unchanged, and `RefreshCaseload` is present and enabled.
      No infrastructure, identity, settings, firewall, database, or schema change was performed.
- [x] Both new installers passed acceptance and were published without overwriting prior files.
      Demo passed five responsive launches, normal closes, exact version, payload, and cleanup; it
      is 101,330,944 bytes with SHA-256
      `47F4CFBEC638B497F1BD4197CCE87C2158EC2683E8FC6FD33126853C34D647E5` in
      `SatiLogica Demo Files`. Local passed exact version, Microsoft-signed embedded LocalDB,
      integrated security, payload, and cleanup; it is 203,636,488 bytes with SHA-256
      `65CDE53216D5ED06C59F3FAAA5593F5B8B061723EEB048142CBAE54F24522BC5` in
      `Sati Desktop`. Each final executable matches its published checksum file, and no temporary
      distribution file remains. The generated Sati installers themselves are not assumed to be
      code-signed.
- [x] Evidence commit `e3591dbcbd1c0cb193f5c4588e79d17c735b7055` is pushed; this checked
      ledger entry indexes that commit. Final verification confirms a clean working tree and local
      `master` equal to `origin/master`. No Production deployment, database migration, firewall
      change, or branch deletion belongs to or was performed by this release.

**Branches.** No branch was merged or deleted. `video-conferencing-design` is retained because it
contains Claude Code's unique ACS design work; `team-chat-design` and `second-machine-setup` remain
because they also contain unique work. The fully merged `claude/cool-jang-f6b3c4` branch remains
because its linked worktree is still checked out.

## Release 1.3.1 — 2026-09-06

"Visible claims and quiet installs." This release makes the exact frozen billing rows visible before
submission, keeps submitted periods moving forward through the 837P workflow, replaces installer
console flashes with a branded progress surface, and adds four decorative but work-safe themes.

- [x] Make the Billing Period selector an actionable draft queue: show only claim-bearing draft
      periods there, and remove a period immediately after it is submitted and locked while keeping
      it available in the 837 generation range and Submission Home.
- [x] Show every frozen claim line for the selected draft period beside the selector, including the
      client, service date, units, charge, procedure, diagnosis, readiness state, and specific
      correction needed.
- [x] Give the desktop-local and API paths one shared `ProfessionalClaimReadiness` rule for the
      exact frozen 837P row. Run it immediately after claim construction and again when periods are
      loaded, submitted, and generated so the preview and authoritative gate cannot disagree.
- [x] Preflight: `master` equals `origin/master`; the release scope is limited to the reviewed
      billing-readiness, installer-presentation, and theme work plus coordinated release records.
      Version 1.3.1 and all four intended distribution filenames are unused. No migration or mapped
      persistence schema changed, so no Demo migration, workstation firewall rule, Production
      deployment, or Production database action is required or authorized.
- [x] Version 1.3.1 is coordinated across the desktop, API, installer builders, readiness checks,
      explicit version assertions, installer examples, and Settings release notes.
- [x] Remove visible PowerShell/command windows from both installer handoffs. Demo uses a validated
      Windows Script Host bridge and Local uses a console-free process; both installation scripts
      show the same accessible Sati-branded indeterminate progress window. The real Windows
      elevation prompt remains when Microsoft LocalDB must be installed.
- [x] Add four complete decorative palettes to Settings: Ironworks Matte, Paisley, Art Nouveau,
      and Mid-Century Modern. Each uses a lightweight tiled vector motif on shell/navigation chrome
      while keeping content surfaces quiet, supplies the full theme contract, renders at runtime,
      and meets 4.5:1 primary-button text contrast.
- [x] Release validation passed. The complete solution builds with zero errors and nine known
      warnings; 2,077 automated tests passed, zero failed, and one optional local-AI evaluation was
      skipped because its explicit on-device authorization flag is absent. The dependency advisory
      audit covered all 13 projects and found no known vulnerable packages. The changed-source
      secret scan found no credential or key patterns, and the model-consistency test reports no
      pending EF model change.
- [x] Verified source commit `d5dd80a` is on `master` and `origin/master`. Demo API deployment
      `bb0082d0db7d4adabb27badf9ca57cfa` succeeded from the 8,788,056-byte 32-bit
      framework-dependent package with SHA-256
      `c958b605f6622d27ab6d52dfc668fe80b4c1f01f06708e45055f9c370a39abc9`.
      The package reports product version `1.3.1+d5dd80a6787b0d489a68251f53888f02e47ef36a`,
      contains no private settings or key files, and retains both reconciliation WebJob files.
      Live and ready are healthy; `/health/version` reports Sati.Api 1.3.1 and contract revision
      `2AA88BC58832`, equal to the desktop contract. Authenticated readiness checks were not run
      because designated synthetic Admin credentials were not supplied to this release process.
- [x] Both new installers passed exact-version acceptance and were published with matching checksum
      files without overwriting prior releases. The Demo installer passed five responsive launches,
      normal closes, version, hidden-script handoff, payload, and cleanup checks; it is 101,314,560
      bytes with SHA-256
      `497c5f813270f6b6081136728dee40e884ce35572d6583ccddbb2aba34b73ed6` in
      `SatiLogica Demo Files`. The Local installer passed version, Microsoft-signed embedded LocalDB,
      integrated-security, hidden-script payload, and cleanup checks; it is 203,626,248 bytes with
      SHA-256 `e2c751770965f7cf11889ed423ce4182c207804626a79f136238c37928d19b68`
      in `Sati Desktop`. The shared branded Demo and Local progress surfaces each passed a visible
      open/update/close lifecycle check. Final copies and checksum contents were independently
      reverified; no temporary distribution file remains. The generated Sati installers themselves
      are not assumed to be code-signed.
- [x] Evidence commit `a59990b` is pushed; this checked ledger entry indexes that commit. No
      Production API deployment, Production migration, Demo migration, or firewall change belongs
      to or was performed by this release.

**Branches.** `team-chat-design`, `second-machine-setup`, and
`origin/claude/local-vs-github-workflow-dlcqpb` are retained because they contain unique work. The
fully merged `claude/cool-jang-f6b3c4` branch is retained because its linked worktree is still
checked out.

## Release 1.3.0 — 2026-09-06

"Private handoffs and test claims." This release prevents outgoing-account content from being
displayed during an account change and completes the synthetic Demo 837P response loop. The mock
workflow remains Demo-only and does not transmit a claim to a real clearinghouse or payer.

- [x] Make account replacement a full-shell privacy barrier: cover the shell before either account
      dialog, save or cancel safely, clear every shell-lifetime account workspace before installing
      the replacement identity, load the new workspace behind the shield, and reject late results
      from old-account asynchronous requests.
- [x] Connect the existing Demo-only mock clearinghouse to Billing Submissions. Require explicitly
      generated test 837P files, submit their exact retained content once, record the synthetic
      transmission plus selected 999/277CA/835 path, and refresh Submission Home for review.
- [x] Preflight: `master` equals `origin/master`; the release scope is limited to the reviewed
      account-handoff and mock-clearinghouse work plus coordinated release records. No migration,
      persistence model, or API schema file changed, so no Demo migration, workstation firewall
      rule, or Production deployment is required or authorized.
- [x] Version 1.3.0 is coordinated across the desktop, API, installer builders, readiness checks,
      version assertions, installer examples, and Settings release notes.
- [x] Release validation passed. The complete solution builds with zero errors and nine known
      warnings; 2,071 automated tests passed, zero failed, and one optional local-AI evaluation was
      skipped because its explicit on-device authorization flag is absent. The dependency advisory
      audit covered all 13 projects and found no known vulnerable packages. The changed-source
      secret scan found no credential or key patterns, and the model-consistency test reports no
      pending EF model change.
- [x] Verified source commit `dd2f6d5` is on `master` and `origin/master`. Demo API deployment
      `0c230dba9bdc455f8afb5dd45fe96410` succeeded from the 8,781,175-byte 32-bit
      framework-dependent package with SHA-256
      `0a5d12c72233b90f693ea07fb7993306350bcb071ee3e892c1011e62a42e9f98`.
      The package reports product version `1.3.0+dd2f6d5fc1ef44ecf31429353389266c44f28dcf`,
      contains no private settings or key files, and retains both reconciliation WebJob files.
      Live and ready are healthy; `/health/version` reports Sati.Api 1.3.0 and contract revision
      `2E69F7DDF962`, equal to the desktop contract. Authenticated readiness checks were not run
      because designated synthetic Admin credentials were not supplied to this release process.
- [x] Both new installers passed acceptance and were published with matching checksum files without
      overwriting prior releases. The Demo installer passed five responsive launches, normal closes,
      exact version, and cleanup; it is 101,310,464 bytes with SHA-256
      `d9ef7b4db45e0ad5ea48b4f4f6085a6d9219fb8342ca162a1dad07250451a156` in
      `SatiLogica Demo Files`. The Local installer passed exact-version, Microsoft-signed embedded
      LocalDB, integrated-security, and cleanup checks; it is 203,616,008 bytes with SHA-256
      `8ec71359a05b3414e5256d605f9e87b038dcfde07b7c3d823e9d134346c2088f` in
      `Sati Desktop`. Final copies and checksum contents were independently reverified; no
      temporary distribution file remains. The generated Sati installers themselves are not
      assumed to be code-signed.
- [x] Evidence commit `697e0c2` is pushed; this checked ledger entry indexes that commit. No
      Production API deployment, Production migration, Demo migration, or firewall change belongs
      to or was performed by this release.

**Branches.** `team-chat-design`, `second-machine-setup`, and
`origin/claude/local-vs-github-workflow-dlcqpb` are retained because they contain unique work. The
fully merged `claude/cool-jang-f6b3c4` branch is retained because its linked worktree is still
checked out.

## Release 1.2.49 — 2026-09-06

"Clear queues and current demos." This release carries the supervisor approval filters and
newest-first paging, billing-period names and 837P readiness guards, clearer administrator
password-reset outcomes, the room-dock chat presentation, and the versioned daily Demo caseload
refresh worker. The refresh remains synthetic-only and is distinct from a full baseline reset.

- [x] Preflight: `master` equals `origin/master`; no migration file changed or was added;
      `SatiContext` reports no pending model change; the temporary SQL firewall rule is absent.
      No Demo migration or Production deployment is required or authorized.
- [x] The daily Demo refresh was deployed separately under its restricted managed identity and
      passed a live run plus an immediate repeat run before this DATT release began.
- [x] Version 1.2.49 is coordinated. The Release build completed with zero errors and nine known
      warnings; 2,067 automated tests passed, zero failed, and one optional local-AI case was
      skipped. The dependency advisory audit covered all 13 projects and found no known vulnerable
      packages. Focused security, paging, rendering, billing, and password-reset tests are included
      in that passing total, and the changed-source secret scan found no credentials or keys.
- [x] Verified source commit `34e06ff` is on `master` and `origin/master`. Demo API deployment
      `157642b8a4ad4dc6ac25b0240115e402` succeeded from the 8,780,461-byte 32-bit
      framework-dependent package with SHA-256
      `cafee80876708b3b8398d04713f0e4066d862f689567856e93f9fe92cfc5ff94`.
      Live and ready are healthy; `/health/version` reports Sati.Api 1.2.49 and contract revision
      `2E69F7DDF962`. The supervisor-filter route also returned 401 without authentication.
- [x] Both new installers passed acceptance and were published with matching checksum files without
      overwriting prior releases. The Demo installer passed five responsive launches, normal closes,
      exact version, and cleanup; it is 101,281,792 bytes with SHA-256
      `821358eb4202d1db27b99a714579212b7b683655940cde05cc2ca4f8445f4d40` in
      `SatiLogica Demo Files`. The Local installer passed exact-version, Microsoft-signed embedded
      LocalDB, integrated-security, and cleanup checks; it is 203,330,314 bytes with SHA-256
      `6b98a068bd77bc5401638beebe0d12f4d01a5a464826e15ae328ad7246896268` in
      `Sati Desktop`. Final copies and checksum contents were independently reverified.
- [x] Evidence commit `57e2572` is pushed; this checked ledger entry indexes that commit. No
      Production API deployment, Production migration, Demo migration, or firewall change belongs
      to or was performed by this release.

**Branches.** Fully merged `signature-portal-design` was safely deleted locally and remotely at
tips `9c19af4` and `359e8ef`. `team-chat-design`, `second-machine-setup`,
`origin/claude/local-vs-github-workflow-dlcqpb`, and the linked
`claude/cool-jang-f6b3c4` worktree are retained because they contain unique, uncertain, or active
context.

## Release 1.2.48 — 2026-09-06

This release carries the reviewed in-app team chat and synthetic electronic-signature portal work.
Both features remain disabled by default; the signature portal is restricted to synthetic testing.

- [x] Demo migration 96 applied to the identity-validated `SatiDemo` database after explicit
      authorization, using a guarded exact-history check. The two new migrations added the chat and
      signature schema; the temporary workstation firewall rule was removed and verified absent.
- [x] Release build and automated verification passed: 2,036 passed, 0 failed, 1 optional AI case
      skipped; nine simulated portal-page checks passed. Demo desktop build succeeded with two
      existing EF1002 warnings. Detailed evidence is in `SIGNATURE_PORTAL_VALIDATION.md`.
- [x] API route inventory and documentation reconcile to 168 protected routes; dependency advisory
      lookup covered 13 restored projects and found no known vulnerable packages.
- [x] Source commit `9c19af4` is on `master` and `origin/master` without history rewriting. The
      Demo API deployment `7e5d70b57e574cb5bfd27262123c2ff4` reports live, ready, and release
      `1.2.48`; its 32-bit framework-dependent package is 8,772,786 bytes with SHA-256
      `42b3dde9daf3d05a158d7f91446f2836f56dfce25ea31af2ad9063a732ab324f`.
- [x] Accepted installers are published without overwriting prior versions. Demo is 101,277,696
      bytes (`bdec62e1521d7bdabbe50e9106b90c4a122d80a0176fd0d4a138ca2c80061dbd`) in the Demo
      distribution folder; Local is 203,597,066 bytes (`7aac1f222b21a2e2f71f1c8764cd36f415158797104fb44bb2cd35bd4c78a8d9`) in the Desktop folder. Both checksum files match.
- [x] Final evidence commit `5e2a3f9` is on `master` and `origin/master`. No Production API
      deployment or Production migration was performed or authorized by this entry.

### Local Production machines

This release changes the local schema as well as the Demo schema. Demo migration 96 is applied;
the desktop applies the same pending migrations when each Local Production machine next starts.

- [ ] SatiLogica workstation: installed version is not rechecked after the release; treat it as
      behind until `1.2.48` is installed and the startup migration completes.
- [ ] Joshu workstation: version remains unverified and must be treated as behind until its
      operator installs `1.2.48` and confirms successful startup migration.


## Unreleased follow-up — fixed Overview roles, faster Statistics, and structured Today's Work

- [x] Remove the Overview Workspace selector, Focus note mode, duplicate Notes workspace, and the
      obsolete center-layout setting and local preference service.
- [x] Keep Current note left, Work Agenda center, and Upcoming Due Dates right at desktop widths;
      stack those same live panels below 1080 effective units. Keep only Productivity in the lower
      center band, shown when at least 700 effective height units are available.
- [x] Replace the compact note header's repeated client name with the nearest open, ready-to-open,
      upcoming, or overdue form state and its relevant dates.
- [x] Replace Statistics' full yearly-note reads with a narrative-free date/minutes projection and
      actor-scoped monthly API totals. Start independent report reads together, show progress and
      load failures, and prevent an older request from overwriting a newer date filter.
- [x] Add local range/scope tests, API tenant isolation and route-manifest coverage, form-status
      tests, and responsive WPF render checks. No database schema change or migration is required.
- [x] Keep the freeform Today's Work scratchpad while grouping today's Scheduled notes into
      Paperwork, Visits, Calls, Emails, and Freeform. Keep Work Agenda in the center at desktop
      widths and bound the structured list so the scratchpad remains usable under Easy Eyes.
- [x] Split new Contact entry into Phone and Email without renumbering legacy enum values. Preserve
      a future plan's specific type, optional form type, and estimated minutes while its actual start
      time remains unset.
- [x] Turn selected sign-in paperwork into retry-safe Scheduled Form notes and omit Scheduled-note
      duplicates from the prompt. Start a row in the existing note panel as an unsaved Pending draft,
      updating that same row only on Save.
- [x] Default started work to the earliest five-minute-grid opening that fits its estimated minutes;
      retain client, type, date, and bracketed replacement text. Support Start, double-click, Enter,
      keyboard focus, screen-reader names, inline load failure, and narrow rendered layouts.

Validation: the complete solution builds with zero errors; 1,340 desktop/domain tests pass with the
one documented optional local-AI competence test skipped, all 386 API tests pass, and all 4 Carika
tests pass. The real Work Agenda view was also rendered at a 360-by-700 effective Easy Eyes width;
all five groups scroll vertically, Start remains visible, and the scratchpad retains usable height.
EF reports no pending model changes.

## Release 1.2.47 — 2026-09-05

"Room to Work." The Overview now responds to the WPF space it actually receives, including window
size, Windows scaling, and Easy Eyes. It moves stable, live workspaces through Wide, Balanced, and
two Compact arrangements without discarding draft state. Work Agenda is the default center
workspace for a missing preference, explicit existing preferences remain respected, and Focus note
temporarily gives the current note the available workspace. Notes, Forms, and Deadlines now explain
their empty, loading, failed, or unselected scope where the existing load boundary can establish it.

Case Management now opens directly onto one feature-navigation row. Help contains Guidance and
Reference; Documents contains AT Requests, Authorized Rep, and Releases. The supervisor Pending
Approvals screen retrieves 10 notes at a time and adds an explicit maximum-units batch command. Each
approval still crosses the normal server or LocalDB boundary for reviewer scope, compliance,
revision, note validity, service-time conflict, and audit checks. Client-save errors now distinguish
a confirmed save followed by a failed screen refresh from a truly unknown save result.

`DISPLAY_MODES_DESIGN.md` records the implemented responsive contract. `LOGGING_DESIGN.md` records
a separate proposed diagnostic-logging and support-bundle design; that document adds no runtime
logging behavior in this release.

**No schema change.** No migration was added or touched, and `dotnet ef migrations
has-pending-model-changes` confirmed that the model still matches the migration snapshot. No Demo
migration or firewall rule is needed.

**Desktop-and-API release.** The API adds the bounded supervisor review-page route and the optional
automatic-approval threshold, so the matching API must be deployed before either desktop installer
is distributed.

- [x] Release-configuration build across the complete solution succeeded with 0 errors. Full test
      suite: 1,325 desktop/domain passed with 1 documented optional local-AI competence test skipped,
      382 API passed, and 4 Carika passed. The first API run correctly caught the new paging route
      missing from `ApiSurface.Routes`; adding it changed the contract revision to `79FB0BD6EAA2`,
      after which the complete affected build and desktop/API suites passed.
- [x] Version 1.2.47 coordinated across desktop/API assemblies, installer builders, readiness
      defaults, installer examples, release-note tracker, and release assertions. Source commit
      `ba129510321cafb10c29ad42001fbd3ab2227ed5` was pushed to `origin/master` before artifact
      generation.
- [x] Published the Demo API. The package was built from the pushed source commit with 70 entries,
      0 backslash entry names, no `appsettings*.json` or key files, and both expected triggered
      WebJob scripts. Packaged `Sati.Api.dll` reports file version `1.2.47.0`.
      `artifacts/Sati.Api-1.2.47.zip` is 9,871,401 bytes with SHA-256
      `FE331B880FA84276BA634FEC241398035702083A73168477C5CA02290747C5F8`. OneDeploy deployment
      `94fd732e970b463b813b4518f357b5df` to `sati-demo-api-satilogica` in `rg-sati-demo` completed
      with `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, and `/health/version` reported product `Sati.Api`, release
      `1.2.47`, contract revision `79FB0BD6EAA2`, equal to the local 146-route manifest.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.47.exe` is 101,117,952 bytes with SHA-256
      `CB3876AE402B4D7BF1189AB5C897811690C445E47EE60B3824D9868CEE2E38ED`. Five 15-second launches
      each reached the visible sign-in window, remained responsive, closed normally with exit code
      0, reported installed version `1.2.47.0`, and cleaned up. This ran on the build workstation,
      so it is not an external clean-machine attestation. Its 92-byte checksum file has SHA-256
      `BA75A7D440B18A665323820EF220A0B805CF9DB10B758F7F6B7F9889232B2462`.
      Local: `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.47.exe` is 203,182,346 bytes with
      SHA-256 `C466A389498C0328B08378EC1FD6469C280D0D09B0B76C7C7A5160C2DA98D135`. Acceptance verified
      installed version `1.2.47.0`, Windows integrated security, no SQL username or password, and
      cleanup. The embedded `SqlLocalDB.msi` carries a Valid Microsoft Authenticode signature. The
      Local checksum file is 93 bytes with SHA-256
      `CC183140CF575DB51A38305ADF24B643A151162EB24CBE2BED16CA897C630103`. Neither installer is
      code-signed.
- [x] Published the installers and checksum files without overwriting existing files:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.47.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.47.exe`
        and its `.sha256`

      Every published SHA-256 was re-compared with its accepted artifact and matched; no temporary
      distribution file remained.
- [x] Final evidence commit with deployment, health, test, acceptance, distribution, and hash
      evidence — this entry.

**Local Production machines.** No schema change, so no workstation migration record is required.

**Branches retained, not deleted.** `second-machine-setup` and
`origin/claude/local-vs-github-workflow-dlcqpb` retain unique work whose current status is uncertain.
`claude/cool-jang-f6b3c4` is fully merged but remains checked out by its linked worktree, so it fails
the safe-deletion rule. No branch is merged or deleted for this release.

**Validation warnings.** NuGet could not query the vulnerability-feed service index during the
build, though restore and compilation succeeded from the resolved package graph. Existing warnings
also remain for the deliberately escaped SQL `BACKUP` statement and test-only nullability/xUnit
style. None was introduced by this release scope, and all required build and test gates passed.

## Release 1.2.46 — 2026-09-04

"Middle Ground." Corrects 1.2.45. That release read "the Notes pane on the Overview" as the whole
role dashboard and traded it with the Scratchpad at the shell level, so turning the setting on only
moved the Scratchpad from the right edge of the screen to the left — reported by Josh with a
screenshot the same afternoon. The intended swap was between the Overview's *middle* notes column
and the Scratchpad, and that is what it now does.

The filter bar and notes grid are extracted into `Views/NotesPanelView.xaml` so one control renders
in either position; it trades places with `ScratchpadView` between the Overview's middle column and
the shell's collapsible side panel. `ShellWindow.xaml`'s main content area returns to a fixed
column, reverting 1.2.45's `Grid.Column` swap.

Two decisions worth keeping:

- **The side panel only takes the notes panel while the Overview is on screen**
  (`ShellViewModel.ShowNotesInSidePanel`). The other Case Management tabs have no notes panel to
  host, so the side slot keeps the Scratchpad rather than leaving Today's Work unreachable.
- **The centered Scratchpad is the shell's own `ScratchpadViewModel`, handed down through
  `CaseManagerDashboardViewModel.AttachScratchpad`, not injected.** That type is registered
  `AddTransient`, so resolving a second one would have compiled and rendered correctly while
  silently discarding whatever was typed into it — the shell saves only its own instance on close
  and on user switch. DI lifetime is a correctness property for a view model holding unsaved text.

**No schema change.** No migration was added or touched; `dotnet ef migrations
has-pending-model-changes` confirmed clean. No firewall rule needed for this release.

**Desktop-and-API release.** Both assembly versions move together per `StabilizationTests`; only
the desktop client changed in substance, so publishing the API is version-parity, not a functional
deploy.

- [x] Release-configuration build across `Sati.csproj` and `Sati.Api/Sati.Api.csproj`, 0 errors.
      Full test suite: 1,293 desktop/domain passed (1 legitimate skip), 374 API passed, 4 Carika
      passed — confirmed both before and after the version bump. New coverage:
      `Sati.Tests/ScratchpadSwapRenderTests.cs` (3 tests) loads the real Overview and reads back
      which panel is actually visible in each state, plus asserts the centered Scratchpad is bound
      to the handed-down instance. Added deliberately: 1.2.45's mistake compiled, passed every
      test, and was only detectable by looking at the screen.
- [x] Version bump to 1.2.46 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs` (title "Middle Ground"), with
      matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`. Fix commit
      `62fa6afc1a55cd1c968170a117699611857e3e59` and release commit
      `2c372bdbb2ca429d32349720a023cf4883a86f42` pushed to `origin/master`.
- [x] Published the Demo API. Package built under .NET 10, 70 entries, 0 backslash entry names,
      `artifacts/Sati.Api-1.2.46.zip` (9,867,277 bytes; SHA-256
      `E2A0156618106DE681474226407E940D07C9A6653A6C6257C6FD2D81E11C2C04`). No `appsettings*.json`
      present; both `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files confirmed
      present. Packaged `Sati.Api.dll` reports file version `1.2.46.0`.
      OneDeploy deployment `453b2ba3f9da46fd8fd483c8ab45851f` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, `/health/version` reported product `Sati.Api`, release
      `1.2.46`, contract revision `78B5A2F71629` — unchanged, expected since nothing in this
      release touches `ApiSurface.Routes` — and confirmed equal to `ApiSurface.Revision` computed
      locally from the same build.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.46.exe` (101,122,048 bytes; SHA-256
      `59f37e20624bd74e3c02ac36841874335fdac8bc15f4de45e71f9cff10d7fcf4`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.46.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` still carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.46.exe` (203,165,450 bytes; SHA-256
      `0720f35e3f39b007dc480e252f80eff93a861b0865b16333193acdd8577d7eb0`). Acceptance passed:
      installed version `1.2.46.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.46.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.46.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

**Local Production machines.** No schema change, so nothing is pending on any workstation beyond
installing the new build. Known machines and the release each is on are not tracked here yet; this
release adds no migration, so a machine still on an older build is behind only in features.

**Branches retained, not deleted.** `second-machine-setup` (5 unmerged commits, including "Require
an administrator account before Sati will run") and `origin/claude/local-vs-github-workflow-dlcqpb`
(1 unmerged commit) both hold unique work of uncertain status. `claude/cool-jang-f6b3c4` is fully
merged but still checked out by the linked worktree at `.claude/worktrees/cool-jang-f6b3c4`, which
fails the safe-deletion rule.

**Known flake, not caused by this change:** one full desktop run aborted with a test host crash
("Fatal error") after 918 tests. It did not reproduce across five subsequent clean runs in both
Debug and Release, and was not root-caused. The shared WPF `Application` on a single STA thread is
the likely area if it recurs.

## Release 1.2.45 — 2026-09-04

"Trading Places." A new Settings option, "Display Scratchpad in the center of the display," swaps
which content occupies the Overview's main panel versus its collapsible side panel: on, the
Scratchpad (Today's Work / Tomorrow's Agenda) fills the main area and the role dashboard collapses
to the side; off (the default), it's the arrangement Sati has always had. The same chevron still
toggles whichever content ends up on the side — the code-behind collapses by grid column index, not
by which control is in it, so the swap needed no change to that collapse/restore logic. Invoked via
`invoke DATT!` the same evening as 1.2.42/1.2.43/1.2.44.

**No schema change.** No migration was added; `dotnet ef migrations has-pending-model-changes`
confirmed clean (`--project Sati.Persistence --startup-project Sati.Persistence`). No firewall rule
needed for this release.

**Desktop-and-API release.** Both assembly versions move together per `StabilizationTests`, though
only the desktop client changed in substance — the API's own behavior is unaffected, so publishing
it is routine version-parity, not a functional deploy.

- [x] Release-configuration build across `Sati.csproj` and `Sati.Api/Sati.Api.csproj`, 0 errors.
      Full test suite: 1,290 desktop/domain passed (1 legitimate skip), 374 API passed, 4 Carika
      passed — confirmed both before and after the version bump. New coverage:
      `Sati.Tests/ScratchpadLayoutPreferenceTests.cs` (4 tests: default, persistence and
      notification, per-user/environment isolation, corrupt-file fallback), mirroring the existing
      `ConsumerPickerSortPreferenceTests.cs` pattern.
- [x] Version bump to 1.2.45 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs`'s release notes (title
      "Trading Places"), with matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`.
- [x] Source commit `b80e552495e39b6bbccf59fd698ca78e2e89ea56` pushed to `origin/master`.
- [x] Published the Demo API. Package built under .NET 10, 70 entries, 0 backslash entry names,
      `artifacts/Sati.Api-1.2.45.zip` (9,867,299 bytes; SHA-256
      `0E3D2FAC3D103BF900D4547E931FB220E41FC98655A85A93FD4AE17837B8E5FB`). No `appsettings*.json`
      present; both `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files confirmed
      present. Packaged `Sati.Api.dll` reports file version `1.2.45.0`.
      OneDeploy deployment `4571aa5a42404a2bab3eb37f2cd033c8` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, `/health/version` reported product `Sati.Api`, release
      `1.2.45`, contract revision `78B5A2F71629` — unchanged, expected since nothing in this
      release touches `ApiSurface.Routes` — and confirmed equal to `ApiSurface.Revision` computed
      locally from the same build.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.45.exe` (101,122,048 bytes; SHA-256
      `e323d67aa8fbdad290d7293f51658a35274d8d8f3bc79811cdb416dc63c5e8ea`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.45.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` still carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.45.exe` (203,165,450 bytes; SHA-256
      `3a5bd5a695e53f499732d80bc89695b9f9c015662e4a7e3e9d531eb3d139e0d5`). Acceptance passed:
      installed version `1.2.45.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.45.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.45.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

**Known open question, not a defect in this release:** `CaseManagerDashboardContentView` (the
Overview's dashboard content) is a five-column layout designed for the wide main panel. When this
setting is on, that view renders in the narrow ~300px collapsible side column instead. Whether it
degrades gracefully there has not been checked against a running Overview screen — flagged to Josh
at implementation time and repeated here rather than silently shipped as untested. Follow-up, if
needed, would be reflowing that view for a narrow column, not part of this release.

## Release 1.2.44 — 2026-09-04

"Last, First." Three follow-ups from testing 1.2.42/1.2.43: the client-list sort preference now
also changes the printed name format, not only list order; the dashboard's sub-navigation pill
uses the lightened button colors in Blue-Gray Pearl and Cedar Grove instead of the darker,
un-lightened accent; and — landing in release notes for the first time, having shipped between
DATT invocations — the Admin dashboard's new Status control, which is the actual answer to "how do
I get a consumer off my active list when it predates creation-date tracking and can never qualify
for the 20-day delete window." Invoked via `invoke DATT!` the same evening as 1.2.42/1.2.43.

**No schema change.** No migration was added; `dotnet ef migrations has-pending-model-changes`
confirmed clean. No firewall rule needed for this release.

- [x] Release-configuration build across the full solution, 0 errors. Full test suite: 1,286
      desktop/domain passed (1 legitimate skip), 374 API passed, 4 Carika passed — confirmed both
      before and after the version bump. Includes a new WPF render test
      (`TheSelectedClientNameFormatFollowsTheSortPreference`) that loads the real client-picker
      ComboBox and reads back its rendered text in both sort states, rather than trusting the
      RelativeSource binding crossing the item template boundary to resolve correctly untested.
- [x] Version bump to 1.2.44 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs`'s release notes (title "Last, First"),
      with matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`. Release notes also cover the Status control
      shipped in the prior commit, which had not yet appeared in a DATT-invoked release.
- [x] Source commit `095d6f855378c8b7826dbff55b61ed9b31b95b48` pushed to `origin/master`.
- [x] Published the Demo API. Package built under .NET 10, 70 entries, 0 backslash entry names,
      `artifacts/Sati.Api-1.2.44.zip` (9,867,291 bytes; SHA-256
      `DD9EBB28896BF5E19F36ED424FD17259F9008529961946F24480AF6E31E624F9`). No `appsettings*.json`
      present; both `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files confirmed
      present. Packaged `Sati.Api.dll` reports file version `1.2.44.0`.
      OneDeploy deployment `63854beb55d148b29be3195c6e5f60fe` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, `/health/version` reported product `Sati.Api`, release
      `1.2.44`, contract revision `78B5A2F71629` — unchanged, expected since nothing in this
      release touches `ApiSurface.Routes` — and confirmed equal to `ApiSurface.Revision` computed
      locally from the same build.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.44.exe` (101,101,568 bytes; SHA-256
      `c5831483d9bbf94a7c7b8fb0b8244f74e5e3bed7e56d693037e5e1070fc518a1`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.44.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` still carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.44.exe` (203,165,962 bytes; SHA-256
      `9752cbbfbfc320202bb75deabcc30769350fe139f02a9c7e7f7f8c360b13e395`). Acceptance passed:
      installed version `1.2.44.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.44.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.44.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

## Release 1.2.43 — 2026-09-04

"A button that explains itself." Fixes the Admin rule-3 delete button, which silently disabled
itself with no explanation whenever the Reason field was empty — reported directly by Josh after
using 1.2.42 ("the delete button wasn't working"). Also adds a personal Settings preference to sort
the Notes client picker by last name. Invoked via `invoke DATT!` the morning after 1.2.42 shipped.

**No schema change.** No migration was added; `dotnet ef migrations has-pending-model-changes`
confirmed clean. No firewall rule needed for this release.

**Desktop-and-API release.** Both assembly versions move together per `StabilizationTests`, though
only the desktop client changed in substance — the API's own behavior is unaffected by either fix,
so publishing it is routine version-parity, not a functional deploy.

- [x] Release-configuration build across the full solution, 0 errors. Full test suite: 1,274
      desktop/domain passed (1 legitimate skip), 374 API passed, 4 Carika passed — confirmed both
      before and after the version bump.
- [x] Version bump to 1.2.43 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs`'s release notes (title "A button that
      explains itself"), with matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`.
- [x] Source commit `61cd5f5eb074b93ee636d28c70e22aa36f70f8e2` pushed to `origin/master`.
- [x] Published the Demo API. Package built under .NET 10 (same throwaway console app as 1.2.42),
      70 entries, 0 backslash entry names, `artifacts/Sati.Api-1.2.43.zip` (9,866,141 bytes;
      SHA-256 `2AA98E131745CB593604AFFCD23EFBA2C001B31CD3472E1E3DF7634E0E6C1D6E`). No
      `appsettings*.json` present; both `App_Data/jobs/triggered/demo-history-reconciliation`
      WebJob files confirmed present. Packaged `Sati.Api.dll` reports file version `1.2.43.0`.
      OneDeploy deployment `4e47dc1573c74b7f9d357d0c29e8a1a9` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, `/health/version` reported product `Sati.Api`, release
      `1.2.43`, contract revision `78B5A2F71629` — unchanged from 1.2.42, expected since neither
      fix in this release touches `ApiSurface.Routes` — and confirmed equal to `ApiSurface.Revision`
      computed locally from the same build.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.43.exe` (101,113,856 bytes; SHA-256
      `73cc596a0ccd211f134a677936d7ed4627f3e1d4efd2cfd7bcc18631b2cae5e1`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.43.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` still carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.43.exe` (203,160,842 bytes; SHA-256
      `75519d8a5845bf183383c1e0a59d922df1c4a8ac082ad357c22bb9d69d24aed9`). Acceptance passed:
      installed version `1.2.43.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.43.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.43.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

## Release 1.2.42 — 2026-09-03

"Room to undo a duplicate." Consumer-deletion, archive, and legal-hold foundation from the
[[Ordinary-consumer deletion, archive status, and bulk-import dedupe (2026-09-03)]] work, plus the
safety-plan/annual-document/privacy-notice workflow from
`ANNUAL_DOCUMENT_RELEASE_READINESS.md` and `NOTE_FORM_ATTESTATION_DESIGN.md`. Invoked via `invoke
DATT!`; preflight paused once for a concurrent-edit check (the working tree was still moving under
a second, independently running agent) and once for explicit authorization of the schema-changing
Demo migration below, both confirmed by Josh before continuing.

**Schema-changing release.** Seven pending migrations: `AddFormAttestations`,
`AddDocumentArtifacts`, `AddPersonCreatedAtAndStatus`, `AddLegalHolds`,
`AddDocumentTemplatesAndSafetyPlans`, `AddSafetyPlans`, `CompleteAnnualDocumentWorkflow` — 94 total
in the chain. `dotnet ef migrations has-pending-model-changes` confirmed clean against the model
before proceeding.

**Release validation, independently re-run rather than only trusted from source preparation:**
Release-configuration build across the full solution, 0 errors. Full test suite in Release
configuration after the version bump: **1,266 desktop/domain passed** (1 legitimate skip, the
opt-in local-AI-model evaluation), **374 API passed**, **4 Carika passed**. `git diff --check`
clean.

- [x] Version bump to 1.2.42 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs`'s release notes (title "Room to undo a
      duplicate"), with matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`. Verified by the Release-configuration test run
      above.
- [ ] Source commit and push to `origin/master`.
- [x] Applied the authorized Demo migration via `scripts/Apply-CompliancePlatformMigrations.ps1`
      (new script; extracted EF's own generated idempotent DDL for the seven target migrations
      verbatim via `sed`, rather than hand-transcribing it, then added fail-closed identity/
      chain-position checks and dry-run/commit control around it). Rollback-only dry run reported
      0/7 target migrations recorded and rolled back cleanly; the real run recorded 7/7 and
      committed; a second real run reported the same 7/7 with no errors, proving idempotency.
      Connected via an Azure AD access token from Josh's own `az` session (not integrated
      security — Azure SQL does not support Windows logins) through the temporary exact-IP
      `SatiDemo` firewall rule Josh added and will remove. Public IP: `72.95.106.10`.
      One bug found and fixed before the first live attempt reached the database: the guard
      script's header here-string had no trailing newline before its closing `'@`, so string
      concatenation ran the last header comment directly into the extracted DDL's first
      `IF NOT EXISTS (`, commenting it out and orphaning its closing paren (`Incorrect syntax
      near ')'`). SQL Server does not partially execute a batch that fails to parse, so nothing
      was touched before the fix; verified by reconstructing the assembled command text locally
      and confirming the fix before reconnecting.
- [x] Published the Demo API. Package built under .NET 10 (a throwaway console app calling
      `System.IO.Compression.ZipFile`, not Windows PowerShell 5.1's `System.IO.Compression.FileSystem`
      assembly, per the `.NET 10, not Windows PowerShell` lesson in `DECISIONS.md`), 70 entries, 0
      backslash entry names, `artifacts/Sati.Api-1.2.42.zip` (9,866,162 bytes; SHA-256
      `3566CA0CB4F91EC6D455B3CE3FE541343E7E55D5F504DAB7857EE4D81EC165AF`). No `appsettings*.json`
      present in the publish output (config comes from App Service settings/Key Vault, never a
      packaged file); both `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files
      confirmed present. Packaged `Sati.Api.dll` reports file version `1.2.42.0`.
      OneDeploy deployment `621076ef4057476a8a60f99548e6230a` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy` (confirming `SchemaDriftHealthCheck` accepted the schema the
      migration above produced), `/health/version` reported product `Sati.Api`, release `1.2.42`,
      contract revision `78B5A2F71629` — matching `ApiSurface.Revision` computed locally from the
      same build, confirmed via a throwaway .NET 10 console app referencing `Sati.Contracts.dll`
      (Windows PowerShell 5.1's `Add-Type` cannot load a .NET 10 assembly directly).
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.42.exe` (101,093,376 bytes; SHA-256
      `022583f901df2baeaf949e023b9d361dfdd795c21dd027c797b49b807686d2fd`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.42.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.42.exe` (203,158,282 bytes; SHA-256
      `122bef469144c1d3fb46bd2c53c2bc3a2a31afabdcd50cb51369ed60b80487ce`). Acceptance passed:
      installed version `1.2.42.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.42.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.42.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

## Compliance attestation and annual documents — designed 2026-09-03

> The September 14 active section supersedes the artifact-prerequisite, Supervisor technical
> override, fixed category-wide release, CA-60-day, due-date-as-cycle-identity, and generic-PUT
> opening portions below. This section remains the implementation history of the earlier slice.

**Steps 1–9 implemented in source on 2026-09-03; not released or deployed.** Release preparation
and remaining operational gates are recorded in `ANNUAL_DOCUMENT_RELEASE_READINESS.md`. Full design in
`NOTE_FORM_ATTESTATION_DESIGN.md`.

The removed note-to-form bridge used a message box in `Views/ShellWindow.xaml.cs`. Saving a note
tagged with a form type could stamp `DateTime.Today`, discarding the note's event date and using
the dashboard selection. Eleven confirmed defects are inventoried in section 1 of the design. Defect 1 is the
serious one: a synthesized `CompletedDate` silently changes which past service dates
`BillingComplianceGate.IsBillingWindowBlocked` treats as billable.

Decided with Josh on 2026-09-03: a form note is **evidence that prompts an attestation, never the
attestation itself**. Prerequisites that live outside Sati must be checked before an attestation is
accepted, with a per-cycle "recorded externally" escape hatch. Document existence is recorded as
metadata plus a SHA-256, never stored bytes. Templates are per agency with a Sati default,
append-only versioned. The annual packet opens 30 days before the anniversary and renders only the
notice and permitted records request plus draft releases and the saved safety-plan content.
Consumer choices, authorization, final safety approval and form attestation remain separate work.

- [x] Steps 1 through 3 of the landing order: `FormAttestation` table and backfill, delete the
      bridge, generalize the Reviews attestation control to all twelve form types. This closes the
      "no in-app control captures an arbitrary completion date for a non-review form" item below.
- [x] Step 4: `DocumentArtifact` and the shared prerequisite registry, including metadata/hash
      recording for generated Agency, DHHS, and Medical releases, Draft handling, external-record
      capture, artifact supersession, Comprehensive Assessment before Reclassification, and a
      reasoned Supervisor technical override. `PUT /api/v1/forms/{id}` is narrowed and
      `CloudFormService.OpenFormAsync` records its opened date.
- [x] Step 5: immutable published template versions, shared resolution/token validation, a
      constrained MigraDoc composer, agency-Admin read/publish routes, and local/cloud privacy
      generation with exact template-version provenance. Josh authorized a generic provisional
      Privacy Practices default; it is visibly marked for agency/privacy/legal review.
- [x] Step 6: seven-section safety-plan authoring in WPF/local/API, locked submitted/reviewed
      versions, scoped supervisor approval/return, optimistic concurrency, PDF source provenance.
- [x] Step 7: immutable privacy-notice receipt/effort rows bound to the current generated artifact;
      generation alone no longer satisfies the prerequisite. Regeneration requires another receipt.
- [x] Step 8: agency-configurable packet window (default 30 days, 0–180 supported), one ZIP and
      manifest, profile/Annual Documents controls and read-time dashboard preparation reminder.
      Existing completed/external releases are explicitly omitted rather than reconstructed.
- [x] Step 9: SHA-256 plus byte-length verification, both during ZIP construction and through a
      staff-selected-file verifier. Primary-care recipient inherits missing organization address/
      phone; request included only after medical release attestation. Download only; staff send it.
- [ ] Replace/review the provisional Privacy Practices wording when Josh has the actual template.
      Confirm agency-specific legal effective date, privacy contact, complaint channels, uses,
      rights, and additional applicable restrictions before production use.
- [x] O-1 through O-7 engineering choices resolved: user-approved generator, Comprehensive
      Assessment prerequisite, reasoned technical override, shared safety structure with supervisor
      review, downloadable/staff-sent records request after medical release, and working verifier.
      Agency/legal review and the retention questions in R-3/R-4 remain operational work, not a
      claim of compliance. See `REGULATORY_CONCERNS.md` and the implementation addendum in the design.
- [x] Form completion and revocation now emit `form.attested` and
      `form.attestation-revoked`; document, safety-plan, receipt and packet events are documented in
      `AUDIT_EVENTS.md` without copying narratives or receipt explanations into metadata.
- [ ] Apply `20260903152847_AddFormAttestations` only through the controlled migration process.
      It has not been applied to Demo; workstation access requires the temporary exact-IP firewall
      rule that only Josh may add and remove.
- [ ] Apply `20260903173950_AddDocumentArtifacts` through the same controlled migration process.
      It has not been applied to Demo or Production.
- [ ] Apply `20260903185920_AddDocumentTemplatesAndSafetyPlans` through the controlled migration process.
      It creates the versioned table and provisional default; it has not been applied to any runtime database.
- [ ] Apply `20260903190302_AddSafetyPlans` and `20260903200511_CompleteAnnualDocumentWorkflow`
      through the same controlled process. No runtime database was changed during preparation.
- [ ] Sati's comprehensive assessments and PCPs are development-only; Evergreen holds the
      production records. Both map to no prerequisite until an Evergreen API integration exists,
      which is a `REGULATORY_CONCERNS.md` item first.

## Release 1.2.41 — 2026-09-03

"A quieter screen." Case note template from the checked meeting facts, a suggested follow-up that
actually appears, an inactivity privacy screen, and lighter buttons in the two orange palettes.

**No schema change.** No migration was added and no persistence contract moved. `UpcomingEventKind`
gained a display-only `UpcomingForm` value; the enum is not persisted and is not part of any DTO.

**Desktop-only release.** `ApiSurface.Revision` is unchanged at `E807EDE42231`, so a 1.2.41 desktop
runs against the deployed 1.2.40 Demo API. The API assembly version moves with the desktop because
`StabilizationTests` requires the two to match, but the hosted Demo API was **not** republished:
that was not part of the requested work. Readiness expectations now name 1.2.41 and will therefore
report a version mismatch until the API is published.

**Requested work.** Josh asked for four changes and then commit, push, merge, release notes in
Settings, a version bump, and both installers. The work landed directly on `master`, so there was
no feature branch to merge.

### Lighter buttons in the orange palettes

- [x] Split button fill from accent type: every theme now supplies `AccentButtonBrush`,
      `AccentButtonHoverBrush`, `AccentButtonPressedBrush`, and `OnAccentButtonBrush`.
- [x] Thirteen themes copy their existing accent values, so nothing about them changed.
- [x] Blue-Gray Pearl and Cedar Grove fill buttons with `#FBA76B` over `#3B1D06` text, roughly
      7.9:1 contrast. Their `AccentBrush` stays `#E25507`, so orange type is untouched.
- [x] `PrimaryButton` is the only style bound to the button set. Selection highlights and accent
      text still bind `AccentBrush`.
- [x] Structure test asserts all fifteen themes supply all four keys, and that the two orange
      palettes' fill is measurably lighter than their accent while keeping dark text.

### Case note template

- [x] `CaseNoteTemplateComposer` writes Meeting Details, Observations, and Discussion and Activity
      from the ticked meeting controls.
- [x] It renders `CaseNoteFactCompiler.VisitFacts` rather than restating the checkboxes, so the
      template and the local-AI draft cannot phrase the same tick two different ways.
- [x] Existing narrative is preserved verbatim below a `MEETING NARRATIVE` header. Nothing is
      removed or rewritten; a second press stacks rather than replacing. See `DECISIONS.md`.
- [x] The Format with Local AI trigger is withdrawn. The drafting pipeline and its review panel are
      untouched and unreachable, so the button can return.
- [x] The button is gated on `IsVisitNote`, not `IsLocalAiEnabled`: it is not an AI feature and
      needs no model.

### Suggested follow-up now appears

- [x] Root cause found and reproduced: the row was built on `GenerateEvents`, which reports only
      forms inside their open/late window. With the default zero-day review window a quarterly
      review is open on exactly its due date, so for an ordinary client the row was blank for
      months. The existing tests drove the panel with a stub event service and never exercised the
      real generator.
- [x] `UpcomingEventService.NextFormSuggestion` reports the client's next outstanding form
      regardless of the window, sharing the one form table, `GetCurrentCycleForm`, and
      `IsSatisfiedAsOf`, so it cannot name a form the compliance gate treats as satisfied.
- [x] `NoteEntryViewModel` prefers an actionable event and falls back to that suggestion.
- [x] Regression tests exercise the real service. They fail against the unfixed code: the probe
      that found this asserted `GenerateEvents` returns nothing for a client effective today, which
      it does.

### Inactivity privacy screen

- [x] After a configurable idle period Sati blurs its whole window behind a Paused card. Any key or
      click clears it, and that first input is consumed rather than delivered.
- [x] `IdleSessionState` owns the rule behind an injectable clock; `ShellWindow` supplies the
      `InputManager` hook and a one-second tick. The view model references no WPF input type.
- [x] `IdleLockPreferenceService` stores the delay per Sati user, Windows profile, and environment,
      mirroring `EasyEyesPreferenceService`. No migration and no agency Settings row.
- [x] Settings offers Never and one minute through one hour, saved immediately for that account.
- [x] A bare mouse move counts only when the pointer actually travelled. Without that the overlay
      woke itself the moment it appeared, because showing it changes what is under the cursor.
- [x] Presented honestly as a privacy screen: the overlay, the Settings help text, and the release
      notes all state that it does not lock Windows. `TryDismiss` and `RequiresUnlockChallenge` are
      the seam a PIN would use.

### Validation

- [x] Release build of the full solution: 0 errors.
- [x] Sati desktop/domain: 1,184 passed, 1 skipped (the documented `SATI_RUN_LOCAL_AI_MODEL_EVAL`
      opt-in). API integration: 324 passed. Carika: 4 passed.
- [x] 26 tests added across the four changes.

### Release evidence

- [x] Source commit `675fb11ef7bda4692343c5875721e047ded1f35c` pushed to `origin/master`. The work
      was done directly on `master`, so there was no branch to merge.
- [x] Demo installer acceptance passed on
      `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.41.exe` (100,925,440 bytes; SHA-256
      `5ea7e68d023fa73bba53e2d93a075e60f1f7702652a20d67bdb8aa4a2380c515`): five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.41.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Evidence: `artifacts/release-1.2.41-demo-installer-acceptance.json`.
- [x] Local installer built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` carries a
      Valid Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.41.exe` (202,985,226 bytes; SHA-256
      `0875b2389ab7cf2234ccad29c949e0e5b62c8a5f68d58e717ad2379daf2a9419`). Acceptance passed:
      installed version `1.2.41.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Generated installers are not code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.41.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.41.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Published the Demo API at 1.2.41. **No firewall rule was requested, added, or removed.** The
      temporary exact-IP rule exists only to reach `SatiDemo` from a workstation for a controlled
      migration; this release adds no migration, and `az webapp deploy` never touches SQL. That
      distinction is the 2026-08-30 decision in `DECISIONS.md`. The allow-list was checked
      afterwards and still holds exactly `sati-demo-api-outbound-01` through `-03`.
- [x] `artifacts/Sati.Api-1.2.41.zip` (9,673,370 bytes; SHA-256
      `E88087B5DBFA1A5F5B053217CABD9D4FFE5EF5F44543907239AB62B5B0234D23`; 70 entries). The packaged
      `Sati.Api.dll` reports `1.2.41+d1802ed4fea1ebc8728540c768faa248acff00f0`. Inspection found 0
      backslash paths, 0 forbidden configuration or key files, no `appsettings*.json`, and both
      `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files.
- [x] The first packaging attempt was discarded unpublished. `ZipFile.CreateFromDirectory` under
      Windows PowerShell 5.1 wrote 20 backslash entry names, which App Service would extract as
      files literally called `App_Data\jobs\...`. The package is now built under .NET 10 with entry
      names normalized to forward slashes, and the count is asserted before use.
- [x] OneDeploy deployment `5b1c29fdd16f4ccaa3823b6d2130844b` completed successfully to the
      existing Demo App Service `sati-demo-api-satilogica` in `rg-sati-demo`.
- [x] `/health/live` returned `{"status":"live"}`, `/health/ready` returned `Healthy`, and
      `/health/version` reported product `Sati.Api`, release `1.2.41`, contract revision
      `E807EDE42231` — unchanged, and equal to the client's `ApiSurface.Revision`. A healthy
      readiness result also means `SchemaDriftHealthCheck` accepted the deployed schema, which is
      the direct confirmation that no migration was owed. Evidence:
      `artifacts/release-1.2.41-demo-readiness.json`; authenticated Admin checks were skipped
      because this workstation has no designated `SATI_DEMO_USERNAME` and password.
- [ ] Consolidate `EasyEyesPreferenceService` and `IdleLockPreferenceService` onto one personal
      preference store. Two near-identical file-IO implementations is one too many.


## Release 1.2.40 — 2026-09-02

"Easy on the eyes." Close confirmation, full-height client editing, personal Easy Eyes mode,
Blue-Gray Pearl and Cedar Grove themes, and visible vector year-navigation arrows.

**No schema or API-contract change.** This release adds presentation behavior and local personal
preference storage only; it requires neither a database migration nor a firewall change.

### Preflight

- [x] Literal `invoke DATT!` received; `master` and `origin/master` began equal at `096e8ef`.
- [x] Release scope reviewed; no persistence migrations, API routes, or network contracts changed.
- [x] No 1.2.40 API ZIP, Local installer, Demo installer, checksum, or distribution artifact exists.
- [x] Retain `claude/cool-jang-f6b3c4` because it is checked out by a linked worktree; retain
      `second-machine-setup` because its seven unique commits are unrelated to this release.
      `feature/caseload-transfer` is already fully merged and is retained rather than deleted.
- [x] Known Local state recorded without assuming an upgrade: SatiLogica's installed executable is
      1.2.23. Joshu has the hash-verified 1.2.39 Local installer downloaded, but no executable in
      Joshu's per-user install folder; Joshu's existing shortcut targets SatiLogica's 1.2.23 copy.

### Release evidence

This release completed in a second `invoke DATT!` pass. The first pass pushed the source commit,
deployed the Demo API, and built the Demo installer, then stopped before the Local installer, the
acceptance gates, distribution, and this record. The second pass reused the existing 1.2.40 API ZIP
and Demo installer rather than rebuilding them, so no artifact was replaced under an existing
version.

- [x] Release build of the full solution: 0 errors, 10 warnings (existing NuGet vulnerability-feed
      reachability, EF raw-SQL, nullable, and xUnit analyzer warnings).
- [x] Sati desktop/domain: 1,158 passed, 1 skipped
      (`LocalAiModelCompetenceTests.ConfiguredModelCompletesGroundedWorkflowAcrossRepresentativeCurrentNoteInputs`,
      the documented `SATI_RUN_LOCAL_AI_MODEL_EVAL` opt-in whose on-device model prerequisite is
      absent). API integration: 324 passed. Carika: 4 passed. Totals: 1,486 passed, 1 skipped,
      0 failed.
- [x] `git diff --check` clean on the release commit; the working tree stayed clean throughout.
- [x] Source commit `ab7613776a093f13f6613ada2808b1c82cefd299` confirmed present on
      `origin/master`; the remote did not advance during the release.
- [x] `artifacts/Sati.Api-1.2.40.zip` (9,674,472 bytes; SHA-256
      `C28EBFECEA3ECDC8D18DF761944E05B33E7F587AD7F81B5E3E89CD64CEA04779`; 79 entries). The packaged
      `Sati.Api.dll` reports `1.2.40+ab7613776a093f13f6613ada2808b1c82cefd299`, proving the package
      was built from the pushed commit. Inspection found 0 backslash paths, 0 forbidden
      configuration or key files, no `appsettings*.json` of any kind, and both required
      `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files. The nine entries it holds
      beyond the 1.2.39 package are directory records, not new files.
- [x] Published only to the existing Demo App Service `sati-demo-api-satilogica` in `rg-sati-demo`.
      OneDeploy deployment `74850bbe93d243c3ab8bff465a240b96` completed successfully and is the
      active deployment. No database migration was required or performed, and no firewall rule was
      requested, added, or altered.
- [x] Hosted `/health/live` returned `{"status":"live"}` and `/health/ready` returned `Healthy`.
      `/health/version` reported product `Sati.Api`, release `1.2.40`, and contract revision
      `E807EDE42231`. The client's `Sati.Contracts.V1.ApiSurface.Revision`, evaluated from the
      Release build under .NET 10, is the same `E807EDE42231`, so client/API contract parity holds.
      Health-only readiness evidence is `artifacts/release-1.2.40-demo-readiness.json`;
      authenticated Admin checks were skipped because this workstation has no designated
      `SATI_DEMO_USERNAME` and password.
- [x] Demo installer acceptance passed on
      `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.40.exe` (100,917,248 bytes; SHA-256
      `56baaa084f09dadda69065be56d4f015dd03b55ffe783b8028c254a7959c53b3`): five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.40.0`, cleanup
      passed. It ran on the build workstation, so it is not a clean external-machine attestation.
      Evidence: `artifacts/release-1.2.40-demo-installer-acceptance.json`.
- [x] Local installer built from the durable repository prerequisite after confirming that
      `artifacts\Prerequisites\SqlLocalDB.msi` carries a Valid Authenticode signature from
      `CN=Microsoft Corporation`. `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.40.exe`
      (202,972,938 bytes; SHA-256
      `14c06c3eedde67320ad8888e33c09ed1a50420ee498653482f66eb77e51087a1`). Acceptance passed:
      installed version `1.2.40.0`, `integratedSecurity=True` with no SQL username or password in
      the Local configuration, cleanup passed. The generated installers are not code-signed; only
      the embedded Microsoft LocalDB prerequisite is.
- [x] Published both installers and their `.sha256` files by copying each to a uniquely named
      temporary sibling, verifying that copy's hash, renaming it to the final versioned name, and
      verifying the final file again. No destination file was overwritten and no temporary file
      remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.40.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.40.exe`
        and its `.sha256`

      Both published hashes are identical to the accepted build artifacts.
- [x] Evidence commit `4cef43e75c6ee64ba8cc67523f5e7b967f321313` pushed to `origin/master`. The
      working tree is clean and local `master` equals `origin/master`.

### Local Production machines

This release changes no schema, so no local migration is pending. Versions are still recorded so
that no machine is assumed to have caught up.

- [x] SatiLogica: `C:\Users\SatiLogica\AppData\Local\Programs\Satilogica\Sati\Sati.exe` is
      **1.2.23**, verified after this release. It is well behind and was not upgraded by this
      workflow.
- [ ] Joshu: **unverified.** `C:\Users\Joshu\AppData\Local\Programs\Satilogica\Sati` is not
      readable from the SatiLogica profile, so its installed version could not be confirmed and
      must be treated as behind. Record it once that machine installs 1.2.40.

## Orange-accent themes and calendar navigation — 2026-09-02

- [x] Add Blue-Gray Pearl and Cedar Grove as complete interchangeable theme palettes, using the
      supplied `#E25507` orange for their main accent and preserving distinct Local AI colors.
- [x] Replace font-dependent year-navigation characters with visible rounded vector-chevron
      buttons, retaining accessible names, tooltips, keyboard focus, and existing commands.
- [x] Verify both palettes supply every theme resource and cover the calendar arrows with a
      UI-structure regression test.

## Easy Eyes presentation mode — 2026-09-02

- [x] Add an off-by-default personal Easy Eyes option to the ungated Appearance settings.
- [x] Persist it per Sati user, Windows profile, and Demo/Production environment without placing
      presentation state in agency data.
- [x] Enlarge the working surface by roughly 30%, hide (without deleting) Narrative columns in
      both note grids, and force the Clients workspace to its horizontal selector while enabled.
- [x] Apply successful changes immediately to the open shell and reload them at sign-in/account
      switch; add persistence and UI-structure regression tests.

## Vocational Rehabilitation assignments — 2026-09-02

- [x] Add consumer-profile assignments for the Vocational Rehabilitation Counselor and the
      counselor's assistant, revealed only while `OpenWithVR` is selected.
- [x] Store the assistant's agency-wide display title in Settings, default it to `VSA`, and
      refresh the Consumers workspace after Settings closes.
- [x] Carry both assignments through shared validation, Local and API saves, optimistic
      concurrency, immutable person versions, and audit history.
- [x] Applied `AddVocationalRehabilitationAssignments` to hosted Demo through the controlled
      migration path in release 1.2.39.

## Existing-profile Credible updates — 2026-09-02

- [x] Add agency setting `AllowCredibleProfileUpdates`, default false, to persistence, API
      settings contracts, Admin Settings UI, and optimistic-concurrency save path.
- [x] When enabled, allow the field-level single-consumer review to fill the currently selected
      edit form without bypassing the ordinary person save/version/audit path.
- [x] Refuse differing nonblank Credible client ids before any form field changes; preserve absent
      or declined fields and every Sati-only field.
- [x] Keep bulk folder matches report-and-skip. Bulk replacement remains a separate, deferred
      workflow requiring recovery and batch-audit design.
- [x] Applied `AddCredibleProfileUpdateSetting` to hosted Demo through the controlled migration
      path in release 1.2.39, using the documented user-managed exact-IP firewall process.

## Duplicate compliance form rows — 2026-09-01

> Historical repair record. The September 14 correction supersedes due date as annual identity,
> every `InForceSince`/born-complete inference, and the conclusion that the old repair settled
> operational re-billing. The active section above owns current migration and recovery status.

Implemented and tested; the repair has NOT yet run against a database holding real records.
Full write-up in `HANDOFF_DUPLICATE_COMPLIANCE_FORMS.md`.

Every `(PersonId, Type, DueDate)` in `SatiProduction` that was generated before `57af6fa`
exists three times — 492 duplicated forms across 25 of 26 clients, 984 surplus rows. Cause was
a read-modify-write race: `GetAllPeopleAsync` ran `EnsureCurrentCycleForms` + `SaveChangesAsync`
unconditionally on every caseload load, startup issued those loads concurrently, and there is no
unique constraint on `dbo.Forms`. `57af6fa` closed the mechanism on 2026-07-24 by serializing the
loads and gating the write behind `EnableEnsureCycleFormsOnLoad = false`. The rows it had already
written were never cleaned up.

It surfaces as a completed form that still blocks billing: `GetCurrentCycleForm` returns one copy
on a due-date tie, so the checkbox reads complete, while `EvaluateComplianceGate` iterates every
row in `Person.Forms` and sees the unreachable copies. Untouched it produces a fresh false block
each quarter, per client.

- [x] Add a unique index on `dbo.Forms (PersonId, Type, DueDate)` —
      `20260901150802_AddUniqueFormPersonTypeDueDateIndex`. `Form.Type` narrowed from
      `nvarchar(max)` to `nvarchar(40)` so it can be indexed. The migration refuses with a
      named message if duplicates remain rather than failing on the index itself.
- [x] Handle the losing writer's `DbUpdateException` in `GetAllPeopleAsync` — discard the
      losing inserts and re-read rather than crashing on a benign concurrent insert.
- [x] Repair the existing rows: `Data/FormDuplicateRepair.cs`, run by `LocalDatabaseUpdater`
      between the pre-migration backup and `MigrateAsync`. Merges only groups holding at most
      one completion fact; a group with two different completion dates is reported and left
      alone. One `AuditEvent` per removed row under `ActorUserId = 0`.
- [x] Close the second duplication path — `NewClientViewModel` now calls
      `Person.AddMissingForms` instead of assigning over `Forms`.
- [x] Declare the same index and length on `ApiDbContext.ServerForm` so the server model
      matches the column it writes to.
- [x] Ran `scripts/Report-DuplicateComplianceForms.sql` against `SatiProduction` on 2026-09-01:
      1,788 form rows, 804 distinct forms, 492 duplicated groups at exactly 3 copies each, 984
      surplus rows, 25 of 26 clients. **Zero conflicted groups** — every group holds at most one
      completion fact, so the repair merges all 492 unattended and the index binds on the same
      launch. One duplicated group blocks billing today: person 1056 `Q1R` due 2026-08-28, the
      reported record, classified `FALSE BLOCK -- work was attested on another copy`.
- [ ] Apply the migration to `SatiDemo` through the controlled path. The desktop repair does
      not run in Demo (`UsesCloudApi` skips the whole block), so if `SatiDemo` holds
      duplicates the migration will refuse there until they are cleared separately.
- [ ] Decide deliberately whether `EvaluateComplianceGate` should read every form or only the
      current cycle, and record it in `DECISIONS.md`. Ask Josh; narrowing it changes whether
      stale prior-cycle documents keep blocking.
- [x] Derived `IsCompliant` from `CompletedDate` and dropped the stored column
      (`AddDerivedFormCompliance`). The `isCompliant` constructor parameter is gone, so the
      state cannot be built. The migration backfills the 147 rows from the cycle start their
      own generator implied, writes a `form.compliance-date-backfilled` audit event per row,
      and never backfills a review, a not-yet-started cycle, or a person with no effective date.
      **Person 1044 is resolved by this**, not by the duplicate repair. (Person 1042's `Q2R` due
      2026-08-24 still blocks, correctly — it is genuinely incomplete.)
- [x] Added `Form.IsSatisfiedAsOf(date)` for the distinct question "is this in force as of
      today", sharing its predicate with `BillingComplianceGate.IsIncompleteAndOverdue`, and
      routed the caseload matrix, `UpcomingEvents`, task rows and `GetComplianceStatus` through
      it. A completion date that has not arrived is recorded but not in force; no screen can
      now call such a form complete while the gate blocks on it.
- [x] Re-enabled cycle form generation and removed `EnableEnsureCycleFormsOnLoad`. Nothing else
      generates forms for an ongoing caseload — clients only still had records because the
      racing pre-`57af6fa` runs pre-created the current *and* next cycle, which run out through
      2027–2028. Safe now because the unique index decides the race and `GetAllPeopleAsync`
      treats losing it as a re-read. `Person.InForceSince` owns the born-in-force rule, so the
      generator no longer mints dateless compliant rows.
- [x] Operational re-billing: **closed 2026-09-01 by Josh** — nothing currently in
      `SatiProduction` will ever be billed for real, since real billing is 6–8 months out. The
      one-time unblocking from the duplicate repair and the 147 backfilled rows therefore has no
      financial consequence. Neither recurs: both corrected rows that were blocking on a missing
      field rather than on a real compliance failure.
- [x] **Intervening cycles get no forms for a backdated admission** — fixed 2026-09-01.
      `EnsureCurrentCycleForms` now generates every cycle from the effective date through the
      one after the current, bounded at 25 cycles and dropping the oldest end so the workable
      cycles are always present. A form that was never created cannot be enforced, so those
      years previously carried no compliance requirements at all.
- [x] **The in-force assumption is scoped to the cycle containing today** — same change. It
      previously applied to any already-started cycle, which was harmless while only the current
      cycle was generated and would have asserted compliance nobody attested across every
      historical year at once. Closed cycles are generated outstanding: Sati has no record of
      whether a closed year's documents were renewed, and a later cycle beginning proves nothing
      because cycles turn over on the anniversary, not because anything was signed.
- [ ] Expect open historical documents on any client entered with a backdated effective date.
      That is the honest reading of an unknown, matching the quarterly-review precedent — do not
      bulk-close and do not invent dates. The creation dialog is where those years get recorded.
- [x] **Superseded 2026-09-14:** the shared blank-date attestation control now covers every form
      type, and opening has its own actual-date capture. No ordinary dashboard/profile action
      stamps `DueDate` or today as completion. The onboarding reconciliation also requires an
      explicitly entered occurrence date before it can create an initial attestation.

## Release 1.2.39 — 2026-09-02

"A better fit." Responsive calendar and compact-display work, guaranteed Consumers overflow,
reviewed existing-profile Credible updates, and Vocational Rehabilitation assignments with an
agency-configurable assistant title.

**Schema-changing release.** Two additive migrations:

- `20260902140636_AddCredibleProfileUpdateSetting` adds the non-null
  `Settings.AllowCredibleProfileUpdates` bit, default false.
- `20260902142303_AddVocationalRehabilitationAssignments` adds nullable
  `People.VrCounselorName` and `People.VrAssistantName` (`nvarchar(150)`) plus non-null
  `Settings.VrAssistantTitle` (`nvarchar(100)`), default `VSA`.

**Authorization and firewall.** Invoked with literal `invoke DATT!`. Josh explicitly authorized
both controlled SatiDemo migrations and confirmed both Local Production machines were on 1.2.38.
Josh added `datt-workstation-temp` for the workstation's exact public IP `66.211.131.66`, then
removed it immediately after migration. The allow-list was verified afterward as exactly the three
`sati-demo-api-outbound-*` entries; the release workflow did not alter the firewall.

### Validation
- [x] Release build of the full solution: 0 errors, 9 warnings (existing NuGet vulnerability-feed
      reachability, EF raw-SQL, nullable, and xUnit analyzer warnings).
- [x] Sati desktop/domain: 1,151 passed, 1 skipped
      (`LocalAiModelCompetenceTests.ConfiguredModelCompletesGroundedWorkflowAcrossRepresentativeCurrentNoteInputs`,
      the documented `SATI_RUN_LOCAL_AI_MODEL_EVAL` opt-in whose prerequisite is absent).
      API integration: 324 passed. Carika: 4 passed.
- [x] `git diff --check` clean; release diff and staged scope reviewed.

### Demo migration and deployment
- [x] `scripts/Apply-CredibleProfileAndVrMigrations.ps1` dry run validated exact
      `SatiDemo` / `Demo`, 1 Settings row and 177 consumers; reported 4 columns and 2 history rows,
      then rolled back.
- [x] Controlled migration committed those 4 columns and 2 EF history rows. A third pass reported
      0 columns and 0 history rows, proving idempotency; no blank VR assistant titles were found.
- [x] `datt-workstation-temp` removed by Josh and verified absent. The SQL allow-list contains only
      `sati-demo-api-outbound-01` through `-03`.
- [x] Source commits `8a5cc185c1de495b016baad1595ca9cbdaaaf700` and
      `cb061a3b8c265b10bd7d9bd9b2e56d6e3805b07f` pushed to `origin/master`. The latter adds the
      compatibility fingerprints for the two persistence-relevant contract changes discovered
      during final package inspection.
- [x] Built
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\Sati.Api-1.2.39.zip`
      from `cb061a3b8c265b10bd7d9bd9b2e56d6e3805b07f` (9,673,491 bytes; SHA-256
      `4FC0373B5CC8E2DBD931ECED175B2FE0368F770A96FC2C7A589FEA7E729FD08A`; 70 entries). Package
      inspection found 0 backslash paths, 0 forbidden configuration/key files, and both required
      Demo history-reconciliation WebJob files. Published only to existing Demo App Service
      `sati-demo-api-satilogica` in `rg-sati-demo`; OneDeploy deployment
      `e45f97e926824f96ba3de82247b47f4a` completed successfully.
- [x] Hosted `/health/live` and `/health/ready` returned healthy. `/health/version` reported
      product `Sati.Api`, release `1.2.39`, and contract revision `E807EDE42231`, exactly matching
      the packaged client/API contract. Readiness therefore also confirmed that
      `SchemaDriftHealthCheck` accepted the migrated Demo schema. Health-only evidence is
      `artifacts/release-1.2.39-demo-readiness.json`; authenticated Admin checks were explicitly
      skipped because this workstation has no designated `SATI_DEMO_USERNAME` / password.

### Local Production machines
- [x] Both known Local Production machines are on 1.2.38 before release, confirmed by Josh.
- [ ] Record each machine after it launches 1.2.39 and applies the pending migrations locally.

### Artifacts
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.39.exe`
      (100,909,056 bytes; SHA-256
      `D0E9101FCBEBAFF70338F433453664A2EAC13D08438A726160C80AF04B1688D0`). All five installed
      launches reached a responsive sign-in window, closed normally with exit code 0, reported
      version 1.2.39.0, and isolated cleanup passed. Evidence is
      `artifacts/release-1.2.39-demo-installer-acceptance.json`; this is a same-machine acceptance,
      not a clean external-machine attestation.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.39.exe`
      (203,245,322 bytes; SHA-256
      `989C0D83B5E45135049ADBA3F676B3357F40BB3150961CAE513214D2194A0387`). Version 1.2.39.0,
      Windows integrated security, and isolated cleanup passed. Embedded `SqlLocalDB.msi`
      (SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) had a valid Microsoft
      Corporation Authenticode signature before use. The generated installers themselves are not
      represented as code-signed.
- [x] Published both accepted installers and only their `.sha256` files through uniquely named,
      hash-verified temporary siblings to
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop` and
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
      Destination hashes and checksum contents match the accepted artifacts; no existing file was
      overwritten and no publication temporary file remains.
- [x] Release evidence commit `da66977` pushed. The checklist-closing commit was then pushed and
      final local `master` confirmed equal to `origin/master`.

### Branches
- [x] `master` and `origin/master` began at `cd22dda`; remote default confirmed as `master`.
- [x] Retain `claude/cool-jang-f6b3c4`: checked out by a linked worktree.
- [x] Retain `second-machine-setup` and remote `claude/local-vs-github-workflow-dlcqpb`: both have
      unique work unrelated to this release.
- [x] Retain merged `feature/caseload-transfer`; it has no unique commits and is not required to be
      deleted.

---

## Release 1.2.38 — 2026-09-01

"Bring a caseload with you." Credible export import — single consumer and bulk folder — plus the
caseload ownership transfer and supervisor distribution screen it depends on. Designed in
`CREDIBLE_IMPORT_DESIGN.md`; all six sequencing steps built and tested.

**A schema-changing release.** Migration `20260901232228_AddPersonCredibleClientId` adds
`People.CredibleClientId` (`nvarchar(32)`, nullable). Additive, no backfill, no data transformation.
It is the dedupe key for import: re-running a folder must report rather than duplicate. Bounded
rather than following the `nvarchar(max)` convention `EvergreenId` and `MaineCareId` use, so a
future filtered unique index on `(AgencyId, CredibleClientId)` does not need a narrowing migration
first — the mistake `Form.Type` had to be corrected for.

**Authorization.** Invoked with the literal `invoke DATT!`. The Demo migration was authorized
explicitly by Josh after the preflight report named it, and the temporary
`datt-workstation-temp` SQL firewall rule for `72.95.106.10` was added by Josh. A general "I defer
to your opinion" earlier in the same conversation was deliberately **not** treated as authorization
for either — see `AGENTS.md` section 5.

**New dependency.** AngleSharp 1.7.2, for reading saved Credible print views. Pure managed, no
native components; the Local installer acceptance gate is what proves it packages onto a clean
machine.

**New API routes.** `PUT /api/v1/people/{personId}/owner` and
`POST /api/v1/people/credible-matches`. Both recorded in `API_AUTHORIZATION.md`; the route
inventory moves from 114 to 116. `SavePersonRequest` gained `CredibleClientId`, recorded as
contract shape `person-credible-client-id-v1`, so a newer client cannot silently lose the dedupe
key against an older server.

### Validation
- [x] Release build of the full solution: 0 errors, 10 warnings.
- [x] Sati desktop/domain tests: 1,126 passed, 1 skipped
      (`LocalAiModelCompetenceTests.ConfiguredModelCompletesGroundedWorkflowAcrossRepresentativeCurrentNoteInputs`
      — the `SATI_RUN_LOCAL_AI_MODEL_EVAL` on-device model evaluation, a documented opt-in whose
      prerequisite is genuinely absent).
- [x] Sati API integration tests: 324 passed. Carika tests: 4 passed.
- [x] `git diff --check` clean.

### Demo deployment
- [x] Temporary `datt-workstation-temp` firewall rule added by Josh for `72.95.106.10`.
- [x] `SatiDemo` reachable and confirmed as `EnvironmentName = Demo`, with
      `People.CredibleClientId` absent and no `__EFMigrationsHistory` row - a clean starting state.
- [x] Controlled Demo migration applied through `scripts/Apply-CredibleClientIdMigration.ps1`,
      written for this release on the `Apply-DerivedFormComplianceMigrations.ps1` pattern: fails
      closed on database and environment identity, guards on the real schema rather than history,
      and verifies an already-present column is `nvarchar(32)` and nullable rather than merely
      correctly named. Three passes: dry run reported 1 column and 1 history row and rolled back;
      the real pass wrote both; the third reported 0 and 0, proving idempotency. Final state
      verified directly: `nvarchar(32)`, nullable, one `__EFMigrationsHistory` row, 0 of 177
      consumers populated.
- [x] `datt-workstation-temp` removed by Josh and verified absent: the allow-list now holds
      exactly the three `sati-demo-api-outbound-*` entries. It was still open at the first check
      after publication, which is longer than the migration needed it; closing it immediately
      after the migration rather than at the end of the release is the habit to keep. The release
      workflow never adds, alters, or deletes a firewall rule.
- [x] API ZIP `artifacts/Sati.Api-1.2.38.zip`, 9,519,208 bytes, SHA-256
      `0EDF6DCC2887B2199540CBB8CC7B53D1693D4C2A3DABAA9A39581E43B72222C9`; 70 files with
      forward-slash entry paths, both WebJob files present, no `appsettings*.json` or key material.
- [x] Deployed only to the existing App Service `sati-demo-api-satilogica`; OneDeploy
      `d48c57ab-2056-42df-bca2-e1dc2abf10b9` succeeded. `/health/live` returns `{"status":"live"}`,
      `/health/ready` returns `Healthy`, and `/health/version` reports `Sati.Api`, release
      `1.2.38`, contract `64831C77F89C`. The revision read from the locally built `Sati.Contracts`
      is also `64831C77F89C`; it moved from `729A9E9F9B2B` because two routes were added. A healthy
      readiness result is the real confirmation the migration satisfied the deployed model, because
      `SchemaDriftHealthCheck` compares the model against the database.

### Local Production machines
- [x] Both machines are on 1.2.38, per Josh. Neither is behind. The desktop applies pending
      migrations at launch, so this is the half of a schema release that the Demo application
      does not speak for.

### Artifacts
- [x] Generated and accepted
      `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.38.exe` (100,896,768 bytes; SHA-256
      `A238FC50AB173E1795D3EF415D6EED53A313B0E4C0A0545793FEC062AB6060EF`) without overwriting an
      artifact: all five installed launches responded, closed gracefully with exit code 0,
      reported version 1.2.38.0, and isolated cleanup passed. Evidence in
      `artifacts/release-1.2.38-demo-installer-acceptance.json`. Those five launches are also what
      proves the new AngleSharp 1.7.2 dependency packages and loads on a clean install.
- [x] Generated and accepted
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.38.exe` (202,957,066 bytes; SHA-256
      `B97803691CDC7C5731D9FBFEFC95103F79ECEFD2051F8EDE84A9794E00B5DA20`) without overwriting an
      artifact: version 1.2.38.0, Windows integrated security with no SQL username or password in
      configuration, and isolated cleanup passed. Its embedded
      `artifacts\Prerequisites\SqlLocalDB.msi` (63,508,480 bytes) carried a valid Microsoft
      Corporation Authenticode signature, verified before use. The generated installers themselves
      are not code-signed.
- [x] Published without overwriting anything. Each file was copied to a uniquely named temporary
      sibling, hash-verified there, renamed to its final versioned name, and verified again:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.38.exe`
        and `SatiLocalSetup-1.2.38.exe.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.38.exe`
        and `SatiDemoSetup-1.2.38.exe.sha256`

      Both destinations resolved inside the named documents root and neither previously held a
      1.2.38 artifact. No API ZIP, LocalDB prerequisite, or private configuration was published to
      either folder.

### Branches
- [x] Merged `feature/caseload-transfer` (7 commits) into `master`; the branch is retained
      locally rather than deleted, because it is the working branch this release came from and
      nothing requires its removal.
- [x] Retained `claude/cool-jang-f6b3c4`: checked out by a linked worktree, which the playbook
      forbids deleting regardless of its ancestry.
- [x] Retained `second-machine-setup` (7 unique commits) and remote
      `claude/local-vs-github-workflow-dlcqpb` (1 unique commit): both hold work unrelated to this
      release.

### Landed after publication - NOT in the 1.2.38 artifacts
- `7c7d92b` added a `ScrollViewer` to the supervisor dashboard toolbar and a
  `ReleaseUiStructureTests` assertion covering both dashboards. It was written while chasing a
  reported "the bulk import option is missing", which turned out to be nobody's bug: the option
  was on the Supervisor tab, where it is designed to be, and was simply not where it was being
  looked for.
- The change is still worth keeping. The toolbar was a bare horizontal `StackPanel`, which clips
  silently once its children exceed the window width, and it had gained two entries in this
  release with no guard.
- **It is not in the published installers.** Source at 1.2.38 now produces different bytes than
  `SatiDemoSetup-1.2.38.exe` and `SatiLocalSetup-1.2.38.exe`. Nothing was overwritten and the
  published artifacts remain the accepted ones; this note exists so that a later build from
  `master` calling itself 1.2.38 is not mistaken for them. It ships in the next release.
- The Clients workspace now exposes automatic overflow for its Overview, section rail, editor, and
  fixed-width document workspaces. A separate compact-display mode detects the physical monitor at
  shell startup. At the 1920 × 1080 boundary it silently starts the horizontal consumer selector and
  tighter layout; below the boundary it also explains the adjustment once and collapses Today's
  Work. It condenses navigation and spacing, enables pixel-rounded display-optimized text, keeps the
  ordinary reopen controls, and does not globally shrink fonts or hit targets. These changes are
  likewise post-publication source and ship in the next release, not the accepted 1.2.38 installers.
- The Clients Overview now reserves a real working height for Notes and Journal instead of leaving
  them in a star row that collapses under the outer overflow viewer. Forms comes immediately before
  them; Contacts and Support Team plus Medical Providers now follow at the bottom as reference
  panels. Both the full roster and horizontal selector expose the same theme-aware person-plus Add
  Client action, replacing the ambiguous circular-arrow glyph. The Overview now measures the inline
  consumer editor against its available width instead of creating a wide horizontal canvas, and the
  compact Forms matrix uses the Overview's one vertical scrollbar rather than adding a nested third
  scrollbar. This is also next-release source.

### Known gaps shipped deliberately
- An SSN in an export is shown and refused rather than saved. Nothing writes an imported SSN yet;
  the row says so instead of appearing to capture it. See `CREDIBLE_IMPORT_DESIGN.md`.
- No `person.imported` audit action. An imported consumer records `person.created`, which is
  accurate; the gap is granularity, and closing it needs a contract change to answer a question
  nobody has asked yet.
- Bulk import and the distribution screen had not been exercised by a person before this release
  pass; both are checked by hand against Demo before installers are published.

---

## Release 1.2.37 — 2026-09-01

"Sati starts again." A one-defect release fixing the 1.2.36 startup refusal described below, and
carrying the whole 1.2.36 change set to Local machines that could not install it.

**Authorization note.** This release ran on Josh's explicit, twice-repeated instruction rather than
the literal `invoke DATT!` phrase. The concern was raised once and reaffirmed; recording it here so
the audit trail says what actually happened.

**Not a schema-changing release.** No migration was added after 1.2.36. `SatiDemo` is already at
that schema and healthy, so no Demo migration, no temporary SQL firewall rule, and no cloud
database action of any kind belongs to this release. The API publish is a version bump only.

**The gate that was missing.** 1.2.36 proved its migration against `SatiDemo` through the guarded
script, which bypasses the desktop startup path entirely — so the code that actually refused was
never exercised before publication. This release adds that check: the real `LocalDatabaseUpdater`
path is run against a genuinely un-migrated `SatiProduction` before any artifact is published.

### Validation
- [x] Release build of the full solution: 0 errors.
- [x] Sati desktop/domain tests: 1,014 passed, 1 skipped (the `SATI_RUN_LOCAL_AI_MODEL_EVAL`
      on-device model evaluation — documented opt-in prerequisite, genuinely absent).
- [x] Sati API integration tests: 302 passed. Carika tests: 4 passed.
- [x] **The gate 1.2.36 lacked.** The dev `SatiProduction` was rewound to the exact pre-1.2.36
      shape — `Forms.Type` back to `nvarchar(max)`, `IX_Forms_PersonId` restored, `IsCompliant`
      re-added, both history rows deleted — and seeded with the reported defect: three `Q1R` rows
      at one due date with one completed, plus a `PCP` pair flagged compliant with no date. The
      real `LocalDatabaseUpdater.UpdateAsync` path then reported **`Applied`**, having taken a
      backup (the database held records), merged 2 duplicate groups removing 3 rows with 0
      conflicts, and applied both migrations.
      Result: one `Q1R` keeping its real completion date `2026-08-28`; one `PCP` backfilled to its
      cycle start `2026-05-30`; `IsCompliant` dropped; unique index present; 2 history rows;
      3 `form.duplicate-removed` and 1 `form.compliance-date-backfilled` audit events.
      This is the first time the repair-then-index sequence ran against real duplicates on SQL
      Server rather than SQLite. The synthetic "Rehearsal Client" remains in the dev database,
      marked `IsTestData`.

### Deployment and artifact evidence
- [x] Source release commit `c48fc12` pushed to `master` and confirmed on the remote before any
      artifact was produced.
- [x] **No cloud database action.** `SatiDemo` was already at the 1.2.36 schema; no migration, no
      temporary firewall rule, and no Azure SQL access of any kind belongs to this release.
- [x] Demo API ZIP `artifacts/SatiApi-1.2.37.zip` built from `c48fc12`. `Sati.Api.dll` reports file
      version `1.2.37.0` and product version
      `1.2.37+c48fc1285df7562d31b85c82ffd9a5f30d1273c0`. 9,502,275 bytes; SHA-256
      `5880468485814AD10B26FD03964ACAD29FF08DD606C82ADAFFE9BF56E6407243`; 70 files with
      forward-slash entry paths, both WebJob files present, no `appsettings*.json` or key material.
      The 1.2.36 package remains in `artifacts`.
- [x] Deployed only to the existing App Service `sati-demo-api-satilogica`; OneDeploy
      `941e7c0cd86e4d838872902e23144b67` succeeded. `/health/live` live, `/health/ready` Healthy,
      `/health/version` reports `Sati.Api`, release `1.2.37`, contract `729A9E9F9B2B` — matching
      the locally built `Sati.Contracts`, and unchanged from 1.2.36 because no contract moved.
- [x] Generated and accepted `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.37.exe`
      (100,487,168 bytes; SHA-256
      `F3D19F4A5F46B77123A4437975E01EB0CAEF872BEE431AC5D0DF50E6CAF88D0E`): five installed launches
      responded, closed gracefully with exit code 0, reported version 1.2.37.0, cleanup passed.
      Evidence in `artifacts/release-1.2.37-demo-installer-acceptance.json`.
- [x] Generated and accepted `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.37.exe`
      (202,546,954 bytes; SHA-256
      `E2CBE465E6AF383C06E3BE881F1010DB3BB56DE1CF23778E1F357F09AC7BCD18`): version 1.2.37.0,
      Windows integrated security, cleanup passed. Its embedded `SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carried a valid Microsoft
      Corporation Authenticode signature, verified before use. The generated installers are not
      represented as code-signed.
- [x] Published both accepted installers and only their `.sha256` files through uniquely named,
      hash-verified temporary siblings to `...\SatiLogica - Documents\Sati Desktop` and
      `...\SatiLogica - Documents\SatiLogica Demo Files`. No file overwritten, no temporary left.
      The 1.2.36 installers remain in place, superseded and deliberately not deleted — **they will
      still refuse to start until 1.2.37 is installed over them.**
- [ ] Local Production machines and the release each is on. Awaiting Josh. Both were on 1.2.35;
      one has 1.2.36 installed and cannot open until 1.2.37 is applied over it.

## 1.2.36 blocks Local startup — found 2026-09-01, fixed, needs 1.2.37

**Every Local Production machine refuses to start on 1.2.36.** Josh hit it installing the release.
The dialog is correct and nothing was written, but the premise it stopped on was false.

`MigrationEffectAnalyzer` judged an `AlterColumnOperation` satisfied on nullability alone.
`AddUniqueFormPersonTypeDueDateIndex` narrows `Forms.Type` from `nvarchar(max)` to `nvarchar(40)`
so it can be indexed, and that column is `NOT NULL` before and after. So on a database that had
had *none* of the migration, the alter read as already applied while the unique index beside it
read as missing — one present, one missing, `PartiallyPresent`, which is the one verdict
`LocalDatabaseUpdater` deliberately will not act on. It is the correct refusal for a genuinely
mixed schema; here the schema was simply un-migrated.

Verified directly against this machine's `SatiProduction`: `Forms.Type` still `nvarchar(max)`,
`IX_Forms_PersonId` still present, `IX_Forms_PersonId_Type_DueDate` absent, `IsCompliant` present,
zero 2026-09 history rows. Nothing of the release had been applied, on either migration.

**Fixed:** an alter is now satisfied only when a declared bound has actually been applied. An
unbounded live column where the migration declares a bound is proof the alter has not run. Only
unbounded-versus-bounded counts as evidence — a column merely wider than declared stays satisfied,
because stopping startup over benign drift is the same class of mistake. Re-run against the live
database, both migrations now report `NotApplied`, which is the ordinary path: backup, duplicate
repair, migrate.

The analyzer had no automated coverage because it reads SQL Server catalog views. It has five
tests now, against a hand-built schema; the headline one fails against the unfixed classifier.

- [ ] Cut 1.2.37 with this fix. 1.2.36 cannot be re-cut — its installers are published and the
      playbook forbids replacing bytes under an existing version.
- [ ] Demo needs no repeat migration; `SatiDemo` is already at the 1.2.36 schema and healthy. The
      1.2.37 API publish is a version bump only.
- [ ] Treat the published 1.2.36 installers as superseded. Do not delete them; publish 1.2.37
      beside them.

## Release 1.2.36 — 2026-09-01

"One record, one answer." A completed 90-day review that kept blocking billing, traced to three
causes that all produce the same symptom and are individually invisible from the screen. Eleven
commits since the 1.2.35 audit commit `95b3b59`.

**This IS a schema-changing release.** Two migrations:
`20260901150802_AddUniqueFormPersonTypeDueDateIndex` and `20260901154714_AddDerivedFormCompliance`.
Both halves must be recorded — the Demo application below, and the Local Production machines, which
receive it only at their own next launch.

**Ordering is forced, not preferred.** `AddDerivedFormCompliance` drops `dbo.Forms.IsCompliant`,
which `InitialCreate` created `bit NOT NULL` with **no default constraint** (verified on `SatiDemo`:
`IsCompliantDefault = 0`). The 1.2.36 API no longer writes that column, so publishing it against a
database that still has the column breaks every `INSERT` into `Forms` — client creation in Demo.
The migration therefore precedes the API publication rather than following it.

### Pre-migration survey of SatiDemo — 2026-09-01, read-only

| Measure | Value |
|---|---|
| People / Forms | 177 / 4,124 |
| Duplicate `(PersonId, Type, DueDate)` groups | **0** — the index applies cleanly |
| `IsCompliant = 1` with no `CompletedDate` | 1,147 |
| Of those: reviews left open / no effective date / future cycle | 0 / 0 / 0 |
| Of those: backfilled from their cycle start | **1,147** |
| `IX_Forms_PersonId_Type_DueDate` present beforehand | no |

Demo has no duplicates because its forms come from the API's `BuildInitialForms`, not the desktop
path whose concurrent-load race produced them locally.

### Validation
- [x] Release build of the full solution: 0 errors.
- [x] Sati desktop/domain tests: 1,009 passed, 1 skipped.
- [x] Sati API integration tests: 302 passed.
- [x] Carika tests: 4 passed.
- [x] The single skip is `LocalAiModelCompetenceTests.ConfiguredModelCompletesGroundedWorkflow`
      `AcrossRepresentativeCurrentNoteInputs`, gated on `SATI_RUN_LOCAL_AI_MODEL_EVAL=1` for the
      on-device Foundry Local model evaluation. Documented opt-in prerequisite, genuinely absent.

### Deployment and artifact evidence
- [x] Source release commit `18ef75e` pushed to `master` and confirmed equal to `origin/master`
      before any artifact was produced.
- [x] Temporary `datt-workstation-temp` firewall rule added by Josh for `72.95.106.10` using
      `scripts/Set-DemoWorkstationFirewallRule.ps1`. The release workflow never created, altered,
      or deleted a firewall rule; it only wrote the script and validated that it parses.
- [x] Controlled `SatiDemo` migration via `scripts/Apply-DerivedFormComplianceMigrations.ps1`,
      guarded on the real schema rather than on `__EFMigrationsHistory`, and fail-closed on
      database and environment identity. Three runs:
      **dry run (rolled back)** and **real run** each reported `TypeNarrowed 1, IndexesAdded 1,
      RowsBackfilled 1147, AuditEventsWritten 1147, IsCompliantDropped 1, HistoryRowsWritten 2`;
      the **third run** reported all zeros, proving idempotency. The script refuses outright if
      duplicate `(PersonId, Type, DueDate)` rows exist, and aborts if the backfill and its audit
      trail disagree on the row count.
- [x] Post-migration state verified independently: `IsCompliant` column absent, unique
      `IX_Forms_PersonId_Type_DueDate` present, `Type` at `nvarchar(40)`, 4,124 forms unchanged,
      1,848 carrying a completion date (701 pre-existing plus 1,147 backfilled), 1,147
      `form.compliance-date-backfilled` audit events, 2 history rows.
- [x] Demo API ZIP `artifacts/SatiApi-1.2.36.zip` built from pushed source commit `18ef75e`.
      `Sati.Api.dll` reports file version `1.2.36.0` and product version
      `1.2.36+18ef75e4fe68c5c1ac8714ab8f9ebe62987c562b`. The ZIP is 9,501,366 bytes with SHA-256
      `1D951038A809A8459BA6B7F2573E3DA1C140061C261482855F3720B404022109`, holds 70 files — an
      identical file set to the 1.2.35 package — including the two `demo-history-reconciliation`
      WebJob files, and contains no `appsettings*.json`, private desktop configuration, credential
      pattern, or key material. The prior known-healthy 1.2.35 package remains in `artifacts`.
      **Rebuilt once before deployment:** `Compress-Archive` on Windows PowerShell 5.1 wrote 29
      backslash-separated entry paths where the known-good 1.2.35 package had none, which would
      have deployed files to wrong paths. Repacked with normalised separators and diffed against
      1.2.35 to confirm no file was gained or lost.
- [x] Demo API deployed only to the existing App Service `sati-demo-api-satilogica`; OneDeploy
      deployment `62c49231d5724f9fb78e9c8d373c16b7` succeeded. `/health/live` returns
      `{"status":"live"}`, `/health/ready` returns `Healthy`, and `/health/version` reports product
      `Sati.Api`, release `1.2.36`, contract revision `729A9E9F9B2B`. The revision read from the
      locally built `Sati.Contracts` is also `729A9E9F9B2B`. A healthy readiness result is the
      real confirmation the migration satisfied the deployed model, because `SchemaDriftHealthCheck`
      compares the model's tables and columns against the database.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.36.exe`
      (100,487,168 bytes; SHA-256
      `0DB4225E5FDBCB76E1D7EC6B9F34A8BB8385B38DA04D358897C8D20BA7284985`) without overwriting an
      artifact: all five installed launches responded, closed gracefully with exit code 0, reported
      version 1.2.36.0, and isolated cleanup passed. Evidence in
      `artifacts/release-1.2.36-demo-installer-acceptance.json`.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.36.exe`
      (202,545,930 bytes; SHA-256
      `8EFF42D1E6D057237FCAF571D8833CA43E2FB28BF86E60AEE867B2D39EFD06D7`) without overwriting an
      artifact: version 1.2.36.0, Windows integrated security, and isolated cleanup passed. Its
      embedded `artifacts\Prerequisites\SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carried a valid Microsoft
      Corporation Authenticode signature, verified before use. The generated installers themselves
      are not represented as code-signed.
- [x] Published both accepted installers and only their `.sha256` files through uniquely named,
      hash-verified temporary siblings to
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop` and
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
      Destination hashes match the accepted artifacts; no existing file was overwritten and no
      temporary file remains.
- [x] `datt-workstation-temp` removed by Josh immediately after the migration and verified absent:
      the allow-list holds exactly the three `sati-demo-api-outbound-01/02/03` entries
      (`52.189.72.76`, `20.118.56.5`, `20.118.56.47`) and nothing else. The removal path re-lists
      after deleting and throws if the rule survives, so this is a verified absence rather than an
      assumed one. The release workflow never created, altered, or deleted a firewall rule.
- [ ] Local Production machines and the release each is on, including any known to be behind.
      Awaiting Josh. Both machines were on 1.2.35 before this release; **neither receives 1.2.36
      until the new Local installer is run there**, and the desktop applies the duplicate repair and
      both migrations at that first launch, not before.

### Branch audit
- [x] Deleted `fix/derived-form-compliance` (`6d98931`), `fix/duplicate-compliance-forms`
      (`3aa960c`) and `handoff/duplicate-compliance-forms` (`1184140`) locally and remotely after
      proving each fully merged, with no unique commits and no worktree holding it.
- [x] Retained `claude/cool-jang-f6b3c4`: fully merged but checked out by a linked worktree.
- [x] Retained `second-machine-setup` (7 unique commits) and remote
      `claude/local-vs-github-workflow-dlcqpb` (1): historical setup and documentation branches from
      2026-08-16 whose current intent is not safe to infer.

## Release 1.2.35 — 2026-09-01

Daily sign-in agenda, explicit quarterly-review attestation, human-accepted suggested follow-ups,
successful-save Notes filter clearing, and the fail-closed legal-hold boundary for ordinary-client
deletion. Twenty commits since the 1.2.34 evidence commit `51fd1aa`.

**This is not a schema-changing release.** No migration was added after 1.2.34. The API change adds
a narrow read over the existing Comprehensive Assessment table and does not depend on a new column
or table, so no Demo database migration or temporary SQL firewall rule applies.

### Validation
- [x] Source release commit `70dea6d` created and pushed to `origin/master` without rewriting
      history. The API and both installers were built from that pushed source.
- [x] Complete Release build passes: 0 errors, 6 warnings (offline NuGet vulnerability feed, the
      existing guarded raw-SQL analyzer warning, and three test-code analyzer/nullability warnings).
- [x] 1,284 tests pass — 978 desktop/domain, 302 API integration, and 4 Carika. One documented
      opt-in local-AI model competence test is skipped because `SATI_RUN_LOCAL_AI_MODEL_EVAL=1`
      is absent.
- [x] 54 focused release regressions pass, including opposite-theme WPF rendering, agenda behavior,
      quarterly attestation, form-date validation, suggested-follow-up acceptance, Notes filter
      clearing/retention, API surface parity, and tenant-scoped assessment reads.

### Deployment and artifact evidence
- [x] Demo API ZIP `artifacts/SatiApi-1.2.35.zip` built from pushed source commit `70dea6d`.
      `Sati.Api.dll` reports file version `1.2.35.0` and product version
      `1.2.35+70dea6dfa3fd2dcc9cb1864d69dfc86c54ca27ca`. The ZIP is 9,635,903 bytes with
      SHA-256 `B344E8EB60063A8451ED924BD393ED3CF8248F904B533CBEE3558DCDF345F2DD`.
      It contains the two `demo-history-reconciliation` WebJob files and no `appsettings*.json`,
      Development/private desktop configuration, credential pattern, or key material. The prior
      known-healthy 1.2.34 API package remains in `artifacts`.
- [x] Demo API deployed only to existing App Service `sati-demo-api-satilogica`; OneDeploy
      deployment `632393f093f9427ea4af6b3b2508fb77` succeeded. `/health/live` returns live,
      `/health/ready` returns Healthy, and `/health/version` reports product `Sati.Api`, release
      `1.2.35`, and contract revision `729A9E9F9B2B`. The revision read directly from the locally
      built `Sati.Contracts` is also `729A9E9F9B2B`. The health-only readiness gate passed;
      authenticated readiness was not required by the release gate and was skipped because the
      synthetic Demo credentials were not present in this process.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.35.exe`
      (100,470,784 bytes; SHA-256
      `42053C72EC06CA094E39A8B8FAF0C7CD489E3D9388C3B54FAF2A5F8FFFFB17C6`) without overwriting an
      artifact: all five installed launches responded, closed gracefully with exit code 0, reported
      version 1.2.35.0, and isolated cleanup passed.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.35.exe`
      (202,810,634 bytes; SHA-256
      `DB339529AD8D7184312C538FB3DC912910A2F4144664F76692E218C48015CAA1`) without overwriting an
      artifact: version 1.2.35.0, Windows integrated security, and isolated cleanup passed. Its
      embedded `SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) had a valid Microsoft
      Corporation Authenticode signature before use. The generated installers themselves are not
      represented as code-signed.
- [x] Published both accepted installers and only their `.sha256` files through uniquely named,
      hash-verified temporary siblings to
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop` and
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
      Destination hashes match the accepted artifacts and checksum contents; no existing file was
      overwritten and no temporary file remains.
- [x] Release evidence commit `5293aad` pushed. The checklist-closing commit was then pushed and
      final local `master` confirmed equal to `origin/master`.

### Branch audit
- [x] Deleted completed `docs/feature-handoffs` at `74b191f` locally and remotely after proving it
      fully merged, had no unique commits, and was not checked out by a worktree.
- [x] Retained `claude/cool-jang-f6b3c4`: fully merged but checked out by a linked worktree that
      also contains an untracked `AGENTS.md`.
- [x] Retained `second-machine-setup` and remote `claude/local-vs-github-workflow-dlcqpb`: both
      contain unique divergent setup/documentation work whose current intent is not safe to infer.

## Quarterly review attestation and refresh repair — 2026-08-31

- [x] Preserve the deliberate split between `ReviewItem` evidence and `Q1R`-`Q4R` form
      attestations; logging evidence does not auto-complete a billing gate.
- [x] Correct the Reviews legend, display the shared matrix-owned attestation status in current-
      quarter and all-quarter views, and expose the state and dates in the detail pane.
- [x] Add explicit completion/reset controls. Completion starts with a blank required date,
      preserves an entered late date, rejects a future date, and writes only through
      `Form.Attest`/`RevokeAttestation` and `IFormService`.
- [x] Centralize the post-form-change cascade so dashboard flags, the caseload matrix, and
      `UpcomingEvents` refresh after dashboard, task-board, form-note, Clients, and Reviews paths.
- [x] Enforce the non-future completion rule in shared contracts, Local persistence, and the API;
      the API returns a validation problem without changing stored state.
- [x] Add regressions for copy, shared status ownership, explicit late dates, historical billing
      windows, rejected future dates, the no-auto-derive boundary, and all completion cascades.
- [ ] Before release, tell case managers that quarters tracked only as Review items remain open
      attestations and may form an operational backlog. Do not bulk-close them or invent dates.
- [x] Replace the older dashboard and Clients quick-toggle convention that recorded `DueDate` with
      the shared blank attestation capture. Completed in the 2026-09-03 steps 1–3 slice above.

## Release 1.2.34 — 2026-08-31

Per-user permissions, the line-by-line audit that followed, and the claim-response half of the
billing exchange. Eleven commits since the 1.2.33 evidence commit `6704c17`.

**This is a schema-changing release.** Two migrations, and the first adds a column
`ValidatedActorFilter` reads on every authenticated request, so the Demo migration must land
BEFORE the dependent API is published. Both halves are recorded below, per the rule added in
`9cbcf0e`: Demo is one database migrated deliberately; Local `SatiProduction` is a separate
database on every machine, migrated by the desktop at its next launch.

| Migration | Effect |
|---|---|
| `20260830224423_AddUserPermissions` | `Users.Permissions` int NOT NULL default 0, plus the legacy-role backfill |
| `20260830231500_SeparateAgencyWideSupervision` | Director 7 → 19, Admin 15 → 31 |

### Validation
- [x] Source release commit `c2f19b6`, pushed to `origin/master` without rewriting history.
- [x] Release build clean: 0 errors, 7 warnings.
- [x] 1,221 tests pass across all three projects — 930 desktop, 287 API, 4 Carika. One skip:
      `LocalAiModelCompetenceTests`, whose documented prerequisite `SATI_RUN_LOCAL_AI_MODEL_EVAL=1`
      is genuinely absent.
- [x] `scripts/Apply-UserPermissionsMigrations.ps1` written and rehearsed against a throwaway
      `SatiDemo` in an isolated LocalDB instance, never against a real database. Dry run rolled
      back with nothing left behind; real run applied; third pass reported every count and proof
      at 0. Two further scenarios exercised: the column-present-without-history drift case that
      makes EF's generated script fail with SQL 2705 (skipped the backfill rather than clobbering
      it, applied only the correction, reconciled history), and both fail-closed guards
      (correction recorded without its base migration; environment identity mismatch).
- [x] Resulting values match `UserPermissionRules.FromLegacyRole`: CaseManager 1, Supervisor 3,
      Director 19, Admin 31, PlatformOperator 0.

### Deployment evidence
- [x] Demo migration applied to real `SatiDemo` with the three-pass sequence, no EF-generated
      script involved. Dry run: column added, 16 users backfilled, 1 Director and 1 Admin
      corrected, 2 history rows, rolled back — verified afterwards that the column was absent,
      zero history rows, 80 migrations, so the rollback genuinely held. Real run: identical
      counts, committed. Third pass: every count and every proof `0`.
- [x] Resulting distribution across 16 accounts, 82 migrations in history: Admin 31 (1),
      CaseManager 1 (12), **Director 19 (1)**, PlatformOperator 0 (1), Supervisor 3 (1).
      Demo held exactly one Director — the account the original backfill would have handed the
      audit export, settings writes, test-data deletion, and provider merge.
- [x] API ZIP built from the pushed source commit `c2f19b6`, confirmed by the stamped
      `ProductVersion 1.2.34+c2f19b6b19b6045cd3f7113ee9eaba2f03ea3395`. 9,494,910 bytes; SHA-256
      `D797B6F426FD9B65EFB5E4404461AA58041614390F1EBDFDFD01952C54257016`. No `appsettings*.json`,
      Development configuration, or key material in the payload; the triggered WebJob is present
      at `App_Data/jobs/triggered/demo-history-reconciliation/`.
- [x] Deployed to `sati-demo-api-satilogica`, deployment `3a36b20eab2148c3a09a1f0df886e718`,
      provisioning state Succeeded.
- [x] `/health/live` live. `/health/ready` **Healthy** — the real confirmation the migration
      satisfied the deployed model, because `SchemaDriftHealthCheck` compares the model's tables
      and columns against the database. `/health/version` reports release `1.2.34` and contract
      revision `88B12BEC015F`.
- [x] Client/API contract parity verified rather than assumed: `ApiSurface.Revision` read directly
      from the locally built `Sati.Contracts` is `88B12BEC015F`, identical to the deployed value.
- [x] Generated and accepted `SatiDemoSetup-1.2.34.exe` (100,442,112 bytes; SHA-256
      `A3798804E1C5671C7B131DCB4ACE06152AC040D6993F8F99F75A31F0037C0020`): five launches, all
      responding, graceful closes, zero exit codes, installed version 1.2.34.0, isolated cleanup
      passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.34.exe` (202,507,018 bytes; SHA-256
      `DD6E77AF60F667A4DD561954A9072D5CD7D63A5629F3768CB0DE3B086AED7534`): version 1.2.34.0,
      integrated security confirmed with no SQL username or password in configuration, isolated
      cleanup passed. The embedded `artifacts\Prerequisites\SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carries a valid Microsoft
      Corporation Authenticode signature, verified before use.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `...\SatiLogica - Documents\SatiLogica Demo Files` and `...\SatiLogica - Documents\Sati
      Desktop`. Destination hashes re-verified after publication and identical to the accepted
      artifacts; no file overwritten; no temporary file left behind.
- [x] The temporary `datt-workstation-temp` firewall rule was added by the user for the migration
      and removed immediately after it. Verified absent afterwards: the allow-list holds exactly
      the three `sati-demo-api-outbound-*` entries. The release workflow never created, altered,
      or deleted a firewall rule.

### Local Production machines
Both machines were on 1.2.33 before this release, per Josh. `SatiDemo` is one database and was
migrated deliberately above; `SatiProduction` is a separate database on each of these machines,
migrated by the desktop at its next launch.

- [x] **This workstation (development machine).** Sati is run from source here, so the release it
      is "on" is the working tree, which was 1.2.33 before this change. Its `SatiProduction` is
      already at **82 migrations** with the permissions backfill applied — its single Admin account
      carries 31 — so the schema half is satisfied on this machine. It was migrated by a
      development build at startup rather than by an installed release: the
      `SeparateAgencyWideSupervision` migration was authored during the 1.2.34 release session, so
      whatever applied it ran from current source the same day.

      Recorded because it is confusing otherwise: the *installed packages* on this machine are
      stale and not the thing being used. The Windows uninstall registry and the on-disk file
      versions both report `Sati (LocalDB) 1.2.23` and `Sati Demo 1.2.27`. A future reader
      comparing installed versions against this record will find them ten releases apart and
      should not conclude the machine was missed.

- [ ] **Colleague's laptop — 1.2.33, has NOT received either migration.** It needs
      `SatiLocalSetup-1.2.34.exe` from `...\SatiLogica - Documents\Sati Desktop`; the desktop will
      apply `AddUserPermissions` and `SeparateAgencyWideSupervision` to its `SatiProduction` at
      first launch, taking a backup first as it does for any change.

      Two things make this the machine to watch. It is the one `AGENDA.md` records as possibly
      still carrying unreconciled migration-history drift, and `SatiProduction` there has not
      received `AddBillingExchangeHistory` or `AddRemittanceDeposits` either. So its first 1.2.34
      launch attempts more than the two migrations named here, against the database most likely to
      disagree with its own history. If it refuses to start, that refusal is correct and
      `scripts/remote-repair` or `scripts/Apply-UserPermissionsMigrations.ps1 -DatabaseName
      SatiProduction` is the path, not a retry.

- [x] Nothing was assumed to have caught up. The one machine reachable from here was inspected
      directly rather than taken on trust; the other is recorded as outstanding rather than
      presumed done.

## Release 1.2.33 — 2026-08-30

Sati repairs the provable half of a migration history disagreement at startup, instead of refusing
to start. No user-facing feature changes and no new migrations.

### Why this release exists
1.2.32 refused to start on three machines with SQL 2705, "Column name 'AgencyId' in table
'Settings' is specified more than once". The refusal was correct — the startup guard backed up,
stopped, and changed nothing — but the message was a provider error, and clearing it needed a
person who could read it and a PowerShell script per machine.

### Startup schema handling
- [x] `MigrationEffectAnalyzer` in `Sati.Persistence` compares what each pending migration declares
      against the live schema before anything is written. Columns, indexes, foreign keys, and
      primary keys are matched by what they map rather than by name.
- [x] Every effect present is recorded rather than applied — an insert into
      `__EFMigrationsHistory` touching no schema and no consumer data. No effect present migrates
      normally, unchanged.
- [x] Only partly present, or a verdict the analyzer cannot reach, still refuses. That is the
      judgement the startup path has always declined to make unattended, and it is unchanged.
- [x] The refusal now names the migration and states that nothing was changed, rather than
      surfacing the provider error.
- [x] Raw SQL and data steps are reported as unverifiable and left out of the verdict. An
      unrecognised operation type counts as unverifiable too, so an unfamiliar migration reports
      `Indeterminate` rather than a confident wrong answer.
- [x] The backup still happens first whenever the database holds records.

### Validation
- [x] Six new tests. The partial-drift guard was confirmed to fail against the ungated code before
      being kept.
- [x] Verified end to end against a real SQL Server database, not only with fakes: a scratch
      database built from the chain, drifted by removing one history row, run through the real
      updater, then dropped. Outcome `Applied`, drift recorded, nothing pending, second run
      `AlreadyCurrent`.
- [x] The analyzer was also run against a genuinely drifted `SatiProduction`, which was restored
      afterwards.

### Release evidence
- [x] Source release commit `d5c640a`, pushed to `origin/master` without rewriting history.
- [x] Full Release build of `Sati.slnx` succeeded. 853 desktop/domain tests passed with the one
      documented opt-in local-AI skip, 250 API integration tests passed, 4 Carika tests passed.
- [x] No schema change. The 80 migration ids are identical to those released in 1.2.32, and the
      contract surface is unchanged, so neither a controlled Demo migration nor a temporary
      firewall rule applied. Verified by comparing id sets, not counts.
- [x] Published the Demo API ZIP built from `d5c640a` (9,462,028 bytes; SHA-256
      `7F6FC2A0850977DE663BB01BF2BAA86C81CE18C03E962F01F6137F27C0F542F3`; no `appsettings*.json`,
      credential, or private desktop configuration in the payload; carries only the
      `demo-history-reconciliation` WebJob). Deployment `8db52990075c479db5870f51629ea1a6`
      succeeded.
- [x] `/health/live` live, `/health/ready` Healthy, `/health/version` reports release 1.2.33 and
      contract revision `7C6F00E77F6E`, matching the client's `ApiSurface.Revision`.
- [x] Full authenticated `Test-DemoReadiness.ps1` gate passed: Admin role, agency 2, 15 users, 177
      people, recent activity, `PolicyOnly` retention, 332 audit events.
- [x] Generated and accepted `SatiDemoSetup-1.2.33.exe` (100,417,536 bytes; SHA-256
      `B80CA22A82532F23B85B30DA583F3D6AB119F9A70B619DF635F5B9478A322DCB`): five responsive
      launches, graceful closes, zero exit codes, version 1.2.33.0, isolated cleanup passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.33.exe` (202,486,026 bytes; SHA-256
      `56C6940794408C194BD0585D9757BF8B772B1EB600250767CE9D3ADA34555B4C`): version 1.2.33.0,
      integrated security confirmed, isolated cleanup passed. The embedded
      `artifacts\Prerequisites\SqlLocalDB.msi` carries a valid Microsoft Corporation Authenticode
      signature, verified before use.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `…\SatiLogica - Documents\Sati Desktop` (Local) and
      `…\SatiLogica - Documents\SatiLogica Demo Files` (Demo). No existing file was replaced.
- [x] The SQL allow-list held exactly its three `sati-demo-api-outbound-*` rules throughout. No
      temporary firewall rule was created or removed by this release.
- [ ] **Not yet proven on a machine that still has the drift.** Every database reachable from here
      was repaired before this shipped, so the self-repair has been verified against recreated and
      scratch drift rather than against an untouched drifted machine. The one remaining is the
      colleague's. Installing 1.2.33 there rather than running `scripts/remote-repair` would be the
      first real-world exercise of it; note that if that is the chosen route.
- [ ] Once distributed, the remaining drifted machine can install this rather than run
      `scripts/remote-repair`. That kit stays available for anyone on an older build.

## Release 1.2.32 — 2026-08-30

Platform-neutral persistence, schema drift detection, and Demo schema changes without a firewall
rule. No user-facing feature changes and no new migrations.

### Controlled migration deployment
- [x] `Sati.Persistence` targets plain `net10.0` and owns the entity model, `SatiContext`, and all 80
      migrations, so nothing that needs the chain is forced onto Windows.
- [x] `SchemaComparison` in `Sati.Contracts.V1` owns the rule for how two descriptions of a schema
      differ, shared by the readiness check and the drift report. `GET /api/v1/admin/schema-drift`
      returns it, Admin only.
- [x] The `demo-history-reconciliation` triggered WebJob runs inside the App Service, so reconciling
      `__EFMigrationsHistory` on Demo no longer needs a temporary exact-IP SQL firewall rule.
      **Corrected 2026-08-31: this originally read "applying a Demo schema change", which overstated
      what shipped.** The job writes only to `dbo.__EFMigrationsHistory` and reads catalog views —
      no `CREATE`, `ALTER`, or `DROP` — which is why it needs only `db_datawriter`. Applying real
      DDL still needs `Sati.Migrator`, which does not exist yet, and until it does a schema-adding
      release still opens the rule. 1.2.34 did. The sentence was read later as a promise the
      release never made; see `DECISIONS.md`, 2026-08-30 and 2026-08-31.
- [x] `SatiDemo`'s migration history reconciled: two ids applied under superseded timestamps removed,
      two chain migrations with no history row written, verified idempotent.

### Defect fixed
- [x] `ServerPerson.FirstName`, `ServerPerson.LastName`, and `ServerClaimLine.Units` were declared
      nullable in the API model while `SatiDemo` has them `NOT NULL`. The database was the stricter
      side, so the model was tightened and no schema was touched.

### Validation and release evidence
- [x] Source release commit `afa59df`, pushed to `origin/master` without rewriting history.
- [x] Full Release build of `Sati.slnx` succeeded. 847 desktop/domain tests passed with the one
      documented opt-in local-AI skip, 250 API integration tests passed, 4 Carika tests passed. Two
      pre-existing `CS8604` nullable warnings remain in provider test files; they are warnings, not
      errors, and predate this change set.
- [x] No schema change. The 80 migration ids are identical to those released in 1.2.31, so neither
      the controlled Demo migration authorization nor a temporary firewall rule applied to this
      release. Verified by comparing id sets, not counts.
- [x] Published the Demo API ZIP built from `afa59df` (9,453,008 bytes; SHA-256
      `8FCE069B21EB30B663DF68DCE4F5081E0ECB035FF4708F61C900D4BD06A5DFF1`; no `appsettings*.json`,
      credential, or private desktop configuration in the payload; carries only the
      `demo-history-reconciliation` WebJob). Deployment `4e65fda9c6a24ef88db74b52bfcc046e` succeeded.
- [x] `/health/live` live, `/health/ready` Healthy, `/health/version` reports release 1.2.32 and
      contract revision `7C6F00E77F6E`, which exactly matches the client's `ApiSurface.Revision`.
      **This restores parity**: the published 1.2.31 installers expected `F929FEB01DEB` while the
      deployed API already served `7C6F00E77F6E`, so a fresh Demo install showed the compatibility
      banner until now.
- [x] Full authenticated `Test-DemoReadiness.ps1` gate passed, not only the health-only form: Admin
      role confirmed, agency 2, 15 users, 177 people, recent activity, `PolicyOnly` retention, 331
      audit events. The 1.2.30 and 1.2.31 releases could only record this as skipped.
- [x] Generated and accepted `SatiDemoSetup-1.2.32.exe` (100,421,632 bytes; SHA-256
      `D6AC3024E6A4D965065EECAF6CC667349B2800815CCE2EE8EBC7F3E2DF4E3A94`): five responsive launches,
      graceful closes, zero exit codes, version 1.2.32.0, isolated cleanup passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.32.exe` (202,474,762 bytes; SHA-256
      `CA4E3FA08C43B9316F79A714D6052ED3C7D1322B6F59E470E8917BD7F06F4534`): version 1.2.32.0,
      integrated security confirmed, isolated cleanup passed. The embedded
      `artifacts\Prerequisites\SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carries a valid Microsoft
      Corporation Authenticode signature, verified before use.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `…\SatiLogica - Documents\Sati Desktop` (Local) and
      `…\SatiLogica - Documents\SatiLogica Demo Files` (Demo). No existing file was replaced.
- [x] The SQL allow-list held exactly its three `sati-demo-api-outbound-*` rules throughout. No
      temporary firewall rule was created or removed by this release.
- [ ] **Flaky test, identified by name at last:
      `DatabaseActivityTests.PatienceStateAppearsOnlyAfterTheConfiguredContinuousDelay`.** It failed
      once in ten runs across this release's gates and once during the 2026-08-30 persistence work,
      always inside `EventuallyAsync`, which polls against a one-second wall-clock deadline. Under
      concurrent build load that budget is too tight for the awaited state transition. It is a
      timing artifact, not a defect in the code under test, and nothing in 1.2.32 touches
      `DatabaseActivityViewModel`. Widen the deadline or make the transition awaited rather than
      polled — deliberately, not inside a release commit to turn a gate green.
- [ ] `SatiProduction` has not received `AddBillingExchangeHistory` or `AddRemittanceDeposits`. The
      desktop applies them on its next direct connection, now from a relocated assembly.
      `LocalDatabaseUpdate` takes a full backup first when the database holds records and names the
      backup path on failure, so the bad case is a legible error rather than a half-applied database.
      Watch that first launch.

## Release 1.2.31 — 2026-08-30

Billing submission home, denial worklist, humanized adjustment reasons, and deposit reconciliation.

### Billing exchange operations
- [x] Add inclusive billing-month range filters and keep each locked monthly period as its own
      retry-safe 837P generation request.
- [x] Add an API-authoritative submission home that groups append-only exchange events into current
      batch rows with claim count, charge value, send time, current status, search, and outstanding
      filters. Synthetic provenance remains a dedicated field.
- [x] Add a denial/unpaid worklist with status and 30/60/90/120+ aging filters, fast claim/payer/date
      search, and a shared CARC group-code explanation catalog for CO/PR/OA.
- [x] Add an explicit 835/EFT deposit model whose shared arithmetic exposes claim payments, PLB
      provider-level adjustments, EFT difference, pending EFT, mismatch, and penny-match states.
- [x] Extend the Demo-only synthetic seed and test exchange with accepted, rejected, partial, denied,
      reversed, unmatched, needs-review, PLB, pending-EFT, and EFT-mismatch contingencies.

### Validation and release evidence
- [x] Apply the identity-validated Demo billing schema runner twice; both real runs found the three
      tables/indexes and migration-history rows already present and changed nothing.
- [x] Source release commit `f3f56cd`, pushed to `origin/master` without rewriting history.
- [x] Full Release build of `Sati.slnx` succeeded. 834 desktop/domain tests passed with the one
      documented opt-in local-AI test skipped, 244 API integration tests passed, 4 Carika tests
      passed.
- [x] Published the Demo API ZIP built from `f3f56cd` (9,230,215 bytes; SHA-256
      `F5EECBE04C05CB3ACDB244817EADFCAA3485101FDC5F23FA6E949E0BAB095374`; no `appsettings*.json`,
      credential, or private desktop configuration in the payload). OneDeploy deployment
      `a140d7d5883f4a3b98b9b5401310b06a` succeeded 2026-08-30T01:40:34Z.
- [x] `/health/live` live, `/health/ready` Healthy, `/health/version` reports release 1.2.31 and
      contract revision `F929FEB01DEB`, which exactly matches the client's `ApiSurface.Revision`.
      `SchemaDriftHealthCheck` returning Healthy is the deployed confirmation that the three
      billing exchange tables are present in `SatiDemo`.
- [x] `Test-DemoReadiness.ps1 -HealthOnly` gate passed. The authenticated extension was skipped:
      no synthetic Demo credentials are configured in this Windows session.
- [x] Generated and accepted `SatiDemoSetup-1.2.31.exe` (100,388,864 bytes; SHA-256
      `707286DFACAEE6A35436EC808E365E3E2A60A6F85356DB23B0F097CAA6F717FE`): five responsive
      launches, graceful closes, zero exit codes, version 1.2.31.0, isolated cleanup passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.31.exe` (202,454,794 bytes; SHA-256
      `E383480A64E65C2F41E01F10CD995E0AD36BB54DDCA3FF4EE2D642D93BA49E34`): version 1.2.31.0,
      integrated security confirmed, isolated cleanup passed. The embedded
      `artifacts\Prerequisites\SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carries a valid
      Microsoft Corporation Authenticode signature.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `…\SatiLogica - Documents\Sati Desktop` (Local) and
      `…\SatiLogica - Documents\SatiLogica Demo Files` (Demo). No existing file was replaced.
- [ ] The rollback-only dry run of `scripts/Apply-BillingExchangeMigrations.ps1` was not repeated in
      this release pass. It reaches `SatiDemo` from the workstation, which needs a temporary
      exact-IP SQL firewall rule that only the user may add and remove. The deployed readiness
      check above already confirms the schema the release depends on.
- [ ] Keep live Office Ally transport, real 999/TA1/277CA/835 ingestion, corrected claims, raw X12
      drill-through, payer certification, note-to-denial loop, auth/unit alerts, forecasting, and
      benchmarking as explicitly deferred work.

### Follow-up
- [x] The `datt-workstation-temp` SQL firewall rule from the 1.2.30 release is gone. Verified
      2026-08-30 against `sati-demo-satilogica-central`: the allow-list holds only the three
      `sati-demo-api-outbound-*` App Service addresses. The box had simply never been ticked;
      nothing in 1.2.31 needed or used the rule.
- [ ] `SatiProduction` has not received `AddBillingExchangeHistory` or `AddRemittanceDeposits`. The
      desktop applies them on its next direct connection; given that database's own history drift,
      watch that first launch.


## Permissions per user, not a user type — 2026-08-30

Completed 2026-08-30. Agency authorization is now a persisted per-user permission set rather than
the legacy `Role` label. All fourteen billing routes require billing permission, so billing access
no longer grants user management, test-data deletion, audit export, operations, or schema reports.

Replace the single role with a per-user permission set: billing, case manager, supervisor,
admin. Someone with the billing permission sees and uses the billing dashboard without
being an Admin.

- [x] One owner in `Sati.Contracts.V1`, beside `BillingComplianceGate` and `BillingRules`,
      so the desktop and the API cannot answer "can this person bill?" differently. A
      `[Flags]` set with `HasBillingPermission`-style predicates rather than four loose
      booleans: call sites stay readable and the set stays extensible.
- [x] Resolve permissions in `ValidatedActorFilter`, which already re-confirms identity,
      role, and agency against the database per request. Not from a token claim — revoking
      billing access should take effect immediately rather than at the next 30-minute token
      expiry.
- [x] Billing domain services take the actor as an explicit parameter rather than reading ambient
      login state. **The value stays server-derived.** A signature that accepts an actor is
      good design; a route that reads a user id from the request body is a tenant-isolation
      hole, and the rule that caller-supplied `userId`/`agencyId` is never trusted does not
      relax here.
- [x] `PlatformOperator` stays orthogonal. It is a separate cross-tenant identity for
      incident telemetry, not a bundle of agency permissions.
- [x] Deny by default. `!= "Admin" → Forbid` becomes `!HasBilling → Forbid`, never "no
      permission matched, so allow".
- [x] Migration backfills existing roles to permission sets.
- [x] **Land it in one change.** The permission gates, route inventory in
      `API_AUTHORIZATION.md`, and a test per route. A half-migrated model where some routes
      check roles and others check permissions is worse than either end state, because the
      gap is invisible until somebody finds it.
- [x] UI visibility follows the permission, but the API enforces independently. Showing the
      dashboard is not what grants access. **Was not true on local Production**, where no API sits
      behind `UserService` — closed 2026-08-31, finding 5 below.

### Line-by-line review — 2026-08-31

The conversion shipped with an explicit caveat: it had not verified that each gate got the right
permission, that no route lost a tenant check, or that the new tests fail against ungated code.
That pass is done and recorded in `API_SECURITY_AUDIT.md` (third pass plus resolution). Tenant
scoping survived intact and the route inventory matches the code exactly. Three findings blocked
release and are fixed; what remains:

- [x] **Denial tests for the supervision gates — done 2026-08-31.** `SupervisionGateTests` covers
      all nine plus the one in `TenantAccess.CanAccessUserAsync`. The actor is a demoted
      supervisor: case management only, still named as user 19's supervisor, which is what a real
      database holds the moment supervision is revoked while supervisees still point at the row.
      That shape is what makes the tests load-bearing — every query beneath these gates is scoped
      by `SupervisorId`, so an ordinary case manager sees an empty list either way and proves
      nothing. Verified: 11 of the 13 fail with every supervisor gate disabled. The two that
      survive are the positive controls, which is correct.
- [x] **Denial tests for the case-management gates — done 2026-09-10.** A billing-only actor is
      temporarily made the real owner of a synthetic consumer, then is denied through the
      self-caseload branch, `OwnsPersonAsync`, consumer creation, and agency Credible-match lookup.
      This exercises each centralized case-management decision without relying on an empty query.
- [x] **Denial tests for `GET /admin/incidents` and `PUT /admin/incidents/{id}/status` — done
      2026-09-10.** A real case manager creates a permitted sanitized incident, is denied both
      administration routes, and an Admin verifies the forbidden update left the group Open.
- [x] **Denial tests for `ProviderDirectoryRules.CanCreateOrEdit` — done 2026-09-10.** A
      billing-only user is denied provider create/edit and contact create/edit/delete across all
      five API gates. This closes the provider-permission coverage gap recorded in the review.
- [ ] **Finding 6: four caseload routes scope by owning `UserId` with no agency predicate.**
      `GET /people/{personId}/journal`, `PUT /people/{personId}`,
      `PUT /people/{personId}/contacts/{contactId}`, `DELETE /contacts/{contactId}`. Pre-existing,
      same class as the accepted `POST /at-requests` item. They also silently miss the
      case-management clause `OwnsPersonAsync` gained in the conversion.
- [ ] **Finding 7: the validated-permissions claim is shadowable by construction.** Safe today
      because `TokenIssuer` never emits it; prefer `HttpContext.Items` over mutating the principal.
- [ ] **No API route for self-service profile editing.** `UpdateOwnContactDetailsAsync` has to go
      through `PUT /api/v1/users/{id}`, which requires supervision or administration, so against a
      hosted database an ordinary case manager cannot change their own email or phone. Unchanged
      behaviour, newly visible now the operation has its own name.

## Controlled migration deployment — 2026-08-30

`CLAUDE.md` lists controlled migration deployment as outstanding cloud-platform foundation. Today a
schema release needs a hand-written `Apply-*.ps1` run from a workstation, which needs a temporary
exact-IP hole in the `SatiDemo` SQL firewall. That hole is the last link in a chain, not the first:
`__EFMigrationsHistory` disagrees with the real schema in both directions, so
`dotnet ef migrations script --idempotent` fails with SQL 2705, so every migration gets its own
bespoke script, so a human must run it, so the firewall must open. There are ten such scripts in
`scripts/`, each a fresh chance to get it wrong. Reconciling the history table dissolves the rest.

### Phase 0 — Build the instrument (done 2026-08-30)
- [x] `SchemaComparison` in `Sati.Contracts.V1` owns the rule for how two descriptions of a schema
      differ, shared by the readiness check, the drift report, and later the migrator verify step.
      It takes plain data, because `Sati.Contracts` carries no package references and must not
      acquire EF Core.
- [x] Report both directions. `SchemaDriftHealthCheck` was one-directional and name-only —
      model-expects-but-database-lacks, columns only — and so was blind to the drift that actually
      breaks releases: objects the database has that the chain never recorded.
- [x] `SchemaSnapshotReader` extracts a snapshot from an EF model and from a live database, provider
      aware so the SQLite-backed integration tests exercise it rather than leaving it to run for the
      first time against Azure SQL.
- [x] A partial model may report only what it needs. `ApiDbContext` maps just the tables the API
      serves, so declaring it authoritative would report every desktop-only table as drift and bury
      the real findings.
- [x] `GET /api/v1/admin/schema-drift`, Admin only, returns the report. `/health/ready` still emits
      only the status word, and its description still never reaches the anonymous response writer.
- [x] Readiness still gates on `PreventsQueries` alone, which is the same set of failures it
      reported before the rule was extracted. Widening the readiness gate is a release-blocking
      decision and belongs in its own change.
- [x] 12 comparison tests and 5 route tests. The Admin gate was confirmed to fail against ungated
      code before the test was kept.
- [ ] Store-type comparison is deliberately absent. EF reports `nvarchar(max)` where
      `INFORMATION_SCHEMA` reports `nvarchar` with length -1, and `decimal(18,2)` as three separate
      columns. Normalizing well enough to avoid false positives is real work, and a drift report
      that cries wolf is worse than one with a documented gap.
- [ ] **Defaults, indexes, and foreign keys are not compared either, and that gap has already cost
      something.** The 2026-08-30 report came back with only three nullability findings, while
      `SatiDemo` was missing a DEFAULT constraint the chain declares — found later by the
      reconciliation's own proofs. A clean report means "no table or column is missing or
      differently nullable", not "the schema matches the chain", and it should not be read as the
      latter. Extending the comparison to constraints is the obvious next increment; until then the
      proofs in `Apply-DemoHistoryReconciliation.ps1` are the more thorough instrument.

### Phase 1 — Reconcile once, per environment
The `SatiDemo` report was taken 2026-08-30 from `GET /api/v1/admin/schema-drift` against deployment
`5ff1a9d9a9c44f8088863badb6761c1a` (contract revision `7C6F00E77F6E`), with no firewall rule opened.
Result: **0 blocking differences, 3 nullability findings, and 4 migration-history discrepancies.**
Every one is the history being wrong rather than the database being wrong, which makes the
reconciliation history-row surgery rather than corrective DDL — the best case the plan allowed for.

That sentence was retracted on 2026-08-30 and then reinstated the same evening. The retraction
claimed `Users.AgencyId` was missing the constant default `AddAgencyId` declares, and therefore that
the reconciliation needed corrective DDL. **That was wrong. The original sentence stands: the
findings were history being wrong, not the database.**

The default constraint `DF__Users__AgencyId__57DD0BE4`, definition `((1))`, has existed since
2026-08-11. The proof reported it absent because the App Service managed identity held only
`db_datareader` and `db_datawriter`, and neither carries `VIEW DEFINITION`. Without that permission
a principal sees the table and its columns but not its constraint rows in `sys.default_constraints`.
Granting `ALTER` on `dbo.Users` implies `VIEW DEFINITION`, and the same proof passed immediately
afterwards against an unchanged database.

The lesson is a real one and worth more than the wasted evening: **a proof that reads catalog views
cannot distinguish "the object is absent" from "the object is invisible to me", and this one
reported the second as the first.** Anything asserting on schema through `sys.*` needs either a
principal with `VIEW DEFINITION` or an explicit check that it can see what it is about to judge.
`SchemaComparison` still does not compare default constraints, indexes, foreign keys, or store
types, so a clean Phase 0 report still means "no table or column is missing or differently nullable"
rather than "the schema matches the chain" — but nothing was found hiding behind that gap.

Two ids were applied under a timestamp that was later regenerated, so the objects exist while the
history row points at an id no longer in the chain. This is the documented SQL 2705 cause: EF
believes the surviving id never ran and an idempotent script tries to recreate columns that are
already there.

| Applied to SatiDemo | Superseded by, in the chain |
|---|---|
| `20260416005941_AddingAgencyId` | `20260416011235_AddAgencyId` |
| `20260825155740_AddConsumerEmail` | `20260825163103_AddConsumerEmail` |

Two have no history row on Demo, and their objects are already present:

- `20260812090000_TenantScopeSettingsAndProviders` adds `AgencyId` to `Settings` and `Providers`.
  `ApiDbContext` maps both as non-nullable `int` and the report shows nothing blocking, which proves
  the columns exist.
- `20260816120000_AddNoteMinutesAndStartTime` is already written guarded, and says so in its own
  comment: a bare `AddColumn` would fail with SQL 2705 on databases that predate it.

**Correction to the original reading of the first of those.** It was recorded here as "in the chain
but not applied". It was not in the chain at all: the source file carried neither `[DbContext]` nor
`[Migration]`, so EF never enumerated it regardless of a correct-looking filename. The original
classification came from listing filenames rather than asking EF, and a filename is not membership.
Both attributes were restored during the 2026-08-30 persistence move, and
`PersistenceAssemblyBoundaryTests` now pins the discoverable count at 80 so the gap cannot silently
reopen. The reconciliation still needs its history row; the reason it was missing is different from
what was first written down.

- [x] Write the `SatiDemo` reconciliation: insert history rows for
      `20260416011235_AddAgencyId`, `20260825163103_AddConsumerEmail`,
      `20260812090000_TenantScopeSettingsAndProviders`, and
      `20260816120000_AddNoteMinutesAndStartTime`, and remove the two superseded rows only after
      confirming each surviving id's objects match the expected semantics rather than merely the
      expected name. Keep the discipline `Apply-ProviderDirectoryMigrations.ps1` already has: fail
      closed on `DB_NAME()` and `SatiDatabaseIdentity`, guard every statement on the actual schema,
      stay rerunnable. Drafted as `scripts/Apply-DemoHistoryReconciliation.ps1`; its PowerShell
      parser is clean, but it has deliberately not been run against any database.
- [x] Ran against live `SatiDemo` 2026-08-30 through the WebJob, at Josh's direction and without the
      restored-copy rehearsal. The dry run refused on the first failed proof; a second run in
      `-ProofsOnly` enumerated the full set. **Exactly one proof fails.**
- [x] ~~`Users.AgencyId` has no constant default of 1.~~ **Retracted. The constraint was never
      missing.** `DF__Users__AgencyId__57DD0BE4`, definition `((1))`, created 2026-08-11. The proof
      reported it absent because the managed identity held only `db_datareader`/`db_datawriter` and
      neither carries `VIEW DEFINITION`, without which constraint rows do not appear in
      `sys.default_constraints`. `GRANT ALTER ON OBJECT::dbo.Users` implies `VIEW DEFINITION`, and
      the proof passed immediately afterwards against an unchanged database. Verified by reading the
      constraint's `create_date` directly and by the history row count never moving from 80.

      Cost of the misdiagnosis: `Add-DemoUsersAgencyIdDefault.ps1`, the
      `demo-users-agencyid-default` WebJob, and the `ALTER` grant all exist to fix a problem that did
      not exist. See the open item below on whether to keep them.
- [x] **Reconciliation applied 2026-08-30.** Four semantic proofs verified, 2 surviving history rows
      written, 2 superseded rows removed, committed. A second run wrote 0 and removed 0, proving
      idempotency. Direct query confirms 80 rows with all four surviving ids present and both
      superseded ids gone. `/health/ready` Healthy afterwards. Run entirely through the WebJob: **no
      firewall rule was opened for any part of it.**
- [x] Rehearsal against a restored copy was skipped at Josh's explicit direction, against the
      script's own recommendation. Recorded rather than quietly omitted. Nothing was lost, but the
      run that misdiagnosed the constraint is a fair argument for rehearsing next time.
- [x] **Constraint job removed 2026-08-30.** `Add-DemoUsersAgencyIdDefault.ps1`, the
      `demo-users-agencyid-default` WebJob, and its packaging are gone; the API package now carries
      only `demo-history-reconciliation`. A job able to perform DDL against a live database, kept
      against a possibility, is a standing surface for no benefit. If a genuine constraint
      divergence ever appears, write the corrective script then.
- [x] **Phase 5 shipped 2026-08-30 (A and B).** `MigrationEffectAnalyzer` in `Sati.Persistence`
      classifies each pending migration against the live schema before anything is written.
      `LocalDatabaseUpdater` records the provable case, still refuses the ambiguous one, and now
      names the migration instead of surfacing SQL 2705. Verified end to end against a real SQL
      Server database, not only with fakes. Reasoning and what was rejected in `DECISIONS.md`.
- [ ] **The migration chain does not replay on an empty database.** A migration reads
      `dbo.SatiDatabaseIdentity`, which is created outside the chain, so `MigrateAsync` against a
      fresh database fails with `Invalid object name`. Real installs create that table first, so
      nothing is broken today, but the chain alone cannot reconstruct a database and the
      rehearsal harness had to work around it. The unmerged `second-machine-setup` branch carries
      a commit named for exactly this; worth reviewing rather than solving twice.
- [ ] **Narrow the grant to `VIEW DEFINITION`.** The proofs read `sys.default_constraints`, which
      `db_datareader`/`db_datawriter` cannot see; that is the whole reason `ALTER` appeared to be
      needed. `ALTER` additionally lets the identity change the table, and with the constraint job
      gone nothing requires it. Commands and ordering in `OPERATIONS.md` — grant `VIEW DEFINITION`
      before revoking `ALTER`, or the proofs fail again for the same invisible reason. A person makes
      the grant; no workflow, script, or agent does.
- [x] Tighten the API model rather than the database for the three nullability findings. The
      database is the stricter side in all three, so the model was the loose one and the fix does not
      touch schema. `ServerPerson.FirstName` and `ServerPerson.LastName` were declared `string?` and
      `ServerClaimLine.Units` was `decimal?`, while `SatiDemo` has all three `NOT NULL`. Before they
      agreed, the API could attempt a null write and take a constraint violation at run time. This is
      drift between the two hand-maintained models over one database, which is the third axis
      Phase 0 was extended to expose, and it found instances on its first real run.
- [ ] Run the same report against `SatiProduction`. It needs the desktop-side reader from Phase 5;
      the API route covers Demo only.
- Exit: the idempotent script runs clean twice against a restored copy of each database and the
  Phase 0 report is empty in both directions. This is the last release that opens the SQL firewall.

### Phase 1.5 — Free the migration chain from the desktop project
Discovered while building Phase 0, and a hard prerequisite for Phase 2. All 80 migrations belong to
`SatiContext` in `Sati.csproj`, which is `net10.0-windows` with `UseWPF`. A migrator that references
it inherits WPF and can only ever run on Windows, which forecloses the Linux-container option in
Phase 3 before it is chosen. `ApiDbContext` is a second, hand-maintained model over the same tables
with no chain of its own, so it cannot substitute.
- [x] Move the entities and `SatiContext` behind a platform-neutral `net10.0` project that the
      desktop, the API, and a migrator can all reference.
- [x] Keep the desktop `LocalDatabaseUpdate` path working unchanged; it migrates the live
      `SatiProduction` at startup and is the highest-regression-risk part of this move.
- [x] Make all 80 migration ids discoverable from `Sati.Persistence`. The hand-authored
      `20260812090000_TenantScopeSettingsAndProviders` source lacked its migration/context
      attributes and was therefore invisible to EF despite being described as part of the chain;
      only that metadata was added.
- [ ] Rehearse the unchanged desktop migration path against a restored `SatiProduction` copy. The
      assembly boundary, EF discovery, full build, and sequence tests are local evidence; they do
      not substitute for the restored-copy exit check.
- Exit: `dotnet ef migrations list` resolves against a project with no WPF reference, and the
  desktop still migrates a restored `SatiProduction` copy cleanly.

### Phase 2 — Sati.Migrator
- [ ] Console project with three modes: `plan` (default; prints pending migration ids and the DDL,
      changes nothing), `apply` (requires a matching environment marker and an explicit
      `--authorized-by`, fails closed otherwise), and `verify` (re-runs the Phase 0 comparison,
      non-zero exit on any drift).
- [ ] Write an `AuditEvent` on apply recording migration ids, authorizer, source commit, and the
      resulting schema fingerprint. This is the integrity evidence `REGULATORY_CONCERNS.md` wants.
- Exit: plan/apply/verify reproduces the Phase 1 end state on a restored copy from both an empty and
  a current database, and a second apply is a no-op.

### Phase 3 — Run it where access already exists
**This must be in place before `SatiProduction` moves to the cloud, and it is a release gate rather
than a preference.** While Demo holds only synthetic data the workstation hole is a proportionate
risk and the cheaper option below is defensible. The moment a cloud database holds real consumer
records, an identity that can alter schema is a different category of exposure and the enforced
boundary stops being optional. A cloud Production deployment must not ship ahead of this phase.

**Decided 2026-08-30: triggered WebJob now, Container Apps Job before cloud Production.** The
original recommendation deferred this phase entirely on cost grounds, which optimised for
proportionate security spend rather than for removing the recurring firewall step — and removing
that step is the actual goal. Because Phase 1.5 made the runner host-agnostic, choosing the cheap
host now locks in nothing; hosting is a thin, swappable layer. Reasoning in `DECISIONS.md`.

- [x] `Sati.Api/WebJobs/demo-history-reconciliation/run.ps1`, packaged by `Sati.Api.csproj` to
      `App_Data/jobs/triggered/demo-history-reconciliation/` alongside the reconciliation script.
      Verified present in the publish output.
- [x] `-UseManagedIdentity` on `Apply-DemoHistoryReconciliation.ps1` acquires the SQL token from the
      App Service identity endpoint, and throws when that endpoint is absent rather than falling
      back to integrated security — off-host that would silently connect as the signed-in developer.
      Verified: it throws from a workstation without attempting a connection.
- [x] Fail-safe default. Anything other than the exact app setting `SATI_RECONCILIATION_MODE=apply`
      is a rollback-only dry run. Manual trigger only; no `settings.job` schedule.
- [x] Deployed 2026-08-30 from `89db2d8` (deployment `97da77ed76c140eaa7974fd1b42efc6e`, API ZIP
      SHA-256 `81AB984D0BA35D52E164CAF20ECE2B9394BC58F454BEC800969A1FE40181E03E`). Health live and
      Healthy, contract revision unchanged at `7C6F00E77F6E`. The job registers as
      `demo-history-reconciliation` with `runCommand: run.ps1`. No firewall rule was opened.
- [ ] ~~Grant the App Service managed identity DDL rights.~~ **Corrected: this job most likely needs
      no new grant.** The reconciliation issues only `INSERT`/`DELETE` on
      `dbo.__EFMigrationsHistory` plus catalog reads — no `CREATE`, `ALTER`, or `DROP` — which is
      `db_datawriter`, already required to serve the API. `db_ddladmin` is owed when `Sati.Migrator`
      applies real schema migrations, not before. Establish the answer by running the dry run, which
      fails closed on a missing permission, rather than by granting speculatively. Any such grant
      stays a security setting a person makes; no workflow, script, or agent performs it.
- [ ] **Rehearse against a restored copy before the first live run.** The script says so in its own
      notes and Phase 1 repeats it. `-WhatIfOnly` rolls back but still connects to the live database
      and takes serializable locks, so the dry run is not free. Either rehearse on a copy, or record
      the decision to accept that risk against synthetic Demo data.
- [x] **The mechanism is proven.** On 2026-08-30 the job ran inside App Service, the managed
      identity authenticated to `SatiDemo` and executed the full proof phase, dry-run mode was
      selected from the absent app setting, and it failed closed with exit code 1 on the one failing
      proof. A second run in `proofs` mode completed and reported. The allow-list held exactly its
      three `sati-demo-api-outbound-*` rules before, during, and after: **no temporary firewall rule
      was opened at any point.** That is the phase's whole proposition, demonstrated end to end.
- [x] The permission question is partly answered. The identity connected and read schema with no new
      grant, so `db_ddladmin` was not needed to get this far. Neither run reached the write phase, so
      `INSERT`/`DELETE` on `__EFMigrationsHistory` remains unproven.
- [x] Real run and idempotency run completed 2026-08-30 through the job, no firewall rule opened.
      Details in the Phase 1 entries above. The permission question is fully answered: the identity
      needed no grant beyond the `db_datawriter` it already held to write history, and the `ALTER`
      grant that was made turned out to matter only for catalog visibility.
- [ ] Once that lands, rewrite `RELEASE_PLAYBOOK.md` section 6 so the migration step stops implying
      a workstation connection, and stop reporting the workstation's public address in preflight.

- [ ] Superseded, kept for the reasoning: the open decision as originally framed.
      - Triggered WebJob: no new infrastructure and already inside the SQL allow-list, but it runs
        under the App Service managed identity. Identity is scoped to the resource, not the process,
        so anything in that site can request a token for any identity assigned to it, which means
        the internet-facing API would effectively hold DDL rights on `SatiDemo`. That is a standing
        privilege escalation on the most exposed component, in exchange for removing a temporary,
        human-supervised hole. Acceptable only while the data is synthetic.
      - Container Apps Job: its own resource and therefore its own identity, genuinely out of reach
        of the API. Recommended, and required once real records are involved.
- [ ] `sati-demo-api-satilogica` runs on `asp-sati-demo-central-f1` — **F1 Free tier, Windows,
      alwaysOn false**. Free tier has no VNet integration, so a private endpoint to SQL is not
      available on the App Service path, and the F1 60-minute daily CPU quota plus site sleep make a
      WebJob fragile for anything long-running. A tier change is part of the real cost of the WebJob
      option.
- [ ] Correct the earlier exit criterion recorded here: a Container Apps Job has its own egress and
      would need a **fourth** permanent allow-list entry, or a VNet with a NAT gateway, or a private
      endpoint. The gain is replacing a recurring, human-opened, workstation-scoped hole with one
      standing rule for a non-interactive service — the same shape as the three App Service rules
      already trusted. It is not the elimination of firewall rules.
- Exit: a schema release completes end to end with no temporary rule added or removed, and the
  allow-list holds only service-scoped entries.

### Phase 4 — Fold into DATT
- [ ] `RELEASE_PLAYBOOK.md` section 6 becomes plan, show the DDL, obtain explicit authorization,
      apply, then verify against `/health/ready`. The human authorization gate does not move; only
      the network hole disappears.
- Exit: the playbook contains no firewall instruction, and `AGENTS.md` item 5 carve-out is no longer
  on the normal path.

### Phase 5 — SatiProduction
`SatiProduction` is local, not Azure: `LocalDatabaseUpdate` calls
`SqlLocalDatabaseMaintenance.MigrateAsync`, which calls `Database.MigrateAsync()` at desktop
startup. No firewall is involved, but the same history drift is, applied automatically to the live
working tool with no plan step and no gate.
- [ ] Share `SchemaSnapshotReader` rather than writing a second one. It lives in `Sati.Api` today
      because nothing else could hold it; `Sati.Contracts` cannot take EF Core. The Phase 1.5
      platform-neutral project is the natural home. A second hand-written reader is a defect.
- [ ] Run the Phase 0 comparison before `MigrateAsync` and refuse with a legible message naming the
      offending object when the pending chain would collide with drift, rather than failing partway
      through a multi-migration chain.
- [ ] Reconcile it in Phase 1 alongside Demo.
- Exit: a schema-adding release either applies cleanly at next launch or refuses with a reason.
  Never a half-applied database.

## Release 1.2.30 — 2026-08-28

Medical provider directory and consumer provider lists. Includes the Admin test-data deletion
work tracked as "Unreleased" below, which shipped in this release.

### Provider directory hierarchy
- [x] `Provider.MedicalKind` (`Individual | Practice | Network`) and a single
      `ParentProviderId` self-reference. Not two typed columns: a hospitalist belongs to a
      network with no practice between, and a second column could disagree with the first.
- [x] `ProviderAffiliation` in `Sati.Contracts.V1` owns the tier rule, loop rejection, depth
      bound, ancestor walk, picker filter, and the delete refusal, so the desktop and the API
      cannot answer differently.
- [x] Deleting an entry with entries beneath it, or on any consumer record, is refused with a
      count and never consumer names.

### Consumer provider list
- [x] `PersonProvider` stores the link and the relationship's own fields, and no copy of the
      practice or network — those derive from the directory on every read.
- [x] `EndDate` alone says whether a link is current; ending keeps the row. No cap on the list.
- [x] At most one current primary care provider and one current link per provider, enforced by
      `ConsumerProviderRules` and filtered unique indexes.

### Superseding the pre-directory fields
- [x] `LegacyProviderLinking` matches the free-text provider fields to directory entries —
      exact only, ambiguity refused rather than resolved — and proposes; a case manager
      confirms. No bulk backfill runs over live consumer records.

### Documents
- [x] `AssessmentNeed` freezes the resolved provider, practice, and network at the moment of
      choosing. The one place the chain is copied rather than derived, so an approved
      assessment keeps saying what it said.

### Shared agency directory curation
- [x] Any caseload role may add and correct directory entries; only an Admin may remove or
      merge them, enforced in both paths.
- [x] A same-name entry warns without blocking.
- [x] `ProviderContact` holds several named people per entry, alongside the organization's
      general directory line.
- [x] An Admin can merge two entries; documents that named the merged entry are left alone.

### Admin test-data deletion
- [x] Shipped as described in the superseded section above, including the
      `AddTestConsumerMarker` migration and its Demo-only backfill.

### Validation
- [x] Full solution builds in Release configuration.
- [x] 828 desktop/domain tests pass, 1 documented opt-in local-AI test skipped.
- [x] 243 API integration tests pass.
- [x] 4 Carika tests pass.

### Deployment and artifact evidence
- [x] Source release commit `afea910` pushed to `origin/master` without rewriting history.
- [x] Applied the SatiDemo schema with `scripts/Apply-ProviderDirectoryMigrations.ps1`: 2 tables,
      3 columns, 7 indexes, 1 foreign key, 4 `__EFMigrationsHistory` rows, and the Demo-only
      backfill marked 177 consumers as test data. A rollback-only dry run preceded it and a
      second run changed nothing, proving idempotency. Existence-guarded rather than EF's
      generated script, because SatiDemo's history and schema disagree in both directions.
- [x] Published `Sati.Api-1.2.30.zip` (9,215,613 bytes; SHA-256
      `40741170E749191182A1054EE01C36215BCAEF1924A6A0E2E41F0732CA936FAD`) from the pushed commit
      to the existing Demo API only, with OneDeploy deployment
      `03cc6a69372247f1bbb061e1e29ca8d6`. The package contains no private desktop settings or
      credential markers.
- [x] Liveness healthy, readiness healthy — `SchemaDriftHealthCheck` therefore confirms the
      database satisfies the deployed model. `/health/version` reports product `Sati.Api` and
      release 1.2.30, and deployed contract revision `58E5DFFE4966` exactly matches the client.
- [x] Generated and accepted `SatiDemoSetup-1.2.30.exe` (100,356,096 bytes; SHA-256
      `552B25007716707BF86EB3758E5BB5BBBF1925D8C1C0C043A892CA907FCB72A7`): five responsive
      launches, graceful closes, zero exit codes, version 1.2.30.0, isolated cleanup passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.30.exe` (202,419,978 bytes; SHA-256
      `B9EBDBAE3ABFA8DEF66C0AEF13255E458BFD3BB40420E9C13EA78E296CE58FE2`): version 1.2.30.0,
      integrated security confirmed, isolated cleanup passed. The embedded `SqlLocalDB.msi`
      carries a valid Microsoft Corporation Authenticode signature.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `…\SatiLogica - Documents\Sati Desktop` (Local) and
      `…\SatiLogica - Documents\SatiLogica Demo Files` (Demo). No existing file was replaced.
- [x] The authenticated Demo Admin extension was skipped: no synthetic Demo credentials are
      configured in this Windows session. All required public release checks passed.

### Follow-up
- [x] Removed the temporary `datt-workstation-temp` firewall rule. Verified absent 2026-08-30 on
      the SQL server `sati-demo-satilogica-central` (the rule lives on the SQL logical server, not
      on the `sati-demo-api-satilogica` App Service named in the original note).
- [ ] `SatiProduction` has not received these four migrations. The desktop applies them on its
      next direct connection; given that database's own history drift, watch that first launch.

## Admin test-data deletion — shipped in 1.2.30

- [x] Add a clearly labeled Admin-only “Delete test consumer” action to the agency Person directory.
- [x] Require an explicit destructive confirmation with the requested test-only affirmation and
      guidance for duplicate or inactive consumers; Cancel and a missing view handler fail closed.
- [x] Enforce Admin role, agency ownership, exact versioned attestation, and optimistic concurrency
      in both local and Demo/API service paths rather than relying on button visibility.
- [x] Let an Admin mark a consumer as synthetic test data only while creating the record, display a
      clear `TEST` badge in the Admin directory, and make that marker immutable after creation.
- [x] Require the durable test-data marker as well as the final deletion attestation. Existing
      Production/local rows remain unmarked; the migration backfills existing rows only when the
      validated database identity is exactly `SatiDemo` / `Demo`.
- [x] Delete the complete consumer-owned test graph in one serializable transaction, retain the
      append-only audit ledger, and add a PHI-minimized `test-data.consumer-deleted` event.
- [x] Block deletion when any note has a billing claim line; do not delete financial, EDI, or audit
      records through this command.
- [x] Add focused local, API, ViewModel, confirmation, rollback, tenant-isolation, concurrency,
      billing-protection, audit, and accessible-interface tests.
- [x] Re-run the complete solution validation after the test-data marker and provider-directory
      curation work: the solution builds; 828 desktop/domain tests pass with one documented opt-in
      local-AI test skipped; all 243 API integration tests and all 4 shared-solution Carika tests
      pass.

## Release 1.2.29 — 2026-08-28

- [x] Allow an editable saved note to be reassigned with the existing Client selector without
      creating a duplicate note.
- [x] Ask the case manager, “Are you sure you want to reassign this note from [name] to [name]?”,
      default the popup to No, and restore the original selection when the move is declined.
- [x] Enforce current-note and target-client ownership in Local and Demo/API, retain workflow and
      optimistic-concurrency protection, and record a PHI-minimized `note.reassigned` audit event
      in the same save transaction.
- [x] Make both scratchpad tabs use the active theme's primary text and caret colors, including dark
      themes, with a rendered Harbor Night regression check.
- [x] Add focused ViewModel, local persistence, API integration, tenant-isolation, audit,
      concurrency, and WPF theme coverage.
- [x] Advance the desktop, API, installer builders, readiness checks, examples, and Settings release
      tracker to 1.2.29 together. No database migration is required for this release.
- [x] Run the complete Release build and every test project: 631 desktop/domain tests passed with
      one documented opt-in local-AI test skipped, all 199 API integration tests and all 4
      authorized shared-solution Carika tests passed, and all 80 focused note-reassignment and
      scratchpad checks passed. All 74 migrations replayed from empty with zero problems, and the
      resulting disposable schema matched all 362 model columns with no drift.
- [x] Commit and push the verified 1.2.29 source release as
      `e329af12dada557a56203aa56411cdf02c375948` on `master` without rewriting history.
- [x] Publish `Sati.Api-1.2.29.zip` (9,302,353 bytes; SHA-256
      `A7C23E88F0079F3F03F896B2811F0328FBC6FA872016A3D844485365FDC270D6`) from the pushed source
      commit to the existing Demo API only with OneDeploy deployment
      `f1303da3894c41f5b5a657f4e007a2ab`. Liveness and readiness are healthy,
      `/health/version` reports product `Sati.Api` and release 1.2.29, and deployed contract
      revision `EE21C645AB81` exactly matches the client. The package contains no private desktop
      settings or credential markers. The optional authenticated Admin extension was skipped
      because no synthetic Demo credentials were configured in this Windows session; all required
      public release checks passed. Release 1.2.28 deployment
      `d06af5344f8543d497727f02338474aa` and its API ZIP remain available as prior known-healthy
      evidence.
- [x] Generate and accept `SatiDemoSetup-1.2.29.exe` (100,311,040 bytes; SHA-256
      `F8A76038BA687DC88F82B4D172E149AF97E370D43FA42D7B8E77477E87A111BC`). It passed five
      responsive sign-in launches, normal closes, exact version 1.2.29.0, public-only
      configuration, and isolated cleanup. The installer and checksum were independently
      hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
- [x] Generate and accept `SatiLocalSetup-1.2.29.exe` (202,633,482 bytes; SHA-256
      `8585A1E32F6E57B8067F32B5D09FB282958D9CF40D79D9A8FA976FB564A183DA`) from Microsoft-signed
      `SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`). It passed exact
      version 1.2.29.0, Windows integrated-security, credential-rejection, and isolated cleanup
      checks. The installer and checksum were independently hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`.
- [x] Merge or delete no branches. Retain linked worktree `claude/cool-jang-f6b3c4` at `c439fa8`
      because it contains an untracked instruction file; retain `second-machine-setup` at
      `f31fdf0` and `origin/claude/local-vs-github-workflow-dlcqpb` at `62b2f83` because they have
      unique setup work unrelated to this release.
- [x] Commit and push final release evidence as
      `b0f204eb3408ffd50d7765d79a2e1fe87975552b`; after this release-index update, confirm a clean
      `master` exactly matching `origin/master`.

## Release 1.2.28 — 2026-08-27

- [x] Correct Add Person email handling so a blank value is genuinely optional while a supplied
      malformed address still receives a specific validation message.
- [x] Expose the email field in the active inline client editor and label it optional.
- [x] Mark first name, last name, date of birth, and biography with accessible required-field
      asterisks; mark the representative-payee fields only where their conditional requirement is
      active.
- [x] Add a compact, non-color-only completion guide that changes required items from orange to
      green as meaningful values are entered and explicitly states that other details are optional.
- [x] Add desktop editor, notification, and shared validation regression coverage for blank,
      whitespace, malformed, and completed-field cases.
- [x] Run the complete Release build; 623 desktop/domain tests passed with one documented opt-in
      local-AI test skipped, all 196 API integration tests and all 4 authorized shared-solution
      Carika tests passed, all 54 focused Add Person checks passed, and 74 migrations replayed with
      zero problems.
- [x] Advance the desktop, API, installer builders, readiness checks, examples, and Settings release
      tracker to 1.2.28 together.
- [x] Commit and push the verified 1.2.28 source release as
      `90d47d764392d20992e306e2eef9ee4d033d40f2` on `master` without rewriting history.
- [x] Publish `Sati.Api-1.2.28.zip` (9,300,111 bytes; SHA-256
      `83AC37E1B38286A0FAF7261900464DF84B54F330152054442CFAAD57FF04EEB9`) from the pushed source
      commit to the existing Demo API only with OneDeploy deployment
      `d06af5344f8543d497727f02338474aa`. Liveness and readiness are healthy,
      `/health/version` reports product `Sati.Api` and release 1.2.28, and deployed contract
      revision `EE21C645AB81` exactly matches the client. The package contains no private settings
      or credential markers; release 1.2.27 deployment `071e2f7b46ff4ac4903d6073747b74e3`
      and its API ZIP remain available as prior known-healthy evidence.
- [x] Generate and accept `SatiDemoSetup-1.2.28.exe` (100,302,848 bytes; SHA-256
      `213298C98CE014B30F5AF0D284A3E0B9A766250EAA1025C765E2D03DD514B5D9`). It passed five
      responsive sign-in launches, normal closes, exact version 1.2.28.0, public-only
      configuration, and isolated cleanup. The installer and checksum were independently
      hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
- [x] Generate and accept `SatiLocalSetup-1.2.28.exe` (202,357,002 bytes; SHA-256
      `C6BCE962ED4ED0CB7D9CE32F2D9B697CBCCA54023B740CD140EBDCC27AACF072`) from
      Microsoft-signed `SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`). It passed exact
      version 1.2.28.0, Windows integrated-security, credential-rejection, and isolated cleanup
      checks. The installer and checksum were independently hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`.
- [x] Merge or delete no branches. Retain linked worktree `claude/cool-jang-f6b3c4` at `c439fa8`
      because it contains an untracked instruction file; retain `second-machine-setup` at
      `f31fdf0` and `origin/claude/local-vs-github-workflow-dlcqpb` at `62b2f83` because they have
      unique setup work unrelated to this release.
- [x] Commit and push final release evidence as
      `5f6a7acaebf6e58ff557ed078584a476f4cc853e`; after this release-index update, confirm a clean
      `master` exactly matching `origin/master`.

## Release 1.2.27 — 2026-08-27

- [x] Correct billing compliance so only enabled, incomplete documents whose due date has passed
      can block; use completion dates rather than the mutable compliance flag, and apply one shared
      rule to current queues, historical service dates, billing, and loss reports in desktop and API.
- [x] Add Admin-only agency settings for 90-day reviews, PCP, Comprehensive Assessment,
      Reclassification, Safety Plan, Privacy Practices, and Agency/DHHS/Medical releases. Preserve
      the former intended set as the migration default and validate unsupported setting bits.
- [x] Add regression and matrix coverage for future, due-today, overdue, completed, prior-cycle,
      disabled, unknown, and historical-window cases, including API queue/approval and Settings
      authorization, tenancy, validation, concurrency, and audit behavior.
- [x] Move AT Requests from the Case Management section bar to the case-manager dashboard bar;
      add Authorized Rep and Releases beside it while reusing the Clients-page document workspaces.
      Keep the existing DHHS Forms, Agency Release, and AT Requests workspaces under Clients.
- [x] Give Notes filter inputs one rendered height and baseline above the data grid.
- [x] Add Pine Coast, Blueberry Mist, and Harbor Night themes with the full required resource set.
- [x] Audit Add Person end to end. Contain command, settings-load, and post-save workspace failures;
      remove the pre-save form deletion; make the Person/forms/lifecycle/audit graph one local
      transaction; and avoid a second API read after a successful create commit.
- [x] Give client-creation failures an accessible three-part explanation of what was saved, what
      failed, and the safest next action. Preserve field-specific server validation and distinguish
      definitely-unsent requests from an uncertain network response so duplicate clients are not
      created during recovery.
- [x] Centralize Person persistence validation in `Sati.Contracts.V1.PersonSaveRules`, enforce the
      authenticated owner/agency at the local seam, and cover required fields, every SQL length,
      enum/date/form invariants, transaction rollback, tenant ownership, API mapping, and crash
      containment with desktop and API tests.
- [x] Run the complete Release validation: the solution build passed; 617 desktop tests passed with
      one opt-in local-AI test skipped; all 196 API tests and all 4 authorized shared-solution Carika
      tests passed; and 74 migrations replayed from empty with zero problems.
- [x] Apply and verify `20260827141239_AddBillingComplianceRequirements` against the
      identity-validated Local `SatiProduction` and synthetic Azure `SatiDemo` targets. Both report
      no pending code migrations, valid default requirement bits, and the new non-null column and
      history row; Demo retained all 177 synthetic People and its temporary exact-IP rule was
      removed and verified absent.
- [x] Advance the desktop, API, installer builders, release checks, examples, and Settings release
      tracker to 1.2.27 together.
- [x] Commit and push the verified 1.2.27 source release as
      `c18f0001de80fc51eaabc502cacc2322026c3a59` on `master` without rewriting history.
- [x] Publish `Sati.Api-1.2.27.zip` (9,300,104 bytes; SHA-256
      `54AA740C74FED38E51D4EEBC09C9C9A13A33BBCBFAC08763AF66809D34E5A8B1`) to the existing Demo API
      only with OneDeploy deployment `071e2f7b46ff4ac4903d6073747b74e3`. Liveness and readiness are
      healthy, `/health/version` reports product `Sati.Api` and release 1.2.27, and deployed contract
      revision `EE21C645AB81` exactly matches the client.
- [x] Generate and accept `SatiDemoSetup-1.2.27.exe` (100,282,368 bytes; SHA-256
      `1B0DBEEECE5BD5EEC18E57B7AFDD5B097E2C134726E28FCD15C3538B2061BD10`). It passed five responsive
      sign-in launches, normal closes, exact version 1.2.27.0, and isolated cleanup, then the installer
      and checksum were hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
- [x] Generate and accept `SatiLocalSetup-1.2.27.exe` (202,356,490 bytes; SHA-256
      `954A837CC0A0E76D2B976E30190FDA17C7032A30A0895B9159C6C3395424A43E`) from the one valid
      Microsoft-signed `SqlLocalDB.msi`. It passed exact version 1.2.27.0, Windows integrated-security,
      and isolated cleanup checks, then the installer and checksum were hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`.
- [x] Delete fully merged, inactive feature branches `codex/repository-stabilization` at `74ea991`
      and `reminder-note-type` at `1fd3d5f` locally and remotely. Retain the linked
      `claude/cool-jang-f6b3c4` worktree plus `second-machine-setup` and
      `origin/claude/local-vs-github-workflow-dlcqpb` because they contain active, untracked, or
      unique work unrelated to this release.
- [x] Commit and push the final release evidence as
      `19455e3fece1322877e1dc0d8d8d7231eb254a0c`; after this release-index update, confirm a clean
      working tree and exact equality between `master` and `origin/master`.

## Release 1.2.26 — 2026-08-26

- [x] Restore an accessible, vertically centered calendar button inside every DatePicker.
- [x] Allow multiple Visit Setting, Appearance, Participation, and Health/Safety observations while
      keeping historical single-choice Visit JSON readable.
- [x] Add personal Win+Shift+1 through Win+Shift+0 typing shortcuts for every role, scoped to the
      note narrative and Scratchpad and kept separate by Windows profile, user, and environment.
- [x] Advance the desktop, API, installer builders, readiness gate, and Settings release tracker to
      1.2.26 together.
- [x] Publish API 1.2.26 with OneDeploy deployment `a479609851bd47f1934a9f75a4770433`.
      Liveness and readiness are healthy, `/health/version` reports 1.2.26, and deployed contract
      revision `15D50B6C6B29` matches the client. API ZIP SHA-256 is
      `AFEBAC10D7BA0496743F147FE18591E66C35B235A5C66D5A429D859A02C1CE3C`.
- [x] Generate and acceptance-test `SatiDemoSetup-1.2.26.exe` (SHA-256
      `2b62e9c8434d70ccbb822d733ce4dc3fb52b530c63385909aae341f2835e5d24`) and
      `SatiLocalSetup-1.2.26.exe` (SHA-256
      `3e09b4be02946a63356511c0c9a0477db8568bc56d25e051e62de61989757341`). Demo acceptance passed
      five responsive launches, normal closes, version 1.2.26.0, and cleanup; Local acceptance
      passed version, Microsoft-signed LocalDB, integrated-security, and cleanup checks.

## Release 1.2.25 — 2026-08-26

- [x] Add consumer email, the focused calendar-day note view, calendar failure containment, and
      future-dated non-billable reminders, with shared persistence rules and regression coverage.
- [x] Correct the Local login failure discovered after installing 1.2.24: Today's Work and
      Tomorrow's Agenda now load sequentially and publish independently, notes-log reads no longer
      fan out across every consumer, and both areas contain failures behind accessible Retry
      actions. The Settings release log describes the recovery behavior. Release verification
      passed 502 desktop and 177 API tests; hotfix source commit `b957506`.
- [x] Advance the client, API, installer builders, release notes, and release tests together to
      1.2.25 so the earlier 1.2.24 Local artifact is never replaced by different bytes.
- [x] Apply and verify migration `20260825163103_AddConsumerEmail` against identity-validated
      Azure `SatiDemo`. The email column already existed, so the guarded operation wrote only its
      missing history row, verified 177 synthetic consumers / 73 migrations, and removed the
      temporary exact-IP rule. Publish API 1.2.25 with OneDeploy deployment
      `29def577984e466db587a9b9632958aa`; live and ready are healthy, release version is 1.2.25, and
      deployed contract revision `15D50B6C6B29` exactly matches the client. API ZIP SHA-256 is
      `956FF72E1874236F7E6FB0E8D8A2A0E1C264740916359CA8A2ADB5B96B382642`.
- [x] Generate and validate the final post-hotfix `SatiDemoSetup-1.2.25.exe` (SHA-256
      `57a4edc651b1b9ade1cc5db53c61ab918303e93125e81552c861b3b586f6010e`) and
      `SatiLocalSetup-1.2.25.exe` (SHA-256
      `34ec15eced890a60c176111ef11cfdd3e4a0cfd58b8dd6e93670c604aebd7fab`). The earlier 1.2.25
      candidate hashes recorded in commit `cd09fd0` are superseded and those candidate artifacts
      must not be distributed. Demo acceptance passed five responsive launches, normal closes,
      version 1.2.25.0, and cleanup; Local acceptance passed version, Microsoft-signed LocalDB,
      integrated-security, and cleanup checks. The unchanged deployed API remains live and ready at
      release 1.2.25 with contract revision `15D50B6C6B29`.

## Calendar entry, Visit selections, and personal typing shortcuts — 2026-08-26

- [x] Restore a themed, vertically centered calendar button to the global DatePicker template,
      preserving the real WPF popup part, keyboard behavior, and an accessible name.
- [x] Replace the four Visit observation dropdowns with checkboxes; store multiple selections in
      additive note-owned JSON while retaining the legacy singular values so older notes still load.
- [x] Teach the local AI fact compiler and concern validation to consume every effective checked
      Visit selection, with regression coverage for old and new JSON.
- [x] Add personal Win+Shift+1 through Win+Shift+0 text snippets under Settings for every role,
      limited to 200 characters and inserted only in an editable note narrative or Scratchpad box.
- [x] Keep shortcut preferences client-local and separate by Windows profile, Sati user, and
      Demo/Production environment; preserve normal Windows behavior outside the marked text boxes.

## Future-dated calendar reminders — 2026-08-26, refined 2026-09-05

- [x] Store an explicitly selected future Reminder as a Scheduled Reminder through one shared
      contracts rule, repeated by the desktop-local and API persistence boundaries.
- [x] Preserve an explicit Reminder's date and narrative while removing service time, minutes,
      form/visit facts, and justification so it cannot drift into review, productivity, or billing.
- [x] Refine non-Reminder future work on 2026-09-05: preserve its selected service/form type and
      estimated minutes as a Scheduled plan, while withholding actual start time and completed facts.
- [x] Refresh the calendar after note saves and show the reminder on its dated calendar entry with
      explicit non-billable wording; keep undated Reminders on the existing journal-only path.
- [x] Cover UI conversion, rendered date access, local persistence, calendar retrieval, API
      normalization, tenant-scoped reads, and the undated-row guard with automated tests.

## Calendar stability and focused day — 2026-08-26

- [x] Contain calendar load, exemption-update, and downstream refresh failures so they produce an
      inline retryable message instead of escaping through WPF's dispatcher and crashing Sati.
- [x] Protect year navigation from stale async responses, invalid bounds, and missing sessions;
      preserve the selected date when refreshed calendar objects replace the prior year model.
- [x] Add an accessible focused-day view showing the service-date notes, client, narrative,
      service time, duration, status, and daily totals, with keyboard-operable day buttons.
- [x] Correct local monthly and yearly note boundaries to include the entire final day, and enforce
      the signed-in user's scope in the local exemption service as the API already does.
- [x] Cover the ViewModel, rendered WPF view, local data services, and API calendar boundaries with
      regression, concurrency, failure-containment, accessibility, and tenant-isolation tests.

## Release 1.2.23 — 2026-08-25

- [x] Add consumer-profile flags for case-manager DHHS representation and Modivcare,
      plus caseload filters for those responsibilities, representative payee, VR, and
      the existing waiver/employment service flags.
- [x] Add and validate the additive People migration. The guarded, exact-IP Demo
      operation verified `SatiDemo`/`Demo`, added both columns for 177 consumers,
      wrote migration `20260825144021_AddConsumerNavigationFlags`, and removed the
      temporary SQL firewall rule immediately afterward.
- [x] Deploy API 1.2.23 with OneDeploy deployment
      `14bfdc40a2ec44b9be517c1c7884cdd9`. `/health/live` and `/health/ready` returned
      HTTP 200; `/health/version` reports 1.2.23 and contract revision `9F387A68FF69`.
- [x] Package `SatiDemoSetup-1.2.23.exe` and `SatiLocalSetup-1.2.23.exe`. Record final
      installer acceptance evidence before distributing outside this workstation.

## Release 1.2.22 — 2026-08-23

- [x] Bump the client, API, and Carika to 1.2.22 and rewrite the Settings release notes around the
      single note panel, session continuity, and the journal-reminder route.
- [x] Confirm the Demo API needs no publish: its `/health/version` reports
      `contractRevision 9F387A68FF69` and `releaseVersion 1.2.21`, and the committed
      `ApiSurface.Revision` is byte-for-byte the same fingerprint over 102 routes, including
      `POST /api/v1/auth/renew`. The deployed surface already matches this source.
- [x] Package `SatiDemoSetup-1.2.22.exe`
      (sha256 `605ff0634d485cdc3610d71608b3665992c1418d3d7e04f454251c71762be484`) and pass
      installer acceptance: five launches, each responding, closing gracefully, exit code 0,
      installed version 1.2.22.0, acceptance copy removed.
- [ ] **Publish the API at 1.2.22.** Not required for function — the route surface is identical, so
      the compatibility check stays quiet — but `/health/version` will keep reporting
      `releaseVersion 1.2.21` until it is redeployed, which makes the number a poor way to tell what
      is running. Deploy from
      `Sati.Api/Properties/PublishProfiles/sati-demo-api-satilogica - Zip Deploy.pubxml`.
- [x] Package `SatiLocalSetup-1.2.22.exe`
      (sha256 `aef3463029ad16bf589f5af00e8c86a758f1c46a2a94333b916e718aac3c8242`) and pass payload
      validation: version 1.2.22.0, integrated security confirmed, acceptance copy removed. The
      embedded LocalDB prerequisite is Microsoft-signed `SqlLocalDB.msi`, sha256
      `0891BF47652D88F06D76A339A5DB37DDC9C801D1E973E14B3B551609F1CFA4CB`.
- [ ] **Keep a durable copy of `SqlLocalDB.msi`.** The 1.2.22 build sourced it from a
      `%TEMP%\SatiInstallerInspect-*` folder left by an earlier installer inspection. The signature
      check makes that safe — a modified MSI cannot carry a valid Microsoft Authenticode signature,
      and it was verified before use — but a release input that lives in a directory Windows may
      clear at any time is not a reproducible one. Archive it somewhere permanent and record the
      hash above.
- [ ] **Decide whether machine-local commentary belongs in a shipped installer.** The Local build
      packages the gitignored `appsettings.json` verbatim, including its `//production` note
      describing this laptop's database history. It carries no credential and no client data, and
      the builder already rejects SQL credentials, but it is internal commentary that reaches an
      end user's disk.

## Ended Demo session — 2026-08-23

- [x] Treat a refused `POST /api/v1/auth/renew` as terminal: latch the client shut, raise
      `CloudApiClient.SessionEnded` once, and fail every later authenticated call locally with
      `CloudSessionEndedException` instead of repeating the rejected renewal per screen.
- [x] Clear the latch when a new token is set, so signing in again reopens the same client.
- [x] Surface an ended session in the Switch User dialog as a stated expiry rather than an empty
      account list, and repair the error line, which was collapsed by a `Visibility` binding that
      outranked its own style and ran a string through `BoolToVisibilityConverter`.
- [x] Renew on the token's own schedule (`SessionKeepAlive`), waking at `expiry - RenewalMargin`
      rather than waiting for a request to land inside a five-minute window that an idle app never
      enters. A fixed poll cannot substitute: a twenty-minute interval steps over the window and
      wakes holding an expired token.
- [x] Gate renewal on real user input, so an active desktop keeps working to the server's
      twelve-hour cap while an unattended workstation lapses after one token lifetime. The idle
      allowance is the renewal gap, not the token lifetime — measuring against the lifetime can
      never close the gate on the first cycle and silently doubles the timeout.
- [x] Offer re-authentication in place: the shell prompts on `ISessionLifetime.SessionEnded`. The
      same person signing back in is not an account switch, so nothing reinitializes and unsaved
      agenda text survives to be saved; a different account takes the existing switch path.
- [ ] Translate `CloudSessionEndedException` into `SessionExpiredException` across the remaining
      cloud services. Only `CloudUserService.GetAllAsync` does so today, so other screens still
      report an ended session through their generic failure path.

## Representative-payee profile — 2026-08-22

- [x] Add `CaseManagerIsRepPayee`, monthly income, and regular check-request needs to the
      authoritative Person profile, API contracts, lifecycle history, and Local/Demo persistence.
- [x] Add accessible Yes/No Profile controls with conditional, validated amount and recurring-needs
      fields; selecting No clears financial details that no longer apply.
- [x] Add the additive People migration and test validation, tenant isolation, audit/version history,
      contract mapping, migration shape, API compatibility, and accessible XAML controls.
- [x] Apply the guarded migration to identity-validated Local `SatiProduction` and Azure `SatiDemo`,
      deploy the matching API, and package both 1.2.21 installers (2026-08-23).
- [ ] Design the later billing-department check-release notification as its own audited workflow with
      request state, amount, purpose, due date, requester, approval/release evidence, and idempotency.
      A representative-payee profile edit must never itself authorize or initiate payment.

## Database wait feedback — 2026-08-22

- [x] Add one payload-free, reference-counted activity tracker for Demo HTTP and Local Production EF
      calls, including success, failure, cancellation, and reader-disposal cleanup.
- [x] Spin the accessible watercolor Bodhi leaf immediately while data calls are active.
- [x] After eight uninterrupted seconds, show a modeless patience window that closes automatically
      when the final overlapping request completes and never appears late after a short request.
- [x] Add deterministic timing, overlap, HTTP failure, EF reader-lifecycle, and XAML accessibility
      coverage.
- [x] Add an all-role Settings preview that holds the same activity tracker for 12 seconds without
      querying a database, so the immediate leaf and eight-second patience window can be tested.
- [x] Classify Demo connectivity failures, retry only proven DNS failures where no request was
      sent, and keep ambiguous writes from being repeated after timeouts or connection loss.
- [x] Present safe, specific Scratchpad recovery guidance without logging or displaying note text,
      and remove expected spinner-timer cancellation exceptions from debugger output.

## Carika limited client — 2026-08-21

- [x] Add an Avalonia client using safe contracts and the API, without EF/SQL/LocalDB.
- [x] Add authenticated caseload profile display and API-mediated draft-note creation.
- [x] Add contract-backed note type, workflow status, and conditional form-type selections, with
      stale draft-load and transcription suppression when the selected person or narrative changes.
- [x] Refresh the limited client's visual hierarchy and accessible note-entry status messaging.
- [x] Add DPAPI-protected, actor/person-bound optional local drafts.
- [x] Add local-only Whisper transcription of an existing WAV with no cloud fallback or auto-download.
- [ ] Add ephemeral microphone capture after privacy indicators, cancellation, device selection,
      audio-lifetime behavior, and accessibility are designed and tested.
- [ ] Add session expiry/re-authentication, sign-out/zeroization, note editing/concurrency UI,
      integration tests, packaging, threat modeling, model controls, and deployment review.

A WPF MVVM case-management desktop app built with EF Core, CommunityToolkit MVVM, and SQL LocalDB.

---

## Demo hardening - 2026-08-13

- [x] Stop calendar-day selection from repeatedly raising layout error dialogs. Display-only
      `Run.Text` bindings are explicitly one-way, repeated identical UI failures are shown once
      per process, and safe XAML location metadata is included in the local technical log.
- [x] Reproduce calendar-day rendering in a real WPF window and cover the binding rule with
      automated regression tests.
- [x] Make the Demo readiness preflight distinguish an older healthy deployment from a matching
      release and report the exact deployment-parity remedy instead of a raw web exception.
- [x] Build and isolated-launch-test the version 1.2.2 installer containing the completed billing
      client, deploy the matching 1.2.2 API to Azure Demo, and verify live/ready/version health.
- [x] Apply the incident migration to Azure Demo, provision the encrypted-credential Global Admin,
      deploy matching API/client 1.2.3, verify least-privilege platform access, and isolated-launch-
      test the versioned installer.
- [x] Repair incident coverage and the supervisory billing handoff in release 1.2.5: durable retry
      outbox, platform-scoped Global Admin reporting, unclean-shutdown detection, No telemetry
      health state, reentrant window-close protection, explicit Save as Draft / Submit for
      Supervisor Review actions, and an end-to-end draft-to-test-837 integration gate. Apply the
      guarded Azure migration, deploy API 1.2.5, and isolated-launch-test its exact installer.
- [x] Correct the asynchronous save-on-close handoff and permit narrowly scoped Global Admin
      self-service password changes in release 1.2.6, with regression coverage proving agency
      user-password resets remain forbidden.
- [x] Route Global Admin account switching through neutral credential entry in release 1.2.7,
      preserving the agency-directory prohibition and containing ordinary picker load failures.
- [x] Show assigned supervisor names explicitly in both administrative and personal user profiles,
      and replace the legacy artwork with a multi-resolution professional application icon in
      release 1.2.8.
- [x] Complete the local release 1.2.9 durability slice: refine the darker Bodhi-leaf icon, finish
      the accessibility audit, make theme resources host-independent, runtime-render the feature
      views, and require repeated normal window shutdown in installer acceptance.
- [x] Regenerate and visually inspect all ten pages of the version-matched offline Demo fallback.
- [ ] Complete authenticated agency-Admin preflight, external-machine installer attestation,
      presenter rehearsal, and final evidence binding for the exact 1.2.9 installer.

---

## Phase 1 — Fix the Foundation ✅
*Goal: App starts, login works, no crashes*

- [x] Fix double `mainWindow.Show()` in `App.xaml.cs`
- [x] Implement `CaseManagerDashboardViewModel.Initialize(user)` — store logged-in user, trigger initial load
- [x] Fix `NewUserViewModel` missing null-conditional on `CloseWindowRequested` event
- [x] Make `IUserService` public
- [x] Remove hardcoded seed user from `LoginWindowViewModel`
- [x] Fix `MainPage_Activated` firing `LoadPeopleAsync()` on every focus — load once only
- [x] Add `OnModelCreating` to `SatiContext` with explicit keys and relationships

---

## Phase 2 — Remove Service Locator, Tighten DI ✅
*Goal: No more `((App)Application.Current).Services`*

- [x] Remove service locator from `LoginWindow.xaml.cs`
- [x] Use `Func<T>` factory injection for window creation throughout
- [x] Audit all remaining `GetRequiredService` calls in view code-behind

---

## Phase 3 — Complete Person/Client Management ✅
*Goal: Add, view, edit, delete clients with validation*

- [x] Add edit support to `NewClientViewModel`
- [x] Add input validation using `[NotifyDataErrorInfo]`
- [x] Fix `RemoveSelectedPerson` — was not awaiting `DeletePersonAsync`
- [x] Eager-load `Forms` and `Notes` in `PersonService.GetAllPeopleAsync`
- [x] Add compliance review dialog on client creation
- [x] EffectiveDate replaced with MM/DD TextBox with CustomValidation and waiver-gating

---

## Phase 4 — Complete Notes Workflow ✅
*Goal: Notes are fully usable — create, edit, delete, filter*

- [x] Add delete note command to `CaseManagerDashboardViewModel`
- [x] Confirm edit flow works end to end
- [x] Add status filtering (not just text search)
- [x] Unit count / duration display
- [x] Add NoteType (Visit, Contact, Other, Form) with per-type narrative templates
- [x] Add FormType nullable property on Note with migration
- [x] Form note submission triggers MarkFormCompleteRequested popup

---

## Phase 5 — Productivity, Settings, and Scheduler ✅
*Goal: Daily work tracking, configurable settings, monthly scheduler*

- [x] Settings model, migration, ISettingsService/SettingsService
- [x] SettingsWindow fully wired — billing, templates, weekday/holiday exclusion flags
- [x] Settings confirmation dialog on close summarizing changed values
- [x] Scratchpad model/service/migration with auto-save timer
- [x] Incentive model/service/migration with productivity dashboard and progress bar
- [x] Scheduler popup with workday tile grid, month navigation, DaysScheduled persistence
- [x] New month prompt — fires PromptSchedulerRequested when wasCreated is true
- [x] NoteType radio buttons with EnumToBoolConverter

---

## Phase 6 — Forms, Deadlines, and Events Dashboard ✅
*Goal: Core business logic — deadline tracking per client*

- [x] UpcomingEvent record and UpcomingEventService with full computation logic
- [x] Upcoming Tasks panel split into two columns (forms left, visits/contacts right)
- [x] Sort radio buttons wired via SortByDate computed property
- [x] Forms checklist bound to real Form entities via ToggleFormCommand
- [x] Compliance flags computed per FormType using GetCurrentCycleForm
- [x] GetCurrentCycleForm on Person model replaces FirstOrDefault throughout
- [x] EnumDescriptionConverter, BoolToVisibilityConverter, InverseBoolConverter
- [x] Description attributes on FormType enum for human-readable display
- [x] Enums moved to top-level namespace
- [x] UserId foreign key on Person, GetAllPeopleAsync filtered by userId

---

## Phase 7 — Note Polish and Client Detail ✅
*Goal: Note workflow reliability, scheduled events in upcoming panel*

- [x] IsEditing reset on client switch
- [x] Form clears on client selection
- [x] In-memory Person.Notes sync for upcoming events
- [x] NoteType persisted to database with migration
- [x] Scheduled visits and contacts appearing in Upcoming Tasks panel
- [x] ContactEvents column added

---

## Phase 8 — Polish and Portfolio Packaging ✅
*Goal: Looks good, handles errors gracefully, ships cleanly*

- [x] Global exception handling in App.xaml.cs with flat-file error log
- [x] User-facing error dialogs
- [x] ScratchpadHistoryWindow with ICollectionView search and full-content preview
- [x] Scratchpad save bug resolved via cancel/reclose pattern on Closing event
- [x] Out-of-month warning for EventDate
- [x] Font configuration — Inter globally, Cambria for narrative/scratchpad fields
- [x] Font size A/A buttons for narrative and scratchpad
- [x] Ctrl+Enter timestamp insertion in scratchpad
- [x] WPF native spell check on narrative and scratchpad fields
- [x] README.md written and pushed
- [x] Self-contained single-file executable published
- [x] SmartScreen blocking resolved

---

## Recently Resolved Bugs
*Verified before the current platform work*

- [x] **Productivity threshold ignores scheduler** — `Incentive.Threshold` hardcodes `* 19`
  instead of using `Settings.ProductivityThreshold`; panel doesn't refresh after scheduler closes
  - Fix 1: Add `UnitsPerDay` snapshot field to `Incentive` model + migration
  - Fix 2: Set `UnitsPerDay = settings.ProductivityThreshold` in `GetOrCreateAsync`
  - Fix 3: `OnIsSchedulerOpenChanged(false)` calls `RefreshIncentiveAsync()` in CaseManagerDashboardViewModel

- [x] **Refactor all services to use IDbContextFactory<SatiContext>** — current pattern holds
  a DbContext open for the entire session, causing change tracker collisions and memory bloat.
  Replace constructor-injected SatiContext with IDbContextFactory<SatiContext> across all
  services. Swap AddDbContext for AddDbContextFactory in App.xaml.cs. Each method creates
  and disposes its own context via `await using var context = _contextFactory.CreateDbContext()`.
  Do before adding any new features.
- [x] **`GetOrCreateAsync` always returns `wasCreated = false`** — new month records never
  trigger the scheduler prompt correctly; newly-created branch should return `true`
- [x] **NoteType edit not persisting** — suspected EF Core tracking issue; NoteType changes
  on existing notes not being written to DB on save
- [x] **Edit form not populating NoteType** — reopening an existing note doesn't restore
  the current NoteType value; initialization/binding bug
- [x] **Stale data in Upcoming Tasks after failed note edit** — downstream of NoteType
  persistence failure
- [x] **Missing "Scheduled" filter in AllNotes combobox** — straightforward omission
- [x] **ExcludeDayAfterThanksgiving unhandled by IsExcludedHoliday**

---

## Deferred Bugs
*Known issues, not blocking daily use*

- [ ] Scheduler day-of-week column alignment shifts month to month — tiles render
  sequentially rather than snapping to fixed Mon–Fri grid positions
- [ ] Stale ExcludedDates entries persist on Incentive after weekday exclusion is removed
  from Settings
- [x] Note abandonment threshold hardcoded to 8 days — wired to
  `SettingsService.AbandonedAfterDays`
- [x] Settings are global rather than per-user — replaced with agency-scoped settings;
  user overrides remain deferred until a concrete requirement exists

---

## Pre-Release Fixes
*Must address before shipping to team or OADS*

- [ ] Annual form regeneration — when a client's anniversary rolls over, generate new
  Form records for the new compliance cycle
- [ ] First login / scheduler prompt — verify `wasCreated` behavior across month boundaries
  once `GetOrCreateAsync` bug is fixed
- [x] Accessibility audit — icon-only buttons expose accessible names;
  compliance checkboxes are labeled; overdue matrix cells include a visible text status
- [ ] Verify migrations apply to an *empty* database before each release. A working
  database only ever receives new migrations and has been hand-patched over time, so two
  classes of breakage stay invisible locally and appear only when someone builds from
  zero — a new machine, a new agency, or a fresh installer test. Both were found
  2026-08-16 and are now fixed:
  1. *Migrations replaying earlier ones* — recorded as applied, so they never re-run
     locally. `UnitstoDecimal` re-added `Notes.ReturnedById` (SQL 2705); `SyncModelState`
     repeated every operation in `AddSupervisorFieldsToNote`. Both bodies are emptied,
     with the files kept so the migration IDs stay in the chain.
  2. *Model/schema drift* — `Notes.Minutes` and `Notes.StartTime` were in the model and in
     every working database, but no migration created them, so a fresh database produced a
     `Notes` table the model could not query.
     `20260816120000_AddNoteMinutesAndStartTime` adds them, `COL_LENGTH`-guarded so it is a
     no-op on databases that already have the columns without a history row for it.
  `Add-Migration` cannot catch case 2: it diffs the model against
  `SatiContextModelSnapshot`, and the snapshot already listed both properties. The gap is
  between the snapshot and what the migration files actually build.
  Run `scripts/Test-MigrationChain.ps1` (no database needed) and
  `scripts/Test-SchemaDrift.ps1` (against a *from-scratch* database) before each release.
  Verified 2026-08-16: 69 migrations replay clean, and a database built from empty reaches
  the login window with all 349 model columns present.

---

## Phase 9 — Cloud Platform Foundation
*Goal: establish the boundary on which every external deployment and future client depends.*

### Current next slice — concurrency and audit-operation breadth

- [ ] Extend revision/concurrency tokens from assessments, notes, AT requests, settings, and scratchpads to
  other records where simultaneous edits could silently lose work.
- [ ] Extend the friendly desktop conflict handling now used by notes, settings, and scratchpads to the remaining concurrent
  records instead of presenting generic API errors.
- [x] Add idempotency keys for externally retried commands beyond the database-enforced claim-line rule.
- [x] Define audit retention, legal-hold gate, controlled export, SQL-principal permissions, and monitoring.
- [ ] Implement legal-hold enforcement, production SQL grants/denies, retention jobs, and external alert routing.
- [ ] Review and remove the pre-existing Azure SQL firewall rule
      `ClientIPAddress_2026-8-12_14-41-29` if no active operator still owns it; the August 13 billing
      deployment's temporary rules were removed, but unrelated pre-existing access was not changed.

### Next major slice after billing — tenant-safe incident and health pipeline

- [x] Capture structured client and API incidents with UTC time, release, agency, actor role,
      operation, correlation/reference ID, severity, exception fingerprint, recurrence count, and
      lifecycle status; never capture note narratives, passwords, tokens, connection strings, or
      unrestricted exception messages.
- [x] Deduplicate repeated failures into stable incident groups while retaining occurrence counts,
      first/last-seen times, affected releases, and a bounded diagnostic envelope.
- [x] Add an Admin-only agency incident table showing severity, status, release, operation,
      date, and fingerprint. Enforce agency scope at the API and data boundaries, not only in the UI.
- [x] Introduce a separately named platform-operator capability for cross-tenant incident visibility.
      Do not treat an ordinary agency Admin as a master account, and audit every cross-tenant view
      or export.
- [x] Define transparent, versioned agency and platform Incident Health v1 scores from recorded
      severity, recurrence, and unresolved age; show every penalty and state explicitly that v1
      does not claim crash-free-session, availability, or job-failure coverage.
- [x] Add desktop incident search/severity/status filters, audited status-edit controls, explicit
      alert thresholds, and concurrency-safe aggregation with exact-count integration coverage.
- [ ] Add safe session denominators, API availability, and scheduled-job outcomes.
- [ ] Add retention, legal-hold, access-review, alerting, and runbook requirements; prove PHI/PII
      minimization, tenant isolation, bounded queries, concurrency, and score calculations with
      automated tests before enabling production collection.

### Completed 2026-08-13 — billing gate and 837P hardening slice

- [x] Replace the obsolete hardcoded queue code with agency-scoped procedure/modifier/rate,
      submitter, payer, and contact configuration, enforced by Admin-only API and service boundaries.
- [x] Revalidate note approval, current compliance, historical billing windows, subscriber fields,
      provider NPI/address/tax fields, and EDI configuration immediately before claim creation.
- [x] Apply the current Section 13 minimum/partial-unit rule and keep service units separate from
      calculated monetary charges in claim lines and 837P CLM/SV1 segments.
- [x] Require submit-and-lock before EDI generation, preserve retry idempotency, generate ISA16,
      calculate SE01 from ST through SE, and validate fixed ISA length and service-line structure.
- [x] Freeze subscriber, provider, submitter, and payer values in an immutable per-claim snapshot so
      later Person or Agency edits cannot silently rewrite a financial record.
- [x] Add structured subscriber claim-address fields and an accessible editor; generate 2010BA
      N3/N4 segments rather than attempting to parse a free-form address.
- [x] Add an idempotent Demo-only seed with three ready and seven deliberately blocked examples;
      verify all ten through the real billing service and keep ready rows first in the queue.
- [x] Back up and verify local Demo and Production databases before applying the migration; seed
      synthetic rows only in Demo.
- [x] Apply the additive migration and the same ten-row seed to Azure `SatiDemo`, verify 3 ready / 7
      blocked through the real service, deploy the matching Demo API, and remove temporary firewall rules.
- [x] Add a test-only synthetic claim exchange that consumes Sati's test 837P, correlates accepted
      representative 999/277CA responses, produces a balanced synthetic 835, and refuses production
      interchanges. This is an internal workflow test, not transport, import, or payer certification.
- [x] Add append-only tenant-owned submission-event and remittance-claim-outcome read models,
      Admin-only local/cloud grids, retry-safe Generated event recording, and a deterministic
      Demo-only catalog covering eight submission and six remittance contingencies. Every seeded
      exchange row is visibly synthetic and the seed remains hard-limited to `SatiDemo`.
- [ ] Obtain the agency's authoritative fee/code configuration and payer enrollment identifiers,
      then pass clearinghouse test-file validation and payer-specific acceptance. Generated 837P
      structure is tested, but Sati is not yet certified for live claims.
- [ ] Implement 999/277CA acknowledgments, claim rejection correction, 835 remittance import,
      payment/reconciliation, void/replacement claims, and operational submission transport.

### Completed 2026-08-13 -- presenter acceptance kit

- [x] Add secret-free JSON evidence output to authenticated Demo API readiness and isolated
  installer acceptance without treating health-only or same-machine results as final proof.
- [x] Add a final verifier that binds fresh API, external-machine, rehearsal, fallback, release,
  and artifact-hash evidence to the exact installer being presented.
- [x] Produce and visually verify a ten-page offline PDF fallback using only synthetic and
  explicitly representative material, with a presenter approval area and honest limitations.
- [x] Document the external-machine, rehearsal, fallback approval, and final acceptance workflow;
  the matching 1.2.3 API deployment, Global Admin verification, and isolated installer launch are
  complete, while the authenticated agency-Admin run, external-machine run, and human attestation
  remain open.

### Completed 2026-08-13 -- release notes and 1.2.0 packaging

- [x] Add a Settings **Release notes** tab tied to the installed assembly version and summarize
  Admin/audit, safety/reliability, Demo/support, and remaining production work.
- [x] Version Debug, Release, Demo, and the installer consistently as 1.2.0 and add a regression test
  so the in-app notes cannot silently drift from the packaged assembly.
- [x] Compile Debug and Demo with zero warnings and pass 78/78 API, authorization, integration,
  migration, reporting, and domain tests before producing the installer.
### Completed 2026-08-12 -- Demo recovery and acceptance tooling

- [x] Replace user-visible exception/stack-trace dumps with calm reference-number messages and a
  PHI-minimized local JSON-lines diagnostic record that excludes exception messages.
- [x] Add a reproducible self-contained Demo publish script that refuses dirty output folders,
  requires the tracked HTTPS endpoint, hashes the executable, and rejects private appsettings.
- [x] Add read-only deployed health/Admin preflight tooling, a ten-minute company-demo runbook,
  recovery guardrails, and explicit final acceptance gates.
- [x] Compile the real `Demo` configuration in CI and locally; produce and inspect a 263 MB
  self-contained artifact; confirm deployed liveness/readiness after an 80-second Free-tier wake-up.
- [ ] Deploy the current client/API pair, run authenticated preflight, launch the package on a clean
  external Windows machine, rehearse the designated synthetic path, and prove canonical reset.
### Completed 2026-08-12 -- local authorization parity

- [x] Require the signed-in case manager at the local assessment service boundary; reject caller ID
  spoofing, unassigned People, cross-agency People, and attempts to alter another author's draft.
- [x] Restrict local supervisor queues and note decisions to the signed-in reviewer, their agency,
  and either assigned supervisees or agency-wide Director/Admin scope.
- [x] Record successful local assessment and supervisor transitions in the same database save as the
  protected state change.
- [x] Add SQLite service-level regression tests for author, reviewer, assignment, agency, and audit
  boundaries while preserving the cloud API as the production authority.
### Completed 2026-08-12 -- operations visibility and records governance

- [x] Add an Admin-only, agency-scoped operations status view with database status, retained audit/EDI
  counts, oldest-record timestamps, and explicit audit/EDI retention policy values.
- [x] Add a reason-gated, one-year/10,000-row bounded audit CSV export; mark it no-store and audit the
  export without copying its business reason into server metadata.
- [x] Publish `OPERATIONS.md` with the legal-hold prerequisite, production SQL-principal separation,
  monitoring/alert expectations, operator checks, and an honest `PolicyOnly` enforcement state.
- [x] Preserve local-development/cloud parity while keeping retention destructive actions disabled
  until legal-hold controls and production authorization are reviewed.
### Completed 2026-08-12 -- billing retry safety

- [x] Give each WPF EDI-generation attempt a stable retry key and preserve it across ambiguous
  failures until the exact file has been returned and saved.
- [x] Persist the exact EDI response behind a tenant-, actor-, and key-scoped unique index so a
  repeated API request replays one file and creates only one success audit event.
- [x] Reject reuse of an EDI retry key for a different period or test/production mode.
- [x] Make billing-period submission repeatable: an already-submitted period returns its original
  success state, while a database concurrency token prevents simultaneous submissions from
  producing duplicate state changes or audit events.
- [x] Carry the retry contract through the shared API contracts and local service boundary without
  beginning the deliberately post-pilot MAUI/Avalonia business-logic extraction.

### Completed 2026-08-20 -- Tomorrow's Agenda

- [x] Add Tomorrow's Agenda beside Today's Work in the existing scratchpad panel, with both drafts
  included in autosave, app-close, and account-switch flushes.
- [x] Store the agenda as the next workday's existing per-user Scratchpad row so it becomes Today's
  Work automatically without a midnight copy or duplicate-promotion state.
- [x] Put the Friday/Saturday/Sunday-to-Monday rule in `Sati.Contracts.V1.WorkAgendaDates` and use
  the API's agency-local clock to select the cloud row; holidays remain deferred until Sati has an
  authoritative agency holiday-calendar policy.
- [x] Preserve revision conflicts independently for both tabs and cover the calendar rule, route
  ownership, stable row identity, and cross-user isolation with automated tests.
- [x] Deploy the matching API surface to hosted Demo with OneDeploy
  `2bf8bd7fb3ea4ca39595a87da836f727`; readiness returned 200 and the live contract revision
  `A4FB297F7FE6` matched the release build.

### Completed 2026-08-12 -- scratchpad concurrency boundary

- [x] Protect each user's daily Scratchpad with a `Revision` token across local SQL, cloud
  contracts, and the API; reject stale and older-client autosaves with `409 stale_scratchpad`.
- [x] Treat unchanged ten-minute autosaves as no-ops so they create neither revision churn nor
  misleading audit activity.
- [x] Preserve the unsaved text after a conflict, stop repeated autosave warnings, block shutdown
  and user switching from discarding it, and provide an explicit Reload Latest recovery action.
- [x] Record only accepted content changes in the PHI-minimized audit envelope and prove stale,
  legacy, cross-user, no-op, migration, and recovery-boundary behavior.

### Completed 2026-08-12 -- settings concurrency boundary

- [x] Add an agency Settings `Revision` token through local SQL, cloud contracts, and the API.
- [x] Reject stale and legacy settings saves with `409 stale_settings` before shared agency policy
  can be silently overwritten; advance the revision only after a successful save.
- [x] Keep the Settings window open with a friendly warning when another administrator saved first,
  preserving the attempted values for comparison with the latest agency settings.
- [x] Prove one success audit event per accepted save, no false success event for rejected attempts,
  older-client fail-closed behavior, and isolation of another agency's settings.

### Completed 2026-08-12 — first audit and concurrency slice

- [x] Add the PHI-minimized `AuditEvent` envelope, migration, action catalog, and Admin-only,
  agency-scoped audit query documented in `AUDIT_EVENTS.md`.
- [x] Record successful authentication, account changes, supervisor note decisions, assessment
  writes/submission, settings changes, billing submission, and EDI generation.
- [x] Commit each protected state transition and its audit event in the same database save/transaction.
- [x] Add an assessment `Revision` concurrency token and reject stale save/submit requests with HTTP 409.
- [x] Make claim-line creation atomic and enforce one claim line per service note with a unique index.
- [x] Test audit minimization/immutability/tenant scope, stale writes, and repeated billing commands.

### Completed 2026-08-12 — note concurrency slice

- [x] Add a Note `Revision` concurrency token and require the revision read by the caller on every
  edit, delete, supervisor approve/override/return, and automated abandonment transition.
- [x] Reject stale Note operations with HTTP 409 before they can overwrite, delete, or supersede a
  newer copy; increment revisions for every successful state transition.
- [x] Preserve an open editor's draft after a conflict, identify fields that differ from the latest
  saved copy, and refresh Notes Log and supervisor queues before another action.
- [x] Load full Note records in the Notes Log instead of caseload summaries so IDs, narratives,
  people, and revisions remain available through the cloud API transition.
- [x] Prove stale note edits, deletes, and supervisor decisions leave the newer record intact.

### Completed 2026-08-12 — AT request concurrency boundary

- [x] Treat an AT request and all of its line items as one revisioned financial aggregate.
- [x] Require the caller's expected revision for update and delete through local and cloud
  persistence; reject stale or legacy writes with `409 stale_at_request`.
- [x] Replace line items and increment the parent revision in one EF transaction so a stale
  aggregate cannot partially alter vendor, money, status, dates, or item details.
- [x] Add a typed desktop-service conflict for the future Save/Open/Delete workflow without
  prematurely separating Save from the deliberately bundled PDF-publishing feature slice.
- [x] Prove stale aggregate replacement and deletion preserve the newer request and its items.

### Completed 2026-08-12 — Person lifecycle audit

- [x] Preserve an append-only, compressed snapshot and field-level change set for every successful
  Person create, profile edit, and journal edit.
- [x] Record actor, agency, UTC timestamp, request correlation ID, and monotonically increasing
  Person revision; reject stale profile saves with HTTP 409.
- [x] Add Admin-only, agency-scoped history and auditor-PDF endpoints with no-store response headers
  and audit events for viewing/exporting the history.
- [x] Give pre-existing People an explicit current-state tracking baseline without pretending older
  changes can be reconstructed.
- [x] Verify cross-agency denial, append-only enforcement, stale-write rejection, and rendered PDF output.

### Completed 2026-08-12 — visible Admin dashboard

- [x] Add a top-level Admin destination that is hidden for every non-Admin role.
- [x] Show agency user, Person, active-user, sign-in, Person-change, and daily audit-event metrics
  without requiring Azure Portal access.
- [x] Add a readable 30-day activity feed with actor, action, resource, and local timestamp.
- [x] Add an agency Person directory, lifecycle timeline, and protected PDF download workflow.
- [x] Support both the cloud API and the transitional local-development database through
  `IAdminService`; preserve server-side Admin and tenant enforcement.
- [x] Add API integration coverage for dashboard scope and non-Admin rejection.

### Completed 2026-08-12 — tenant-enforcement breadth

- [x] Inventory every protected API route and document its authoritative tenant owner in
  `API_AUTHORIZATION.md`.
- [x] Add cross-agency rejection tests for reports, billing exports, AT requests, assessments,
  supervisor actions, and generated files.
- [x] Revalidate the token's user, agency, and role against the database on every protected request.
- [x] Centralize actor, caseload, supervisor, and assessment-authorship checks in `TenantAccess`.
- [x] Prevent supervisors from authoring a case manager's assessment while preserving read/review access.
- [x] Revalidate every billing-export source note and person against the exporting agency.

### Completed 2026-08-12 — API boundary verification

- [x] Add a dedicated cross-platform API integration/authorization test project.
- [x] Prove unauthenticated requests are rejected at the protected API boundary.
- [x] Prove cross-agency reads and writes are rejected for users, people, and providers.
- [x] Keep the API test project independent from WPF and desktop persistence.
- [x] Run the API tests in CI alongside the existing domain/migration tests.

### Repository structure — targeted follow-up, not a reshuffle

The solution-level boundaries (`Sati`, `Sati.Api`, `Sati.Contracts`, desktop/domain tests, and
API integration tests) are coherent. Avoid a broad folder move while the API transition is active.
Address these pressure points when the affected code is next changed:

- [ ] Split the large `Sati.Api/Endpoints/ApiEndpoints.cs` by feature route group while preserving
  one `/api/v1` composition point.
- [ ] Eliminate schema drift between `SatiContext` and `ApiDbContext`; make server-side persistence
  and migrations authoritative before removing local EF from the distributed WPF client.
- [ ] Rename/move the root WPF project to an explicit `Sati.Wpf` project only after direct cloud
  database responsibilities are removed; doing it now would create churn without changing coupling.
- [ ] Archive historical session material out of this active agenda once the current platform
  priorities are stable; keep a short current-work section at the top.

### Architecture and solution structure

- [x] Add `Sati.Api` ASP.NET Core project.
- [x] Add shared, versioned request/response contracts that do not expose EF entities.
- [ ] Move EF Core, migrations, password hashing, and authoritative domain operations behind the API.
- [ ] Implement HTTP-backed desktop services behind the existing service interfaces where the
  contracts remain appropriate.
- [ ] Remove direct database connectivity and `Database.Migrate()` from distributed clients.
- [x] Add API health checks, structured logs, correlation IDs, and startup validation.
- [ ] Add operational metrics and alert wiring.

### Identity and authorization

- [x] Move Sati credential verification server-side; return a safe profile and short-lived token.
- [x] Never return `PasswordHash` or `Salt` to a client.
- [ ] Add token expiration, revocation, secure recovery, and brute-force/rate-limit controls.
- [ ] Define capabilities independently from menu visibility and coarse job titles.
- [x] Derive actor, tenant, role, and caseload server-side instead of trusting supplied IDs.
- [ ] Evaluate Microsoft Entra ID/External ID and MFA for production organizations.

### Tenant model

- [ ] Decide shared-database, database-per-tenant, or hybrid production isolation.
- [x] Define the authoritative tenant owner for every protected aggregate.
- [x] Replace global settings with tenant-scoped settings; add user overrides only where required.
- [ ] Centralize tenant enforcement with query filters/interceptors and command authorization.
- [x] Add automated cross-tenant read/write/export rejection tests.
- [ ] Add tenant provisioning, suspension, migration, export, and deletion/retention procedures.

### Records, audit, and concurrency

- [x] Create append-only `AuditEvent` records for protected actions without copying
  unrestricted narrative PHI into log messages.
- [ ] Define which sensitive read events require auditing without creating an unusable volume of noise.
- [ ] Add immutable document versions, amendments, attestations, and electronic signatures.
- [ ] Define retention and legal-hold behavior by record class.
- [ ] Extend optimistic concurrency tokens and user-facing conflict resolution beyond the completed
  assessment, Person, Note, and AT persistence records. AT desktop conflict recovery lands with its
  still-deferred Save/Open/Delete and PDF-publishing workflow.
- [ ] Make commands retry-safe and idempotent where duplicate execution would cause harm.
- [ ] Move billing, approval, and submission transitions into explicit server transactions.

### Testing and delivery

- [x] Add unit, API integration, authorization, and migration-consistency tests.
- [ ] Add end-to-end tests for the packaged Demo client and deployed API.
- [x] Establish CI build and test validation across the solution.
- [ ] Add CI dependency scanning, deployment migration validation, and artifact creation.
- [ ] Establish controlled database deployment and rollback; clients never migrate cloud schemas.
- [ ] Add backup verification, point-in-time recovery exercises, disaster-recovery objectives, and
  incident alerts.
- [ ] Produce a signed self-contained Demo installer after API access is working.
- [ ] Test installation, authentication, updates, and removal on a clean non-developer machine.

### Azure Demo milestone

- [x] Split local Production and Demo databases and add fail-closed identity markers.
- [ ] Extract a versioned canonical superhero/sitcom Demo seed independent of migrations.
- [x] Provision `SatiDemo` in Azure SQL without moving production data.
- [x] Host the Demo API with managed identity and least-privilege SQL access.
- [x] Restrict Azure SQL so tester devices do not connect directly.
- [x] Deploy and live-test a daily canonical caseload refresh with rolling dates, a separate managed
      identity, six deliberate teaching exceptions, profile/claim validation, and repeat-run safety.
- [x] Implement the source for a full baseline reset that pauses mutations, removes arbitrary Demo
      changes, restores demonstration logins, and invalidates all pre-reset sessions.
- [ ] Deploy and live-test that full reset and send failures to an approved notification target.
- [ ] Complete an API inventory and migrate all workflows included in the colleague Demo.
- [ ] Run security, tenant-boundary, concurrency, reset, and clean-install acceptance tests.

### Production readiness gate

Production cloud migration is not authorized by completion of the Demo milestone. It requires a
separate risk assessment, architecture review, operational runbook, BAA/vendor review, penetration
testing plan, user-access process, incident-response process, and explicit approval to move real
working data.

## Mobile and Avalonia Strategy
*Decision: no bottom-up Avalonia rewrite before the cloud platform boundary exists.*

WPF remains Sati's full-power, data-dense Windows client. Cross-platform reach will come first from
the shared API and portable contracts, not from forcing the current desktop shell onto phones.
Avalonia remains a candidate for a focused mobile client and, only if real demand emerges, a future
cross-platform desktop client.

### Prerequisites

- [ ] Complete the API foundation for authentication, people, notes, and upcoming work.
- [ ] Extract portable projects that do not depend on WPF, EF Core, desktop dialogs, or local
  filesystem paths. Initial target structure:
  - `Sati.Contracts` — versioned API request/response DTOs
  - `Sati.Domain` — portable domain concepts and authoritative pure calculations
  - `Sati.Client` — authentication, HTTP transport, and shared client services
  - `Sati.Api` — server authority, EF Core, authorization, audit, and integrations
  - `Sati.Wpf` — existing Windows professional client
- [ ] Keep platform-neutral service contracts free of `Window`, `Dispatcher`, `SecureString`,
  EF entities, and other Windows/persistence-specific types.
- [ ] Define mobile security, session, offline-storage, synchronization, and device-loss rules
  before storing any protected data on a phone.

### Define the first mobile product

Do not treat the desktop application as the mobile specification. Validate the smallest useful
field-work scope, likely:

- secure login and session expiration;
- today's work and upcoming deadlines;
- client lookup and essential contact details;
- quick visit/contact note capture;
- interruption-safe local drafts and later synchronization;
- optional camera/document capture; and
- future EVV check-in/out if required.

Billing administration, EDI, provider maintenance, system settings, dense compliance matrices,
and broad supervisory analytics are not assumed to belong in the first mobile client.

### One-week Avalonia spike — after API prerequisites

- [ ] Create separate Avalonia core and Android projects; do not replace `Sati.Wpf`.
- [ ] Implement login, client list, one client summary, and basic note capture against the API.
- [ ] Approximate the Sati theme without attempting full WPF style parity.
- [ ] Test touch targets, navigation, interruption recovery, slow/offline behavior, and session
  expiration on a physical Android device—not only an emulator.
- [ ] Record porting friction around XAML styles, converters, accessibility, dialogs, and shared
  ViewModels.
- [ ] Estimate iOS requirements separately, including macOS/Xcode, signing, provisioning, and
  distribution.
- [ ] Decide whether to proceed based on field usability and maintenance cost rather than the fact
  that the sample compiles.

### Explicit non-goals

- No week-long attempt at whole-application Avalonia feature parity.
- No mobile client that connects directly to SQL or embeds a database credential.
- No virtualized desktop-window interface presented as a finished phone experience.
- No parallel copy of authoritative business rules in WPF and Avalonia.
- No abandonment of WPF unless cross-platform desktop demand and a measured migration case justify
  it.
## Billing Pipeline (historical plan; superseded by the completed hardening slice above)

### Data model changes needed
- [ ] Add `CompletedDate DateTime?` to `Form` + migration
- [ ] Add `IsBillable bool` to `Note` + migration (default true, computed at creation)
- [ ] Add `BillingStatus` enum: `Pending | Approved | Rejected | Queued`
- [ ] Add `SupervisorApprovedById int?` and `SupervisorApprovedAt DateTime?` to `Note`

### Logic
- [ ] Add `FormComplianceStatus` enum: `NotYetDue | InWindow | CompliantOnTime | CompliantLate | Overdue | NoForm`
- [ ] Add `GetComplianceStatus(FormType, DateTime, Settings)` to `Person`
- [ ] Wire `IsBillable` computation into `SubmitNewNoteAsync` — check all required forms at `EventDate`
- [ ] `IsBillable == false` notes route to supervisor review queue, not billing queue

### Supervisor dashboard
- [ ] Add billing review queue sub-view — shows all `IsBillable == false` notes
- [ ] Supervisor approve → `IsBillable = true`, set `SupervisorApprovedById` and `SupervisorApprovedAt`
- [ ] Supervisor reject → note stays non-billable, logged in audit trail

### Rules
- Compliant on time + in window = billable
- Compliant late = compliant on paper, gap period unbillable
- Overdue = not billable until resolved and supervisor-approved
- Notes persist regardless of billability — always a valid service record
---

## Future Roadmap

### Upcoming-item rule ownership

- [ ] Reconcile the deliberately different scopes of `IUpcomingEventService` (the settings-driven
  open/late window used by dashboards and suggestions) and
  `NewClientViewModel.RefreshUpcomingItems` (all non-compliant forms on the selected-client page).
  Decide whether both scopes remain product requirements, then give each a named shared owner so a
  third hand-written upcoming-item calculation cannot appear.

### Local AI case-note drafting

Closed-world revision shipped 2026-08-22: prior notes, assessments, Bio, deadlines, contacts, and
other historical records were removed from AI context. The selected-client route now returns only
the own-caseload person's ID and first name and never receives rough-note text. Every rough fragment
and every selected Visit checkbox, selector value, attendee, or detail becomes a stable required
fact. The model must return fact-cited JSON; shared deterministic rules reject the whole result for
omission, selector-value loss, wrong-section use, or unsupported names, numbers, quotations,
negation, and content words. `Not documented`, `Not assessed`, and unchecked controls assert nothing.

The original narrative remains unchanged until explicit acceptance. A source fingerprint and latest-
request identity prevent a result from publishing or being accepted after the person, narrative, or
template state changes. Consumer presence is explicitly selected instead of defaulting to present.
Cross-consumer model switching fails closed if unload fails. Follow-up is supported by a current fact
or exactly `No follow-up was documented.`; historical form deadlines are no longer inferred.
The model can explicitly select Sati's validated deterministic renderer instead of risking an
unsupported rewrite. Runtime failures and rejected model output use the same renderer with a visible
warning. The opt-in device gate requires safe completion of all synthetic scenarios through the real
local runtime; explicit safe deferral is accepted rather than forcing unsupported prose.

Before any shared or production release:

- Obtain the authoritative agency case-note policy and replace/refine `AI_CASE_NOTE_RULES.md`.
- Assemble at least 50-100 de-identified rough-note/approved-note examples spanning visits,
  contacts, forms, sparse notes, ambiguity, quotations, negative statements, and safety content.
- Define and pass acceptance thresholds for zero invented facts, retained attribution/negation,
  required formatting, latency, memory use, and accessibility.
- Run the separately controlled local-model/device evaluation suite; deterministic compiler,
  grounding, tenant-scope, and stale-client regressions are now automated. The representative
  target-device smoke gate runs only with `SATI_RUN_LOCAL_AI_MODEL_EVAL=1` so ordinary CI cannot
  acquire model weights implicitly.
- Persist an audit record for accepted AI drafts: source, draft, final user-edited text, rule-set
  version, model alias/version/hash, user, timestamps, and explicit acceptance. Decide retention and
  access rules before adding this to the database.
- Add model-download/retry controls; test first-run, cached/offline, low-disk, unavailable-model,
  corrupt-cache, and runtime-unload behavior on supported devices. In-flight generation now cancels
  when its selected client or source inputs change.
- Complete privacy, security, clinical/documentation, labor, and records-retention review. Confirm
  runtime telemetry is disabled or contains no PHI and that no cloud fallback can occur.
- Pin the approved model variant and license terms; prevent an unreviewed catalog update from
  changing production output.
- Add an administrative release gate so production builds default to disabled until formally
  approved, even if a development `appsettings.json` is copied accidentally.
*Parked for post-OADS or v2.0+*

- [ ] **Historical productivity viewer** — query past Incentive rows paired with monthly
  note data to display a full productivity history per user; infrastructure already exists
- [ ] Per-client detail view — all notes, forms, compliance status, and upcoming events
  scoped to one client
- [ ] User management / admin panel — add, edit, deactivate users
- [ ] Soft-delete recycle bin for notes
- [ ] DataGrid column cleanup — replace AutoGenerateColumns with explicit column definitions
- [ ] Sati.Core extraction — shared class library for models, services, EF context
- [ ] Sati.Api — ASP.NET Core Web API (GET /clients, POST /notes)
- [ ] Sati.Mobile — MAUI app for field note entry and upcoming task visibility
- [ ] Azure Cognitive Services Speech-to-Text for field note dictation
- [ ] reMarkable integration — push visit PDFs, pull annotated docs

---

## Comprehensive Assessment, PCP, and OADS Workflow
*Goal: Replace Evergreen with a person-centered, waiver-agnostic assessment and plan workflow that is practical for case managers, reviewable by supervisors and OADS, and safe for billing.*

### Comprehensive Assessment — first functional slice shipped 2026-08-07

> Superseded 2026-09-14 where this historical slice sets the CA deadline to 60 days. The current
> annual default is due 90 days before the target and available 120 days before it.

- [x] Replace the client-workspace placeholder with a desktop-oriented assessment editor.
- [x] Add eight navigable domains with an initial set of practical questions.
- [x] Add expandable guidance to every question: why it is asked, what a complete
  answer includes, and what to avoid.
- [x] Persist drafts in `ComprehensiveAssessments` and auto-save after edits.
- [x] Store the assessment body as one versioned JSON aggregate while workflow and
  ownership fields remain relational/queryable.
- [x] Record report contributors separately from the assigned case-manager author.
- [x] Use one team assessment by default with optional dissenting perspectives.
- [x] Model support as combinable characteristics, not a false linear scale.
- [x] Enforce logical exclusions: "No support currently needed" clears active support
  selections; `Varies` requires at least one concrete support and an explanation.
- [x] Require every question to be addressed before submission; follow-up-required
  answers do not count as complete.
- [x] Add structured identified needs with type, desired result, and optional provider
  association.
- [x] Enforce authoring by caseload ownership. Supervisor status alone does not grant
  permission to rewrite another case manager's answers.
- [x] Add submission to supervisor review and immutable approved/superseded states in
  the domain model.
- [x] Change the new/default Comprehensive Assessment deadline from 120 to 60 days
  before the PCP anniversary.
- [x] Add migration `20260807120000_AddComprehensiveAssessments`.

### Comprehensive Assessment — content and usability follow-up

- [ ] Flesh out the assessment with the additional specific questions identified by
  case-manager and OADS review; keep questions waiver-agnostic.
- [ ] Add a realistic good-answer example to every question, alongside the existing
  inclusion and avoidance guidance.
- [ ] Review every question for plain language, practical answerability, duplication,
  trauma-informed wording, dignity, and relevance to authorization.
- [ ] Decide which questions are conditional and implement branching without hiding
  previously entered answers.
- [ ] Add question-level comments, supervisor flags, resolution state, and return reason.
- [ ] Add a visible validation summary with links that move focus directly to every
  incomplete or contradictory answer.
- [ ] Validate contributor rows, identified needs, provider associations, and dissenting
  opinions—not only the core question set—before submission.
- [ ] Add need urgency, current supports, unmet component, health/safety implication,
  responsible next action, status, and resolution history.
- [ ] Replace the temporary provider-name entry with selection from the future
  consumer/provider association model while retaining a document snapshot.
- [ ] Add `Not assessed` follow-up ownership and due date; permit completion only through
  an explicitly documented supervisor exception where policy allows.
- [ ] Add autosave retry/recovery, unsaved-change shutdown flush, concurrency handling,
  and protection against two sessions editing the same draft.
- [ ] Remove the service-locator construction in
  `ComprehensiveAssessmentWorkspace.xaml.cs`; inject/factory-create the workspace and
  ViewModel consistently with Sati's DI rule.
- [ ] Perform keyboard-only, JAWS, high-contrast, 200% scaling, and 1280x768 layout QA.
- [ ] Add unit tests for completion rules, support-selection exclusions, ownership,
  version immutability, serialization compatibility, and submission transitions.

### Supervisor assessment workflow

- [ ] Add a supervisor review queue for Comprehensive Assessments.
- [ ] Allow supervisors to flag individual sections/questions, comment, and return a
  submission without rewriting the author's answers.
- [ ] Make supervisor approval wholesale, with all unresolved flags blocking approval.
- [ ] Permit supervisors who carry a caseload to author only their own assigned clients'
  assessments; keep the author and reviewer capabilities separate.
- [ ] Record submission, return, resubmission, approval, actor, timestamps, reason, and
  exact version in append-only audit history.
- [ ] On approval, lock the version and mark the matching legacy `Form` complete through
  the existing `Form` invariant rather than writing compliance fields directly.

### Documents, signatures, and versions

- [ ] Publish a version-identified Comprehensive Assessment PDF.
- [ ] Retain both the generated unsigned PDF and uploaded physically signed scan.
- [ ] Record signer, role, signature method, upload actor, and timestamps against the
  exact frozen version.
- [ ] Add the same publish/print/upload workflow to the PCP.
- [ ] Ensure any post-publication edit creates a new version and signature cycle; never
  replace or silently mutate a signed or approved version.
- [ ] Establish secure document storage, malware scanning, retention, download/export
  authorization, and accessible PDF generation.

### Electronic signature portal — vetted direction

The checklist below describes the broader regulated feature. The synthetic implementation and
remaining activation work are recorded under **Electronic signature handoff — implemented synthetic
scope and activation work (2026-09-06)** near the end of this document. Its completed pieces do not
clear real-use, multi-signer, program-acceptance, or operating requirements in this broader list.

The signature feature should be a secure Sati web portal reached from an email
notification—not a Sati-operated mail server and not an email reply treated as the
authoritative signature. Email is the delivery channel; Sati owns the identity check,
review, intent-to-sign action, immutable evidence, and resulting signed artifact.

#### Policy gates before implementation

- [ ] Obtain written OADS/OMS confirmation that the MaineCare electronic-signature
  notice supersedes the OADS PCP manual's physical-signature instructions for the PCP
  Face Sheet and for annual/reversioned plans.
- [ ] Confirm separately whether electronic signing is accepted for provider/team
  Agreement Sheet signatures; the current MaineCare notice expressly discusses member
  signatures, while the published OADS manual still says implementing Team Members must
  physically sign the Agreement Sheet.
- [ ] Confirm what exact evidence Resource Coordinators must receive and whether a
  generated signed PDF plus audit certificate qualifies as the retained original.
- [ ] Confirm the required signers and signature meaning for the Comprehensive Assessment
  independently from the PCP workflow.
- [ ] Document the authority and identity-proofing rules for a Person, guardian,
  authorized representative, case manager, provider implementer, supervisor, and OADS
  Resource Coordinator. A proxy must sign in the proxy's own name and capacity, never as
  though they were the Person.
- [ ] Preserve a paper, in-person, and accessible assisted-signature path. Electronic
  transactions must be consensual and must not become a condition of receiving services.

Policy basis reviewed 2026-08-07:

- [MaineCare's electronic-signature notice](https://www1.maine.gov/dhhs/oms/providers/provider-bulletins/notice-regarding-electronic-signatures-2024-09-16)
  permits member electronic signatures under enforcement discretion when the system
  authenticates the signer, prevents signing an incomplete document, complies with
  privacy/security requirements including HIPAA, and retains proof plus the signed record.
- [Maine UETA](https://legislature.maine.gov/statutes/10/title10ch1051sec0.html)
  recognizes an electronic process adopted with intent to sign, requires agreement to
  transact electronically, permits codes/security procedures as attribution evidence,
  and requires an accurate record that remains accessible for later reference.
- [42 CFR 441.301(c)(2)(ix)](https://www.ecfr.gov/current/title-42/chapter-IV/subchapter-C/part-441/subpart-G/section-441.301)
  requires the PCP to be finalized with the individual's informed written consent and
  signed by the individual and all people/providers responsible for implementation.
- The [published OADS PCP manual](https://www.maine.gov/dafs/bablo/sites/maine.gov.dhhs/files/documents/PCPManualpdf.pdf)
  still instructs case managers to obtain physical signatures and maintain originals;
  this unresolved conflict is why written OADS/OMS direction is a release gate.

#### Signature workflow and domain model

- [ ] Freeze a complete, version-identified document before creating any signature
  request. Store the final PDF, document version, cryptographic hash, and publication
  timestamp; never allow the published content to mutate in place.
- [ ] Model `SignatureEnvelope`, `SignatureRequest`, `RequiredSigner`, and append-only
  `SignatureEvent` records separately from document approval and authorization state.
- [ ] Create one uniquely addressed request per required signer. Do not treat a shared
  family email, provider group mailbox, meeting attendance, or document contribution as
  proof that a particular person signed.
- [ ] Track signer name, role/capacity, organization, required/optional status, delivery
  status, authentication method, signed/declined/requested-changes state, and timestamps.
- [ ] Give the signer three explicit outcomes after reviewing the exact frozen document:
  sign/agree, decline, or request changes. Capture the exact intent and consent language
  displayed at the moment of signature.
- [ ] Keep signature completion, supervisor approval, OADS wholesale approval,
  Classification, service authorization, and billability as distinct states. One must
  never silently imply another.
- [ ] Require a new document version and signature cycle after any substantive change.
  Retain the prior version, its signatures, and the reason for supersession.
- [ ] Generate a signed PDF or audit certificate logically associated with the frozen
  PDF, and retain both the original final artifact and complete signature evidence.
- [ ] Distribute a downloadable/printable copy to the Person/guardian and other entitled
  participants after completion, using the same access controls as the signing portal.

#### Email delivery and authentication

- [ ] Use a managed outbound transactional-email service under the required HIPAA
  agreement and configure domain authentication/deliverability. Do not operate inbound
  SMTP or require the signer to reply to an email.
- [ ] Keep notification emails generic and free of assessment/PCP content and unnecessary
  identifiers. Do not automatically attach PHI; the opaque, random, single-use link opens
  the protected portal where the document is shown after authentication.
- [ ] Confirm the email address and preferred confidential communication method during
  enrollment, and support correction/revocation without silently retargeting an existing
  signature request.
- [ ] Make link tokens high-entropy, single-use, short-lived, revocable, rate-limited,
  and stored only as hashes. Never place a person ID, document ID, MaineCare ID, or other
  meaningful identifier in a URL.
- [ ] If a pre-established signing code is retained, establish it only after verified
  enrollment, hash it using the password-storage standard, never reveal it to staff, and
  rate-limit/lock failed attempts. Do not send the code through the same email as the link.
- [ ] Prefer an existing authenticated provider account, passkey, or short-lived code
  delivered through an independent verified channel. NIST does not treat email as an
  acceptable independent out-of-band authentication channel.
- [ ] Design a verified recovery and assisted-signing process for people who forget a
  code, share an email account, lack a mobile phone, use supported decision making, or
  cannot independently operate the portal. Recovery must not be easier to exploit than
  the normal signature flow.

#### Security, evidence, operations, and accessibility

- [ ] Put the public signing surface behind a trusted web application/API boundary;
  the WPF client and direct database access cannot serve as the Internet-facing security
  boundary. Authorize every request by capability, organization, signer, document version,
  and workflow state.
- [ ] Record append-only evidence including document hash/version, signer identity and
  capacity, authentication method, consent/intent text, issuance/view/sign timestamps,
  delivery events, failed attempts, revocation/expiration, and administrative actions.
- [ ] Decide through privacy/security review whether IP address and user-agent evidence is
  necessary and proportionate; document retention and access rules for that metadata.
- [ ] Encrypt PHI in transit and at rest, segregate signing secrets from document storage,
  prevent replay, scan uploaded physical-signature fallbacks for malware, and include the
  portal/vendor in risk analysis, incident response, breach response, and business-
  associate agreements.
- [ ] Add reminders, expiration, resend, email-bounce handling, signer replacement,
  revocation, and escalation without changing the frozen document or losing history.
- [ ] Make the signing page work at common mobile and desktop sizes with keyboard-only
  navigation, screen readers, magnification, high contrast, plain language, limited-
  English support, and an accessible downloadable document.
- [ ] Add automated tests for incomplete-document blocking, wrong signer, shared email,
  expired/replayed links, brute-force limits, version mismatch, signer replacement,
  decline/request-changes, partial multi-signer completion, post-signature mutation,
  audit immutability, and separation from OADS approval/authorization.

This is a medium-to-large architecture feature despite its intentionally simple signer
experience. The email sender is a small component; the public portal, trusted API,
identity and authority proof, immutable document/version handling, multi-signer state,
audit evidence, accessibility, and policy acceptance are the substantive work.

### Person-Centered Plan

- [ ] Build the PCP at intake and annually, using the approved assessment and live
  consumer profile as sources without silently mutating approved plan versions.
- [ ] Record all PCP meeting participants, roles/relationships, invitation/attendance
  status, attendance method, and signature/acknowledgment state.
- [ ] Default the assigned case manager as meeting organizer, with an audited override.
- [ ] Add profile-to-PCP change rules: informational, potentially material, material,
  and authorization-affecting.
- [ ] Generate a reviewable PCP change set or amendment from material profile changes;
  require case-manager confirmation and supervisor review for important changes.
- [ ] Add OADS Resource Coordinator review with section flags/comments but wholesale
  approval/return of the PCP.
- [ ] Add authorized services as a deliberately unfinished section boundary now; later
  connect it to Providers, authorization periods, units, frequency, duration, funding,
  assessed needs, and goals using immutable snapshots.
- [ ] Preserve the distinction between assessment facts, supervisor attestation, PCP,
  OADS decision, classification, and authorization.

### Classification and future OADS access

- [ ] Keep Comprehensive Assessment and PCP waiver-agnostic.
- [ ] Implement waiver/level-of-care determination in Classification, including future
  Lifespan Waiver support.
- [ ] Add the OADS Resource Coordinator role and narrow capabilities rather than relying
  on menu visibility or broad job-title permissions.
- [ ] Record approval, denial, return, effective/expiration dates, cited evidence,
  decision-maker, and immutable decision history.
- [ ] Add assignment, delegation, temporary coverage, reassignment, and recusal workflows.

### Deadlines, reminders, reviews, and billing

> Superseded 2026-09-14 where this list says CA is due 60 days before the PCP, leaves resumption
> undefined, or treats the due day as unbillable. The active section records the confirmed dates,
> day-after-due boundary, service-date policy, exact exception, and recovery separation.

- [ ] Reconcile already-generated Comprehensive Assessment `Form.DueDate` values from
  the legacy 120-day offset to the agreed 60-day-before-PCP rule through an inspected
  dry run; the migration changes the setting/default but deliberately does not guess at
  existing records.
- [ ] Continue using the existing Form/ReviewItem reminder calculations as the canonical
  deadline source; do not create parallel assessment date arithmetic.
- [ ] Block PCP submission when its Comprehensive Assessment is overdue, with a documented
  supervisor-or-higher override containing reason, actor, timestamp, expiration, and
  affected version.
- [ ] At midnight after PCP expiration, mark subsequently submitted case notes permanently
  unbillable; there is no grace period and later PCP completion is not retroactive.
- [ ] Apply the same permanent billing-gap rule to overdue 90-day reviews, using Sati's
  existing review due-date calculations.
- [ ] Preserve all concurrent unbillable reasons on the note and show them before note
  submission as well as in billing exports.
- [ ] Define and implement the exact instant at which billability resumes after a late
  PCP or 90-day review is completed.
- [ ] Prevent unbillable notes from entering MIHMS claim generation while retaining them
  as valid service documentation.
- [ ] Add regression tests around midnight boundaries, back-entered notes, multiple
  simultaneous compliance failures, overrides, and permanent non-retroactivity.

## Session Log

| Date | Phase | What was done |
|------|-------|---------------|
| 3/19 | Ph5 | Settings model, migration, ISettingsService/SettingsService, SettingsViewModel, wired into CaseManagerDashboardViewModel |
| 3/20 | Ph5 | Scratchpad model/service/migration, auto-save timer, NoteType enum, template insertion |
| 3/21 | Ph5 | NoteType radio buttons, EnumToBoolConverter, Incentive model/service/migration, productivity dashboard, weekday/holiday exclusion flags |
| 3/22 | Ph5 | SchedulerViewModel, WorkdayTile, Incentive ExcludedDates, ISessionService singleton, scheduler popup XAML |
| 3/23 | Ph5 | Scheduler popup fully working — tile toggling, month navigation, DaysScheduled persistence |
| 3/23 | Ph5 | SettingsWindow fully wired — billing, templates, weekday/holiday flags, auto-save on close |
| 3/23 | Ph5 | Day after Thanksgiving flag, tuple return from GetOrCreateAsync, custom new month prompt |
| 3/25 | Ph6 | SafetyPlan + PrivacyPractices added to FormType. 18 form deadline offset properties in Settings. UpcomingEventService created. |
| 3/26 | Ph6 | UpcomingEventService wired into CaseManagerDashboardViewModel, UserId FK on Person, GetAllPeopleAsync filtered by userId |
| 3/28 | Ph6 | Upcoming Tasks split into two columns, sort radio buttons, EffectiveDate refactor, form note templates, FormType on Note with migration |
| 3/29 | Ph6 | MarkFormCompleteRequested wired end to end, compliance checklist bound to real data, GetCurrentCycleForm added, ComplianceReviewWindow on client creation |
| 3/29 | Ph7 | Note workflow fixes — IsEditing reset, form clears, NoteType persistence, scheduled visits/contacts in Upcoming Tasks |
| 4/8  | Bug | Diagnosed productivity threshold bug — hardcoded * 19, stale _incentive after scheduler closes. Fix plan: UnitsPerDay snapshot field, OnIsSchedulerOpenChanged refresh, GetOrCreateAsync wasCreated fix. Historical productivity viewer scoped as future feature. |
| 8/6  | AT | AT Request item entry (slice 1c) — item cards with Name/URL/Cost/Qty on the editor left pane, live subtotal/passthrough/total readout, `ATRequestItemEditorViewModel` write-through with parent total-change callback. Added `Url` field to `ATRequestItem` (+migration). Provider slice 1: `Provider` model, `ProviderType`/`WaiverService` enums, `Settings.SalesTaxRate` + `DefaultPassthroughProviderId`, migration `AddProviderAndSalesTax`, Maine AT Solutions seeded. |
| 8/7  | AT | Provider slice 2: `IProviderService`/`ProviderService`, `ProviderEditorViewModel` (bit-per-checkbox flags + passthrough reveal), `ProvidersViewModel` master-detail, `ProvidersView`, Providers tab grafted into CM sub-nav, DI wired. Settings window gained sales-tax-rate box + default-passthrough-provider dropdown (SelectedValue→int? FK). |
| 8/7  | Assessment | First functional Comprehensive Assessment slice: versioned JSON-backed draft, autosave, eight-domain desktop editor, practical per-question guidance, contributors, dissent, combinable support characteristics, structured needs, caseload ownership, completion gate, supervisor submission state, migration, and 60-day default offset. |



## From note-entry extraction + per-consumer Journal session (2026-07-29)

### Data integrity
- [ ] **Duplicate forms surfacing in billing window.** `EvaluateBillingWindow`
      displayed the same PCP three times for one client (Christian Bobe) — it
      iterates `Forms.Where(gated)` with no dedup, so duplicate/multi-cycle PCP
      rows all match. Not cosmetic: duplicate `Form` rows on a Person could skew
      compliance elsewhere. Trace where the triplicate came from (form generation?
      rollover?) before deduping the display — the display is the symptom, the
      rows are the bug.

### Data loss risk
- [x] **App-close flush for Journal + Scratchpad.** Quitting via the window X
      within 2s of typing loses the Journal tail (debounce hasn't fired). Same
      gap likely affects Scratchpad — its only shutdown save is in
      `ShellViewModel.ReinitializeAsync` (user-switch), not true app-close. Fix:
      `ShellWindow.OnClosing` handler calling `_notesViewModel.Clients.FlushJournalAsync()`
      and the scratchpad save. User-switch is already covered.

### Consolidation left unfinished
- [ ] **NotesLog has two compliance dialogs.** The extracted entry module carries
      its own (entry-form path); the old host-level overlay in `NotesLogView.xaml`
      (`Grid.ColumnSpan="5"`, fixed `Width="460"`, no height bound) still serves the
      context-menu "Mark Note Logged" path via `NotesWindowViewModel`. Both work,
      driven by different VMs, but it's one screen with two dialog definitions.
      Fold the context-menu path onto the module's dialog, delete the host overlay.
- [ ] **User-switch doesn't reset the NotesLog entry module's draft.** Dashboard's
      `Reset()` cascades to its `NoteEntry.Reset()`; NotesLog's copy only gets
      `SetPeople` on reload (which clears selection but not a half-typed narrative).
      Add `NotesLog.NoteEntry.Reset()` to the reinit path.

### Cleanup
- [ ] **Delete dead `NotesWindowViewModel.LoadAsync`.** Superseded by `ReloadAsync`
      (which also does sentinel reset + property notifications). Nothing calls the
      private `LoadAsync` — confirmed via Shell lifecycle. Shadow copy of load logic.
- [ ] **`SendToSupervisor`/`Cancel` event asymmetry in `NotesWindowViewModel`.**
      `Cancel` fires `NoteStatusChanged`; `SendToSupervisor` doesn't. Looks
      backwards. Verify intent, align.

### Architectural debt (widened, not fixed)
- [ ] **Tiered loading — `GetAllPeopleAsync` still fat-loads.** Base row now pulls
      `Bio` AND `Journal` (both `nvarchar(max)`) plus the full Notes/Forms graph via
      dual `.Include` + `.AsSplitQuery()`. Journal's on-demand methods dodge this,
      but the base load is unchanged. Still the `RESOURCE_SEMAPHORE` culprit; now
      with one more unbounded column riding along. Projection-to-summary-DTO remains
      the fix.

## From AT Requests + Provider Directory session (2026-08-07)

### Shipped
- [x] **AT Request item entry (slice 1c).** Editor left pane now has an "Items or
      Services" section: one card per line item with Name, URL, Cost, and Quantity,
      plus an Add Item button and per-card Remove. `ATRequestItemEditorViewModel`
      is the write-through row wrapper; cost/qty edits fire a parent callback that
      re-raises the request totals. Live subtotal / 15% passthrough / total readout
      under the sales-tax box mirrors the form preview.
- [x] **`ATRequestItem.Url`.** Nullable string, stored on the item now, destined for
      the future screenshots-with-clickable-links page 2. NOT rendered on the page-1
      OADS form. URL extraction from retailer pages was scoped and **rejected** —
      scraping is fragile and out of place in a case-management app. (+migration)
- [x] **Provider directory (slices 1-2).** New `Provider` model (structured address,
      `[Flags] WaiverService OfferedServices`, `ProvidesPassthroughService` bool,
      flat passthrough billing strings). `ProviderType`/`WaiverService` enums.
      Providers tab under CM sub-nav - master-detail CRUD, passthrough checkbox
      reveals the three billing fields. Maine AT Solutions seeded as the passthrough
      default. `IProviderService`/`ProviderService`, `ProviderEditorViewModel`,
      `ProvidersViewModel`, `ProvidersView`, DI, migration `AddProviderAndSalesTax`.
- [x] **Settings: sales tax + default passthrough provider.** `Settings.SalesTaxRate`
      (0.055 default, a rate not an amount) and nullable `DefaultPassthroughProviderId`
      FK. Settings window gained a tax-rate box and a provider dropdown
      (`SelectedValue`->int? FK, `SelectedValuePath=Id`).

### Remaining on this feature (slices 3-4)
- [ ] **AT page passthrough dropdown (slice 3).** Dropdown of passthrough providers,
      pre-selected to `Settings.DefaultPassthroughProviderId`. On select, snapshot-copy
      the provider's Name/BillingLocationEis/ProgramContact/BillingContact onto the
      request's `Vendor*` fields (same freeze-at-select semantics as client/CM). Keep
      a nullable `ProviderId` FK on `ATRequest` alongside the snapshot.
- [ ] **Item numbers on the OADS form.** 1, 2, 3... in the form's Item # column via
      WPF `AlternationIndex` + a +1 converter. No data change.
- [x] **Sales-tax freeze (slice 4).** Landed 2026-08-15. Tax is calculated from
      `Settings.SalesTaxRate` and frozen as an amount on the request, with a per-request
      manual override.
- [x] **AT request Save + Publish PDF.** Landed 2026-08-15 as one slice, as intended.
      Open/Save/Publish/Reopen/Export, attestation, publication lock, and the generated
      PDF. See the decision entry of the same date.

### Deferred (needs a later slice)
- [x] **Retain the executed AT request PDF — decided against, 2026-08-15.** Not deferred;
      rejected. A published request cannot change, so the PDF is a pure function of a frozen
      record and storing it would duplicate that record, screenshots and all. See the decision
      entry "The PDF is regenerated, never retained." The one residual risk is exporter code
      changes, noted on the OADS-layout item below.
- [ ] **Match the OADS Authorized Payment Information Form layout.** The generated PDF is a
      Sati document carrying the same information, not a reproduction of the state form.
      Requires the blank form. Layout change only — `ATRequestPdfExporter` is the sole owner.
      NOTE when this lands: PDFs are regenerated rather than retained, so changing the layout
      changes how every historical request re-renders. The figures stay correct; the
      presentation will not match what was submitted at the time. Accepted, but decide
      deliberately rather than discovering it afterwards.
- [ ] **Decide whether an electronic-signature standard applies to AT requests.** Sati records
      an attestation, and says so on the document. Whether OADS requires a signature meeting a
      specific standard is a question for counsel and OADS, not for the repository.
- [ ] **Client<->provider association.** The AT dropdown can only list *all* passthrough
      providers - Sati has no link from a consumer to *their* home/community-support
      provider, so it can't pre-select "this client's agency." That association is its
      own model + slice. `OfferedServices` (the four waiver flags) is inert until it lands.
- [ ] **AT Assessments as a waiver service.** Maine AT Solutions actually offers it;
      left out of `WaiverService` until something consumes the offering data.
- [ ] **Providers tab governance.** Provider directory is agency-level shared reference
      data but currently lives under CM sub-nav. In the multi-user future it should be
      admin-curated to prevent duplicate provider rows across CMs.

### Tech debt confirmed against disk this session
- [ ] **Dead files still present:** `ViewModels/SchedulerViewModel.cs` and
      `Models/WorkdayTile.cs` - delete together. (`Models/Event.cs` already removed.)
- [ ] **Filename typo:** `Data/Billing/IdeService.cs` should be `EdiService.cs`
      (class name is fine; only the file is misnamed).

## From MVVM / SOLID architecture audit (2026-08-07)

The current architecture has a sound base: ViewModels do not access EF directly,
services generally use injected interfaces and per-method `IDbContextFactory` contexts,
the composition root is centralized in `App.xaml.cs`, and important compliance behavior
already lives in `Person` and `Form`. The items below are targeted refactors, not grounds
for a rewrite.

### P1 — security and active correctness

- [ ] **Enforce assessment authorship on every write at the service boundary.**
      `ComprehensiveAssessmentService.SaveDocumentAsync` accepts only an assessment ID
      and document, so it can update another author's editable draft if invoked outside
      the current UI path. Require a trusted authenticated actor/capability and verify
      ownership, assignment, organization, and workflow status before saving. Apply the
      same rule to submission instead of treating a caller-supplied user ID as identity.
- [x] **Enforce supervisory authorization inside `SupervisorService`.** The historical service
      trusted a supplied supervisor ID and UI scope switch. Current local/API paths derive and
      revalidate the actor, permission, agency, and supervisory scope. The September 14 follow-up
      additionally makes an exception note-specific, revision-checked, explicitly attested, and
      limited to every exact current blocker ID.
- [x] **Fix the new-account `AssignedAgency` null path.** `NewUserViewModel.CreateUser`
      dereferences `AssignedAgency.Id`, but `AssignedAgency` is never initialized and
      `NewUserWindow` has no agency selector. Add the intended agency source/selection,
      validate it before user creation, and cover the flow with a test. This is the
      nullable warning currently emitted at `NewUserViewModel.cs:70`.

### P2 — MVVM boundaries and maintainability

- [ ] **Remove the Comprehensive Assessment service locator.**
      `ComprehensiveAssessmentWorkspace` constructs its own ViewModel through
      `Application.Current.Services`. Create the workspace/ViewModel through the
      composition root or a typed injected factory so dependencies are explicit and the
      workspace is testable.
- [ ] **Move concrete dialogs and WPF application access out of ViewModels.** Several
      ViewModels directly call `MessageBox.Show`, `Application.Current`, `ShowDialog`, or
      depend on a concrete `UserMessageDialog`. Replace these with narrow interaction,
      navigation, notification, and dispatcher/scheduler abstractions—or events handled
      by the View. Keep purely visual window ownership in code-behind.
- [ ] **Make View event subscriptions detachable.** `ClientsView.OnDataContextChanged`
      attaches an anonymous `ComplianceReviewRequested` handler without removing it from
      the previous ViewModel. Use a named handler or
      an explicit attach/detach lifecycle. On unload, also detach
      `ComprehensiveAssessmentWorkspace` from its parent ViewModel. Verify that view
      recreation or user switching cannot produce duplicate dialogs or retained views.
- [ ] **Split `CaseManagerDashboardViewModel` by feature responsibility.** It is roughly
      900 lines with about sixteen constructor dependencies and currently coordinates
      notes, forms, upcoming work, incentives, clients, statistics, reviews, providers,
      calendar, and compliance effects. Retain it as a thin dashboard/module coordinator
      while moving feature state and commands into focused child ViewModels/application
      services.
- [ ] **Split `NewClientViewModel` by feature responsibility.** It is roughly 830 lines
      and owns client CRUD/editing, notes loading, forms, reviews, appointments,
      healthcare reference data, and journal autosave. Extract focused client-profile,
      journal, appointment, and compliance/document components while preserving the
      current single Overview experience.
- [ ] **Extract a versioned Comprehensive Assessment definition catalog.**
      `ComprehensiveAssessmentViewModel.BuildSections` currently owns question text,
      guidance, support applicability, validation, navigation, mapping, persistence, and
      autosave. Move the content/schema into a dedicated, versioned definition provider
      so questions can grow or branch without modifying the editor and so existing JSON
      answers remain reproducible against the definition version that created them.
- [x] **Add an automated domain/workflow test project.** Prioritize
      `EvaluateComplianceGate`, `EvaluateBillingWindow`, midnight and back-entry rules,
      permanent unbillability, override separation, assessment support exclusions,
      completion rules, ownership, serialization compatibility, and workflow transitions.
      No test project was found during this audit.

### P3 — cleanup and consistency

- [ ] **Inject `IPasswordHasher` into `AuthService`.** It is already registered, but
      `AuthService.AuthenticateAsync` constructs `PasswordHasher` directly.
- [ ] **Align DI registrations with effective lifetimes.** `ScratchpadViewModel`,
      `NewClientViewModel`, `UserManagementViewModel`, and `PendingApprovalsViewModel` are
      registered transient but captured by singleton parents. Decide whether each is
      intentionally session-long; register it accordingly or create it through an
      explicit scope/factory.
- [ ] **Delete the hidden legacy client-entry panel and stale state.** `ClientsView.xaml`
      still contains the old entry form and chevron in two zero-width columns, while
      `NewClientViewModel` retains `IsEntryPanelOpen`, `ToggleEntryPanel`, and legacy
      naming. Remove the duplicate markup/commands after confirming the inline Overview
      editor covers add, edit, cancel, and delete.
- [x] **Resolve the disabled cycle-form feature switch.** Done 2026-09-01: the flag is removed
      and generation runs again. It was suppressing a race that the unique index on
      `dbo.Forms (PersonId, Type, DueDate)` now decides, and leaving it off was quietly starving
      the caseload of future cycle forms. See the duplicate-form entry at the top of this file.
- [ ] **Make the README architectural claim accurate.** It currently says ViewModels
      have no knowledge of Views and window creation uses factories throughout. Update it
      after the boundary work above, or describe the remaining pragmatic exceptions until
      they are removed.

### Audit verification

- [x] Rebuilt the current working tree successfully to an isolated output directory while
      the running Sati process held the normal output DLL. The rebuild completed with no
      errors and two distinct warnings: the `AssignedAgency` nullable dereference and the
      constant-false unreachable code described above.

### Deferred from the service-day work (2026-08-14)

- [ ] **Surface service start times in the remaining note lists.** The focused calendar day now
      shows the service-time range through the shared `ServiceTimeline` rule. `NotesLogView` and
      `ClientsView`'s note grid still show only date and minutes; a time column there would let a
      case manager spot a gap or a clash without opening the entry panel.
- [ ] **Extend the overlap rule to the other note-creating paths.** `NewClientViewModel` and any
      other flow that calls `INoteService.AddNoteAsync` directly writes a note with no start time.
      Those notes claim no time and conflict with nothing, which is correct but means the day bar
      does not show them. Decide whether those flows should collect a start time.
- [x] **Decide whether Scheduled notes should reserve time softly.** Resolved 2026-09-05 for future
      plans: estimated minutes remain, actual start time stays empty, and starting work edits the
      Scheduled row in place and selects the earliest open window. Historical or same-day Scheduled
      rows that already carry a start still represent a claimed window.

### Deferred from the API security audit (2026-08-14)

See `API_SECURITY_AUDIT.md` for the full findings. Items reviewed and consciously left alone:

- [ ] **Decide the sign-in lockout policy deliberately.** `LoginAttemptGuard` allows 12 attempts per
      username per minute, so anyone who knows a username can hold that person out of the system.
      Every lockout design trades this against credential stuffing; this one has not been chosen on
      the record. Options include per-IP limits alongside per-username, exponential backoff instead
      of a hard window, or an unlock path for a supervisor.
- [ ] **Give the sign-in guard shared state before running more than one API instance.**
      `LoginAttemptGuard` is per-process, so the effective attempt limit multiplies by the instance
      count. Acceptable for single-instance Demo; not for a scaled deployment.
- [ ] **Make person lookups consistently use `TenantAccess.OwnsPersonAsync`.** `POST /at-requests`,
      `GET /people/{personId}/reviews`, and `GET /people/{personId}/appointments/latest` resolve the
      person by id and authorize on the owning user rather than also asserting
      `person.AgencyId == actor.AgencyId`. Cross-agency access is blocked today because
      `CanAccessUserAsync` requires the target user to be in the actor's agency, so this is
      consistency and defense in depth rather than an open hole. Needs a test covering a person row
      whose agency disagrees with its owner's.

### Documentation and naming cleanup (2026-08-15)

- [ ] **Rename `Data/Cloud/CloudUnavailableServices.cs`.** The filename is a historical misnomer.
      It now contains twelve fully migrated HTTP service implementations (`CloudUserService`,
      `CloudSupervisorService`, `CloudBillingService`, and others), not unavailable stubs. Anyone
      reading the file list will draw the wrong conclusion about how much of Demo is migrated.
- [x] **Settle the company name's casing.** Resolved 2026-08-15: `SatiLogica` is the formal
      rendering everywhere in code, documentation, and user-visible strings. `Satilogica` remains
      acceptable in informal prose. Azure resource names stay lowercase because DNS requires it.
      Applied across the `<Company>` property, installer and uninstaller, Start Menu folder,
      registry publisher, `%LOCALAPPDATA%` paths, and scripts. No upgrade story was needed:
      Windows paths and registry keys are case-insensitive, so existing installs and stored data
      under `Satilogica\` resolve unchanged.
- [ ] **Introduce the program names only as each ships.** `SatiLogica` is the platform;
      `Sati` is the case-management program. `Karuna` (service-provider documentation) and
      `Upekkha` (OADS-facing waiver management) are roadmap names for work that does not exist
      yet and should stay internal until the quarter before each ships.
- [ ] **Reconcile the live Demo API version.** `DATABASE_ENVIRONMENTS.md` last recorded the deployed
      API at 1.2.8 while the packaged client is 1.2.17. Deploy and verify a matched pair before
      collecting final company-demo evidence, then update the release-history note.

### Organization identity and the Karuna handoff (2026-08-15)

Design recorded in `DECISIONS.md`, "Provider directory entries are local knowledge about a shared
organization". The identifier capture landed on 2026-08-15 because it is the only part that cannot
be added retroactively. Everything below waits for Karuna.

- [x] **Capture durable identifiers on provider directory entries.** `Provider.Npi` and
      `Provider.MaineCareProviderId`, NPI check-digit validated, unique per agency via filtered
      indexes, enforced in both the API and the transitional local service.
- [ ] **Introduce the Organization registry.** Platform-wide canonical identity — legal name and
      external identifiers only. Add `Provider.OrganizationId` as a nullable link in the same
      migration. Deliberately not created yet: do not add a column pointing at a table that
      does not exist.
- [ ] **Build the match-and-link flow for onboarding.** Exact match on identifier first, then a
      reviewed candidate list for name/address near-matches. Linking must never merge or delete a
      directory entry, and must never repoint an existing foreign key.
- [ ] **Model `AgencyRelationship`.** Which case-management agencies have an active passthrough
      relationship with which organization. Without it, an organization going live would appear in
      every agency's passthrough picker — both wrong billing options and a cross-tenant
      disclosure.
- [ ] **Add the published passthrough contact set,** maintained by the organization's own tenant.
      An explicit outward-facing payload, never a projection of internal contact records. Validate
      it for completeness the same way the local form is validated: one bad publish would degrade
      every linked agency's billing contacts at once.
- [ ] **Add contact resolution with local override.** Published wins by default once linked; local
      values are demoted rather than deleted so an agency can re-assert its own named contact in
      one click.
- [ ] **Notify and audit the swap.** A notice to each affected agency when it lands, a quiet
      indication at point of use, and audit events on both sides — these contacts feed a financial
      document.
- [ ] **Flag stale drafts rather than re-snapshotting.** An AT request drafted before a swap and
      submitted after must report which vendor fields changed and let the user decide, following
      the note conflict-reconcile idiom. Never silently rewrite a financial document.
- [ ] **Consider backfilling identifiers for existing directory entries.** Rows created before
      2026-08-15 have none. A one-time prompt when a provider is next edited would close the gap
      without a bulk data exercise.

## Note pipeline — outstanding after the 2026-08-17 review

- [ ] **Give an approved note an amendment path.** Approved is terminal for every actor, so a
      supervisor who approves in error has no remedy even before a claim line exists. The right
      shape is an immutable approved version plus a linked amending note, not an un-approve that
      rewrites the record. Touches the claim-line linkage and the 837P path, which is why it was
      not folded into the workflow-table work. See `DECISIONS.md`.
- [ ] **Audit the abandonment sweep.** Neither `NoteService.UpdateAbandonedNotesAsync` nor
      `POST /notes/abandon-overdue` records an audit event, so a status change the system makes on
      its own is the one transition with no trail. Bulk writes need a summary event rather than
      one per note.
- [ ] **Make the overdue sweep respect the concurrency token.** The API route uses
      `ExecuteUpdateAsync`, which bypasses `Revision`, so a note being edited at that moment can be
      abandoned underneath its author. Bounded today by the `Pending`-only filter, but it is the
      one write in the pipeline that ignores optimistic concurrency.
- [ ] **Close the create-time gap on overlapping service time.** `AddNoteAsync` checks for an
      overlapping block and then saves in a separate step, with no transaction spanning the two.
      Two concurrent saves can both pass the check. The unique index that protects claim lines has
      no equivalent here; a database-level exclusion constraint would be the durable fix.
- [ ] **Give the local tenant rules one owner.** `Data/LocalTenantAccess.cs` mirrors
      `Sati.Api.Security.TenantAccess` by hand because the two query different entity types against
      different contexts. Two hand-written copies of a scope rule is exactly what the platform rule
      about single ownership warns against; it is tolerable only while the desktop keeps a local
      EF path at all.
- [ ] **Decide whether the desktop may assume Eastern time.** The desktop review path uses
      `DateTime.Today` where the API now uses the agency clock. Correct on a Maine workstation,
      wrong anywhere else.

## Hosted Demo migration deployment — found live on 2026-08-17

- [x] **Detect a database behind the model.** `SchemaDriftHealthCheck` compares the API model's
      tables and columns against the database and fails `/health/ready` naming what is missing,
      instead of letting the gap surface as a 500 from whichever feature touches the new column
      first.
- [ ] **Decide how SatiDemo actually receives migrations.** Nothing advances it today. The desktop
      runs `Database.Migrate()` (`App.xaml.cs:238`) but only when connected straight to SQL, and in
      Demo it goes through the API over HTTP; `Sati.Api` never migrates; `scripts/Publish-Demo.ps1`
      has no database step. A release that adds a column therefore ships code the database cannot
      satisfy. On 2026-08-17 that took out `GET /providers` — and with it AT request creation —
      and `POST /incidents`, so the telemetry channel could not report the outage either.
      The detector above makes this visible; it does not fix it.
- [ ] **Reconcile migration history with reality on the long-lived databases.** SatiDemo and
      SatiProduction have acquired columns outside the chain, so `__EFMigrationsHistory` and the
      actual schema disagree in both directions. EF's idempotent script guards only on history and
      fails with SQL 2705 on a column that exists without its history row. Until the two are
      reconciled, applying migrations to those databases needs existence-guarded scripts rather
      than the generated one.

## Journal reminders — outstanding after the 2026-08-18 change

- [x] **Deploy the API so the reminder route exists.** Done by the 1.2.21 deployment and confirmed
  2026-08-23 by unauthenticated probe: `people/{id}/journal/entries`, `people/{id}/ssn`,
  `people/{id}/forms.pdf`, and `people/{id}/agency-release.pdf` now all answer 401, where the last
  three answered 404 on 2026-08-19. The original note follows for the record.
  The hosted Demo API was release 1.2.17 on
  2026-08-18, which predates `POST /people/{personId}/journal/entries`. Until it is published from
  `Sati.Api/Properties/PublishProfiles/sati-demo-api-satilogica - Zip Deploy.pubxml`, every Demo
  reminder takes the transitional whole-journal fallback and the client page says so. Verify with an
  unauthenticated POST to that route: 401 means the route is present, 404 means the server is still
  behind.
  **The same deployment now gates four more routes.** Confirmed live on 2026-08-19 by unauthenticated
  probe — `people/{id}/notes` and `people/{id}/contacts` answer 401 while `people/{id}/ssn`,
  `people/{id}/forms.pdf`, and `people/{id}/agency-release.pdf` answer 404. In the DHHS wizard that
  404 surfaces as "the record was not found or is outside your caseload", which points the case
  manager at a caseload problem that does not exist. This is the third time a behind-server has been
  diagnosed as something else; see the startup version-comparison item below, which would replace
  per-route handling with one check.
- [x] **Remove the whole-journal fallback once nothing predates the route.** Done 2026-08-23 once
  the probe above showed the route live everywhere it needed to be. The 404 `catch`,
  `JournalReminderResult.UsedLegacyJournalWrite`, the client-page warning band text it drove, and
  `Sati.Tests/JournalReminderFallbackTests.cs` are removed; `ApplyExternalJournal` now clears a
  stale warning instead of setting one. See `DECISIONS.md`.
- [x] **Detect a behind-server generally.** Built 2026-08-19 and extended 2026-08-22. **Comparing the release number would
  not have worked** — on the day this was written the hosted API and the client both reported
  1.2.17 while the server was missing five routes, because a release is numbered when it is cut and
  not when a route is added. The comparison is therefore over the route and persistence-contract
  manifest: `ApiSurface` in `Sati.Contracts.V1` holds the generated route list, named contract-shape
  revisions, and a fingerprint of both,
  `/health/version` reports the fingerprint (never the list — that is a map of the attack surface),
  and `IApiCompatibilityService` compares once at sign-in and raises a "SERVER OUT OF DATE" banner
  with the cause. `ApiSurfaceTests` fails the build if the route manifest drifts from the API's real
  endpoint table and proves that a contract-shape change alters the fingerprint. This prevents a
  newer client from silently sending profile fields an older server would ignore. The check never throws and never blocks
  sign-in: an unreachable server is a network problem other screens report better.
- [ ] **Audit other `GetAsync<T>` calls for legitimately empty bodies.** `GetJournalAsync` threw on
  any client whose journal was never written, because `GetAsync<string?>` treats a null result as an
  empty response. Fixed there with `GetStringOrNullAsync`; other nullable-scalar routes may carry
  the same latent fault.
- [x] **Distinguish journal reminders from dated calendar reminders.** An undated Reminder remains a
  stamped journal entry and is not duplicated. A future-dated Reminder is stored once as a
  non-billable Scheduled note so the calendar, note history, and upcoming-event views can find it.

## DHHS form fill — remaining verification and profile work

The official-form filler, encrypted cloud SSN envelope, audited API routes, local
and cloud service implementations, migrations, and desktop workflow are now built.
See `DECISIONS.md`, "An official DHHS form is filled, never redrawn" and "An SSN is
cloud-only".

- **Profile gap the forms exposed.** `Person` has no email. The Release form's
  optional combined telephone/email box receives the phone number when present;
  email remains blank for hand-completion until the profile has an appropriate
  email field.
- **Field rendering is unverified against DHHS.** The byte comparison proves the
  page is unchanged; it does not prove a DHHS intake worker accepts the field
  appearance. The forms set `/NeedAppearances` so the viewer rebuilds appearance
  streams. Worth one printed submission before this is used in earnest.

## Production behind the API, without losing local Production (2026-08-18)

Direction set by Josh: "move local Production behind the API" means **adding
API-backed access to the Production database while preserving both operating
modes**, not retiring the local one. The desktop must continue to support Demo and
Production as explicitly selected data sources.

Requirements for that work:

- Explicit environment selection, extending the existing bootstrap chooser and the
  validated hard-coded environment mapping in `DATABASE_ENVIRONMENTS.md`.
- Separate credentials, configuration, service identities, and Key Vault keys per
  environment.
- Authorization checks on the API-backed Production path equal to Demo's —
  `TenantAccess` plus `ValidatedActorFilter`, not a relaxed variant.
- Conspicuous UI labeling, as the Demo indicator does today, so the operating mode
  is never ambiguous on screen.
- Safeguards against cross-environment reads and writes. `DatabaseIdentityValidator`
  and `dbo.SatiDatabaseIdentity` already gate this at connection time; per-environment
  Key Vault keys extend it to the data itself, since one environment's ciphertext is
  inert against the other's vault.

**Where plaintext SSNs exist, by mode** (see `DECISIONS.md`, "An SSN is cloud-only"):

| Mode | Plaintext SSN |
|---|---|
| Demo (API-backed) | Only in API process memory during entry and form fill, and inside the generated PDF. Demo data is synthetic. |
| Production via local EF (today) | None. SSN is cloud-only; the column is never populated or read on this path. |
| Production via API (planned) | Same as Demo: API process memory, and the generated PDF. |

Never at rest outside ciphertext, never in a DTO, an EF entity exposed to a client,
a log, telemetry, an exception, a cache, or a backup.

**Document generation splits by where the protected data is, not by document.**
Superseded by Josh's 2026-08-18 direction that local Production keep generation on
the workstation: `DhhsFormFiller` lives in the shared `Sati.Forms` library and runs
in whichever process holds the data. On the cloud path it runs server-side, because
that is where the decryptable SSN is and it must not travel. On the local path it
runs on the workstation with no network and no SSN at all. One implementation of the
stamping, two callers — a second copy would be the duplication CLAUDE.md forbids.
The AT request PDF carries no protected field and stays where it is.

**The generated PDF is itself plaintext.** The form has an SSN box, so the finished
document contains the number in the clear by design. Encryption protects the
database; it cannot protect the artifact the fill produces. Once a case manager
saves, prints, emails, or uploads that PDF, the number is loose in whatever handled
it. The controls that matter there are the BitLocker requirement in
`OPERATIONS.md`, agency-approved storage locations, and the audit event on
generation — not anything in the crypto.

### DHHS form work status

Everything below the UI is built and tested as of 2026-08-18: the encrypted columns
and the `AddEncryptedSsn` migration, the audited `POST /people/{personId}/forms.pdf`,
the SSN read and write routes, log-redaction enforcement, and both
`IDhhsFormService` implementations.

- **Migration applied 2026-08-19.** `AddEncryptedSsn` and the remaining queued schema
  migrations were applied to `SatiDemo`; local `SatiProduction` was already current.
  Controlled migration deployment remains manual; see the hosted-Demo item above.
- **The Demo Key Vault key was provisioned 2026-08-20.** The Demo API now receives
  the versionless `Ssn__KeyUri` for `ssn-demo` in the purge-protected
  `sati-demo-kv-satilogica` vault. Its system-assigned identity has only `wrapKey`
  and `unwrapKey`. Production still requires a separate key before the Production
  API path stores SSNs; never reuse the Demo vault or key there.
- **Synthetic Demo SSNs were seeded 2026-08-20.** The Admin-only operational route
  encrypted deterministic synthetic values for all 177 agency People through the
  Demo Key Vault and recorded `person.ssn-updated` per Person. The route remains
  effective only when startup validates exactly `SatiDemo` / `Demo`; ordinary SSN
  routes remain own-caseload only. `scripts/Seed-DemoSsns.ps1` is the repeatable
  wrapper for a future approved Demo reset. Do not write SSN columns directly in
  Azure SQL.
- **Demo users and agencies carry no synthetic representative information,** so every
  Demo fill currently reports the representative boxes as needing hand-completion.
- **Desktop UI completed 2026-08-19.** The selected consumer now has a `DHHS Forms`
  workspace covering both official forms, grouped consumer-directed selections,
  masked SSN status/update on the cloud path, local-Production explanation, PDF save,
  missing-field warnings, automation names, keyboard reachability, live status text,
  and selection clearing when the consumer changes. Signatures and signing dates are
  intentionally left to the fillable PDF rather than treated as ordinary data entry.
- **Local Production always prints a blank SSN box,** because SSNs are cloud-only and
  that path has no key. Reported through `DhhsFormResult.BlankFields` rather than
  left for the case manager to notice on paper. Confirmed with Josh 2026-08-18.
- **`SsnMask.IsWellFormed` is a shape check, not proof of ownership.** Nothing local
  can establish that a number belongs to the consumer.

## Agency releases and transportation documents (2026-08-19)

- **Agency release completed.** The selected-consumer workspace, shared validation contract,
  local/cloud service seam, audited no-store API route, two-page Sati PDF, staff-attestation
  confirmation, and automated desktop/API tests are in place. Consumer signatures are deliberately
  left for the document rather than represented as ordinary data entry.
- **Transportation source forms analyzed; implementation is next.** The ModivCare Standing Order
  and LogistiCare Single Trip PDFs supplied on 2026-08-19 are both one-page, flat PDFs with zero
  AcroForm fields or widgets. They cannot use the DHHS field-filling path.
- **Preserve the official/vendor page.** Build a coordinate-overlay definition for each exact source
  revision and prove that the original page content stream remains unchanged, rather than redrawing
  a lookalike. Put both behind an `ITransportationFormService` local/cloud seam, derive consumer,
  agency, and logged-in requestor identity on the authoritative side, and audit generation. Before
  operational use, print one sample of each and confirm acceptance plus the required MaineCare
  billing-section interpretation with the transportation broker.

## Local schema updates — outstanding after the 2026-08-19 safety net

See `DECISIONS.md`, "The desktop backs up before it migrates a database with records
in it".

- **No audit event for a schema change.** The startup migration runs before sign-in,
  so there is no actor and `LocalAuditTrail.Record` cannot be called. The backup file
  is the only trace. Options: attribute to a system actor, or defer the event until
  the first sign-in after an applied migration and record it then.
- **Backups are never pruned.** Every migration on a database with records writes a
  new `.bak` under `%LOCALAPPDATA%\Sati\schema-backups` and nothing removes old ones.
  Fine at the current rate; wrong once several people are running it for a year.
- **The backup is not verified.** `BACKUP DATABASE` returning without error is taken
  as success. A `RESTORE VERIFYONLY` would prove it is readable before the migration
  proceeds, which is the entire point of taking it.
- **Untested against a real diverged database.** The diverged-history path is covered
  by a fake that throws the right exception. Nothing has exercised it against an
  actual database whose `__EFMigrationsHistory` disagrees with its schema — and the
  other Windows login's `SatiProduction` is the most likely place that is true.

## SSN panel — outstanding after the 2026-08-19 profile work

- **`DhhsFormsViewModel` still has its own SSN code.** `SsnPanelViewModel` is now the
  shared owner and the consumer profile uses it, but the forms workspace was not
  refactored onto it in the same pass. Two implementations of "how do we show and
  store an SSN" is the duplication this class was created to remove; finish the move
  and delete the older copy. The forms workspace's `SsnStorageExplanation` is already
  stale — it still says local Production does not store numbers.
- **A revealed number does not time out.** It clears when the consumer changes, when
  Hide is pressed, and on any failure, but a panel left open on a locked-away
  workstation keeps showing it. A short auto-hide would close that.
- **The reveal is not rate limited.** Nothing stops a bulk read of every consumer's
  number one profile at a time. Each read is audited, so it is visible after the
  fact, but nothing makes it slow or noisy while it happens.

## Brochure source pipeline — outstanding after the 2026-08-22 recovery

The brochure now builds from `marketing/brochure/brochure.html`; see `DECISIONS.md`. What the
recovery did not settle:

- The build is verified by eye. There is no check that a slide's copy still fits its panel, and
  SVG text does not wrap, so a lengthened line silently overruns. A width assertion per text run
  would catch it.
- `scripts/build-brochure.ps1` depends on a Chromium browser being installed and on Segoe UI and
  Georgia being present. Both hold on the current build machine and neither is asserted.
- The remaining ten slides still use the original screenshot crops, several of which are stale
  relative to release 1.2.20. Reshooting them is separate work.
- The recovered source reproduces the original slide 1 layout apart from the leaf. Nothing has
  been reviewed for message, ordering, or claim accuracy, and `REGULATORY_CONCERNS.md` has not
  been applied to the brochure copy.

The pre-recovery ReportLab PDF is deliberately not retained. The HTML source is the baseline;
there is no earlier version to fall back to, and none is wanted.

## Brochure restructure — outstanding after the 2026-08-22 rewrite

The deck is 14 slides. The intended five movements were interrogative intro (1-3), case-manager workflow
(4-6), billing gates (7-9), admin, security and platform (10-12), and Carika plus direction
(13-14). Every headline is a question; slide 14 answers them. Outstanding:

- Slides 11, 12 and 13 carry dashed placeholders, not artwork. Slide 11 wants the environment
  chooser or the Demo-indicated sign-in, slide 12 wants an architecture diagram, slide 13 wants
  Carika showing a profile beside a transcript awaiting review. Each label says what to supply.
- Slide 14 now carries the roadmap and the close on one page. If the closer needs room to
  breathe, split it into a fifteenth slide.
- The layout guard in `brochure.html` (`checkLayout()`, auto-run into the browser console) is
  not enforced by `scripts/build-brochure.ps1`. A silent overrun would still ship.
- Slide 13's claims are scoped to the current Carika slice: profile display and note drafting,
  imported audio rather than live capture, separately provisioned models. Re-check that slide
  against `Carika/README.md` whenever the slice moves.
- The interrogative pass rewrote every headline. Copy has not been reviewed against
  `REGULATORY_CONCERNS.md`, and slide 9's "lost units were lost visits" asserts a relationship
  between billing data and client contact that nothing in the deck substantiates.

## Brochure slide order — parked 2026-08-23

Guided forms was moved from position 5 to position 10 and everything between shifted up one. The
current order is: cover, day in view, consumer record, note capture, review tracking, supervisor
workflow, billing gates, productivity, audit and administration, guided forms, security, platform,
Carika, close.

This crosses two of the movements the deck was structured around. Guided forms is case-manager
work sitting inside the admin, security and platform run, and the "do the work without the detour"
construction introduced on slide 4 no longer has its two intended partners adjacent to it. The
move was made deliberately and marked as temporary; either the movements or the frame needs
revisiting before the deck is final.

`checkLayout()` in `brochure.html` now also verifies that each slide's position, `data-slide`
attribute and footer number agree, and that element ids are unique across slides.

## Notes page consolidation — outstanding after the 2026-08-23 change

The notes log's Note Detail panel is gone; the shared entry panel now shows a selected note in a
locked View Note mode with a padlock toggle into Edit Note, and filters moved to a band above the
grid. Left open:

- **A note is only checked for staleness when it is unlocked.** `VerifyLoadedNoteIsCurrentAsync`
  covers the case that matters — finding out before editing rather than after — but a note left
  open in View Note for an hour still shows the copy it was loaded with, and nothing announces a
  supervisor's return while it sits there. Deliberate: polling every open panel to catch an
  uncommon event was rejected (see `DECISIONS.md`). If notes start being changed underneath
  case managers often enough to matter, the next step is a push or a refresh-on-window-activation,
  not a timer.

- **The panel has never been opened in a running client since the change.** `NotePanelRenderTests`
  now loads the real views against the real resource dictionary and asserts the resulting element
  state, which covers the parts a human would otherwise have had to check — but not how any of it
  looks. The filter band's `WrapPanel` reflow points and the note panel's column width in
  particular are guesses that only a person at the screen can judge.

Closed since that entry was written:

- ~~Nothing warns when the note being viewed has since changed on the server.~~ Unlocking now
  re-reads the note and compares `Revision`, behind the unlock so the panel never freezes on it.
  An untouched panel reloads to the current version; a panel with unsaved typing is warned and left
  alone; a removed note and a failed read each say so. The save-time concurrency check is unchanged
  and still authoritative.
- ~~No entry point back to a blank New Note from the dashboard.~~ `StartNewNoteCommand` on the
  module is offered as an always-visible New Note button in the panel header and as Escape, bound
  both on the module and on each host page. It keeps the selected client, which matters most on the
  dashboard, where that property scopes the whole page. The notes log's Deselect Note button was
  removed as a leftover of the two-panel design.
- ~~The dashboard host does not ask before replacing a draft.~~ Both hosts now route their
  double-click through `NoteEntryViewModel.OpenForEdit`, which owns the unlock-or-load-or-ask
  decision. Neither host repeats it, so they cannot drift apart again.
- ~~Layout is verified structurally, not visually.~~ `NotePanelRenderTests` loads `NotesLogView`
  and `NoteEntryView` on a real STA thread with the application's resources and asserts runtime
  grid placement, that a locked narrative is `IsReadOnly` while staying enabled and focusable,
  that pickers and radio buttons are disabled, that the save button is collapsed, and that the
  attendee checkboxes inside the `ItemsControl` lock too. Each assertion was confirmed to fail
  against a deliberately broken view.
- ~~Notes filter inputs relied on mismatched implicit heights and margins.~~ ComboBoxes, search,
  date pickers, the units summary, and attention actions now share an explicit 36-pixel height;
  the WPF render test measures their actual heights and row baselines at runtime.

### One WPF Application per test process

`WpfUiHarness` now owns the assembly's only `Application` and the STA thread it runs on. WPF's
one-per-AppDomain flag is never cleared — not even by `Application.Shutdown()` — so a second
creator does not merely conflict, it fails permanently, and *which* test fails depends on run
order. `StabilizationTests.ParameterlessFeatureViewsCanOpenRenderAndCloseOnAnStaThread` used to
build its own; it now borrows the harness and installs its host through `RunWithHost`. Any future
test that needs a real view must go through the harness rather than constructing an `Application`.

## Provider hierarchy and consumer provider list — designed 2026-08-28

Design recorded in `DECISIONS.md`: "Provider affiliation is one parent link, not three typed tiers"
and "A consumer's provider list stores the link, never the resolved chain". Nothing below is built.

This supersedes the older **Client↔provider association** item from the 2026-08-07 AT session —
that link is slice 2 here, and is deliberately not medical-only.

### Slice 1 — Directory hierarchy ✅ (2026-08-28)

- [x] Add `Provider.MedicalKind` (`Individual | Practice | Network`, nullable; required when
      `Type == Healthcare`) and `Provider.ParentProviderId` self-reference. `OnDelete(Restrict)`
      plus an explicit refusal in both services that names the affiliated entries, rather than
      `SetNull` silently promoting a subtree to top level.
- [x] Add `ProviderAffiliation` to `Sati.Contracts.V1` as the single owner of the tier rule, the
      ancestor-loop rejection, the depth bound (`MaxDepth = 10`), the chain resolution both clients
      render, the parent-picker filter, and the delete-refusal text.
- [x] Migration `20260828180603_AddProviderAffiliation`. Both columns nullable with no backfill:
      every existing row is legitimately unaffiliated, and guessing a tier from a name is the fuzzy
      matching the durable identifiers exist to avoid.
- [x] `ProviderEditorViewModel`: designation combo, parent picker filtered to legal parents only,
      affiliation revealed only for healthcare entries, a derived read-only chain, and an
      explanation when no legal parent exists. Changing tier or leaving medical clears a selection
      that is no longer legal instead of leaving it to fail at save.
- [x] `ProviderDto` / `SaveProviderRequest` gain optional `MedicalKind` and `ParentProviderId`,
      following the optional-parameter pattern `Npi` already uses. API validates the same rule
      through the same contracts type; `ProviderDto.Type` is unchanged.
- [x] `ProvidersViewModel` gained a `SaveError` surface. The directory's refusals — duplicate
      identifier, illegal affiliation, delete with entries beneath — previously had nowhere to go
      and were thrown into an unobserved task. A refused save keeps the editor open with the
      entered values intact.
- [x] Tests: 31 rule, 9 local-service, 13 view-model, 13 API. The cross-agency, loop, and
      delete-guard tests were each confirmed to fail against the guard removed, including one
      view-model test that was rewritten after it turned out to be passing on the tier rule rather
      than the loop check it claimed to cover.

**Outstanding from this slice**

- [ ] `ProvidersView` has no *structural* render test. `StabilizationTests`'
      `ParameterlessFeatureViewsCanOpenRenderAndCloseOnAnStaThread` already loads, measures, and
      arranges it, so the XAML is known to parse and its resources to resolve — but it runs with no
      DataContext, so no binding path is exercised. `NotePanelRenderTests` is the pattern for the
      affiliation reveal, the filtered picker, and the error banner.
- [ ] A long-lived database whose `__EFMigrationsHistory` has diverged needs the same treatment
      `Apply-ProviderDurableIdentifiersMigration.ps1` gave the identifier columns. The desktop
      startup path backs up and migrates normally; only a diverged database needs the runner.
- [ ] A clinician's affiliation is not versioned. Moving a physician between practices rewrites
      what every consumer profile displays, with no record of the previous affiliation. Correct for
      live profile data, and the reason documents must snapshot in slice 4 — but if "who was her
      practice in March" is ever asked of the directory itself, this is where it gets answered.

### Slice 2 — Consumer provider list ✅ (2026-08-28)

- [x] `PersonProvider` model: `ProviderId`, role, `IsPrimaryCare`, start/end dates, release-on-file,
      display order. No cap. **No active flag** — `EndDate` is the only fact that says a link is
      current; see `DECISIONS.md`. A free-text note was deliberately deferred rather than dropped.
- [x] At-most-one current primary care provider per consumer, and one current link per provider —
      both in `ConsumerProviderRules`, both backed by filtered unique indexes, neither a UI
      convention. Both filter on `EndDate IS NULL`, so a consumer may return to a provider they left.
- [x] `IConsumerProviderService` with the transitional local EF implementation and a `Cloud*` HTTP
      one. Four routes gated through `TenantAccess.OwnsPersonAsync`, inventoried in
      `API_AUTHORIZATION.md`, declared in `ApiSurface`.
- [x] The interface takes `personId` alongside `linkId` on end and remove. Reading the consumer off
      the row would let a caller-supplied link id select the scope it is then validated against.
- [x] Profile UI: picker leading with individuals, the selected provider's chain shown before
      committing, derived practice and network read-only on each row, primary care pinned first,
      past providers collapsed behind a disclosure.
- [x] Accessibility — automation names on every repeater row carrying provider, role, and status;
      the past-provider disclosure is a keyboard-reachable `Expander`; "Ended 4 Mar 2026" is text,
      so status never depends on noticing a shade of grey.
- [x] The panel is its own `ConsumerProvidersView`, not markup inside `ClientsView`. It binds only
      to `ConsumerProvidersViewModel`, and a control that loads on its own can be asserted on its
      own — which is what made the per-row command bindings provable rather than assumed.
- [x] Tests: 14 rules, 13 local-service, 13 view-model, 5 WPF render, 11 API. The cross-agency,
      link-scope, and primary-care guards were each confirmed to fail with the guard removed, as
      was the row command binding. The stale-load test was rewritten after the first version could
      have passed whenever the newer load happened to finish second, which would have proved
      nothing about the request tracker.
- [x] A directory entry on any consumer record cannot be deleted — found by a test that failed on
      its first run, because only the foreign key was stopping it. The refusal carries a count and
      never consumer names.

**Outstanding from this slice**

- [x] **The Admin test-data deletion command explicitly handles `PersonProviders`.** Both service
      paths delete and count the rows, and the result contract and PHI-minimized audit metadata
      report that count instead of relying on an unreported cascade.
- [ ] **No audit event on a consumer provider change.** Matches `PersonContact`, which also records
      none, but removal is the one operation here that destroys a record and is the obvious first
      candidate for an audited profile-child event.
- [ ] **The panel is render-tested; its host is not.** `ConsumerProvidersViewRenderTests` loads
      `ConsumerProvidersView` with a DataContext and asserts the row commands, the disabled Add
      button, the derived affiliation being text rather than an input, the collapsed disclosure,
      and the assertive live region. What stays unverified is that `ClientsView` hands it the
      right DataContext — the smoke test loads `ClientsView` without one.
- [ ] **`SortOrder` has no interface.** The column, the rule, and the ordering all exist and are
      tested; nothing yet lets a case manager reorder the list, so every row is added at the end.

### Slice 3 — Reconcile the superseded fields ✅ (2026-08-28)

- [x] **No backfill.** The plan said "backfill by name match"; that became a per-consumer linking
      prompt instead. A bulk write across live consumer medical records should not run unreviewed,
      and the failure mode is asymmetric — unlinked is visibly unfinished, wrong looks finished.
      See `DECISIONS.md`, "The legacy provider fields are linked by hand, never backfilled".
- [x] `LegacyProviderLinking` in `Sati.Contracts.V1`: exact trimmed case-insensitive matching only,
      an explicit `Ambiguous` outcome that refuses to pick between duplicate names, and guidance
      text that names the next action for each outcome.
- [x] The provider panel offers the link when free text names a primary care provider and no
      current link says the same; one click creates the `PersonProvider` row with `IsPrimaryCare`
      set and no invented start date. Where the typed healthcare system disagrees with the derived
      network, the panel says so rather than preferring either.
- [x] **No schema change was needed.** The target of `PrimaryCareProvider` is a `PersonProvider`
      row, and of `HealthcareSystemName` the derived network — a column for either would have been
      the fourth copy this work exists to remove. Both legacy strings are kept and never cleared.
- [x] `PersonContactKind.HealthcareProvider` redefined as a human contact *at* a provider rather
      than retired; existing rows are real people and the directory does not answer "who do I
      phone at that office".
- [x] Tests: 15 matcher, 8 panel-linking, 3 render. The near-miss cases a fuzzy matcher would get
      wrong are named explicitly, and one test asserts that merely opening a consumer writes
      nothing.

**Outstanding from this slice**

- [ ] **Retire the Settings-managed `HealthcareSystems` JSON list.** Deliberately still in place:
      the field it feeds is still the only record for consumers nobody has linked yet, so it cannot
      go until the reconciliation is finished. Retiring it early would strand those consumers.
- [ ] **No agency-wide view of what is still unlinked.** The per-consumer prompt is enough to
      finish the work but not to plan it; a supervisor cannot see how much is left.
- [ ] **Nothing marks a consumer as deliberately not linkable.** A consumer whose free text names
      somebody who will never be in the directory keeps its prompt forever, and there is no way to
      say "reviewed, leaving as text".

### Slice 4 — Documents ✅ (2026-08-28)

- [x] `AssessmentNeed` gained `ProviderPracticeSnapshot` and `ProviderNetworkSnapshot` beside the
      existing name and id, frozen by `ProviderAffiliation.Snapshot` at the moment of choosing.
      The document is stored as JSON, so this needed no migration and older documents deserialize
      unchanged.
- [x] The free-text provider box on a need is replaced by a picker over the consumer's **current**
      linked providers, closing the deferred "replace the temporary provider-name entry" item.
      A need whose provider was typed before the directory, or who has since left the consumer's
      list, still renders exactly what it recorded.
- [x] The Person-Centered Plan quotes the frozen triple rather than the bare name.
- [x] Tests: 9 covering the snapshot, including that an already-taken snapshot does not move when
      the directory does, that a hospitalist with no practice does not render a dangling separator,
      and that a need written before this change still reads correctly.
- [x] `StabilizationTests`' feature-view smoke host gained the two new service registrations, so
      the assessment workspace stays covered rather than being skipped.

**Outstanding from this slice**

- [ ] **Fixed-row forms still do not take N providers in the case manager's order.** The rule and
      the ordering exist in `ConsumerProviderRules.OrderForDisplay`, and `SortOrder` is stored, but
      no document currently renders a provider table — so there is nothing yet to apply it to. It
      lands with the first form that has provider rows.
- [ ] **The assessment workspace resolves services in its view constructor.** Pre-existing
      service-locator usage that this slice added two more entries to, and the reason the smoke
      test needs a host at all. Worth moving to constructor injection when that view is next
      touched properly.
- [ ] **No test covers the assessment need picker end to end.** The snapshot function and the model
      are tested; the picker binding and the freeze-on-select path are not.

### Prerequisite promoted by this design

- [x] **Provider directory governance.** Previously deferred as tidiness. Once entries have parents,
      a duplicate network row splits the tree with no view that reveals it, so admin curation and a
      merge path for duplicates become a precondition for slice 3 rather than later polish. The
      role split, same-name warning, named contacts, Admin merge, audit event, UI, and focused tests
      are now in place.

### Open, deliberately not designed yet

- [ ] A clinician affiliated with two practices — hospital privileges plus a private practice. A
      single parent says one. Accepted for now; if it becomes real, the location belongs on the
      `PersonProvider` link rather than as a second parent, which would reintroduce the
      disagreement the single parent exists to prevent.
- [ ] Waiver-side tier vocabulary. The parent link, cycle guard, and resolution walk are already
      general; only the `Individual | Practice | Network` naming is medical.

## Provider directory as a shared agency rolodex — assessed 2026-08-28

Asked whether providers created by one case manager become an agency-wide pool everyone draws from.
**They already do.** `Provider.AgencyId` scopes every directory entry to the agency, `GetAllAsync`
returns the whole agency's directory, and both the Providers tab and the consumer profile's picker
read from it. Nothing is per-user. What is missing is not sharing — it is the governance a shared
pool needs to stay usable.

- [x] **Writes now use the same role policy in both paths.** Case managers, supervisors, directors,
      and Admins may add/correct shared directory entries; deletion and merge are Admin-only and
      are enforced below the interface.
- [x] **Duplicate detection warns on the way in.** Uniqueness is enforced only on `Npi` and
      `MaineCareProviderId`, both optional and both usually absent when an entry is created from a
      phone call. Two case managers each typing "MaineHealth" get two rows. Since the affiliation
      work, that no longer merely clutters the list — it splits the hierarchy, with half the
      practices hanging off each row. The editor now shows a normalized same-name warning without
      blocking legitimate organizations that happen to share a name.
- [x] **Admin merge is implemented.** It repoints
      `ParentProviderId`, `PersonProvider.ProviderId`, `Settings.DefaultPassthroughProviderId`, and
      `AssessmentNeed.ProviderId` — except the last, which must **not** move: a document froze that
      entry deliberately. It also moves named contacts, refuses ambiguous live consumer-link
      conflicts, runs transactionally, and records a PHI-minimized `provider.merged` event.
- [x] **The directory has curation tools.** The shared Providers interface now includes the warning,
      named-contact editor, and Admin-only merge confirmation workflow.
- [ ] **Cross-agency sharing is a different problem and already designed.** Each agency holding its
      own Spurwink row is correct, not redundant — see `DECISIONS.md`, "Provider directory entries
      are local knowledge about a shared organization". A directory shared *between* agencies waits
      on the canonical Organization registry, and reconciliation there links rather than swaps.

## HANDOFF RESOLVED — provider directory curation completed 2026-08-28

The earlier handoff below was completed in the same working branch. Provider contacts and the
test-consumer marker have migrations generated but deliberately not applied by ordinary feature
work; deployment and migration remain release-playbook actions.

### What is finished and working

- **Role split.** `ProviderDirectoryRules.CanCreateOrEdit` (CaseManager/Supervisor/Director/Admin)
  and `CanDeleteOrMerge` (Admin only), applied in *both* paths. This closed the live inconsistency
  where local Production let any case manager delete while the API returned 403 on create.
- **Same-name detection.** `ProviderDirectoryRules.SameNameWarning` — normalized (trim, collapse
  whitespace, case-insensitive), warns and never blocks, because two real organizations can share
  a name.
- **Multiple named contacts.** `ProviderContact` model, EF config both sides, migration
  `20260828193518_AddProviderContacts`, service methods, four API routes, DTOs, cloud client,
  `ApiSurface` updated and its test passing. Deliberately *separate* from
  `Provider.PrimaryContact`/`Phone`, which stay as the organization's general directory line —
  those are facts about the organization, contacts are facts about people who work there.
- **Merge.** `ProviderDirectoryRules.ValidateMerge` plus implementations in both paths. Moves
  affiliated entries, consumer links, and contacts to the survivor; adopts identifiers and parent
  only where the survivor has none; refuses on conflicting NPI/MaineCare ids, mismatched tiers, and
  loops. **Deliberately does NOT repoint `AssessmentNeed.ProviderId`** — a document froze that
  entry and rewriting it would change what an approved assessment says.
- Three API tests updated to the new policy, and two of the earlier delete-guard tests moved onto
  an Admin session.

### Completion of the handed-off work

1. **UI complete.** The provider editor presents a non-blocking same-name warning, named-contact
   maintenance, and an Admin-only merge review with a destructive confirmation that fails closed.
2. **Tests complete.** Shared rules, both merge paths, contacts, stale selection loads, confirmation,
   authorization/tenancy, frozen assessment references, and rendered WPF controls have focused
   coverage.
3. **Merge audit complete.** Both persistence paths retain `provider.merged` with IDs and counts,
   never consumer names.
4. **Documentation complete.** Architecture, route authorization, audit catalog, and durable design
   decisions describe the implemented boundary.

## Daily sign-in agenda — completed 2026-09-01

- [x] Show a theme-aware, accessible agenda after scratchpad and caseload initialization, including
      overdue forms, upcoming work, a quiet-period Comprehensive Assessment suggestion, and the
      permanent Demo indicator.
- [x] Store the enabled toggle and once-per-day marker locally per environment and Sati user; an
      opted-out or already-shown user reaches no agenda data source.
- [x] Initially keep the feature read-only except for explicit selected-line appends to Today's
      Work, with ordinary navigation to the existing form surface and no compliance transition.
- [x] Replace selected-line appends on 2026-09-05 with structured Scheduled Form notes. The existing
      note lifecycle already owns client linking, type, status, authorization, audit, concurrency,
      and retention, so no second task entity or scratchpad-text parser is needed.
# Safety-plan authoring (2026-09-03)

The shared, versioned Safety Plan structure is implemented in source with a Draft → Ready for review → Approved/Returned workflow and WPF authoring/review controls. Approval requires a non-author supervisor whose actual caseload scope includes the consumer; agency equality alone is not sufficient. Unapproved PDF output is marked draft and cannot satisfy the annual Safety Plan prerequisite. The scaffolded migrations have not been deployed to Demo or Production.

## Ordinary-consumer deletion, archive status, and bulk-import dedupe (2026-09-03)

Full write-up in `DECISIONS.md`, "Ordinary-client deletion within a 20-day window, and a real
(narrow) legal-hold registry." Design in `HANDOFF_CLIENT_DELETION_POLICY.md`, now updated to
match what shipped.

- [x] Bulk Credible import dedupe checks CredibleClientId, then MaineCareId, then normalized
      name+DOB, matching CREDIBLE_IMPORT_DESIGN.md's specified match order — previously
      CredibleClientId only, which is why the workflow demo's re-import created duplicates for
      consumers who predate Credible import.
- [x] Credible import now maps `address1` to `Person.Address` as well as `Person.BillingStreet` —
      previously only the claim-address field was populated, which the demo also surfaced.
- [x] `Person.CreatedAtUtc` (immutable, migrated), `Person.Status` archive field, and their
      exclusion from `GetAllPeopleAsync` and everything generated from it.
- [x] `ConsumerDeletionRules` (window + A1 billing-integrity gate), `AdminService.DeleteConsumerInWindowAsync`
      and the matching API route, itemized audit tombstone with a PHI-exclusion test.
- [x] `PersonStatusRules`, `AdminService.SetPersonStatusAsync` and the matching API route —
      case manager may set NoLongerServed/Deceased on their own caseload, only Admin may set Ghost.
- [x] A real, minimal `ILegalHoldRegistry` (`LocalLegalHoldRegistry` / `ApiLegalHoldRegistry`) over
      a new `LegalHold` table, plus Admin place/release actions and API routes. Deliberately
      narrower than `OPERATIONS.md`'s full record-class/scope hold model — see the two items below.
- [x] Admin dashboard: a typed-name confirmation dialog (`TypedConfirmationDialog`, Confirm
      disabled until the exact consumer name is typed) for rule-3 deletion, alongside the existing
      test-consumer-delete action. Not yet visually exercised in a running app — the ViewModel
      command logic has full test coverage; the XAML has not been rendered/clicked through.
- [ ] **Dual-control legal-hold release.** `OPERATIONS.md`'s legal-hold gate requires a second
      approver to release a hold; the shipped registry is single-admin release. Explicitly scoped
      out at implementation time (Josh's call) rather than an oversight — needs a real design pass
      (who the second approver is, whether it blocks or just double-records) before building.
- [ ] **Legal-hold registry is scoped to gating consumer deletion only.** It does not implement
      `OPERATIONS.md`'s general record-class/scope hold model and does not by itself satisfy that
      gate for any other retention or purge job — those items (line ~2364, ~2653 above) remain open.
- [ ] Show record counts to the Admin *before* confirming rule-3 deletion, per
      HANDOFF_CLIENT_DELETION_POLICY.md's audit section. Shipped without a pre-count preview
      endpoint; the confirmation dialog names the categories of data that will be deleted but not
      exact counts. The success notice after deletion does show exact counts.
- [ ] `address2` is not mapped on Credible import — no fixture or real export confirms Credible's
      actual label text for it, and the mapper's design is to never guess a label.

## Client edit incident follow-up (2026-09-05)

- [ ] Identify the original exception behind local Production support reference 54546DF49635.
      The screenshot alone does not establish whether persistence or the subsequent UI refresh
      threw. Confirmed post-save failures now report saved status correctly. The local incident
      reporter retains a fingerprint but no exception stack; correlate safe diagnostic details
      with the displayed reference as part of the logging work. No Production record was queried
      or changed during this investigation.

## Pending approvals performance and threshold batch action (2026-09-05)

- [x] Load the first 10 review notes from the database, then load further pages on downward scroll
      or Load more. Preserve the case-manager filter and reject obsolete load results.
- [x] Add explicit "Approve all within threshold", default 4 units per note. No approval occurs on
      load, scroll, or threshold edits. Traverse unloaded notes in bounded pages and retain normal
      authorization, compliance, revision, time-conflict and audit checks on each approval.
- [x] Verify pagination after earlier approvals, threshold boundaries, user-triggered behavior,
      and stale-load suppression. Permission, threshold and stale-load tests fail when their
      respective guards are removed. Final targeted suites: 132 API and 52 desktop/domain passed.
      No live approvals or database migration was performed.
- [x] Published the API-only Demo filter hotfix on 2026-09-06. The 8,777,987-byte package has
      SHA-256 `CE17C1B5DD49F29795576B38741FB5758E69FC99D673A2B1CCB78433A40EAC79`;
      OneDeploy deployment `753b493bd57e4d38b17af6714d8b8d19` succeeded. Liveness returned
      `live`, readiness returned `Healthy`, and `/health/version` reported `Sati.Api` 1.2.48 with
      contract revision `4FF4AE13D9DC`. The new protected filter route returned HTTP 401 without
      a login instead of 404, confirming it is hosted. No Production service, database migration,
      firewall rule, or installer was touched.

## Adaptive display modes — implemented 2026-09-05

Full handoff and acceptance criteria: `DISPLAY_MODES_DESIGN.md`. Work Agenda is the default center
workspace. Easy Eyes remains a single personal switch; fitting the layout is automatic.

- [x] Replace one-time physical-resolution selection with the specified finite-viewport policy,
      including scaling, responsive transitions, center-default migration and preference isolation.
- [x] Implement Wide/Balanced/Compact placements, labeled access to every supporting feature,
      existing navigation overflow and a single live agenda host that remains reachable outside Overview.
- [x] Adapt the shared note editor for short windows and add explicit Focus note, preserving
      commands, conditional fields, validation, drafts, caret/selection/undo and keyboard access.
- [x] Add safe empty/loading/error states and explicit scope labels. Keep the existing palette, font
      choices, Easy Eyes and editor text controls.
- [x] Render and inspect the four layout tiers, run responsive/editor/preference tests, and update
      ARCHITECTURE.md with implemented ownership. Full desktop/domain suite: 1,325 passed and one
      optional local-AI competence test skipped. A release run remains separate.
- [ ] If user testing shows a need, add constrained user-resizable pane widths and a reset action.
- [ ] At extremely narrow widths, consider replacing the existing horizontal navigation overflow
      with a labeled selector. All destinations remain keyboard reachable through the current strip.
- [ ] Later, separately consider replacing explicit font sizes with shared typography resources.
      This is not required for the adaptive-layout implementation and must preserve the one-toggle
      Easy Eyes experience if undertaken.

No database change, version bump, deployment, or DATT release action was performed.

## Team chat — reviewed synthetic-data implementation (2026-09-05)

See `TEAM_CHAT_DESIGN.md`, `TEAM_CHAT_REVIEW.md`, `TEAM_CHAT_GUIDE.md` and
`TEAM_CHAT_VALIDATION.md`. The preceding no-database-change statement belongs to adaptive layout.

- [x] Implement the reviewed API-only synthetic chat, explicit consumer-scoped membership,
      durable history, release evidence, retained redaction, session/privacy protections and WPF
      workspace. Full solution: 1,808 passed, zero failed, one optional local-AI test skipped.
- [ ] Repeat dependency advisory lookup before release; the full build could not reach NuGet's
      vulnerability feed, so a fresh package vulnerability assessment was not completed.

- [ ] Resolve existing empty-database migration failures found during the isolated chat rehearsal:
      generated SQL batches fail in `20260419144051_AddNoteApprovalFields` when a just-added Npi
      column is referenced in the same batch; direct EF migration stops in
      `20260828195515_AddTestConsumerMarker` when a conditional statement references absent
      `dbo.SatiDatabaseIdentity`. Prior migrations were not edited in the chat implementation.
      Record full-chain replay evidence before deployment; isolated chat-table success is narrower.

- [ ] Rehearse controlled Demo deployment, compatibility and two-workstation reconnect/access
      removal before enabling hosted synthetic chat. Complete real assistive-technology acceptance.
- [ ] Approve agency HIPAA/Maine applicability, permitted audiences, restricted-record handling,
      business associate responsibilities, training and privacy-incident procedures.
- [ ] Establish chat retention, broader preservation/hold scope and controlled discovery/export
      including hidden originals, misfiled/general-room content and backups. No automated deletion
      until those controls exist and are reviewed.
- [ ] Complete account disablement and immediate session invalidation across the platform.
      Password reset does not revoke every current session; room removal is chat-specific only.
- [ ] Complete approved API-backed Production, runtime append-only SQL permissions, restore
      evidence and routed monitoring before real-client chat is considered.
- [ ] If cross-caseload consumer coordination is needed, design an approved access process;
      do not weaken the existing consumer-access gate to deliver it.
- [ ] Consider private messages, delegated room management, historical-access grants, attachments,
      note promotion, presence and multi-instance latency as separately reviewed additions.

### Electronic signature handoff — implemented synthetic scope and activation work (2026-09-06)

- [x] Shared document-purpose/code/workflow rules; exact complete original freezing; immutable
      signer-specific requests, consent, events, completions and separate derived PDF packages.
- [x] Authenticated staff routes and Annual Documents workspace; public portal with isolated
      identity design, narrow SQL model/grant script, request/session checks, CSRF, secure cookies,
      short leases, code lockout, explicit consent/intent and paper/assistance choices.
- [x] Private Azure storage/key/email adapters, encrypted durable outbox, guarded package/mail
      workers, exact retry identities, and honest suppressed/submitted/provider-result reporting.
- [x] Synthetic SQL migration, role-denial, lock/concurrency and rollback rehearsal; automated
      regression and deliberately removed-guard evidence in `SIGNATURE_PORTAL_VALIDATION.md`.
- [x] Plain-language findings and legal/operating outline in `SIGNATURE_PORTAL_REVIEW.md` and
      `SIGNATURE_PORTAL_GUIDE.md`; original proposal labeled as superseded where it conflicts.
- [x] September 14 source follow-up: API-only, idempotent compliance projection for eligible
      consumer/guardian completions, linked to an exact form or recipient release obligation and
      using the agency-local date of the immutable signing instant. This supersedes only the prior
      “signing never attests completion” rule; it does not enable Production or expand the portal's
      SQL authority.
- [ ] Apply a separately authorized controlled migration/deployment. Provision and verify the
      environment marker first. Direct EF migration of a disposable marked database passed all
      96 migrations; this does not repair the older generated-SQL-script Npi batch defect recorded
      during chat work. Do not describe the old script or an unmarked database as validated.
- [ ] Provision separate portal hosting/managed identity/private storage/key rights and exact
      runtime SQL grants. Verify actual deployed identities, network limits and token-free edge
      logs; code and a disposable-role rehearsal do not prove the deployed permissions.
- [ ] Complete approved synthetic mail testing, current DNS/sender verification, bounce/delivery
      event integration and external alert routing. No email was sent during this implementation.
- [ ] Complete hands-on browser/mobile/keyboard/screen-reader acceptance and an accessible PDF
      strategy. Generated evidence is not a tagged PDF; assisted/paper alternatives remain necessary.
      The browser screenshot launch was blocked by automatic approval review; automated DOM and
      HTTP checks are not a substitute for that acceptance work.
- [ ] Obtain reviewed agency release/notice/disclosure wording, authority/identity procedures,
      MaineCare/OADS/state-form decisions where applicable, and special-record/minor/multiple-signer
      handling. Part 2 and Maine confidentiality rules are not satisfied by a generic signature.
- [ ] Complete contracts/BAAs, risk assessment, incident response, tested restore, retention/holds,
      evidence discovery/export, later-copy delivery and training before considering real PHI.
      Signature history is retained and test-consumer deletion is blocked when it exists.
- [ ] Establish an approved API-mediated Production implementation separately. Current Production
      remains hard-disabled; no real-use switch or legal-clearance setting is provided by this build.

## Check request workflow — implemented 2026-09-09

- [x] Add a Check Requests destination to each consumer profile with a per-consumer history,
      modern themed editor, accessible field names, and a live preview of the original form.
- [x] Snapshot consumer, agency, assigned case manager, and supervisor data at draft creation;
      leave payee, address, amount, needed-by date, and reason as deliberate entries.
- [x] Use one shared publication rule, revisioned local/API persistence, own-caseload writes,
      supervisory read access, authenticated publisher identity, an audit event, and a permanent
      publication lock. Corrections are new requests rather than edits to published financial data.
- [x] Reproduce the original one-page bordered form and cut line in `CheckRequestPdfExporter`,
      offer Publish PDF at the bottom of the editor, and allow faithful regeneration from the list.
- [x] Add the controlled `AddCheckRequests` migration and update API compatibility/authorization
      inventories. No database migration or deployment was performed.
- [x] Make the interim routing explicit: publication is presented as “PDF prepared,” after which
      the case manager manually emails the attachment to Finance and CCs the supervisor. Printed
      names, PDF preparation, and the CC do not constitute or record supervisor approval.
- [ ] Before real operational use, have the agency confirm that the preserved “Signature” labels
      and routing meet its finance procedure. Sati identifies the staff and records the publisher;
      this feature does not claim electronic-signature or supervisor-approval status.
- [x] Add the server-authoritative workflow over a frozen request: submit to the assigned
      supervisor, approve or return with a reason, deliver to an authorized Finance destination,
      and record check release and receipt acknowledgement. Each checkpoint has role/tenant
      authorization, timestamps, actors, append-only events, and database uniqueness; publication
      itself is still only PDF preparation and never approval or proof of delivery.

## Consumer profile photos — implemented 2026-09-12

- [x] Add a prominent portrait at the top of the consumer profile and increase the selected
      consumer's name by 20%; the current approved treatment uses clean image edges.
- [x] Add dedicated JPG/PNG storage with shared byte/type/dimension limits, separate local and
      cloud services, a controlled migration, and person-scoped API routes.
- [x] Recheck tenant and caseload authority on the server, require expected revisions for
      replacement/removal, prevent response caching, and audit update/removal without photo bytes.
- [x] Clear the portrait on every consumer switch and regression-test that a delayed old load
      cannot display the wrong consumer's image.
- [ ] Apply migration `20260912053013_AddPersonPhotos` and release compatible API and desktop
      builds through the normal approved deployment process. No live database was changed here.
- [ ] Before storing real consumer photographs, adopt agency-approved consent/notice, minimum-use,
      retention/legal-hold, export and deletion procedures. The feature supplies technical access
      controls; it does not decide those policies.

## Unreleased — mock clearinghouse denial scenarios (2026-09-19)

- [x] Add Demo-only mock scenarios for common whole-claim denials — duplicate (CO-18), coverage
      ended (CO-27), no authorization (CO-197), missing information (CO-16), not covered (CO-96),
      benefit maximum (CO-119) — alongside timely filing (CO-29), plus a mixed-outcome 835.
- [x] Explain stored denial reasons in the billing worklist: the reason catalog now matches the
      bare CARC that ingestion stores, with or without a group prefix.
- [ ] Retain RARCs (835 `LQ`) and every CAS reason, not only the first. The reader accepts `LQ`
      but discards it, and outcomes keep one reason code, so the mock emits no RARCs and each of
      its denials carries a single reason. Needs a reader change and a schema change.
- [ ] Service-line-level denials and multi-line claims. The mock still supports one service line
      per claim and adjusts at claim level only.

## Unreleased — bank deposits and claim corrections (2026-09-19)

- [x] Record the bank deposit behind a remittance as append-only entries, with optimistic
      concurrency, a required reason for a correction, and reconciliation derived from the latest
      entry. `Reconciled` is computed on read, never stored.
- [x] Correct a sent claim: resend (frequency 1) after a rejection, or replace (7) or void (8) an
      adjudicated claim citing the payer's claim number, in a correction-only 837P.
- [x] Store per-claim 277CA verdicts and the 835 payer claim control number, which nothing
      retained before.
- [x] Apply migration `20260919212417_AddEftDepositsAndClaimCorrections` to Demo with
      `scripts/Apply-EftDepositsAndClaimCorrectionsMigration.ps1` (`-WhatIfOnly` first, then twice).
      Rehearsed 2026-09-19 against a scratch LocalDB copy of the schema at the previous
      migration: the dry run rolled back clean, the apply was idempotent, and the resulting
      schema is identical to the one `dotnet ef database update` produces. It adds four
      tables plus `RemittanceClaimOutcomes.PayerClaimControlNumber` and `EdiGenerations.IsCorrection`;
      it is additive only. Applied under explicit authorization on 2026-09-20: the guarded Demo
      dry run, apply, and second apply all succeeded, and the user removed the temporary exact-IP
      firewall rule afterward. Full evidence is in the 1.3.21 release section above.
- [ ] Confirm against the MaineCare/Office Ally 837P companion guide that frequency 7 and 8 with
      `REF*F8` are accepted as written, and whether a void must repeat the original charge exactly.
- [ ] Corrections are Demo-only in effect: response import is enabled only in Demo, so a claim in
      local Production has no recorded answers and offers no correction.
- [ ] Answers imported before this release have no per-claim 277CA verdict, so their claims read as
      awaiting an answer rather than rejected.

## Unreleased — Annual Forms workflow and faithful document display (2026-09-19)

- [x] Replace the separate client-level Releases, DHHS Forms, Safety Plan, and Annual Documents
      tabs with one compact Annual Forms tab containing Overview, Releases, DHHS Documents, Safety
      Plan, and Privacy Practices.
- [x] Make Overview the clear starting point: show the complete service-year range, explain that
      changing/selecting a year does not modify records, report successful loads, and give each
      workflow row a direct action into the correct inner tab.
- [x] Remove separately drawn “live document” facsimiles for externally owned DHHS, CWIC, and
      Housing forms and label their screens as entry workspaces. Generation continues to fill the
      retained source PDFs; those PDFs, not a WPF approximation, are the review/submission output.
- [x] Retain live previews only for Sati-owned documents whose preview faithfully represents the
      generated output (currently AT and Check Requests).
- [ ] Optional: embed an actual generated-PDF viewer if the added dependency and accessibility
      behavior are justified. It must display the same bytes Sati saves, never a second rendering.

## Unreleased — note activities and manual attestation links (2026-09-22)

- [x] Let a work note document several selected activities; keep Reminder separate. A late form
      on a mixed note holds the entire note from billing.
- [x] Match manual form and recipient-specific release checkmarks to exact linked notes. Reuse
      an agreeing note; stop on date conflict or ambiguous legacy evidence; create and link a
      Pending draft when none exists. Reclassification's implied assessment gets its own draft.
- [x] Preserve reasoned release attestation revocation and require a supervisor after note
      submission or Admin once a claim line exists. Show unclaimed approved linked
      notes in the supervisor return queue.
- [x] Give Admin an audited source-date correction for claimed form and release notes.
      Keep the original claim service date and flag its claim line for billing review.
- [x] Add Legacy Dark with Legacy's typography, leaf, and gradient styling using the
      supplied Black Bean, Sienna, Brown Sugar, Dun, and Bone palette. Its
      navigation and raised panels now use distinct dark shades, while editable
      fields and check controls use light surfaces with dark ink. Selection is
      dark with light ink. The all-theme contrast sweep and a direct Legacy Dark
      field-control check pass.
- [x] Build the solution, verify migration snapshot, and run focused desktop and API tests.
- [x] Refresh the selected client profile from the newly loaded caseload after a note save,
      so a form completed by logging a linked note appears without restarting Sati.
      A desktop regression test covers replacement of a selected client's form state.
- [x] When a manual form checkmark finds one exact-linked form-only Scheduled note,
      preview its planned date beside the selected actual completion date. On
      confirmation, convert that same note to a Pending draft dated for the work
      and save the attestation in one transaction. Reject a stale confirmation.
      The note still needs its narrative and normal submission. Mixed,
      Reclassification, claimed, or ambiguous notes keep the correction route.
- [ ] Joshu LocalDB follow-up: verify whether the closing "scheduled work that did
      not get done" prompt moved this Safety Plan note to 9/23. That prompt defaults
      each unfinished Scheduled note to "Move" and applies the next-workday date
      when the user chooses "Apply and close". The exact Joshu note history is not
      available from this login. A moved plan is still not completion evidence;
      the final Logged note must use the actual work date.
- [x] Apply migrations `20260922154932_AddMultiActivityNotes`,
      `20260922161704_LinkReleaseNotesToExactObligations`, and
      `20260922162222_TrackReleaseAttestationRevocation`, plus
      `20260922191918_SupportReleaseAttestationReviewFlags` to identity-checked `SatiDemo`
      after explicit user approval. The guarded runner found the expected 112-row starting
      boundary, completed a rollback-only rehearsal, verified the resulting 116-row schema,
      and confirmed rollback restored the prior state. The live apply committed all four;
      the second run verified the 116-row schema and made no changes. The existing Demo API
      `/health/ready` returned `Healthy` afterward. The user removed the temporary
      `datt-workstation-temp` firewall rule, and a read-only Azure allow-list query
      verified it absent; the existing Demo API and refresh outbound rules remain.
      Local Production has not been
      migrated; its pending migrations run when the new Local client is installed and launched.
