using System.Globalization;

namespace Sati.Contracts.V1;

/// <summary>
/// The only release categories Sati tracks for annual compliance. Recipient identity is
/// represented by an assignment key; it is not represented by inventing more categories.
/// </summary>
public enum ReleaseObligationCategory
{
    Agency,
    Medical,
    Dhhs
}

public enum ReleaseAssignmentKind
{
    MedicalProvider,
    ServiceOrWaiverProvider
}

public enum ReleaseObligationTrigger
{
    AnnualRenewal,
    AssignmentStart
}

public enum ReleaseAttestationSource
{
    Manual,
    ElectronicSignature
}

/// <summary>
/// A provider/service relationship from which recipient-specific release obligations are
/// derived. AssignmentKey must be a durable, non-display identifier for this relationship.
/// KnownOn is when Sati first had enough information to make a mid-cycle release available.
/// </summary>
public sealed record ReleaseAssignmentFact(
    string AssignmentKey,
    ReleaseAssignmentKind Kind,
    DateTime StartsOn,
    DateTime? EndsOn,
    DateTime KnownOn);

/// <summary>A deterministic desired release row for one annual compliance cycle.</summary>
public sealed record ReleaseObligationPlan(
    string StableKey,
    ReleaseObligationCategory Category,
    ReleaseObligationTrigger Trigger,
    DateTime TargetEffectiveDate,
    string? AssignmentKey,
    DateTime AvailableOn,
    DateTime DueOn,
    DateTime AppliesFromOn,
    DateTime? RetiredOn);

public sealed record ExistingReleaseObligation(
    string StableKey,
    string? AssignmentKey,
    DateTime? RetiredOn);

public sealed record ReleaseObligationRetirement(string StableKey, DateTime RetiredOn);

public sealed record ReleaseObligationReconciliation(
    IReadOnlyList<ReleaseObligationPlan> ToCreate,
    IReadOnlyList<ReleaseObligationRetirement> ToRetire,
    IReadOnlyList<string> UnchangedKeys,
    IReadOnlyList<string> PreservedHistoricalKeys);

/// <summary>
/// Pure generation/reconciliation owner for release obligations. Reconciliation only adds a
/// missing row or supplies a prospective retirement date; it never deletes an old obligation.
/// </summary>
public static class ReleaseObligationRules
{
    public const int AnnualAvailabilityDays = 90;

