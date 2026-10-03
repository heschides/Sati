# Archived documents

These documents finished their job: a handoff that was implemented, a checkpoint for a branch
that has since merged, or a point-in-time review superseded by later releases. They are kept
for history and for the reasoning they record. None of them describes current state, and none
is an instruction to an implementer.

Links inside an archived document were written relative to the repository root and are left
as written. Read a link like `SIGNATURE_PORTAL_REVIEW.md` as `../SIGNATURE_PORTAL_REVIEW.md`.

Archived 2026-10-03, against release 1.3.34.

| Document | Why it was archived | Current source of truth |
|---|---|---|
| `ANNUAL_DOCUMENT_RELEASE_READINESS.md` | Source-preparation record for release 1.2.41 (2026-09-03); later releases shipped it. | `NOTE_FORM_ATTESTATION_DESIGN.md`, `AGENDA.md` |
| `CLAUDE_CODE_HANDOFF.md` | Workspace-preparation and bounded-startup handoff; shipped in 1.3.28. | `AGENDA.md` (Release 1.3.28), `DECISIONS.md` |
| `CODEX_HANDOFF.md` | Compliance attestation and annual-document handoff; implemented. Not to be confused with `karuna/CODEX_HANDOFF.md`, which is current. | `NOTE_FORM_ATTESTATION_DESIGN.md` |
| `DASHBOARD_DESIGN_REVIEW.md` | Point-in-time visual review from 2026-08-14, before many later dashboard releases. | `DECISIONS.md` |
| `HANDOFF_90DAY_REVIEW_FLAG.md` | Implemented and tested 2026-08-31. | `NOTE_FORM_ATTESTATION_DESIGN.md` |
| `HANDOFF_ANNUAL_COMPLIANCE.md` | Continuation checkpoint for the `codex/clarify-project-notes` branch. | `AGENDA.md`, `DECISIONS.md` |
| `HANDOFF_LOGIN_AGENDA_WINDOW.md` | Implemented 2026-09-01; replaced by the structured Today's Work successor on 2026-09-05. | `ARCHITECTURE.md`, `DECISIONS.md` |
| `HANDOFF_SIGNATURE_PORTAL.md` | Original design-only brief; the implementation now exists. | `SIGNATURE_PORTAL_REVIEW.md`, `SIGNATURE_PORTAL_GUIDE.md`, `SIGNATURE_PORTAL_VALIDATION.md` |
| `HANDOFF_SUGGESTED_FOLLOWUP.md` | Implemented 2026-08-31 (`c0cc157`). | `AI_CASE_NOTE_RULES.md`, `DECISIONS.md` |
| `HANDOFF_TEAM_CHAT.md` | Pointer to the original team-chat handoff; the review supersedes it. | `TEAM_CHAT_REVIEW.md`, `TEAM_CHAT_GUIDE.md` |

## Kept in the root on purpose

- `HANDOFF_CLIENT_DELETION_POLICY.md`: code and tests across `Sati.Contracts`, `Sati.Api`, and
  `Data/` cite it as the policy of record.
- `HANDOFF_DUPLICATE_COMPLIANCE_FORMS.md`: a migration's and a script's runtime error messages
  tell the operator to read it, and it marks its piece 4 and an adjacent defect as not done.
- `CONCURRENCY_AUDIT.md`, `API_SECURITY_AUDIT.md`, `SECURITY_AUDIT_2026-09-03.md`,
  `SECURITY_REVIEW_2026-09-10.md`: dated audit evidence. Other documents cite most of them, and
  not every finding they record has been confirmed resolved, so they stay where a reader will
  find them.
- `DEMO_ACCEPTANCE.md`: `scripts/Test-CompanyDemoAcceptance.ps1` and its tests depend on it.
- `WORKSPACE_REFLECTIONS.md`: the CC0 dedication for `Helpers/WorkspaceReflections.cs`.
