using System.Globalization;

namespace Sati.Contracts.V1;

/// <summary>
/// Owns the note shape that makes one exact form attestation part of the note's
/// atomic Logged transition. Callers must not infer this from a Form label alone.
/// </summary>
public static class FormNoteAttestationRules
{
    public static bool AttestsExactFormOnLog(
        string? status,
        int? activities,
        string? noteType,
        string? formType,
        int? formId) =>
        string.Equals(status, "Logged", StringComparison.Ordinal) &&
        IsExactNonReleaseFormActivity(activities, noteType, formType, formId);

    public static bool IsExactNonReleaseFormActivity(
        int? activities,
        string? noteType,
        string? formType,
        int? formId) =>
        !string.IsNullOrWhiteSpace(formType) &&
        formId is > 0 &&
        NoteActivityRules.Has(activities, noteType, NoteActivity.Form) &&
        !FormNoteLinkRules.IsRelease(formType);
}

public sealed record AnnualFormCycleAmbiguity(
    int SelectedFormId,
    DateTime SelectedTargetEffectiveDate,
    int RenewalFormId,
    DateTime RenewalTargetEffectiveDate,
    DateTime RenewalAvailableOn)
{
    public string Reason =>
        "This completion selects plan target " +
        SelectedTargetEffectiveDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) +
        ", but the incomplete renewal for plan target " +
        RenewalTargetEffectiveDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) +
        " was already available on " +
        RenewalAvailableOn.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) +
        ". Select the renewal if that is the work you completed. If the older plan is intentional, " +
        "document it through an exact linked Form note, enter a written justification, and send the " +
        "nonbillable note to your supervisor. Sati will not move the evidence automatically.";
}

/// <summary>
/// Detects an exact older annual-form selection made after the next incomplete
/// renewal became available. The rule never redirects, completes, or moves
/// evidence; it only requires the existing written-justification review path.
/// </summary>
public static class AnnualFormCycleDisambiguationRules
{
    public static AnnualFormCycleAmbiguity? Evaluate(
        int personId,
        string? formType,
        int? selectedFormId,
        DateTime? activityDate,
        IReadOnlyCollection<FormFact> forms,
        ComplianceScheduleSettings schedule)
    {
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentNullException.ThrowIfNull(schedule);

        if (personId <= 0 || selectedFormId is not int formId || formId <= 0 ||
            activityDate is not DateTime occurredOn ||
            string.IsNullOrWhiteSpace(formType) ||
            !ComplianceScheduleRules.HasRenewalOverlap(formType))
            return null;

        var selected = forms.SingleOrDefault(candidate =>
            candidate.FormId == formId &&
            candidate.PersonId == personId &&
            string.Equals(candidate.FormType, formType, StringComparison.OrdinalIgnoreCase));
        if (selected?.TargetEffectiveDate is not DateTime selectedTarget ||
            selectedTarget == default)
            return null;

        selectedTarget = selectedTarget.Date;
        var renewal = forms
            .Where(candidate =>
                candidate.FormId != selected.FormId &&
                candidate.PersonId == personId &&
                string.Equals(candidate.FormType, selected.FormType,
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.CompletedDate is null &&
                candidate.TargetEffectiveDate is DateTime candidateTarget &&
                candidateTarget != default &&
                candidateTarget.Date > selectedTarget &&
                ComplianceScheduleRules.AvailableOn(
                    candidate.FormType, candidate.DueDate, schedule).Date <= occurredOn.Date)
            .OrderBy(candidate => candidate.TargetEffectiveDate)
            .ThenBy(candidate => candidate.FormId)
            .FirstOrDefault();
        if (renewal?.TargetEffectiveDate is not DateTime renewalTarget)
            return null;

        return new AnnualFormCycleAmbiguity(
            selected.FormId,
            selectedTarget,
            renewal.FormId,
            renewalTarget.Date,
            ComplianceScheduleRules.AvailableOn(
                renewal.FormType, renewal.DueDate, schedule).Date);
    }
}
