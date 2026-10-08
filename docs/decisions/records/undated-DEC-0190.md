<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## September 29 — Form wizard progress belongs in an encrypted author draft

Partially answered PDF wizards need a durable answer set before they can generate a
valid document. One mutable `FormWizardProgress` row per consumer, author, and form
stores the current answer JSON and section index under envelope encryption. Tracked
release keys include their obligation id; DHHS annual release keys include the exact
target date. Save requires the revision that was read,
so two sessions cannot silently overwrite each other. Each operation rechecks the
current caseload; the client does not choose author or agency scope. Reads and
writes receive answer-free audit events. Draft deletion waits for a reviewed
retention and legal-hold policy.

The row is editor state, not a signed record, submitted application, or completion
attestation. Generated PDF artifacts retain their existing versioned history.
Rejected: unencrypted client preferences or a workstation-only file. Those would
expose sensitive household answers or leave Demo drafts tied to one computer and
would not enforce current caseload ownership on a later read.

