namespace Sati.Contracts.V1;

/// <summary>
/// Form notes naming billable form work must identify the exact obligation.
/// Drafts and release notes may remain unlinked; historical notes are never
/// assigned a form by inference.
/// </summary>
public static class FormNoteLinkRules
{
    public static string? Validate(
        string? noteType,
        string? formType,
        string? status,
        int? formId,
        string? correctionReason,
        int? activities = null)
    {
        if (correctionReason?.Length > 1_000)
            return "A form date correction explanation cannot exceed 1,000 characters.";

        if (!NoteActivityRules.Has(activities, noteType, NoteActivity.Form))
            return formId is null ? null : "Only a Form note may select a form obligation.";

        if (string.IsNullOrWhiteSpace(formType))
            return "Choose the form type for this note.";

        if (formId is <= 0)
            return "Choose a valid form obligation.";

        if (string.Equals(status, "Logged", StringComparison.Ordinal) &&
            !IsRelease(formType) && formId is null)
            return "Select the exact review or annual form obligation before submitting this Form note.";

        return null;
    }

    public static bool IsRelease(string? formType) => formType is
        "Release_Agency" or "Release_DHHS" or "Release_Medical";
}
