namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Financial workflows, clearer records, timely prompts";
    public const string ReleaseDate = "September 14, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Finance and Representative Payee work have a dedicated path",
            [
                "Finance users can work with billing without exposing unnecessary consumer health details in the billing interface.",
                "The Representative Payee workspace provides consumer ledgers, supervisor-approved check requests, check release, and receipt acknowledgement with an audit trail.",
                "Weekly defaults create reviewable check-request drafts rather than submitting or releasing money automatically.",
                "Case managers receive sign-in, shutdown, and time-off prompts for due drafts and can turn personal reminders off in Settings."
            ]),
        new(
            "Required case-note and annual-document work is clearer",
            [
                "Submitted case notes require a deliberate goal-progress choice: None, Minimal, Moderate, or Substantial.",
                "Annual Documents separates each required document type, provides a clear send-for-review action, and shows pending, denied, and signed status history.",
                "Safety Plans now describe the annual load cycle in plain language.",
                "Historical records remain unchanged when administrators revise forms or staff prepare a new draft."
            ]),
        new(
            "Account access ends when it should",
            [
                "Administrators can disable and re-enable retained staff accounts without deleting their work or authorship history.",
                "Password changes, password resets, explicit revocation, disablement, and re-enablement invalidate older sign-ins instead of allowing a stale session to continue.",
                "Sati rechecks current permissions around protected work and prevents a slow response from an earlier account or client selection from repopulating the screen.",
                "Local database checks reduce accidental misuse by the supported client; they do not replace the planned API-only Production boundary."
            ]),
        new(
            "Consumer and billing records keep their evidence",
            [
                "Standalone form deletion is retired so an incomplete, future, optional, or completed obligation cannot disappear and silently reopen a billing window.",
                "Case notes and billing exports enforce the same current permission and compliance decisions in the client and API.",
                "Service-time and billing-submission writes have SQL Server concurrency coverage for overlapping work and competing submissions.",
                "Clearinghouse responses are retained as encrypted, immutable receipts and matched to the exact submitted generation before claim or payment status changes."
            ]),
        new(
            "Evergreen work has an explicit Sati attestation",
            [
                "Comprehensive Assessment and Person-Centered Plan authoring are off by default in Demo while those records are completed in Evergreen.",
                "Billing still requires a time-stamped completion attestation; saving an ordinary case note never silently creates one.",
                "Agency settings can expose Sati's authoring workspaces later without changing historical forms or removing the existing compliance gate.",
                "The attestation record keeps who recorded it, when it was recorded, the stated completion date, and any later revocation."
            ]),
        new(
            "Two public-program packets now have live builders",
            [
                "The MaineHealth Benefits Counseling referral packet and Maine OADS Housing Support Funds application appear as live form-and-preview workspaces.",
                "Sati prefills facts already held in the consumer, guardian, provider, and case-manager profiles while leaving application-specific answers editable.",
                "Generated packets preserve the supplied publishers' pages, leave signatures blank, and record a versioned Draft artifact rather than rewriting an earlier packet.",
                "Housing review warnings call out the $3,000 ceiling, supporting proof, subsidy and Shared Living questions, and keep the DHHS-only page untouched."
            ]),
        new(
            "Document work is easier to verify before it becomes history",
            [
                "Agency releases, DHHS forms, templates, safety plans, CWIC packets, and Housing Support Funds applications show a live document preview beside entry controls.",
                "Annual-document work remains divided by document type and keeps generated, submitted, signed, denied, and superseded evidence distinct.",
                "Case notes require the state-defined goal-progress choice: None, Minimal, Moderate, or Substantial.",
                "Consumer photos load separately from the caseload list and use bounded, inspected JPG or PNG content with audited replacement."
            ]),
        new(
            "The public Demo stays on a useful calendar",
            [
                "The canonical Demo baseline now records a deliberate timeline anchor and rolls workflow dates by the exact elapsed number of days after each reset.",
                "Repeated refreshes on the same day are idempotent, and later refreshes move only the additional days instead of aging the seed twice.",
                "Scheduled demonstration work is rebuilt across a rolling horizon so Today's Work and upcoming windows do not empty as the original snapshot gets older.",
                "The refresh verifies current upcoming forms, scheduled work, authoring settings, and Evergreen attestations before reporting success."
            ]),
        new(
            "Still required before commercial Production use",
            [
                "Retire direct-database Production clients, complete Production identity and MFA, and finish privileged-maintenance and last-administrator recovery controls.",
                "Complete real clearinghouse sandbox certification, companion-guide coverage, corrected and void claim workflows, bank reconciliation, enrollment, and monitored transport.",
                "Complete independent legal, security, accessibility, privacy, program, and retention review before real clinical, signing, claims, or cross-agency use.",
                "The provisional Privacy Practices template remains a starting point requiring agency and legal approval, not final legal language."
            ])
    ];
}
