namespace Sati.Contracts.V1;

/// <summary>
/// Owns the identity guard for a note generated from the sign-in agenda.
/// An exact annual obligation may have only one active agenda-created work item,
/// even after that item moves to another date or becomes a draft.
/// </summary>
public static class ScheduledAgendaNoteRules
{
    public const string InvalidIntentMessage =
        "Agenda-generated work must be a Scheduled Form note linked to one exact form obligation.";

    public static bool IsValidCreateIntent(
        bool isAgendaGenerated,
        string? status,
        string? noteType,
        int? formId,
        long? releaseObligationId) =>
        !isAgendaGenerated ||
        (string.Equals(status, "Scheduled", StringComparison.Ordinal) &&
         string.Equals(noteType, "Form", StringComparison.Ordinal) &&
         formId is > 0 &&
         releaseObligationId is null);

    public static bool ClaimsExactForm(int? status, int? formId, int exactFormId) =>
        formId == exactFormId && status is not (NoteWorkflow.Cancelled or NoteWorkflow.Abandoned);
}
