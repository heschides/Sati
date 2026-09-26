using System.Globalization;

namespace Sati.Contracts.V1;

/// <summary>Where an annual item belongs on the Annual Forms overview.</summary>
public enum PlanYearGroup
{
    Plan,
    Releases,
    SafetyAndNotices,
    CheckIns,
    OneTime
}

/// <summary>
/// One answer per item to "what does this need from me?". Done means an attestation
/// completed the obligation, the same fact billing reads; a saved or generated PDF alone
/// never makes an item done.
/// </summary>
public enum PlanYearItemState
{
    Done,
    Overdue,
    Open,
    ComingUp
}

/// <summary>The screen that records or prepares an item.</summary>
public enum PlanYearWorkspace
{
    ClientOverview,
    Releases,
    DhhsDocuments,
    SafetyPlan,
    PrivacyPractices
}

public enum PlanYearPosition
{
    Past,
    InForce,
    Upcoming
}

/// <summary>Document preparation facts for one plan year, as the annual-document service reports them.</summary>
public sealed record PlanYearDocumentFacts(
    IReadOnlyList<DocumentArtifactDto> Artifacts,
    IReadOnlyList<int> AcknowledgedArtifactIds,
    bool AuthorizedRepresentativeOnFile);

public sealed record PlanYearItem(
    string Key,
    string Title,
    PlanYearGroup Group,
    PlanYearItemState State,
    DateTime? DueOn,
    DateTime? AvailableOn,
    DateTime? CompletedOn,
    string StateLabel,
    string WhenText,
    string Detail,
    string ActionLabel,
    PlanYearWorkspace Workspace,
    Guid? ReleaseObligationId = null)
{
    public bool NeedsYou => State is PlanYearItemState.Overdue or PlanYearItemState.Open;
}

public sealed record PlanYear(
    DateTime Start,
    DateTime EndInclusive,
    PlanYearPosition Position,
    IReadOnlyList<PlanYearItem> Items)
{
    public string Label => PlanYearOverview.Label(Start);
    public int NeedsYouCount => Items.Count(item => item.NeedsYou);
    public int ComingUpCount => Items.Count(item => item.State == PlanYearItemState.ComingUp);
    public int DoneCount => Items.Count(item => item.State == PlanYearItemState.Done);

    /// <summary>The first day any of this year's work can be started, or null when nothing is scheduled.</summary>
    public DateTime? WorkOpensOn => Items
        .Where(item => item.AvailableOn is not null)
        .Select(item => item.AvailableOn)
        .Min();
}

/// <summary>
/// The Annual Forms overview's plan years. A plan year runs from one anniversary of the
/// initial effective date to the day before the next, exactly the targets billing and
/// form generation use (<see cref="ComplianceScheduleRules.TargetEffectiveDatesThroughNext"/>).
/// Each item belongs to the year it serves, not the year its due date falls in: the
/// assessment and PCP are prepared before their year begins and the reviews fall inside it.
/// </summary>
public static class PlanYearOverview
{
    private const int SoonDays = 60;

    public static string Label(DateTime start) =>
        string.Create(CultureInfo.InvariantCulture, $"{start.Year}–{(start.Year + 1) % 100:00}");

    /// <summary>Every plan year from admission through the next renewal, oldest first.</summary>
    public static IReadOnlyList<DateTime> Starts(DateTime initialEffectiveDate, DateTime today) =>
        ComplianceScheduleRules.TargetEffectiveDatesThroughNext(initialEffectiveDate, today);

    /// <summary>The plan year in force today; before admission, the first one.</summary>
    public static DateTime InForceStart(DateTime initialEffectiveDate, DateTime today) =>
        ComplianceScheduleRules.CurrentTargetEffectiveDate(initialEffectiveDate, today);

    public static DateTime EndInclusive(DateTime initialEffectiveDate, DateTime start) =>
        AnnualDocumentCycle.EndInclusive(initialEffectiveDate, start);

