using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.ViewModels;

/// <summary>
/// Matrix summary for every exact recipient obligation in one release category.
/// A category is not represented by one synthetic form: two medical providers are
/// two independent rows and both must be reflected here.
/// </summary>
public sealed class ReleaseCellViewModel
{
    private readonly IReadOnlyList<ReleaseComplianceFact> _obligations;
    private readonly IReadOnlyList<ReleaseComplianceFact> _outstanding;
    private readonly bool _universalDhhsMissing;
    private readonly DateTime _today;

    public ReleaseCellViewModel(
        Person person,
        ReleaseObligationCategory category,
        DateTime today)
    {
        ArgumentNullException.ThrowIfNull(person);
        var date = today.Date;
        _today = date;
        var target = person.EffectiveDate is DateTime effective
            ? ComplianceScheduleRules.CurrentTargetEffectiveDate(effective, date)
            : (DateTime?)null;
        var facts = ((IEventSource)person).ReleaseComplianceFacts;
        _obligations = target is null
            ? []
            : facts
                .Where(item => item.Category == category &&
                               item.TargetEffectiveDate?.Date == target.Value.Date &&
                               (item.RetiredOn is null || date < item.RetiredOn.Value.Date))
                .OrderBy(item => item.DueOn)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal)
                .ToList();
        _outstanding = _obligations
            .Where(item => ReleaseAttestationRules.CompletedOn(
                    item.StableKey, item.Attestations) is not DateTime completed ||
                completed.Date > date)
            .ToList();
        _universalDhhsMissing = category == ReleaseObligationCategory.Dhhs &&
                                target is not null && _obligations.Count == 0;

        DueDate = _outstanding.FirstOrDefault()?.DueOn.Date ??
                  (_universalDhhsMissing ? target : null);
        IsOpen = _outstanding.Any(item =>
            date >= (item.AvailableOn ?? item.DueOn).Date);
        Status = ComputeStatus(date);
    }

    public FormCellStatus Status { get; }
    public DateTime? DueDate { get; }
    public bool IsOpen { get; }

    public string CellText
    {
        get
        {
            if (_universalDhhsMissing)
                return "NOT SET UP" + (DueDate is DateTime missingDue
                    ? $"{Environment.NewLine}Due: {missingDue:M/d/yy}"
                    : "");
            if (_obligations.Count == 0)
                return "Not required";
            if (_outstanding.Count == 0)
                return _obligations.Count == 1
                    ? "Complete"
                    : $"Complete: {_obligations.Count}/{_obligations.Count}";

            var completedCount = _obligations.Count - _outstanding.Count;
            var prefix = DueDate is DateTime due && due < _today
                ? $"OVERDUE: {completedCount}/{_obligations.Count} complete"
                : $"Open: {_outstanding.Count}/{_obligations.Count} remaining";
            return DueDate is DateTime dueDate
                ? $"{prefix}{Environment.NewLine}Due: {dueDate:M/d/yy}"
                : prefix;
        }
    }

    private FormCellStatus ComputeStatus(DateTime today)
    {
        if (!_universalDhhsMissing && _obligations.Count == 0)
            return FormCellStatus.Complete;
        if (_outstanding.Count == 0 && !_universalDhhsMissing)
            return FormCellStatus.Complete;
        if (DueDate is not DateTime due)
            return FormCellStatus.NotYetOpen;
        if (due.Date < today)
            return FormCellStatus.Overdue;
        if (due.Year == today.Year && due.Month == today.Month)
            return FormCellStatus.DueThisMonth;
        var nextMonth = today.AddMonths(1);
        return due.Year == nextMonth.Year && due.Month == nextMonth.Month
            ? FormCellStatus.DueNextMonth
            : FormCellStatus.NotYetOpen;
    }
}
