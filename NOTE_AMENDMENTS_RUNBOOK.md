# Approved-note amendments — source and controlled activation

Prepared October 4, 2026. No runtime migration, deployment, external transmission,
or access to real SatiProduction data was performed for this feature.

## Verification evidence

Private synthetic checks passed: 47 focused API tests (amendments, claim corrections,
API surface, and dispatch), 70 desktop tests (local services, note pipeline, review
views, and document/EDI session isolation), and two isolated SQL Server migration
and two-connection locking tests. The dispatch suite also passed 17 tests after the
single-attempt execution scope was added around the worker's owned transaction and
upload. This scope supports SQL retry-enabled contexts without replaying transport.

Regression probes removed independent-review and stale-revision guards and confirmed
both corresponding API tests failed before restoring the guards. Removing the
worker's single-attempt scope also made its execution-scope assertion fail. These
checks use retained TRX evidence in `TestResults`; none used a runtime PHI database.
The offline EF model consistency check and `git diff --check` passed. Existing
compiler/analyzer warnings remain outside this feature's scope.

## Workflow and authority

In Notes Log, open **Approved amendments**, load the approved-note queue, and select
the original. The current case manager proposes corrected narrative, service date,
duration, start time, and Unbilled status with a reason. Save preserves a draft
version; Submit preserves a separate submitted version. A current supervisor with
direct or agency-wide reach can return, reject, or approve the submitted aggregate
revision. The author and current owner cannot review it, even with combined roles.
Returned content and explanations remain in history. Rejected and approved aggregates
are terminal; a later correction creates another aggregate based on the effective
approved version. Original content, approval metadata, revision, and exact evidence
links remain unchanged. A note with amendment history cannot be overwritten or deleted.
The existing narrow correction route remains available for linked notes before any
amendment history exists; an amendment cannot change an exact form/release service date.

Clinical approval makes that proposal visible in the amendment comparison. The ordinary
note remains the original record. The amendment view exposes the original, effective
approved content, every saved/submitted version, and every review event together.
General note reads, calendars, and historical productivity remain original-record
projections; broader effective-content/report projections remain follow-up work.

## Financial policy selected by Josh

Narrative changes have no billing effect. A change in service date, duration, start time,
or Unbilled status relative to the preceding effective content requires a separate,
explicit financial review. Pending, returned, and rejected proposals never change
billing. Billing > **Amendment review** exposes only service facts, not narrative.
A billing-capable actor other than the author/current owner records an explanation
against the exact approved financial version and original revision. Clinical and
financial review may share an independent reviewer with both capabilities.

For a note without a claim, reviewed service facts pass through the existing billing
validation and rate selection. The new claim line records `AmendedNoteVersionId`.
There is still only one original claim line per note. Corrected facts are projected
onto a detached billing source; the original Note is never changed.

For an existing submitted claim, review alone never alters or sends it. In the existing
correction screen, supply the exact reviewed amendment version and choose the action
permitted by `ClaimCorrectionRules`. A replacement/resubmission also requires an
explicit corrected positive charge, rather than deriving a historical rate from a
rounded charge. It freezes corrected date, units, charge, subscriber/provider snapshot,
and amendment lineage in the append-only correction. A void repeats the standing
claim's exact financial snapshot, including an earlier replacement when applicable.
An Unbilled amendment cannot be used for replacement/resubmission. New original files
and queued dispatches cannot send a claim that predates its financial amendment.
Existing retained files remain immutable and reproducible.

The service-day lock protects submission/approval and worker transition to Sending.
Financial approval waits for Sending/OutcomeUnknown transmission to be resolved.
Queued work invalidated by a financial amendment is cancelled before upload. A new
amendment never creates an automatic resend. Original and effective amended time
reservations are conservatively checked against future note/amendment writes.

## Explicit boundaries and remaining integration work

- A claim already in a **draft** billing period remains blocked when its source
  changes. The existing submitted-claim correction workflow cannot adjust that draft;
  an append-only draft adjustment adapter is still needed. Do not submit incorrect
  original facts or delete history to work around this restriction.
- A correction moving service into a different billing month fails the existing
  period-readiness check. Cross-period financial allocation needs a separate adapter.
- Desktop-local claim corrections are already unsupported; local unclaimed-note
  creation and review have parity, while an existing local claim stays protected.
- Exact form/release evidence-date changes require their existing evidence workflow
  before creating an amendment; amendments do not rewrite obligation attestations.
- Agency amendment/signature policy, records retention/legal holds, and payer acceptance
  of particular corrections remain stakeholder/legal/payer gates. This source is not
  a representation of compliance or live-billing readiness.
- Broader clinical exports, calendars, productivity projections, transfer of an
  unfinished amendment between owners, and release from the conservative original
  time reservation need explicit follow-up. Do not silently treat the original as a
  flattened amended record or drop linked history during deletion/retention work.

## Controlled activation

1. Review the above policy with the agency and appropriate regulatory stakeholders.
   Rehearse with private synthetic records first. Coordinate Demo refresh/baseline
   handling with the operational runbooks before relying on Demo history as evidence.
2. Confirm the target's database identity and expected migration chain. Stop all
   writers for the approved maintenance window. This document grants no operational
   permission and no firewall, credential, or identity changes.
3. Review the additive `20261004120026_AddNoteAmendments` migration and
   `scripts/Apply-NoteAmendmentsMigration.guarded.sql` against the approved target.
   The Demo runner `scripts/Apply-NoteAmendmentsMigration.ps1` pins its hash, checks
   identity, and serializes against Demo mutations/reset. Run `-WhatIfOnly`, apply,
   then rerun for idempotency, only after explicit authorization. Its predecessor
   is `20261002221023_AddScheduledNoteMoves`; it is not a bootstrap script. The
   original EF-generated `NoteAmendmentsMigration.sql` is offline reference evidence,
   not the controlled deployment runner. Four new tables and nullable lineage fields are
   added; no historical narrative, claim, or submitted file is backfilled or rewritten.
4. Deploy a matching API, contracts, and desktop build together after schema activation;
   verify API surface compatibility. Do not leave older writers running after the
   first amendment is recorded. Earlier source does not enforce the new history guard.
5. Verify synthetic own-caseload drafting, independent review, denied foreign/self
   access, return/resubmission, retry identity, financial hold/review, and exact linked
   correction. Confirm original rows and retained files remain unchanged and failed
   actions leave no partial history or audit.
6. Keep the schema and roll forward after use. `Down` refuses any amendment history;
   it is only available before the first amendment. Restoring an older application
   after use requires an explicit compatible plan, not destructive schema rollback.

Offline design commands accept `-- --synthetic-design`; they neither read runtime
connection settings nor connect to a runtime database. Private SQL rehearsal:
`pwsh -File scripts/Test-IsolatedLocalDb.ps1 -NoteAmendmentsOnly`. That wrapper creates
and removes only its own `SatiSqlTests_<guid>` instance.
