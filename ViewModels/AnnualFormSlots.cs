using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Contracts.V1;
using Sati.Helpers;
using Sati.Models;

namespace Sati.ViewModels;

public enum AnnualFormSlotRole
{
    /// <summary>The obligation for the plan in force today (or, before admission, the first plan).</summary>
    Current,

    /// <summary>The obligation for the next plan, shown only while it is being prepared.</summary>
    Renewal
}

/// <summary>
/// One exact annual obligation as the client profile presents it. The slot names its
/// row by target effective date, so activating it can never open a different year's
/// record. Every status here is read from the shared rules; this type only words them.
/// </summary>
public sealed class AnnualFormSlotViewModel
{
    public AnnualFormSlotViewModel(
        FormType type,
        AnnualFormSlotRole role,
        DateTime targetEffectiveDate,
        Form? form,
        DateTime today)
    {
        Type = type;
        Role = role;
        TargetEffectiveDate = targetEffectiveDate.Date;
        Form = form;

        var date = today.Date;
        Status = FormCellStatusCalculator.Compute(form, date);
        OpeningDeadline = form is null
            ? null
            : BillingComplianceGate.OpeningDeadline(type.ToString(), form.DueDate);
        IsOpeningLate = form is { OpenedDate: null, CompletedDate: null } &&
                        OpeningDeadline is DateTime deadline &&
                        date > deadline.Date;
        StatusText = BuildStatusText(date);
    }

    public FormType Type { get; }
    public AnnualFormSlotRole Role { get; }
    public DateTime TargetEffectiveDate { get; }
    public Form? Form { get; }
    public FormCellStatus Status { get; }

    /// <summary>
    /// A recorded completion. This is what the checkbox has always shown; whether the
    /// completion is in force yet is carried by <see cref="Status"/>.
    /// </summary>
    public bool IsComplete => Form?.IsCompliant ?? false;

    public bool IsMissing => Form is null;
    public bool IsRenewal => Role == AnnualFormSlotRole.Renewal;
    public bool IsOverdue => Status == FormCellStatus.Overdue;
    public DateTime? DueDate => Form?.DueDate;
    public DateTime? OpenedDate => Form?.OpenedDate;
    public DateTime? CompletedDate => Form?.CompletedDate;

    /// <summary>The date the gate's opening obligation falls due, when the type has one.</summary>
    public DateTime? OpeningDeadline { get; }

    /// <summary>
    /// The opening deadline has passed and the document was never opened. Whether this
    /// blocks billing is the agency policy's decision and is reported by the gate.
    /// </summary>
    public bool IsOpeningLate { get; }

    public bool NeedsAttention => IsOverdue || IsOpeningLate || IsMissing;

    public string CheckBoxLabel => Role == AnnualFormSlotRole.Renewal
        ? $"Renewal for {TargetEffectiveDate:MM/dd/yy}"
        : AnnualFormSlots.Label(Type);

    public string PlanPhrase => Role == AnnualFormSlotRole.Renewal
        ? $"renewal for the plan starting {TargetEffectiveDate:MM/dd/yy}"
        : $"plan starting {TargetEffectiveDate:MM/dd/yy}";

    public string StatusText { get; }

    public string AutomationName =>
        $"{Person.FormDisplayName(Type)}, {PlanPhrase}: {StatusText}";

    private string BuildStatusText(DateTime today)
    {
        if (Form is null)
            return "No record exists for this plan";

        var parts = new List<string>();
        if (Form.CompletedDate is DateTime completed)
        {
            parts.Add($"Completed {completed:MM/dd/yy}");
            return string.Join(" · ", parts);
        }

        parts.Add(IsOverdue
            ? $"OVERDUE, was due {Form.DueDate:MM/dd/yy}"
            : $"Due {Form.DueDate:MM/dd/yy}");

        var (past, verb) = OpeningWords(Type);
        if (Form.OpenedDate is DateTime opened)
        {
            parts.Add($"{past} {opened:MM/dd/yy}");
        }
        else if (OpeningDeadline is DateTime deadline)
        {
            if (IsOpeningLate)
                parts.Add($"LATE, not {past.ToLowerInvariant()}; was due to {verb} by {deadline:MM/dd/yy}");
            else if (Role == AnnualFormSlotRole.Renewal || deadline.Date >= today)
                parts.Add($"{char.ToUpperInvariant(verb[0])}{verb[1..]} by {deadline:MM/dd/yy}");
        }

        return string.Join(" · ", parts);
    }

    // The assessment is "started" in the agency's own vocabulary; plans are "opened".
    private static (string Past, string Verb) OpeningWords(FormType type) =>
        type == FormType.ComprehensiveAssessment
            ? ("Started", "start")
            : ("Opened", "open");
}

/// <summary>
/// One annual document type in the client profile: the obligation in force, and the
/// renewal beside it while that renewal is being prepared. The row object is stable
/// so refreshing a slot updates bindings instead of rebuilding focused controls.
/// </summary>
public sealed partial class AnnualFormRowViewModel(FormType type) : ObservableObject
{
    public FormType Type { get; } = type;
    public string Label => AnnualFormSlots.Label(Type);

    [ObservableProperty]
    private AnnualFormSlotViewModel? current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRenewal))]
    private AnnualFormSlotViewModel? renewal;

    public bool HasRenewal => Renewal is not null;
}

/// <summary>
/// Chooses which exact annual obligations the client profile shows. Selection is
/// presentation; the timing it depends on comes from <see cref="ComplianceScheduleRules"/>.
/// </summary>
public static class AnnualFormSlots
{
    public static IReadOnlyList<FormType> Types { get; } =
    [
        FormType.PCP,
        FormType.ComprehensiveAssessment,
        FormType.Reclassification,
        FormType.SafetyPlan,
        FormType.PrivacyPractices
    ];

    public static string Label(FormType type) => type switch
    {
        FormType.ComprehensiveAssessment => "Comp Assessment",
        _ => Person.FormDisplayName(type)
    };

    public static (AnnualFormSlotViewModel? Current, AnnualFormSlotViewModel? Renewal) Resolve(
        Person? person,
        FormType type,
        DateTime today,
        ComplianceScheduleSettings schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (person?.EffectiveDate is not DateTime effectiveDate)
            return (null, null);

        var date = today.Date;
        var currentTarget = ComplianceScheduleRules.CurrentTargetEffectiveDate(effectiveDate, date);
        var current = new AnnualFormSlotViewModel(
            type,
            AnnualFormSlotRole.Current,
            currentTarget,
            person.GetCurrentCycleForm(type, date),
            date);

        var typeName = type.ToString();
        if (!ComplianceScheduleRules.HasRenewalOverlap(typeName))
            return (current, null);

        var upcomingTarget = ComplianceScheduleRules.UpcomingTargetEffectiveDate(effectiveDate, date);
        // Renewal rows are found by explicit identity only. A row without a target
        // is legacy data that cannot say which plan it belongs to.
        var upcoming = Person.FindFormForTargetEffectiveDate(person.Forms, type, upcomingTarget);
        var dueDate = upcoming?.DueDate ??
                      ComplianceScheduleRules.DueDate(typeName, upcomingTarget, schedule);
        var availableOn = ComplianceScheduleRules.AvailableOn(typeName, dueDate, schedule);
        if (!ComplianceScheduleRules.IsRenewalUnderway(
                availableOn, upcoming?.OpenedDate, upcoming?.CompletedDate, date))
            return (current, null);

        return (current, new AnnualFormSlotViewModel(
            type,
            AnnualFormSlotRole.Renewal,
            upcomingTarget,
            upcoming,
            date));
    }
}
