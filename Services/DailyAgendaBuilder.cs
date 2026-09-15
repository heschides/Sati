using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.Services;

public enum DailyAgendaItemKind
{
    OverdueForm,
    UpcomingWork,
    SuggestedAssessment
}

public sealed record DailyAgendaItem(
    string Key,
    int PersonId,
    string PersonName,
    string Title,
    DateTime DueDate,
    DailyAgendaItemKind Kind,
    bool BlocksBilling,
    FormType? FormType = null,
    int? FormId = null,
    DateTime? TargetEffectiveDate = null,
    Guid? ReleaseObligationId = null)
{
    public bool IsOverdue => Kind == DailyAgendaItemKind.OverdueForm;
    public string DueText => $"due {DueDate:MMMM d, yyyy}";
    public string ScratchpadLine => DailyAgendaText.FormatItem(this);
}

public sealed record DailyAgendaBuildResult(
    int PersonCount,
    int LookaheadDays,
    int OverdueTotal,
    IReadOnlyList<DailyAgendaItem> OverdueItems,
    IReadOnlyList<DailyAgendaItem> UpcomingItems,
    DailyAgendaItem? AssessmentSuggestion)
{
    public bool HasOverdue => OverdueTotal > 0;
    public bool HasUpcoming => UpcomingItems.Count > 0;
}

public static class DailyAgendaText
{
    public static string FormatItem(DailyAgendaItem item) => item.Kind switch
    {
        DailyAgendaItemKind.OverdueForm =>
            $"Overdue: {item.Title} for {item.PersonName} — {item.DueText}",
        DailyAgendaItemKind.SuggestedAssessment =>
            $"Comprehensive Assessment for {item.PersonName} — {item.DueText}",
        _ => $"{item.Title} — {item.DueText}"
    };
}

/// <summary>
/// Builds the read-only agenda snapshot from the caseload already loaded during
/// shell initialization. It never changes form or assessment state.
/// </summary>
public sealed class DailyAgendaBuilder(IUpcomingEventService upcomingEvents)
{
    public const int SectionLimit = 5;

