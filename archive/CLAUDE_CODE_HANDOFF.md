# Claude Code handoff — workspace preparation and bounded startup

Date: 2026-09-25

## Objective

Finish verification and release preparation for the post-login workspace-preparation and startup-
performance change. The implementation is present in the commit containing this file. Do not
rebuild it from scratch or broaden it into a cloud migration.

The user asked for Sati to remain responsive with LocalDB and Azure as notes accumulate, to show an
honest visual cue while authenticated startup work runs, and to provide interesting reading during
that interval. The exact visible status is:

> We are preparing the Sati workspace.

Billing-permission users must also retain a useful Billing Overview without restoring lifetime
history loads.

## Implemented

- Removed the fixed three-second pre-login splash delay.
- Added an opaque, themed, accessible post-login preparation window that is painted before database
  or API work, remains through the shell's first render, and closes on success or failure.
- Added 100 original Sati reflections with deterministic nonrepeating rotation and a scoped CC0-1.0
  dedication in `WORKSPACE_REFLECTIONS.md`. The changing reflection is intentionally not a live
  region; its actual text remains available to assistive technology.
- Prepared the case-management caseload once and published the same person instances to Overview,
  Clients, and Notes Log.
- Limited caseload `Person.Notes` to Scheduled summaries from the Maine business date through 30
  days ahead. Narratives and visit-documentation JSON are excluded. Monthly-contact evidence is an
  explicit, separate `ContactFacts` contract input.
- Deferred hidden Notes Log full-note reads, Client settings, Calendar year data, Billing, and
  Supervisor work until their first actual navigation. Account changes invalidate caches and late
  responses are rejected.
- Made Billing Overview first-use and account-scoped. Billing-only accounts use the same awaited
  path.
- Replaced Billing Overview's lifetime period/claim-line graph with a tenant-scoped aggregate:
  all draft charge value plus exactly six monthly charge totals.
- Replaced Local and API billing candidate entity graphs with explicit scalar projections. Forms
  and releases are loaded independently, avoiding the former Cartesian expansion. Narrative,
  visit JSON, biography, journal, and API encrypted-SSN envelope columns are excluded.
- Kept authoritative billing, contact, compliance, and authorization rules in their existing
  owners. No rule was duplicated into the WPF presentation layer.
- Added regression tests for query shape, payload privacy, authorization, first-use loading,
  account-switch races, scheduled-note boundaries, preparation-window lifecycle, accessibility,
  and responsive sizing.
- Updated `AGENDA.md`, `ARCHITECTURE.md`, `DECISIONS.md`, and `API_AUTHORIZATION.md`.

## Verification completed

These focused suites passed before the handoff commit:

- Desktop startup/caseload slice: 125 passed, 0 failed, 0 skipped.
- API caseload/contract slice: 7 passed, 0 failed, 0 skipped.
- Desktop Billing/Supervisor slice: 26 passed, 0 failed, 0 skipped.
- API Billing aggregate/authorization slice: 7 passed, 0 failed, 0 skipped.
- An earlier preparation-window-focused run passed 245 tests before the final integration edits.
- Final combined desktop regression set: 98 passed, 0 failed, 0 skipped.
- Final combined API regression set: 111 passed, 0 failed, 0 skipped.
- Final serialized full suite: 3,548 passed, 0 failed, 11 skipped. The skips are the existing
  opt-in Local AI or live SQL Server acceptance tests.
- Final no-restore solution build: succeeded with 0 warnings and 0 errors.
- `git diff --check`: no whitespace errors; Git emitted only expected LF/CRLF notices.

The first full-suite attempt was sandboxed and correctly failed tests requiring DPAPI or AppData
writes. The final full-suite result above used the normal Windows user profile. During that pass an
older client-edit test was corrected to use the explicit note-service boundary rather than assume
the startup caseload still contains Logged note history.

## Remaining before a release

1. Confirm the handoff commit is still at the tip of `master` and `origin/master`; do not introduce
   a merge unless a later remote commit makes one necessary.
2. Perform a manual smoke test only after confirming that the user's working Sati instance can be
   closed without losing unsaved work. Verify:
   - the preparation window appears after login, not before it;
   - it remains visible until the main shell is rendered;
   - mixed case-manager/Billing users land in Case Management without hidden Billing work;
   - Billing-only users land in Billing and see a populated Overview;
   - first navigation to Billing, Supervisor, Notes Log, Clients, and Calendar loads once;
   - account switching cannot publish data from the previous account.

3. Capture cold/warm Local and hosted Demo timings with production-shaped synthetic data if the
   user wants a measured latency target. Do not use Production data.
4. Use the repository's DATT process only if the user sends a message whose entire trimmed content
   is `invoke DATT!`. Read `RELEASE_PLAYBOOK.md` completely at that time. This handoff does not
   authorize a release, Production deployment, Azure migration, firewall change, or artifact
   overwrite.

## Known scaling boundaries — do not misrepresent these as completed

- Billing candidate discovery is much smaller and avoids the measured graph explosion, but Billing
  Overview still validates every approved-unbilled candidate to produce exact ready/blocked totals.
  For an unusually large backlog, add a bounded or server-aggregated contract and share its result
  with the Queue; never silently truncate financial counts.
- First Notes Log navigation remains a sequential, unbounded per-consumer full-note load. Startup no
  longer pays this cost. `AGENDA.md` tracks a bounded server-side page/search contract.
- Caseload preparation still performs provider/release reconciliation fanout per person/target.
- The remaining caseload profile row may still materialize `Person.Journal` locally and encrypted
  profile envelope fields inside the API. Move those behind selected-client detail boundaries.
- No representative hosted-Demo benchmark has been run. Query-shape tests prove removed overfetch,
  not a guaranteed Azure latency target.

## Data and deployment notes

- This change requires no EF migration and no Azure SQL migration.
- Do not add direct Azure SQL access or credentials to the desktop client.
- Demo and Production remain separate environments. Do not copy or inspect Production data for
  performance testing.
- The new API route is `GET /api/v1/billing/overview-periods/{year}/{month}` and is tenant-scoped,
  permission-gated, represented in `ApiSurface`, and documented in `API_AUTHORIZATION.md`.
- The caseload contract shape marker prevents an old/new client-server pair from silently treating
  its bounded scheduled-note window as complete contact history.

## Review emphasis

Before changing or releasing the work, inspect `App.xaml.cs`, `Data/PersonService.cs`,
`Sati.Api/Endpoints/ApiEndpoints.cs`, `ViewModels/CaseManagerDashboardViewModel.cs`,
`ViewModels/Billing/BillingOverviewViewModel.cs`, and `ViewModels/ShellViewModel.cs`. Pay special
attention to first-render failure handling, account-switch request identities, tenant predicates,
and the distinction between scheduled-note summaries and `ContactFacts`.
