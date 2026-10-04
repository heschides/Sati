namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Approved-note amendments and provider organization";
    public const string ReleaseDate = "October 4, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Amend approved notes with a retained history",
            [
                "Notes Log includes approved-note amendments with original and proposed content together, a reason, retained versions, and an independent supervisor review.",
                "Returned proposals can be revised and resubmitted. Approval keeps the original note unchanged and identifies the exact reviewed amendment.",
                "Changes to service date, duration, time, or Unbilled status hold billing until a separate financial review. Narrative-only amendments leave billing unchanged.",
                "Existing submitted claims require an explicit linked correction; Sati does not automatically resend them. Draft-claim adjustments and cross-month corrections remain unavailable."
            ]),
        new(
            "Organize a consumer's providers",
            [
                "Filter provider assignments, reorder them with keyboard controls or drag and drop, and copy contact details.",
                "Ordering is saved for the consumer and conflicts ask you to refresh before replacing someone else's changes."
            ]),
        new(
            "Keep work current across the day",
            [
                "An open desktop refreshes date-dependent work when the day changes and can retain safe operational incident reports while the API is unavailable.",
                "Server maintenance and Claim.MD sandbox preparation have additional coordination safeguards. Their operational activation remains a separate controlled step."
            ]),
        new(
            "Write notes from a consumer's page",
            [
                "The Clients page has a Notes tab for adding or editing that consumer's notes and reviewing their note history in one place.",
                "If you have unsaved note changes, Sati asks before switching to another note or consumer. Saving refreshes the consumer's note list and dashboard."
            ]),
        new(
            "Read calendar unit counts more clearly",
            [
                "A day's main unit count includes Pending, Logged, and Approved units. Compliance blocked and Cancelled units remain visible in the breakdown without enlarging that main count.",
                "Past Scheduled work appears as Rescheduled in the daily breakdown, while the note keeps its Scheduled status. This label means planned work remains on a past day; it does not by itself mean someone moved the note.",
                "When a scheduled note is moved to another date, the calendar keeps a count on its earlier date while that note remains. Moves made before this update cannot be reconstructed."
            ]),
        new(
            "Explore service statistics",
            [
                "Statistics can group your caseload by week, month, quarter, or year and show all consumers or one consumer. It separates documented, Pending, compliance-blocked, and billable-marked versus intentionally unbilled work, with service-day and current overdue-Pending backlog counts.",
                "Claim units locked for billing are shown separately from original claim units with a recorded non-synthetic transmission. A transmitted claim is not evidence of payer receipt or payment; synthetic Demo transmissions are excluded.",
                "Form and Visit slices can overlap when one note has both activities, and their labels reflect the note's current activities. The overdue-Pending count describes unresolved work today, not when completed notes were once late."
            ]),
        new(
            "Scan note lists without losing detail",
            [
                "The Notes Log date filters have more room, and long note text now appears as a compact one-line preview in Notes Log and the Clients note lists.",
                "The full note remains available in the editor and through the preview's tooltip and screen-reader help."
            ]),
        new(
            "Record a consumer's schedule",
            [
                "The Clients profile now records doctor appointments and recurring day program or work hours, with a month calendar for a quick view.",
                "ModivCare ride status, pickup times, and a reference are staff-entered tracking fields; Sati does not book a ride with the broker."
            ]),
        new(
            "See productivity by month",
            [
                "Overview's productivity panel is more compact, with its calendar beside the headline numbers and arrows for previous months.",
                "Past months show secured units and the saved goal when available; current-month forecasts stay with the current month."
            ]),
        new(
            "Work with the client journal",
            [
                "The journal toolbar has smaller and larger text buttons. They change the reading size without changing the saved journal."
            ]),
        new(
            "Keep the active caseload current",
            [
                "Changing a consumer to Deceased or another archived status in Admin now updates the Clients menu without restarting Sati.",
                "Sati allows one desktop instance at a time on a computer, including across Demo and Local."
            ]),
        new(
            "Move through the Safety Device request",
            [
                "The Safety Device workspace reveals one section and one device editor at a time, then offers a review screen before saving the draft.",
                "Saved progress restores the section and populated device rows. Required signatures remain outside Sati."
            ]),
        new(
            "Save form answers and return later",
            [
                "Save progress preserves your entered answers in the DHHS, release, CWIC, Housing Support Funds, Safety Device, and Benefits Application workspaces.",
                "Resume saved restores your last saved answers and Benefits Application step. Saving progress does not sign or complete a form."
            ]),
        new(
            "Prepare the OFI benefits application",
            [
                "The Clients Forms tab now includes a step-by-step wizard for the April 2024 SNAP, TANF, and MaineCare application.",
                "Generate a draft PDF for applicant review and signing. Additional household members and insurance plans may require continuation sheets."
            ]),
        new(
            "Opening dates and completion dates are distinct",
            [
                "Comprehensive Assessments are available at least 30 days before completion is due; PCPs are available at least 90 days before completion is due, including with older zero-day settings.",
                "Form choices show Open by and Complete by separately. PCPs also show their effective date."
            ]),
        new(
            "Choose a quieter Modern palette",
            [
                "Modern Stone, Sand, Taupe, Sage, and Clay combine deeper neutral surroundings with light reading areas.",
                "Settings groups themes by style and alphabetizes choices within each group."
            ]),
        new(
            "Prepare the OADS Safety Device request",
            [
                "Clients and Documents now has a Safety Device tab for entering device details, planning answers, and the team meeting date.",
                "Generate PDF fills the April 2026 OADS form and saves a versioned draft. Review and obtain the required signatures outside Sati before submission."
            ]),
        new(
            "Annual plan notes show their year and billing status",
            [
                "A Person-Centered Plan note can be marked Annual for one confirmed plan-year step. Sati shows which year it belongs to and offers a non-annual revision when the date is outside that year's window.",
                "Late annual work stays in the clinical record as Unbilled. Form notes also reuse the existing active note for the same work instead of creating a duplicate."
            ]),
        new(
            "Form and notes corrections",
            [
                "The DHHS Authorization to Release PDF now accepts every disclosure-purpose and general-record choice shown in the form workspace.",
                "Goal Progress starts at None for form work unless you chose another value, and the Notes Log keeps its All Persons filter after loading."
            ]),
        new(
            "Create a one-off release without provider setup",
            [
                "Provider requirements and One-off releases now sit side by side in the Releases workspace, so an occasional recipient does not have to be entered in the provider directory first.",
                "Each one-off release keeps its own recipient details and follows the same Generated, Prepared, and Completed path as a provider release. External signing remains the default; an agency-enabled Sati signature invitation also shows Delivered after Sati records successful delivery.",
                "Separate one-off recipients in the same service year remain separate documents, with their own retained identity and signing evidence."
            ]),
        new(
            "Turn a prepared recipient into a provider",
            [
                "After preparing a one-off release, choose Add to provider list to open a review window with the recipient details already filled in.",
                "You can save the directory entry alone or also assign it to the consumer. The prepared document stays intact and records the provider it became without being rewritten as a different release."
            ]),
        new(
            "Representative Payee choices stay responsive",
            [
                "Changing the Representative Payee Yes or No choice no longer lets the radio button being cleared write a competing value and interrupt the Clients workspace."
            ]),
        new(
            "Releases follow a clear, visible path",
            [
                "Each agency release now moves through Generated, Prepared, and Completed, so the page shows what has happened and what comes next without treating PDF creation as completion.",
                "Prepare for delivery opens an editable release panel with a smooth horizontal transition. Back and final preparation return to the obligation without losing the workflow context.",
                "The optional Sati-hosted signing path will add Delivered only after Sati can prove that it sent the invitation. It remains off unless an agency Admin and the environment both enable it."
            ]),
        new(
            "Externally signed releases keep their evidence together",
            [
                "External signing is the default: generate the final PDF, have it signed on paper or through an approved signing service, then upload the exact signed PDF to Sati.",
                "Completion requires staff to attest to the document, signer identity and authority, and visible signature and date. Sati keeps the signed PDF with an integrity hash and links the evidence only to the selected release obligation.",
                "Uploading a file does not independently prove that a signature is legally valid. The staff attestation records who reviewed it and what they confirmed."
            ]),
        new(
            "Sati shows that it is preparing your workspace",
            [
                "After you sign in, a window says \"We are preparing the Sati workspace.\" and stays until your workspace has appeared. It closes on its own, including when startup fails and Sati reports the problem.",
                "While you wait, it shows a short original Sati reflection that changes every 20 seconds. A screen reader can read the reflection, but a change does not interrupt it.",
                "The three-second pause before the sign-in window is gone."
            ]),
        new(
            "Startup loads only what you see first",
            [
                "Your caseload is loaded once at sign-in and shared by Overview, Clients, and Notes Log. Startup no longer reads note narratives or visit details for the whole caseload.",
                "Notes Log, Calendar, Billing, and Supervisor load the first time you open them, and switching accounts clears what they loaded.",
                "The first visit to Notes Log still reads every note for your caseload, so it can take a moment with a large caseload."
            ]),
        new(
            "Billing Overview reads totals, not the full history",
            [
                "Billing Overview now asks for the draft total and six monthly totals instead of every billing period and claim line. Accounts with only Billing access still open to a filled-in Overview.",
                "Billing's checks of approved notes no longer read note narratives, visit details, client journals, or encrypted identity fields."
            ]),
        new(
            "Unfinished work can stay on today",
            [
                "When you close Sati, each scheduled item that is not done can now be kept on today, for when you will open Sati again before the day ends. \"Keep all on today\" sets every item at once.",
                "An item left from an earlier day moves to today instead, because it cannot stay on a finished day. Its old start time is cleared.",
                "Moving to your next workday is still selected first, so pressing Enter works as it did before. Sati asks about kept items again the next time it closes."
            ]),
        new(
            "Updates wait until Sati is closed",
            [
                "The Demo and Local installers check for a running Sati or Sati Demo before replacing application files.",
                "If Sati is open, the installer stops with a clear message and leaves the existing installation unchanged. Close Sati, then run the installer again."
            ]),
        new(
            "One form task stays one agenda item",
            [
                "Reclassification and review work linked to an exact form is no longer scheduled again when its display wording changes.",
                "During this update, only untouched transition duplicates with matching details are retired from the agenda. Their records are retained for audit, not deleted, and anything ambiguous is left unchanged for review."
            ]),
        new(
            "The form note can supply its own completion date",
            [
                "An exact form note can now be marked Logged using the completion date it is documenting, while every unrelated billing safeguard still applies.",
                "If a newer annual cycle is waiting, Sati does not move evidence from the older cycle. Continuing requires a written case-manager justification and supervisor review."
            ]),
        new(
            "Large Outlook calendars import by streaming",
            [
                "The Outlook calendar import no longer stops at the former 20 MiB file limit. It reads large ICS files incrementally instead of holding several full copies in memory.",
                "Unsupported attachments and descriptions are skipped during import, and bounded safety limits still protect the application from malformed or unreasonably large calendar data."
            ]),
        new(
            "Legacy Dark has four clear depths",
            [
                "Legacy Dark now follows a consistent 1-to-4 depth hierarchy in the same warm palette: lightest fields, distinct navigation, darker content panels, and the deepest inset work areas."
            ]),
        new(
            "Scheduled form work becomes a dated draft",
            [
                "If a form has one linked Scheduled note, its checkmark shows the planned note date beside the actual completion date you selected.",
                "Confirming changes that same note to a Pending draft on the work date and records the completion. The note still needs to be written and logged.",
                "The client profile refreshes its form status after the save, without restarting Sati."
            ]),
        new(
            "One note can describe several activities",
            [
                "Choose Visit, Phone, Email, Form, or Other together when they belong in one note. Reminder remains a separate, nonbillable item.",
                "A note that includes late form work cannot be billed, even when it also describes a call or visit."
            ]),
        new(
            "Checkmarks and notes use the same evidence",
            [
                "Checking a form or release creates a linked draft note when none exists. If the same work is already in a note with the same date, Sati uses that note instead.",
                "If the dates disagree, Sati stops and explains how to correct the existing note and attestation. A supervisor handles a submitted note before a claim line exists; Admin handles it afterward."
            ]),
        new(
            "Admin corrections keep billing history visible",
            [
                "An Admin can correct the source date of a claimed form or release note with an explanation and confirmed evidence. The old claim service date remains in the record and billing receives a review flag.",
                "A genuinely late form note remains nonbillable unless its wrong source fact is corrected and the normal billing rule passes."
            ]),
        new(
            "Legacy Dark joins the theme choices",
            [
                "Legacy Dark keeps Legacy's leaf and Palatino type, with Black Bean backgrounds, warmer Sienna panels, and light Bone fields with dark text. Choose it in Settings."
            ]),
        new(
            "Annual form dates identify the right plan year",
            [
                "Annual Forms now distinguishes the current plan from the renewal being prepared, so a completed assessment is not mistaken for last year's plan.",
                "The default Comprehensive Assessment due date is 90 calendar days before the plan starts. A December 16 plan has a September 17 assessment due date."
            ]),
        new(
            "Form notes and billing stay tied to the exact work",
            [
                "When a form-work note becomes Logged, its activity date attests the selected form. Late work remains in the clinical record but its note cannot be billed.",
                "Changes after supervisor or billing handoff create visible review flags. An Admin may correct a technical date error with a recorded explanation and source evidence before billing eligibility is recalculated.",
                "Saved forms and their linked notes are retained. The form screen explains why deletion is unavailable and directs staff to the correction workflow."
            ]),
        new(
            "Today's Work has a clear focus cue",
            [
                "The red outline that could surround the whole Today's Work panel was misleading focus decoration, not an error. It has been removed.",
                "Real loading and saving problems still appear as labeled messages with an action to try again.",
                "Keyboard navigation remains visible: the focused tab now has an accent outline and a bold label."
            ]),
        new(
            "Annual Forms is one clear workspace",
            [
                "The client profile now keeps Overview, Releases, DHHS Documents, Safety Plan, and Privacy Practices together under Annual Forms.",
                "Overview is the starting point. It shows the complete service-year range, tells you when that year has loaded, and gives every workflow row a direct Next step button to the right tab.",
                "Viewing a different year only loads its saved work. It does not change historical records."
            ]),
        new(
            "The document you generate is the document you submit",
            [
                "State and agency forms are now plainly labeled as entry workspaces instead of showing a second, approximate drawing of the form.",
                "Generate PDF fills the retained source document. That generated PDF is the copy to review, sign, save, and submit.",
                "A live preview remains only where Sati owns the document and the preview faithfully represents the generated output."
            ]),
        new(
            "Profile photos are cropped and made safe to store",
            [
                "After choosing a phone photo, you can position a square crop before saving it.",
                "Sati turns rotated photos upright, makes a compact 512-pixel JPEG, and removes camera metadata such as GPS location.",
                "Large phone photos and motion photos can now be prepared without weakening the upload limits that protect the database and API."
            ]),
        new(
            "The client journal has pages, formatting, and checkboxes",
            [
                "A journal can now have named pages, bold, italic, and underlined text, plus checkboxes for items you want to track.",
                "Existing plain-text journals still open normally and become a paged journal only after you edit them in the new editor.",
                "Checking an item can put its text on the calendar for a future date after you confirm it; cancelling leaves the journal item unchanged."
            ]),
        new(
            "Old scheduled items no longer distort today's calendar",
            [
                "A Scheduled item dated before today is treated as lapsed and stays off the calendar, the Overview thumbnail, supervisor workload, and productivity forecasts.",
                "The item is not deleted. The existing close-time cleanup still lets you move it to the next workday or remove it."
            ]),
        new(
            "Demo billing can record deposits and prepare claim corrections",
            [
                "A remittance can now record the bank deposit that funded it, and a correction keeps the earlier entry while requiring a reason.",
                "In Demo, imported payer responses can identify a rejected or adjudicated claim and prepare a resend, replacement, or void without changing the original claim.",
                "Local Production does not import those responses, so claim corrections are not available there. Payer companion-guide acceptance must still be confirmed before real submission."
            ]),
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
