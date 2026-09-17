namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "The client profile and notes log keep working";
    public const string ReleaseDate = "September 17, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "The Clients panel no longer stops responding",
            [
                "In 1.3.14, the annual forms area could fail while drawing, with an \"unexpected problem\" message. After that the Clients panel stopped responding until Sati was restarted.",
                "The panel now draws normally, including for a client with no current annual forms."
            ]),
        new(
            "Mark Note Logged says why it cannot",
            [
                "When a note cannot be marked Logged, for example because it has no goal progress, the notes log now says why instead of showing an \"unexpected problem\" message.",
                "The note keeps its previous status on screen, because nothing was saved. Choose a goal progress in the note (None is allowed) and mark it Logged again.",
                "Hold for Compliance and Send to Supervisor behave the same way when a change is refused."
            ]),
        new(
            "Note menus act on the note you point at",
            [
                "Right-clicking the notes log or the dashboard notes list opens Mark Note Logged or Delete Note only over a note, never over the column headings.",
                "If you choose to keep an unsaved edit when right-clicking another note, the menu does not open, so it cannot act on the note you were editing."
            ]),
        new(
            "Hidden assessment and plan tabs stay quiet",
            [
                "When an agency has the Comprehensive Assessment or Person-Centered Plan workflow turned off, Sati no longer tries to load it in the background each time a client is selected."
            ]),
        new(
            "Client edits save again",
            [
                "Since 1.3.11, saving a client that had release records stopped with \"Client Save Status Unconfirmed\". The changes were not saved, although the message could not say so.",
                "Saving a client now writes only that client's own details and any new forms, never the rest of the loaded record.",
                "A client changed somewhere else after you opened it, or a change the database refuses, now says plainly that nothing was saved.",
                "Provider assignments were never affected; they save from their own panel."
            ]),
        new(
            "Next year's documents appear before they are due",
            [
                "PCP, Comp Assessment, Reclassification, Safety Plan, and Privacy Practices show the plan in force and, once next year's document is available, an indented renewal checkbox for it.",
                "The renewal becomes the only checkbox on its plan's start date, finished or not. A renewal completed early stays beside the current plan until then.",
                "Each line says in words whether it is complete, due, overdue, opened, or late to open or start; the assessment is started 120 days before the plan.",
                "Quarterly reviews and releases are unchanged."
            ]),
        new(
            "Monthly contact",
            [
                "The client list shows each client's last visit, phone call, or email, in red with \"overdue\" after 30 days. The client profile's Last contact uses the same rule and now counts visits.",
                "An agency can choose to require monthly contact for billing. It is off by default: when on, service more than 30 days after the last contact, or after the plan's effective date, waits for the next contact, and the contact's own day is billable.",
                "An agency can also choose to require the Comprehensive Assessment to be started 120 days before the plan. That is off by default too."
            ]),
        new(
            "Leap-day plans",
            [
                "A client whose plan began on February 29 now has next year's renewal found on February 29 of a leap year, instead of a February 28 date no record carries.",
                "No existing records were affected; the first affected date would have been February 28, 2027."
            ]),
        new(
            "Sati starts on a database that has not had the annual compliance update",
            [
                "1.3.12 could still stop at startup saying part of the update was already present, on a database where none of it had been applied.",
                "The update rebuilds one existing index in place. The check that compares an update against the database recognised the old index as the new one, and read that as evidence the update had run.",
                "An index an update drops and rebuilds under its own name looks the same before and after, so it is no longer treated as proof of anything. An index that is missing still counts.",
                "Nothing was written during that refusal, and no records were affected. Everything delivered in 1.3.11 and 1.3.12 is unchanged and included."
            ]),
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
