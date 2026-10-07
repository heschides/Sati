# Comprehensive Assessment review — Prompt 04

Source implemented October 7, 2026. Separately authorized release 1.3.37 applied Demo
migrations 127/128, published the matching API, replaced the synthetic baseline, and
verified one completed reset. See AGENDA.md for exact hashes and operation evidence.
Assessment signing and approval of an agency's assessment policy remain activation gates.

## Workflow and authority

Enable the existing agency Comprehensive Assessment authoring setting after schema
and API/client acceptance. An assigned case manager authors Draft/Returned content.
Supervisors carrying caseloads may author their own consumers but cannot review their
own submissions. Review uses the current assigned supervisor or existing agency-wide
supervision authority. Admin/Billing permissions alone grant no clinical review.
Reassignment removes the prior author's review access; coverage/recusal policy beyond
these existing relationships remains an organizational activation decision.

The author chooses the exact annual Comprehensive Assessment Form. Submission saves
first, then validates the exact stored revision and SHA-256 with the shared version-1
catalog/completeness rules. Contributors, every answer disposition, supports, therapy
and activity fields, needs/no-needs reasoning, provider snapshots, and differing
perspectives must be documented. Follow-up-required answers cannot submit. Existing
historical provider snapshots remain frozen; new associations must match currently
linked canonical providers. Nothing updates submitted answers on directory changes.

Each submission creates an immutable cycle snapshot. Reviewers can comment or flag a
document, section, question, contributors, or needs; they cannot rewrite answers.
Returns require a reason. Authors edit returned content and can respond to unresolved
flags; only an independent reviewer resolves flags with a documented explanation.
Blocking flags carry across cycles until resolved. Nonblocking observations remain
visible but do not block wholesale approval. Every review write requires the current
assessment revision and latest snapshot/hash. A conflict requires explicit reload;
approval is never retried against newer content automatically.

Legacy ReadyForReview records without snapshots remain outside the review queue.
Their author explicitly chooses **Reopen legacy submission for validated review**;
answers remain unchanged, reopening is audited, and resubmission requires the new
completeness checks and exact annual Form. Approved legacy records stay immutable;
the author may explicitly create a new document version. No historical snapshot,
annual target, approval, or completion evidence is synthesized by migration.

The queue displays the oldest 200 reviewable submissions. Completing/returning items
and reloading exposes later items. Pagination and richer coverage policy are deferred.
Selection/account request identities prevent delayed loads from replacing current
content. Review text must be saved or cleared before changing selection.

## Approval, PDF, completion, and signatures

Approval freezes the latest cycle, generates a version-identified PDF through the
existing DocumentArtifact owner, and appends its decision/audit in one serializable
transaction. Exact assessment/version/cycle/submission/hash identify the PDF and audit.
Historical PDF exports cannot supersede the current approved artifact. Download/export
uses existing permissions and the existing user-directed PDF save dialog.

Existing Sati Form policy permits explicit staff attestation of actual completion;
it does not make supervisor approval or an unsigned PDF a signature. Approval alone
leaves Form completion unchanged. The optional **Attest actual Form completion with
approval** checkbox requires the actual completion date. The server rechecks the exact
Form identity, canonical anniversary, current configured deadline, availability window,
and future-date restrictions. It applies the existing Form invariant, attestation
ledger, and separate form.attested audit atomically with approval. Ledger evidence
cites the identical snapshot/hash and approval artifact. Already-attested Forms require
their existing correction workflow. Profile, caseload/matrix, and note context refresh
after a saved decision. Failed final writes roll back all these effects.

PDF generation is export of a specific frozen version, not delivery/publication to
OADS, payer acceptance, or external sharing. The new ComprehensiveAssessment artifact
kind deliberately has no signature-policy catalog entry: hosted signing and physical
signature upload/completion for this kind remain disabled pending approved signers,
meaning, agency acceptance, and operating requirements. Reuse the existing signature
owner when those gates are resolved; do not introduce a competing signature store.

Submitted snapshots and review comments are protected clinical records. General audit
metadata contains IDs, revisions, hashes and artifact references only, not clinical
answers or comment text. Snapshots/events cannot be modified/deleted through either EF
context. Consumer deletion refuses retained assessment review history; archive instead.
No destructive retention adapter for these clinical records is enabled by this work.

## Controlled schema activation

1. Review the current release playbook and migrations 127 (RecordsGovernance) then 128
   (`20261007111016_AddAssessmentReviewCycles`). The hosted predecessor for this script
   must be exactly 127, ending at `20261007004626_AddRecordsGovernance`. Deployed 126
   cannot skip Prompt 03's controlled migration. Local migration instructions must
   follow existing environment identity/PHI protections; no local PHI rehearsal is
   authorized by Prompt 04.
2. Verify a recoverable backup/restore point, compatible API/client source, the schema
   diff, and the pinned guarded SQL. Migration 128 adds two tables and restrictive
   ownership/history FKs; it neither rewrites assessments nor completes Forms.
3. During a separately authorized Demo deployment, pause/quiesce mutation/reset workers
   as required by the playbook. The runner takes the existing exclusive reset lock.
   Any exact-IP firewall rule must be added/removed by Josh; this implementation makes
   no security changes. Do not publish API/client code before their schema exists.
4. Run `scripts/Apply-AssessmentReviewMigration.ps1 -WhatIfOnly`, then the same runner
   without the switch, then rerun it to prove idempotency. Each validates the exact
   Demo endpoint/name/identity, migration predecessor, columns, indexes and trusted
   restrictive FKs. Schema and migration history commit together; the rehearsal rolls
   back. It cannot target SatiProduction. Review and repin SQL after any alteration.
5. Recapture a separately approved synthetic Demo baseline using existing full-reset
   operations before resuming resets: added tables otherwise make the old baseline
   fail closed. Verify a reset outcome and schema/API/client contract compatibility.
6. Accept the full synthetic workflow through both author/reviewer accounts, keyboard
   navigation and high-contrast/scaled display. Broader JAWS/200%/1280x768 and agency
   policy acceptance remain human activation checks. Build a release only on a new
   valid DATT invocation; this source task did not cut an installer.

Downgrade is allowed only while both new tables are empty. Migration Down throws
53814 if review evidence exists. Prefer code rollback preserving the additive schema
and records; never delete evidence to make a downgrade succeed.

## Verification evidence

Private fixtures reproduce API completeness bypass and submit-after-failed-save before
the repair. Revision and self-review guard removal each makes its regression fail
with an unauthorized successful approval; both guards were restored before final tests.
Focused tests cover returned/resubmitted snapshots, carry-forward flags, immutable
content, current configured deadlines, PDF/artifact provenance, explicit completion,
unauthorized discovery, stale selection/account loads and final-write rollback.
SQL rehearsals use a fresh isolated `SatiSqlTests_<guid>` instance and owned synthetic
databases, never shared MSSQLLocalDB or SatiProduction. Final counts are recorded in
AGENDA.md with TRX evidence. Schema scripts have been prepared, not run against Demo.
