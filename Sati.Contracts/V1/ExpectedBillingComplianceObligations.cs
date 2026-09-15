namespace Sati.Contracts.V1;

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

    public static string MissingFormObligationId(string type, DateTime targetEffectiveDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (targetEffectiveDate == default)
            throw new ArgumentException(
                "A target effective date is required.", nameof(targetEffectiveDate));
        return $"missing-form:{type}:{targetEffectiveDate:yyyy-MM-dd}";
    }
}
