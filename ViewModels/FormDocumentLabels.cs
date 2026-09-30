using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.ViewModels;

/// <summary>Names the document's own dates without changing its annual identity.</summary>
public static class FormDocumentLabels
{
    public static string AssessmentDates(DateTime dueDate, string dateFormat = "MM/dd/yy") =>
        OpeningAndCompletionDates(FormType.ComprehensiveAssessment, dueDate, dateFormat);

    public static string PcpDates(Form form, string dateFormat = "MM/dd/yy")
    {
        var dates = OpeningAndCompletionDates(FormType.PCP, form.DueDate, dateFormat);
        return form.TargetEffectiveDate == default
            ? dates
            : $"{dates}; effective {form.TargetEffectiveDate.ToString(dateFormat)}";
    }

    private static string OpeningAndCompletionDates(FormType type, DateTime dueDate, string dateFormat)
    {
        var openBy = BillingComplianceGate.OpeningDeadline(
            type.ToString(), dueDate)!.Value;
        return $"open by {openBy.ToString(dateFormat)}; complete by {dueDate.ToString(dateFormat)}";
    }

    public static string NoteSelection(Form form)
    {
        var name = Person.FormDisplayName(form.Type);
        if (form.Type == FormType.ComprehensiveAssessment)
            return $"{name} — {AssessmentDates(form.DueDate, "M/d/yy")}";
        if (form.Type == FormType.PCP)
            return $"{name} — {PcpDates(form, "M/d/yy")}";
        if (form.TargetEffectiveDate == default)
            return $"{name} — due {form.DueDate:M/d/yy}";
        return $"{name} — plan starting {form.TargetEffectiveDate:M/d/yy}; due {form.DueDate:M/d/yy}";
    }
}