    /// <summary>
    /// Builds one plan year. <paramref name="forms"/> are the stored compliance rows; the
    /// rows billing expects but no one has created yet are added here from the same rule
    /// billing uses, so a missing row shows as not started rather than disappearing.
    /// </summary>
    public static PlanYear Build(
        DateTime initialEffectiveDate,
        DateTime start,
        DateTime today,
        IEnumerable<ComplianceFormSnapshot> forms,
        IEnumerable<ReleaseComplianceFact> releases,
        ComplianceScheduleSettings schedule,
        PlanYearDocumentFacts? documents = null)
    {
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(schedule);

        var effective = initialEffectiveDate.Date;
        start = start.Date;
        today = today.Date;
        var inForce = InForceStart(effective, today);
        var position = start < inForce ? PlanYearPosition.Past
            : start == inForce ? PlanYearPosition.InForce
            : PlanYearPosition.Upcoming;

        var releaseFacts = ExpectedBillingComplianceObligations
            .IncludeMissingDhhs(effective, releases, today)
            .Where(fact => fact.TargetEffectiveDate?.Date == start)
            .ToList();

        var yearForms = ExpectedBillingComplianceObligations
            .IncludeMissingForms(effective, forms, today, schedule)
            .Where(form => TargetOf(effective, form) == start)
            .ToList();

        var items = new List<PlanYearItem>();
        foreach (var form in yearForms)
        {
            if (IsLegacyReleaseForm(form.Type) && releaseFacts.Count > 0)
                continue; // The recipient-specific obligations replace the old fixed release rows.
            items.Add(FormItem(form, today, schedule, documents));
        }

        foreach (var release in releaseFacts)
        {
            if (release.RetiredOn is DateTime retired && retired.Date <= release.DueOn.Date)
                continue; // Never became actionable.
            var completed = ReleaseAttestationRules.CompletedOn(release.StableKey, release.Attestations);
            if (completed is null && release.RetiredOn is DateTime retiredOn && retiredOn.Date <= today)
                continue; // No longer required and never signed.
            items.Add(ReleaseItem(release, completed, today));
        }

        if (position == PlanYearPosition.InForce && documents is { AuthorizedRepresentativeOnFile: false })
            items.Add(AuthorizedRepresentativeItem(documents));

        return new PlanYear(
            start,
            EndInclusive(effective, start),
            position,
            items.OrderBy(item => GroupOrder(item.Group))
                .ThenBy(item => item.DueOn ?? DateTime.MaxValue)
                .ThenBy(item => item.Title, StringComparer.Ordinal)
                .ToList());
    }

    private static DateTime TargetOf(DateTime effective, ComplianceFormSnapshot form)
    {
        if (form.TargetEffectiveDate is DateTime target && target != default)
            return target.Date;
        var cycle = FormAttestationRules.ResolveCycleForForm(effective, form.Type, form.DueDate, null);
        if (cycle is null)
            return default;
        return IsReview(form.Type) ? cycle.Value.CycleStart.Date : cycle.Value.CycleEnd.Date;
    }

    private static PlanYearItem FormItem(
        ComplianceFormSnapshot form,
        DateTime today,
        ComplianceScheduleSettings schedule,
        PlanYearDocumentFacts? documents)
    {
        var due = form.DueDate.Date;
        var available = ComplianceScheduleRules.AvailableOn(form.Type, due, schedule);
        var completed = form.CompletedDate?.Date;
        var state = StateOf(due, available, completed, today);
        var (group, workspace) = form.Type switch
        {
            "SafetyPlan" => (PlanYearGroup.SafetyAndNotices, PlanYearWorkspace.SafetyPlan),
            "PrivacyPractices" => (PlanYearGroup.SafetyAndNotices, PlanYearWorkspace.PrivacyPractices),
            "Q1R" or "Q2R" or "Q3R" or "Q4R" => (PlanYearGroup.CheckIns, PlanYearWorkspace.ClientOverview),
            "Release_Agency" or "Release_DHHS" or "Release_Medical" => (PlanYearGroup.Releases, PlanYearWorkspace.Releases),
            _ => (PlanYearGroup.Plan, PlanYearWorkspace.ClientOverview)
        };
        var detail = DocumentDetail(form.Type, documents);
        var action = state switch
        {
            PlanYearItemState.Done => "View",
            PlanYearItemState.ComingUp => "",
            _ => form.Type switch
            {
                "SafetyPlan" => detail.Length > 0 ? "Continue" : "Start",
                "PrivacyPractices" => detail.StartsWith("Notice generated", StringComparison.Ordinal) ? "Record receipt" : "Prepare notice",
                "Q1R" or "Q2R" or "Q3R" or "Q4R" or "PCP" or "ComprehensiveAssessment" or "Reclassification" => "Record completion",
                _ => "Open"
            }
        };

        return new PlanYearItem(
            form.ObligationId ?? $"form:{form.Type}:{due:yyyy-MM-dd}",
            FormTitle(form.Type),
            group,
            state,
            due,
            available,
            completed,
            StateLabel(state, signed: false),
            WhenText(state, due, available, completed, today, signed: false),
            detail,
            action,
            workspace);
    }

    private static PlanYearItem ReleaseItem(ReleaseComplianceFact release, DateTime? completed, DateTime today)
    {
        var due = release.DueOn.Date;
        var available = (release.AvailableOn ?? release.DueOn).Date;
        var state = StateOf(due, available, completed, today);
        var title = release.Category switch
        {
            ReleaseObligationCategory.Dhhs => "DHHS release",
            ReleaseObligationCategory.Agency => "Agency release",
            ReleaseObligationCategory.Medical => "Medical release",
            _ => "Release"
        };
        if (!string.IsNullOrWhiteSpace(release.RecipientDisplayName))
            title += $" · {release.RecipientDisplayName.Trim()}";

        return new PlanYearItem(
            release.StableKey,
            title,
            PlanYearGroup.Releases,
            state,
            due,
            available,
            completed,
            StateLabel(state, signed: true),
            WhenText(state, due, available, completed, today, signed: true),
            "",
            state switch
            {
                PlanYearItemState.Done => "View",
                PlanYearItemState.ComingUp => "",
                _ => "Open release"
            },
            PlanYearWorkspace.Releases,
            release.ObligationId);
    }