    public static IReadOnlyList<ReleaseObligationPlan> GenerateCycle(
        DateTime targetEffectiveDate,
        IEnumerable<ReleaseAssignmentFact> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var target = RequireDate(targetEffectiveDate, nameof(targetEffectiveDate));
        var nextTarget = target.AddYears(1);
        var facts = assignments.Select(NormalizeAndValidate).ToArray();
        var duplicate = facts
            .GroupBy(x => x.AssignmentKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException(
                $"Assignment key '{duplicate.Key}' appears more than once.", nameof(assignments));

        var plans = new List<ReleaseObligationPlan>
        {
            Annual(target, ReleaseObligationCategory.Dhhs, assignmentKey: null, retiredOn: null)
        };

        foreach (var assignment in facts.OrderBy(x => x.AssignmentKey, StringComparer.Ordinal))
        {
            // End dates are effective at the beginning of that date. A relationship ending on
            // the annual effective date is therefore not an active annual recipient.
            var hasPositiveInterval = assignment.EndsOn is null ||
                                      assignment.EndsOn.Value.Date > assignment.StartsOn.Date;
            var overlapsCycle = assignment.StartsOn.Date < nextTarget &&
                                (assignment.EndsOn is null || assignment.EndsOn.Value.Date > target);
            if (!hasPositiveInterval || !overlapsCycle)
                continue;

            var category = CategoryFor(assignment.Kind);
            if (assignment.StartsOn.Date <= target)
            {
                plans.Add(Annual(
                    target, category, assignment.AssignmentKey, assignment.EndsOn?.Date));
                continue;
            }

            var dueOn = assignment.StartsOn.Date.AddDays(-1);
            plans.Add(new ReleaseObligationPlan(
                StableKey(target, category, ReleaseObligationTrigger.AssignmentStart,
                    assignment.AssignmentKey),
                category,
                ReleaseObligationTrigger.AssignmentStart,
                target,
                assignment.AssignmentKey,
                assignment.KnownOn.Date,
                dueOn,
                assignment.StartsOn.Date,
                assignment.EndsOn?.Date));
        }

        return plans
            .OrderBy(x => x.DueOn)
            .ThenBy(x => x.StableKey, StringComparer.Ordinal)
            .ToArray();
    }

    public static ReleaseObligationReconciliation ReconcileCycle(
        DateTime targetEffectiveDate,
        IEnumerable<ReleaseAssignmentFact> assignments,
        IEnumerable<ExistingReleaseObligation> existing)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(existing);

        var facts = assignments.Select(NormalizeAndValidate).ToArray();
        var desired = GenerateCycle(targetEffectiveDate, facts);
        var stored = existing.ToArray();
        var storedByKey = stored
            .GroupBy(x => x.StableKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var toCreate = desired
            .Where(plan => !storedByKey.ContainsKey(plan.StableKey))
            .ToArray();
        var retirements = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        foreach (var plan in desired)
        {
            if (plan.RetiredOn is not DateTime retiredOn ||
                !storedByKey.TryGetValue(plan.StableKey, out var row) ||
                row.RetiredOn is not null)
                continue;
            retirements[row.StableKey] = retiredOn.Date;
        }

        // If an end date makes a zero-length assignment disappear from the desired set, an
        // already-created row still receives the end date. It is retained rather than deleted.
        var assignmentEnds = facts
            .Where(x => x.EndsOn is not null)
            .ToDictionary(x => x.AssignmentKey, x => x.EndsOn!.Value.Date, StringComparer.Ordinal);
        foreach (var row in stored.Where(x => x.RetiredOn is null && x.AssignmentKey is not null))
        {
            if (assignmentEnds.TryGetValue(row.AssignmentKey!, out var retiredOn))
                retirements[row.StableKey] = retiredOn;
        }

        var toRetire = retirements
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ReleaseObligationRetirement(pair.Key, pair.Value))
            .ToArray();
        var desiredKeys = desired.Select(x => x.StableKey).ToHashSet(StringComparer.Ordinal);
        var retirementKeys = retirements.Keys.ToHashSet(StringComparer.Ordinal);
        var unchanged = stored
            .Where(row => desiredKeys.Contains(row.StableKey) &&
                          !retirementKeys.Contains(row.StableKey))
            .Select(row => row.StableKey)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var preserved = stored
            .Where(row => !desiredKeys.Contains(row.StableKey) &&
                          !retirementKeys.Contains(row.StableKey))
            .Select(row => row.StableKey)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return new ReleaseObligationReconciliation(toCreate, toRetire, unchanged, preserved);
    }

