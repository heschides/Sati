namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Unfinished plans move on at the end of the day";
    public const string ReleaseDate = "September 18, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Closing Sati tidies up scheduled work that did not get done",
            [
                "When you close Sati, any scheduled item dated today or earlier that is still only scheduled is listed, with its client and type.",
                "For each one, choose to move it to your next workday or delete it. Moving is already selected, so pressing Enter never loses anything. \"Move all\" and \"Delete all\" set every item at once.",
                "The next workday skips weekends, agency holidays and excluded weekdays, and your own time off.",
                "Keep Sati open changes nothing. Notes you have started, logged, or had approved are never listed, and paperwork that is still due returns on the daily agenda even if you delete its plan.",
                "The first time you close after updating, the list may include older plans left on past days. Clearing them also stops those days being held open in Documented Avg."
            ]),
        new(
            "A Legacy theme with the original leaf",
            [
                "Settings now offers Legacy: the original copper leaf and window icon, warm blush-cream colors, and Palatino type throughout, including the splash and sign-in screens.",
                "Every other theme keeps the watercolor leaf. The desktop shortcut's icon does not change."
            ]),
        new(
            "Projected and Secured per day now count past days you can still write up",
            [
                "These figures divided only by the days ahead, so they assumed every remaining unit had to come from new service. If you document in batches, that read far too high.",
                "They now also divide by past workdays whose notes are not written yet and whose documentation window is still open. The work happened; writing it up still produces units.",
                "FUTURE DAYS is now DAYS TO CAPTURE, and shows the split, for example \"14 days · 10 ahead · 4 to write up\".",
                "A past day leaves that count as soon as you write it up or mark it as having no billable work."
            ]),
        new(
            "Marking a day with no billable work",
            [
                "Some workdays produce nothing billable and are not time off. Tick such a day on the calendar and it counts in Documented Avg as a zero, which is honest: the month still requires the same units, so the remaining days have to make them up.",
                "It also stops being counted as work waiting to be written, so the pace tells the truth that day rather than a week later.",
                "Time off is still separate, and still right-click. Time off lowers the monthly requirement; a day with no billable work does not."
            ]),
        new(
            "A reminder before a day falls out of the billing window",
            [
                "At sign-in and again when closing Sati, any workday with nothing documented whose window closes today or tomorrow is named, with the last day you can still document it.",
                "Closing Sati offers to stay open so you can write them. The reminder lists dates only.",
                "After that window closes the day cannot be billed, its units are gone, and the pace needed for every remaining day goes up."
            ]),
        new(
            "Supervisors see settled days with no billable work",
            [
                "Your supervisor's monthly productivity list shows how many days you marked as having no billable work, and which dates, once each day's documentation window has closed.",
                "While a day is still inside its window it stays yours: you can still write it up, and it is not shown."
            ]),
        new(
            "Writing up one note no longer drags your average down",
            [
                "Documenting a single 15-minute review on Monday used to put Monday into your Documented Avg at one unit, and it stayed low until you wrote up the rest of that day.",
                "A day now joins the average once it looks finished: it has documented work and nothing left on its schedule. Until then it shows plainly on the calendar and says \"Not counted yet\".",
                "Each open day's square has a tick box. Tick it to count that day now, or clear it to hold a day back. The tick is only offered while the day is still inside its 7-day documentation window.",
                "After that window closes the day counts on its own, whatever is left scheduled on it, so a tick you forget cannot hold a real service day out of your average.",
                "Under Documented Avg the panel now says what it divided by, for example \"6 days · 2 still open\"."
            ]),
        new(
            "Right-click a day in Month view to schedule time off",
            [
                "The Month view now takes the same right-click as the Year view: it marks a day as time off, or restores it. Time off still lowers your monthly requirement, and is separate from whether a day counts toward your average."
            ]),
        new(
            "The calendar shows which days count toward your average",
            [
                "A day counts once it carries a pending, logged, or approved note, from the first of this month through today. Those days are tinted light green in the month and year calendars.",
                "A day counted only through pending notes is a paler green with a pink border: it is in the average, but nothing on it is logged or approved yet.",
                "Future days are never tinted, because they are not in the average yet. Today is tinted only once it has a note of its own.",
                "Each day square now lists its units and the statuses they are in, for example \"6 units · Pending 2 · Logged 4\".",
                "Overview shows the same month as a small read-only picture under your productivity bar."
            ]),
        new(
            "Documented Avg no longer counts days that have not happened",
            [
                "The average divided your units by every day carrying a pending note, including days still in the future. A visit scheduled for next week therefore lowered the average you saw today.",
                "It now divides only by days through today. If you schedule notes ahead, your Documented Avg will read higher than it did before this release.",
                "Nothing about billing, incentives, or note status changed. Secured, Recoverable, and the pace figures are unchanged."
            ]),
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
