namespace Sati.Contracts.V1;

/// <summary>
/// The exact compliance task a form-work note documents. ActivityDate is the
/// date of work on the note, not the date the note was entered or submitted.
/// </summary>
public sealed record FormWorkNoteFact(
    int PersonId,
    string FormType,
    DateTime? ActivityDate,
    int? LinkedFormId);

public sealed record FormWorkObligationFact(
    int FormId,
    int PersonId,
    string FormType,
    DateTime DueDate,
    DateTime? CompletedOn);

public sealed record FormWorkBillingResult(
    bool Passed,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Billability of the note documenting a specific form. This is separate from
/// the ordinary historical billing window: completing an overdue form can
/// restore billing for later, unrelated work, but does not make the late form
/// work itself billable. A supervisor compliance exception or recovery must not
/// bypass this form-work decision.
/// </summary>
public static class FormWorkBillingRules
{
    public static FormWorkBillingResult Evaluate(
        FormWorkNoteFact note,
        FormWorkObligationFact? linkedForm)
    {
        ArgumentNullException.ThrowIfNull(note);

        // Releases have their own authorization and billing rules. This rule
        // deliberately does not impose a form-completion deadline on them.
        if (IsRelease(note.FormType))
            return new FormWorkBillingResult(true, []);

        if (note.LinkedFormId is not int formId || formId <= 0 || linkedForm is null ||
            linkedForm.FormId != formId ||
            linkedForm.PersonId != note.PersonId ||
            !string.Equals(linkedForm.FormType, note.FormType, StringComparison.Ordinal))
        {
            return new FormWorkBillingResult(false,
                ["This form-work note is not linked to its exact form obligation."]);
        }

        var reasons = new List<string>();
        if (note.ActivityDate is null)
            reasons.Add("This form-work note has no activity date.");
        if (linkedForm.CompletedOn is not DateTime completedOn)
        {
            reasons.Add("The linked form has no current completion attestation.");
        }
        else
        {
            if (note.ActivityDate is DateTime activityDate &&
                activityDate.Date != completedOn.Date)
            {
                reasons.Add("The form-work note's activity date does not match the attested completion date.");
            }

            if (completedOn.Date > linkedForm.DueDate.Date)
            {
                reasons.Add($"The linked form was completed after its {linkedForm.DueDate:MMM d, yyyy} due date.");
            }
        }

        return new FormWorkBillingResult(reasons.Count == 0, reasons);
    }

    /// <summary>Recognizes this rule's reasons in a supervisor queue projection.</summary>
    public static bool IsFormWorkReason(string reason) =>
        reason.StartsWith("This form-work note ", StringComparison.Ordinal) ||
        reason.StartsWith("The linked form ", StringComparison.Ordinal) ||
        reason.StartsWith("The form-work note's ", StringComparison.Ordinal);

    public static bool IsRelease(string formType) => formType is
        "Release_Agency" or "Release_DHHS" or "Release_Medical";
}