    public DailyAgendaBuildResult Build(
        IEnumerable<Person> people,
        Settings settings,
        DateTime asOfDate)
    {
        var today = asOfDate.Date;
        var caseload = people.ToList();

        var overdueForms = caseload
            .SelectMany(person => person.Forms
                .Where(form => !IsSupersededLegacyReleaseForm(person, form))
                .Where(form => BillingComplianceGate.IsIncompleteAndOverdue(
                    form.DueDate,
                    form.CompletedDate,
                    today))
                .Select(form => new DailyAgendaItem(
                    FormAgendaKey(person.Id, form),
                    person.Id,
                    person.FullName,
                    BillingComplianceGate.DisplayName(form.Type.ToString()),
                    form.DueDate.Date,
                    DailyAgendaItemKind.OverdueForm,
                    BillingComplianceGate.IsRequired(
                        form.Type.ToString(),
                        settings.BillingComplianceRequirements),
                    form.Type,
                    form.Id > 0 ? form.Id : null,
                    form.TargetEffectiveDate == default
                        ? null
                        : form.TargetEffectiveDate.Date)))
            .ToList();
        var overdueReleases = caseload
            .SelectMany(person => ((IEventSource)person).ReleaseComplianceFacts
                .Where(item => item.RetiredOn is null || today < item.RetiredOn.Value.Date)
                .Where(item => today >= item.AppliesFromOn.Date)
                .Where(item => ReleaseAttestationRules.CompletedOn(
                    item.StableKey, item.Attestations) is not DateTime completed ||
                    completed.Date > today)
                .Where(item => item.DueOn.Date < today)
                .Select(item =>
                {
                    var typeName = ReleaseBillingRules.FormTypeFor(item.Category);
                    var title = BillingComplianceGate.DisplayName(typeName);
                    if (!string.IsNullOrWhiteSpace(item.RecipientDisplayName))
                        title += $" — {item.RecipientDisplayName.Trim()}";
                    return new DailyAgendaItem(
                        item.ObligationId is Guid id
                            ? $"release:{id:D}"
                            : item.StableKey,
                        person.Id,
                        person.FullName,
                        title,
                        item.DueOn.Date,
                        DailyAgendaItemKind.OverdueForm,
                        BillingComplianceGate.IsRequired(
                            typeName, settings.BillingComplianceRequirements),
                        Enum.Parse<FormType>(typeName),
                        TargetEffectiveDate: item.TargetEffectiveDate?.Date,
                        ReleaseObligationId: item.ObligationId);
                }))
            .ToList();
        var allOverdue = overdueForms.Concat(overdueReleases)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.PersonName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // LateReview events describe the same forms as the unbounded lookback.
        // The lookback owns them so one form cannot appear in two sections.
        var upcoming = upcomingEvents
            .GenerateEvents(caseload, settings, today)
            // Scheduled notes now surface automatically in Today's Work on their
            // date. Re-offering future scheduled notes here would create a second
            // apparent task when a user selected one. This prompt recommends only
            // actionable form work.
            .Where(item => item.Kind == UpcomingEventKind.OpenReview)
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(SectionLimit)
            .Select(item => new DailyAgendaItem(
                AgendaKey(item),
                item.PersonId,
                item.ClientName,
                item.Title,
                item.Date.Date,
                DailyAgendaItemKind.UpcomingWork,
                false,
                item.FormType,
                item.FormId,
                item.TargetEffectiveDate?.Date,
                item.ReleaseObligationId))
            .ToList();

        var assessmentSuggestion = allOverdue.Count == 0 && upcoming.Count == 0
            ? caseload
                .SelectMany(person => person.Forms
                    .Where(form => form.Type == FormType.ComprehensiveAssessment &&
                                   form.CompletedDate is null)
                    .Select(form => new DailyAgendaItem(
                        $"assessment:{FormAgendaKey(person.Id, form)}",
                        person.Id,
                        person.FullName,
                        "Comprehensive Assessment",
                        form.DueDate.Date,
                        DailyAgendaItemKind.SuggestedAssessment,
                        false,
                        FormType.ComprehensiveAssessment,
                        form.Id > 0 ? form.Id : null,
                        form.TargetEffectiveDate == default
                            ? null
                            : form.TargetEffectiveDate.Date)))
                .OrderBy(item => item.DueDate)
                .ThenBy(item => item.PersonName, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
            : null;

        return new DailyAgendaBuildResult(
            caseload.Count,
            GuaranteedLookaheadDays(settings),
            allOverdue.Count,
            allOverdue.Take(SectionLimit).ToList(),
            upcoming,
            assessmentSuggestion);
    }

    private static string FormAgendaKey(int personId, Form form) =>
        form.Id > 0
            ? $"form:{personId}:{form.Id}"
            : form.TargetEffectiveDate != default
                ? $"form-target:{personId}:{form.Type}:{form.TargetEffectiveDate:yyyyMMdd}"
                : $"legacy-form:{personId}:{form.Type}:{form.DueDate:yyyyMMdd}";

    private static string AgendaKey(UpcomingEvent item)
    {
        if (item.ReleaseObligationId is Guid obligationId)
            return $"release:{obligationId:D}";
        if (item.FormId is int formId)
            return $"form:{item.PersonId}:{formId}";
        if (item.FormType is FormType type && item.TargetEffectiveDate is DateTime target)
            return $"form-target:{item.PersonId}:{type}:{target:yyyyMMdd}";
        return $"event:{item.PersonId}:{item.Kind}:{item.Date:yyyyMMdd}:{item.Title}";
    }

    private static int GuaranteedLookaheadDays(Settings settings) =>
        new[]
        {
            settings.PcpOpenDaysBefore,
            settings.CompAssessmentOpenDaysBefore,
            settings.ReclassificationOpenDaysBefore,
            settings.SafetyPlanOpenDaysBefore,
            settings.PrivacyPracticesOpenDaysBefore,
            settings.ReleaseAgencyOpenDaysBefore,
            settings.ReleaseDhhsOpenDaysBefore,
            settings.ReleaseMedicalOpenDaysBefore,
            settings.ReviewOpenDaysBefore,
            30 // Scheduled-note lookahead in UpcomingEventService.
        }.Min();

    private static bool IsSupersededLegacyReleaseForm(Person person, Form form)
    {
        if (form.Type is not (FormType.Release_Agency or FormType.Release_DHHS or
            FormType.Release_Medical))
            return false;
        var target = (form.TargetEffectiveDate == default
            ? form.DueDate
            : form.TargetEffectiveDate).Date;
        return ((IEventSource)person).ReleaseComplianceFacts.Any(item =>
            item.TargetEffectiveDate?.Date == target);
    }
}
