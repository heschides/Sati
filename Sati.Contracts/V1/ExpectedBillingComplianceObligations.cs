namespace Sati.Contracts.V1;

/// <summary>An expected release obligation with no stored row, and the provider link naming its recipient.</summary>
public sealed record MissingReleasePlan(ReleaseObligationPlan Plan, ReleaseProviderLinkFact? Recipient);

/// <summary>
/// Completes the set of annual compliance facts before billing evaluates it.
/// Billing must not interpret an absent database row as completed work.
/// </summary>
public static class ExpectedBillingComplianceObligations
{
    private const int MaximumCycles = 150;

    /// <summary>
    /// Adds an explicit, incomplete snapshot for each expected annual form row that
    /// is absent. Existing rows remain authoritative for their dates and evidence.
    /// </summary>
    public static IReadOnlyList<ComplianceFormSnapshot> IncludeMissingForms(
        DateTime? initialEffectiveDate,
        IEnumerable<ComplianceFormSnapshot> storedForms,
        DateTime asOfDate,
        ComplianceScheduleSettings schedule)
    {
        ArgumentNullException.ThrowIfNull(storedForms);
        ArgumentNullException.ThrowIfNull(schedule);

        var projected = storedForms.ToList();
        if (initialEffectiveDate is not DateTime effectiveDate)
            return projected;

        var explicitIdentities = projected
            .Where(form => form.TargetEffectiveDate is DateTime target && target != default)
            .Select(form => (form.Type, Target: form.TargetEffectiveDate!.Value.Date))
            .ToHashSet();
        var targetlessDueDates = projected
            .Where(form => !form.TargetEffectiveDate.HasValue ||
                           form.TargetEffectiveDate.Value == default)
            .Select(form => (form.Type, Due: form.DueDate.Date))
            .ToHashSet();

        foreach (var target in ComplianceScheduleRules.TargetEffectiveDatesThroughNext(
                     effectiveDate, asOfDate, MaximumCycles))
        {
            foreach (var type in PersonSaveRules.FormTypes)
            {
                var due = ComplianceScheduleRules.DueDate(type, target, schedule);
                if (explicitIdentities.Contains((type, target.Date)) ||
                    targetlessDueDates.Contains((type, due.Date)))
                {
                    continue;
                }

                projected.Add(new ComplianceFormSnapshot(
                    type,
                    due,
                    CompletedDate: null,
                    ObligationId: MissingFormObligationId(type, target),
                    TargetEffectiveDate: target.Date));
                explicitIdentities.Add((type, target.Date));
            }
        }

        return projected;
    }

    /// <summary>
    /// Adds the universal annual DHHS obligation when its exact annual row is
    /// absent. Recipient-specific Agency and Medical obligations remain derived
    /// from provider assignments; callers continue to pass their reconciled rows.
    /// </summary>
    public static IReadOnlyList<ReleaseComplianceFact> IncludeMissingDhhs(
        DateTime? initialEffectiveDate,
        IEnumerable<ReleaseComplianceFact> storedObligations,
        DateTime asOfDate)
    {
        ArgumentNullException.ThrowIfNull(storedObligations);

        var projected = storedObligations.ToList();
        if (initialEffectiveDate is not DateTime effectiveDate)
            return projected;

        var dhhsTargets = projected
            .Where(item => item.Category == ReleaseObligationCategory.Dhhs &&
                           item.TargetEffectiveDate is DateTime target && target != default)
            .Select(item => item.TargetEffectiveDate!.Value.Date)
            .ToHashSet();
        foreach (var target in ComplianceScheduleRules.TargetEffectiveDatesThroughNext(
                     effectiveDate, asOfDate, MaximumCycles))
        {
            if (!dhhsTargets.Add(target.Date))
                continue;

            var plan = ReleaseObligationRules.GenerateCycle(target, []).Single();
            projected.Add(new ReleaseComplianceFact(
                plan.StableKey,
                plan.Category,
                plan.DueOn,
                plan.AppliesFromOn,
                plan.RetiredOn,
                [],
                TargetEffectiveDate: plan.TargetEffectiveDate));
        }

        return projected;
    }

    /// <summary>
    /// Completes the release fact set from the consumer's provider assignments.
    /// This is the read-side safety net: if reconciliation failed to persist a
    /// recipient row, billing still fails closed for the exact expected release.
    /// </summary>
    public static IReadOnlyList<ReleaseComplianceFact> IncludeMissingReleases(
        DateTime? initialEffectiveDate,
        IEnumerable<ReleaseComplianceFact> storedObligations,
        DateTime asOfDate,
        IEnumerable<ReleaseProviderLinkFact> providerLinks)
    {
        ArgumentNullException.ThrowIfNull(storedObligations);
        ArgumentNullException.ThrowIfNull(providerLinks);

        var projected = storedObligations.ToList();
        foreach (var missing in MissingReleasePlans(
                     initialEffectiveDate,
                     projected.Select(item => item.StableKey),
                     asOfDate,
                     providerLinks))
        {
            var plan = missing.Plan;
            projected.Add(new ReleaseComplianceFact(
                plan.StableKey,
                plan.Category,
                plan.DueOn,
                plan.AppliesFromOn,
                plan.RetiredOn,
                [],
                TargetEffectiveDate: plan.TargetEffectiveDate,
                AvailableOn: plan.AvailableOn,
                RecipientDisplayName: missing.Recipient?.RecipientDisplayName));
        }

        return projected;
    }

    /// <summary>
    /// Every release obligation the consumer's plan years and provider assignments call
    /// for whose row is not among <paramref name="storedKeys"/>, with the provider link
    /// that names its recipient. The billing safety net projects these; maintenance that
    /// creates the missing rows uses the same list, so the two cannot disagree.
    /// </summary>
    public static IReadOnlyList<MissingReleasePlan> MissingReleasePlans(
        DateTime? initialEffectiveDate,
        IEnumerable<string> storedKeys,
        DateTime asOfDate,
        IEnumerable<ReleaseProviderLinkFact> providerLinks)
    {
        ArgumentNullException.ThrowIfNull(storedKeys);
        ArgumentNullException.ThrowIfNull(providerLinks);
        if (initialEffectiveDate is not DateTime effectiveDate)
            return [];

        var links = providerLinks.ToArray();
        var existingKeys = storedKeys.ToHashSet(StringComparer.Ordinal);
        var linksByKey = links.ToDictionary(
            link => ReleaseAssignmentResolution.AssignmentKey(link.LinkId),
            StringComparer.Ordinal);

        var missing = new List<MissingReleasePlan>();
        foreach (var target in ComplianceScheduleRules.TargetEffectiveDatesThroughNext(
                     effectiveDate, asOfDate, MaximumCycles))
        {
            var resolution = ReleaseAssignmentResolution.Resolve(
                target,
                DateTime.MaxValue.Date,
                requiresServiceProvider: false,
                links);
            foreach (var plan in ReleaseObligationRules.GenerateCycle(
                         target, resolution.Assignments))
            {
                if (!existingKeys.Add(plan.StableKey))
                    continue;

                missing.Add(new MissingReleasePlan(
                    plan,
                    plan.AssignmentKey is not null &&
                    linksByKey.TryGetValue(plan.AssignmentKey, out var link)
                        ? link
                        : null));
            }
        }

        return missing;
    }

    public static string MissingFormObligationId(string type, DateTime targetEffectiveDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (targetEffectiveDate == default)
            throw new ArgumentException(
                "A target effective date is required.", nameof(targetEffectiveDate));
        return $"missing-form:{type}:{targetEffectiveDate:yyyy-MM-dd}";
    }
}