    public static ReleaseObligationCategory CategoryFor(ReleaseAssignmentKind kind) => kind switch
    {
        ReleaseAssignmentKind.MedicalProvider => ReleaseObligationCategory.Medical,
        ReleaseAssignmentKind.ServiceOrWaiverProvider => ReleaseObligationCategory.Agency,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string StableKey(
        DateTime targetEffectiveDate,
        ReleaseObligationCategory category,
        ReleaseObligationTrigger trigger,
        string? assignmentKey)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        if (!Enum.IsDefined(trigger))
            throw new ArgumentOutOfRangeException(nameof(trigger));

        var target = RequireDate(targetEffectiveDate, nameof(targetEffectiveDate));
        var categoryPart = category switch
        {
            ReleaseObligationCategory.Agency => "agency",
            ReleaseObligationCategory.Medical => "medical",
            ReleaseObligationCategory.Dhhs => "dhhs",
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
        var triggerPart = trigger switch
        {
            ReleaseObligationTrigger.AnnualRenewal => "annual",
            ReleaseObligationTrigger.AssignmentStart => "assignment-start",
            _ => throw new ArgumentOutOfRangeException(nameof(trigger))
        };

        if (category == ReleaseObligationCategory.Dhhs)
        {
            if (assignmentKey is not null || trigger != ReleaseObligationTrigger.AnnualRenewal)
                throw new ArgumentException("A DHHS annual obligation cannot name an assignment.");
            return $"release:v1:{target:yyyy-MM-dd}:dhhs:annual";
        }

        var normalizedAssignment = NormalizeKey(assignmentKey, nameof(assignmentKey));
        return FormattableString.Invariant(
            $"release:v1:{target:yyyy-MM-dd}:{categoryPart}:{triggerPart}:{Uri.EscapeDataString(normalizedAssignment)}");
    }

    private static ReleaseObligationPlan Annual(
        DateTime target,
        ReleaseObligationCategory category,
        string? assignmentKey,
        DateTime? retiredOn) =>
        new(
            StableKey(target, category, ReleaseObligationTrigger.AnnualRenewal, assignmentKey),
            category,
            ReleaseObligationTrigger.AnnualRenewal,
            target,
            assignmentKey,
            target.AddDays(-AnnualAvailabilityDays),
            target,
            target,
            retiredOn);

    private static ReleaseAssignmentFact NormalizeAndValidate(ReleaseAssignmentFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (!Enum.IsDefined(fact.Kind))
            throw new ArgumentOutOfRangeException(nameof(fact.Kind));

        var key = NormalizeKey(fact.AssignmentKey, nameof(fact.AssignmentKey));
        var startsOn = RequireDate(fact.StartsOn, nameof(fact.StartsOn));
        var knownOn = RequireDate(fact.KnownOn, nameof(fact.KnownOn));
        return fact with
        {
            AssignmentKey = key,
            StartsOn = startsOn,
            EndsOn = fact.EndsOn?.Date,
            KnownOn = knownOn
        };
    }

    private static string NormalizeKey(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A stable assignment key is required.", parameterName);
        return value.Trim();
    }

    private static DateTime RequireDate(DateTime value, string parameterName)
    {
        if (value == default)
            throw new ArgumentException("A date is required.", parameterName);
        return value.Date;
    }
}

public sealed record ReleaseAttestationFact(
    string ObligationKey,
    DateTime CompletedOn,
    DateTime RecordedAtUtc,
    ReleaseAttestationSource Source = ReleaseAttestationSource.Manual,
    int? SignatureCompletionId = null,
    long? AttestationId = null);

public static class ReleaseAttestationRules
{
    public const string FutureCompletionMessage = "The completion date cannot be in the future.";
    public const string UtcRecordedTimeMessage = "The recorded timestamp must be UTC.";
    public const string SignatureEvidenceMessage =
        "An electronic-signature attestation must identify its signature completion.";

    public static IReadOnlyList<string> Validate(
        DateTime completedOn,
        DateTime agencyToday,
        DateTime recordedAtUtc,
        ReleaseAttestationSource source,
        int? signatureCompletionId)
    {
        var errors = new List<string>();
        if (completedOn == default)
            errors.Add("A completion date is required.");
        else if (completedOn.Date > agencyToday.Date)
            errors.Add(FutureCompletionMessage);

        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            errors.Add(UtcRecordedTimeMessage);
        if (!Enum.IsDefined(source))
            errors.Add("The attestation source is not supported.");
        else if (source == ReleaseAttestationSource.ElectronicSignature &&
                 signatureCompletionId is null or <= 0)
            errors.Add(SignatureEvidenceMessage);
        else if (source == ReleaseAttestationSource.Manual && signatureCompletionId is not null)
            errors.Add("A manual attestation cannot identify an electronic signature completion.");

        return errors;
    }

