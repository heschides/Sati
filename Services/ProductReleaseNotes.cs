namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Startup reads the update correctly";
    public const string ReleaseDate = "September 15, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Sati starts normally on a database that is simply due for the update",
            [
                "1.3.11 could stop at startup saying part of the update was already present, on a database where none of it had been applied.",
                "The check that compares an update against the database now ignores a change it cannot observe, instead of reading it as evidence the update had run.",
                "Nothing was ever written during that refusal, and no records were affected. This release lets the same database update normally.",
                "Everything delivered in 1.3.11 is unchanged and included."
            ]),
        new(
            "Each annual obligation belongs to its own effective date",
            [
                "PCP, assessment, reclassification, safety plan, privacy, releases, and reviews are now identified by the annual effective date they belong to, not by a deadline.",
                "Current and upcoming years stay separate, and a missing form stays visibly missing instead of borrowing a neighboring year's row.",
                "Comprehensive Assessment is available 120 days and due 90 days before the target; reclassification is due 30 days before it; reviews are due 90, 180, 270, and 360 days after."
            ]),
        new(
            "Completion is an attestation, with its real date",
            [
                "Recording completion asks for the date the work actually happened and keeps Sati's own received time separately.",
                "No document is required first. Notes, PDFs, and receipts remain useful evidence, not a second gate.",
                "A completed reclassification means the same year's assessment was completed too, so Sati asks for that actual date and records both attestations together."
            ]),
        new(
            "Billing follows the service date",
            [
                "The due day and the completion day are billable; a document that became overdue later cannot reach back and make an earlier service non-billable.",
                "Compliance requirements are now recorded as dated agency policy versions, so each note is judged by the rule in force on its own service date.",
                "A note inside a compliance gap can be held, or sent to a supervisor with a written justification. The justification carries the work into review; it does not release the billing."
            ]),
        new(
            "Supervisor exceptions and administrative recovery",
            [
                "A supervisor exception applies to one note and names the exact obligations it releases, with a reason and an explicit attestation.",
                "After every named obligation is genuinely satisfied, an administrator can recover otherwise valid approved notes through a checklist that freezes exactly what it relied on.",
                "Submitted and finalized billing is never silently rewritten; a policy correction raises a review flag instead."
            ]),
        new(
            "Releases are per recipient",
            [
                "Agency and medical releases now follow each active provider assignment, and every consumer has one annual DHHS release.",
                "Ending an assignment or withdrawing authorization applies going forward and never erases a completed release.",
                "Where a guardian exists, only the guardian signs these forms."
            ])
    ];
}
