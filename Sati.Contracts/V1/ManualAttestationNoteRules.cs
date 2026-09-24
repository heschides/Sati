namespace Sati.Contracts.V1;

/// <summary>Guidance when a manual completion conflicts with its exact linked note.</summary>
public static class ManualAttestationNoteRules
{
    public const string ScheduledDuplicateCancellationAuditAction =
        "note.scheduled-duplicate-cancelled";
    public const string ScheduledConversionRequiredCode = "scheduled_form_note_conversion_required";
    public const string ScheduledNoteChangedMessage =
        "The Scheduled note changed before confirmation. Refresh the form and try again.";

    public const string UnlinkedFormNoteMessage =
        "An existing unlinked form note may already document this work, so Sati cannot create a second note. " +
        "Open that note and confirm its actual activity date. If it is editable, use FORM OBLIGATION / PLAN YEAR " +
        "below Form Type to select this plan. Submit it as Logged only after the work is complete. " +
        "If it is no longer editable, ask a supervisor to return it for correction. " +
        "If a billing claim line exists, contact Admin instead.";

    public static string? Conflict(
        DateTime attestedOn,
        DateTime? noteDate,
        int? noteStatus,
        bool hasClaimLine)
    {
        if (noteDate?.Date == attestedOn.Date &&
            noteStatus is NoteWorkflow.Pending or NoteWorkflow.Logged or NoteWorkflow.Approved)
            return null;

        if (noteStatus == NoteWorkflow.Scheduled && !hasClaimLine)
        {
            var plannedDate = noteDate is DateTime date
                ? date.ToString("MMM d, yyyy")
                : "undated";
            return $"Linked note: Scheduled for {plannedDate} (planned). " +
                $"Completion: {attestedOn:MMM d, yyyy} (actual). " +
                "Open the note, set its actual date, and save as Pending before retrying the checkmark, " +
                "or submit it as Logged to attest.";
        }

        var action = hasClaimLine
            ? "Contact an Admin. This note has a billing claim line, so its correction must follow the audited claim-correction workflow."
            : noteStatus is NoteWorkflow.Logged or NoteWorkflow.Approved
                ? "Contact a supervisor to return the note for correction before billing."
                : "Open the existing note and correct its activity date or status before attesting.";
        return $"The existing note does not agree with the completion date {attestedOn:MMM d, yyyy}. " +
            "If an earlier attestation is wrong, revoke it with a reason; its history must be retained. " +
            action;
    }

    public static bool CanConvertScheduledFormNote(
        int? status,
        string? noteType,
        int? activities,
        bool hasClaimLine) =>
        status == NoteWorkflow.Scheduled &&
        !hasClaimLine &&
        string.Equals(noteType, "Form", StringComparison.Ordinal) &&
        NoteActivityRules.Effective(activities, noteType) == NoteActivity.Form;

    /// <summary>
    /// Whether an exact-linked note must participate in manual-attestation
    /// evidence resolution. Status alone is deliberately insufficient to ignore
    /// a row: even a cancelled note may retain protected evidence or review
    /// references. The duplicate-repair migration is the one narrow exception,
    /// because it emits a dedicated audit event only after proving the cancelled
    /// Scheduled row has no such references.
    /// </summary>
    public static bool CompetesForManualAttestation(
        int? status,
        bool hasScheduledDuplicateCancellationAudit) =>
        status != NoteWorkflow.Cancelled ||
        !hasScheduledDuplicateCancellationAudit;

    public static string ScheduledConversionPrompt(
        string formName,
        DateTime? plannedOn,
        DateTime completedOn)
    {
        var planned = plannedOn is DateTime date
            ? date.ToString("MMM d, yyyy")
            : "undated";
        return $"{formName}: planned note {planned} → actual work {completedOn:MMM d, yyyy}. " +
            "Use this note as a Pending draft and record completion? The note still needs writing.";
    }

    public static string ScheduledConversionToken(
        int noteId,
        int revision,
        DateTime? plannedOn,
        DateTime completedOn) =>
        $"{noteId}:{revision}:{plannedOn?.Ticks ?? 0}:{completedOn.Date.Ticks}";
}