    /// <summary>
    /// The attestation itself establishes completion. No artifact or document-version fact is
    /// accepted here by design. Withdrawal is also deliberately absent: it changes prospective
    /// authorization, not the historical completion fact.
    /// </summary>
    public static DateTime? CompletedOn(
        string obligationKey,
        IEnumerable<ReleaseAttestationFact> attestations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(obligationKey);
        ArgumentNullException.ThrowIfNull(attestations);
        return attestations
            .Where(x => string.Equals(x.ObligationKey, obligationKey, StringComparison.Ordinal))
            .Select(x => (DateTime?)x.CompletedOn.Date)
            .Min();
    }
}

public sealed record ReleaseSignerPolicy(SignerCapacity RequiredCapacity, string Label);

public static class ReleaseSigningRules
{
    public const string ConsumerLabel = "Consumer signature";
    public const string GuardianOnlyLabel = "Guardian signature only";

    public static ReleaseSignerPolicy For(bool hasGuardian) => hasGuardian
        ? new ReleaseSignerPolicy(SignerCapacity.Guardian, GuardianOnlyLabel)
        : new ReleaseSignerPolicy(SignerCapacity.Consumer, ConsumerLabel);

    public static bool CanSign(bool hasGuardian, SignerCapacity capacity) =>
        capacity != SignerCapacity.AuthorizedRepresentative &&
        capacity == For(hasGuardian).RequiredCapacity;
}

public static class ReleaseAuthorizationRules
{
    /// <summary>
    /// Withdrawal takes effect on its stated date. It does not alter CompletedOn and therefore
    /// does not rewrite compliance for an earlier date.
    /// </summary>
    public static bool IsActive(
        DateTime? completedOn,
        DateTime? withdrawnOn,
        DateTime asOfDate) =>
        completedOn is DateTime completed && completed.Date <= asOfDate.Date &&
        (withdrawnOn is null || asOfDate.Date < withdrawnOn.Value.Date);
}

public sealed record ReleaseComplianceFact(
    string StableKey,
    ReleaseObligationCategory Category,
    DateTime DueOn,
    DateTime AppliesFromOn,
    DateTime? RetiredOn,
    IReadOnlyCollection<ReleaseAttestationFact> Attestations,
    Guid? ObligationId = null,
    DateTime? TargetEffectiveDate = null,
    DateTime? AvailableOn = null,
    string? RecipientDisplayName = null);

/// <summary>
/// Evaluates one release category for one exact annual target. Agency and medical releases are
/// recipient-specific, so every still-required row must have its own attestation. An empty set is
/// therefore compliant for those optional categories. DHHS is universal, so a missing DHHS row is
/// itself noncompliant.
/// </summary>
public static class ReleaseComplianceRules
{
    public static bool IsCategoryCompliant(
        ReleaseObligationCategory category,
        DateTime targetEffectiveDate,
        DateTime asOfDate,
        IEnumerable<ReleaseComplianceFact> obligations)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        if (targetEffectiveDate == default)
            throw new ArgumentException("A target effective date is required.",
                nameof(targetEffectiveDate));
        if (asOfDate == default)
            throw new ArgumentException("An as-of date is required.", nameof(asOfDate));
        ArgumentNullException.ThrowIfNull(obligations);

        var target = targetEffectiveDate.Date;
        var date = asOfDate.Date;
        var required = obligations
            .Where(obligation =>
                obligation.Category == category &&
                obligation.TargetEffectiveDate is DateTime obligationTarget &&
                obligationTarget.Date == target &&
                // AppliesFromOn governs the historical billing gate. A known obligation
                // remains visible as outstanding throughout its preparation window.
                (obligation.RetiredOn is null || date < obligation.RetiredOn.Value.Date))
            .ToArray();

        if (required.Length == 0)
            return category != ReleaseObligationCategory.Dhhs;

