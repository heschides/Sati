namespace Sati.Contracts.V1;

/// <summary>Guidance when a manual completion conflicts with its exact linked note.</summary>
public static class ManualAttestationNoteRules
{
    public static string? Conflict(
        DateTime attestedOn,
        DateTime? noteDate,
        int? noteStatus,
        bool hasClaimLine)
    {
        if (noteDate?.Date == attestedOn.Date &&
            noteStatus is NoteWorkflow.Pending or NoteWorkflow.Logged or NoteWorkflow.Approved)
            return null;

        var action = hasClaimLine
            ? "Contact an Admin. This note has a billing claim line, so its correction must follow the audited claim-correction workflow."
            : noteStatus is NoteWorkflow.Logged or NoteWorkflow.Approved
                ? "Contact a supervisor to return the note for correction before billing."
                : "Open the existing note and correct its activity date or status before attesting.";
        return $"The existing note does not agree with the completion date {attestedOn:MMM d, yyyy}. " +
            "If an earlier attestation is wrong, revoke it with a reason; its history must be retained. " +
            action;
    }
}