    private static PlanYearItem AuthorizedRepresentativeItem(PlanYearDocumentFacts documents)
    {
        var artifact = Latest(documents, AnnualDocumentKind.DhhsAuthorizedRepresentative);
        return new PlanYearItem(
            "once:dhhs-authorized-representative",
            "DHHS authorized representative",
            PlanYearGroup.OneTime,
            PlanYearItemState.Open,
            null,
            null,
            null,
            artifact is null ? "Not started" : "In progress",
            "Needed once, not every year",
            artifact is null ? "" : DescribeArtifact(artifact),
            artifact is null ? "Start" : "Continue",
            PlanYearWorkspace.DhhsDocuments);
    }

    private static PlanYearItemState StateOf(DateTime due, DateTime available, DateTime? completed, DateTime today)
    {
        if (completed is not null)
            return PlanYearItemState.Done;
        if (today < available)
            return PlanYearItemState.ComingUp;
        return today > due ? PlanYearItemState.Overdue : PlanYearItemState.Open;
    }

    private static string StateLabel(PlanYearItemState state, bool signed) => state switch
    {
        PlanYearItemState.Done => signed ? "Signed" : "Done",
        PlanYearItemState.Overdue => "Overdue",
        PlanYearItemState.Open => "Open",
        _ => "Coming up"
    };

    private static string WhenText(
        PlanYearItemState state, DateTime due, DateTime available, DateTime? completed, DateTime today, bool signed)
    {
        switch (state)
        {
            case PlanYearItemState.Done:
                return $"{(signed ? "Signed" : "Done")} {Date(completed!.Value)}";
            case PlanYearItemState.Overdue:
                return $"Overdue since {Date(due)}";
            case PlanYearItemState.ComingUp:
                return $"Opens {Date(available)} · due {Date(due)}";
            default:
                var days = (due - today).Days;
                return days == 0 ? $"Due today, {Date(due)}"
                    : days <= SoonDays ? $"Due {Date(due)} · in {days} day{(days == 1 ? "" : "s")}"
                    : $"Due {Date(due)}";
        }
    }

    private static string DocumentDetail(string formType, PlanYearDocumentFacts? documents)
    {
        if (documents is null)
            return "";
        switch (formType)
        {
            case "SafetyPlan":
                return Latest(documents, AnnualDocumentKind.SafetyPlan) is { } plan ? DescribeArtifact(plan) : "";
            case "PrivacyPractices":
                var notice = Latest(documents, AnnualDocumentKind.PrivacyPractices);
                if (notice is null)
                    return "";
                return documents.AcknowledgedArtifactIds.Contains(notice.Id)
                    ? "Receipt recorded"
                    : $"Notice generated {Date(notice.GeneratedAtUtc.ToLocalTime())}; record receipt";
            default:
                return "";
        }
    }

    private static DocumentArtifactDto? Latest(PlanYearDocumentFacts documents, AnnualDocumentKind kind) =>
        documents.Artifacts
            .Where(artifact => artifact.Kind == kind.ToString())
            .OrderByDescending(artifact => artifact.GeneratedAtUtc)
            .FirstOrDefault();

    private static string DescribeArtifact(DocumentArtifactDto artifact)
    {
        var on = Date(artifact.GeneratedAtUtc.ToLocalTime());
        return artifact.Origin switch
        {
            nameof(DocumentArtifactOrigin.Draft) => $"Draft saved {on}",
            nameof(DocumentArtifactOrigin.GeneratedInSati) => artifact.BlankFields.Count == 0
                ? $"PDF generated {on}"
                : $"PDF generated {on} with blank fields",
            nameof(DocumentArtifactOrigin.RecordedAsExternal) => "Recorded on file",
            _ => ""
        };
    }

    private static string FormTitle(string type) => type switch
    {
        "PCP" => "Person-Centered Plan",
        "ComprehensiveAssessment" => "Comprehensive Assessment",
        "Reclassification" => "Reclassification",
        "SafetyPlan" => "Safety Plan",
        "PrivacyPractices" => "Privacy Practices notice",
        "Q1R" => "90-day review 1",
        "Q2R" => "90-day review 2",
        "Q3R" => "90-day review 3",
        "Q4R" => "90-day review 4",
        "Release_Agency" => "Agency release",
        "Release_DHHS" => "DHHS release",
        "Release_Medical" => "Medical release",
        _ => type
    };

    private static int GroupOrder(PlanYearGroup group) => group switch
    {
        PlanYearGroup.Plan => 0,
        PlanYearGroup.Releases => 1,
        PlanYearGroup.SafetyAndNotices => 2,
        PlanYearGroup.CheckIns => 3,
        _ => 4
    };

    private static bool IsReview(string type) => type is "Q1R" or "Q2R" or "Q3R" or "Q4R";

    private static bool IsLegacyReleaseForm(string type) =>
        type is "Release_Agency" or "Release_DHHS" or "Release_Medical";

    private static string Date(DateTime value) =>
        value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