        return required.All(obligation =>
            obligation.Attestations.Any(attestation =>
                string.Equals(attestation.ObligationKey, obligation.StableKey,
                    StringComparison.Ordinal) &&
                attestation.CompletedOn != default &&
                attestation.CompletedOn.Date <= date));
    }
}

public sealed record ReleaseBillingSnapshot(
    string ObligationKey,
    string Type,
    DateTime DueDate,
    DateTime? CompletedDate,
    string? RecipientDisplayName = null);

/// <summary>Projects recipient obligations into the existing shared billing gate.</summary>
public static class ReleaseBillingRules
{
    public static IReadOnlyList<ReleaseBillingSnapshot> BuildSnapshots(
        IEnumerable<ReleaseComplianceFact> obligations,
        DateTime serviceDate)
    {
        ArgumentNullException.ThrowIfNull(obligations);
        var date = serviceDate.Date;
        return obligations
            .Where(obligation => date >= obligation.AppliesFromOn.Date &&
                (obligation.RetiredOn is null || date < obligation.RetiredOn.Value.Date))
            .Select(obligation => new ReleaseBillingSnapshot(
                obligation.ObligationId is Guid id && id != Guid.Empty
                    ? $"release:{id:D}"
                    : obligation.StableKey,
                FormTypeFor(obligation.Category),
                obligation.DueOn.Date,
                ReleaseAttestationRules.CompletedOn(
                    obligation.StableKey, obligation.Attestations),
                obligation.RecipientDisplayName))
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.ObligationKey, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<ComplianceFormSnapshot> BuildComplianceSnapshots(
        IEnumerable<ReleaseComplianceFact> obligations,
        DateTime serviceDate) =>
        BuildSnapshots(obligations, serviceDate)
            .Select(item => new ComplianceFormSnapshot(
                item.Type,
                item.DueDate,
                item.CompletedDate,
                ObligationId: item.ObligationKey,
                RecipientDisplayName: item.RecipientDisplayName))
            .ToArray();

    public static IReadOnlyList<BillingComplianceObligationSnapshot> BuildRecoveryObligations(
        int personId,
        IEnumerable<ReleaseComplianceFact> obligations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(personId, 0);
        ArgumentNullException.ThrowIfNull(obligations);

        return obligations.Select(obligation =>
        {
            var completedOn = ReleaseAttestationRules.CompletedOn(
                obligation.StableKey, obligation.Attestations);
            var evidence = completedOn is null
                ? null
                : obligation.Attestations
                    .Where(item => item.CompletedOn.Date == completedOn.Value.Date)
                    .OrderBy(item => item.RecordedAtUtc)
                    .ThenBy(item => item.AttestationId)
                    .Select(item => item.AttestationId is long id && id > 0
                        ? $"release-attestation:{id}"
                        : item.SignatureCompletionId is int signatureId && signatureId > 0
                            ? $"signature-completion:{signatureId}"
                            : $"release-attestation-recorded:{item.RecordedAtUtc:O}")
                    .FirstOrDefault();
            var type = FormTypeFor(obligation.Category);
            var name = BillingComplianceGate.DisplayName(type);
            if (!string.IsNullOrWhiteSpace(obligation.RecipientDisplayName))
                name += $" — {obligation.RecipientDisplayName.Trim()}";
            return new BillingComplianceObligationSnapshot(
                obligation.ObligationId is Guid id && id != Guid.Empty
                    ? $"release:{id:D}"
                    : obligation.StableKey,
                personId,
                name,
                BillingComplianceGate.RequirementFor(type),
                obligation.DueOn.Date,
                completedOn?.Date,
                obligation.AppliesFromOn.Date,
                obligation.RetiredOn?.Date,
                evidence);
        }).ToArray();
    }

    public static string FormTypeFor(ReleaseObligationCategory category) => category switch
    {
        ReleaseObligationCategory.Agency => "Release_Agency",
        ReleaseObligationCategory.Medical => "Release_Medical",
        ReleaseObligationCategory.Dhhs => "Release_DHHS",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}
