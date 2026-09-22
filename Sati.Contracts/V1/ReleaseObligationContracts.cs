namespace Sati.Contracts.V1;

/// <summary>
/// The minimum provider-link facts needed to derive release recipients. ProviderType is the
/// persisted directory value (Healthcare, Waiver, or Other); display names never become keys.
/// </summary>
public sealed record ReleaseProviderLinkFact(
    int LinkId,
    int ProviderId,
    string ProviderType,
    string? Role,
    DateTime? StartsOn,
    DateTime? EndsOn,
    DateTime? KnownOn,
    string RecipientDisplayName);

public static class ReleaseLinkageIssueCodes
{
    public const string MissingServiceProvider = "missing_service_provider_link";
    public const string MissingAssignmentStart = "missing_assignment_start_date";
    public const string MissingAssignmentKnownDate = "missing_assignment_known_date";
    public const string LegacyCategoryCompletionNeedsReview =
        "legacy_release_completion_needs_review";
}

/// <summary>
/// A release configuration or retained-evidence problem that needs human review. The message may
/// identify a provider already visible on the consumer's record, but the stable code is what
/// application logic should use.
/// </summary>
public sealed record ReleaseLinkageIssue(
    string Code,
    string Message,
    int? ProviderLinkId = null,
    int? ProviderId = null);

/// <summary>
/// Completion retained on the former one-row-per-category release model. It is evidence to review,
/// not enough information to identify or complete a recipient-specific obligation.
/// </summary>
public sealed record LegacyReleaseCompletionFact(
    string FormType,
    DateTime TargetEffectiveDate,
    DateTime CompletedOn);

/// <summary>
/// Makes a legacy category-level completion visible while exact release obligations are still
/// outstanding. This rule deliberately reports only; it never projects or copies completion.
/// </summary>
public static class LegacyReleaseCompletionReview
{
    public static IReadOnlyList<ReleaseLinkageIssue> FindIssues(
        DateTime targetEffectiveDate,
        DateTime asOfDate,
        IEnumerable<LegacyReleaseCompletionFact> legacyCompletions,
        IEnumerable<ReleaseComplianceFact> exactObligations)
    {
        ArgumentNullException.ThrowIfNull(legacyCompletions);
        ArgumentNullException.ThrowIfNull(exactObligations);
        if (targetEffectiveDate == default)
            throw new ArgumentException("An annual effective date is required.",
                nameof(targetEffectiveDate));
        if (asOfDate == default)
            throw new ArgumentException("A review date is required.", nameof(asOfDate));

        var target = targetEffectiveDate.Date;
        var asOf = asOfDate.Date;
        var outstandingByCategory = exactObligations
            .Where(item => item.TargetEffectiveDate?.Date == target)
            .Where(item => item.RetiredOn is null || asOf < item.RetiredOn.Value.Date)
            .Where(item => ReleaseAttestationRules.CompletedOn(
                item.StableKey, item.Attestations) is null)
            .GroupBy(item => item.Category)
            .ToDictionary(group => group.Key, group => group.Count());

        return legacyCompletions
            .Where(item => item.TargetEffectiveDate.Date == target && item.CompletedOn != default)
            .Select(item => new
            {
                Fact = item,
                Category = CategoryFor(item.FormType)
            })
            .Where(item => item.Category is not null &&
                           outstandingByCategory.ContainsKey(item.Category.Value))
            .GroupBy(item => item.Category!.Value)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var completedOn = group.Select(item => item.Fact.CompletedOn.Date).Min();
                var category = group.Key;
                var outstandingCount = outstandingByCategory[category];
                var exactDescription = category == ReleaseObligationCategory.Dhhs
                    ? "the exact DHHS obligation"
                    : outstandingCount == 1
                        ? "the exact recipient obligation"
                        : $"the {outstandingCount} exact recipient obligations";
                return new ReleaseLinkageIssue(
                    ReleaseLinkageIssueCodes.LegacyCategoryCompletionNeedsReview,
                    $"A legacy {DisplayName(category)} was marked complete on " +
                    $"{completedOn:MMM d, yyyy} for the {target:MMM d, yyyy} annual cycle. " +
                    $"That category-level record does not complete {exactDescription}, so Sati " +
                    "did not copy its date. Review the retained evidence and separately attest " +
                    "each applicable exact release.");
            })
            .ToArray();
    }

    private static ReleaseObligationCategory? CategoryFor(string? formType) => formType switch
    {
        "Release_Agency" => ReleaseObligationCategory.Agency,
        "Release_Medical" => ReleaseObligationCategory.Medical,
        "Release_DHHS" => ReleaseObligationCategory.Dhhs,
        _ => null
    };

    private static string DisplayName(ReleaseObligationCategory category) => category switch
    {
        ReleaseObligationCategory.Agency => "Agency release",
        ReleaseObligationCategory.Medical => "Medical release",
        ReleaseObligationCategory.Dhhs => "DHHS release",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}

public sealed record ReleaseAssignmentResolutionResult(
    IReadOnlyList<ReleaseAssignmentFact> Assignments,
    IReadOnlyList<ReleaseLinkageIssue> Issues);

/// <summary>
/// Maps retained consumer-provider links to the two recipient-bearing release categories. It
/// deliberately cannot infer a recipient from a waiver flag or from free-text profile fields.
/// </summary>
public static class ReleaseAssignmentResolution
{
    public const string AssignmentKeyPrefix = "person-provider:";

    public static ReleaseAssignmentResolutionResult Resolve(
        DateTime targetEffectiveDate,
        DateTime observedOn,
        bool requiresServiceProvider,
        IEnumerable<ReleaseProviderLinkFact> providerLinks)
    {
        ArgumentNullException.ThrowIfNull(providerLinks);
        var target = RequiredDate(targetEffectiveDate, nameof(targetEffectiveDate));
        var observed = RequiredDate(observedOn, nameof(observedOn));
        var nextTarget = target.AddYears(1);
        var assignments = new List<ReleaseAssignmentFact>();
        var issues = new List<ReleaseLinkageIssue>();
        var hasServiceRecipient = false;

        foreach (var link in providerLinks.OrderBy(item => item.LinkId))
        {
            if (link.LinkId <= 0 || link.ProviderId <= 0)
                throw new ArgumentException("Provider links require persisted link and provider identifiers.",
                    nameof(providerLinks));

            ReleaseAssignmentKind? kind = link.ProviderType?.Trim() switch
            {
                "Healthcare" => ReleaseAssignmentKind.MedicalProvider,
                "Waiver" => ReleaseAssignmentKind.ServiceOrWaiverProvider,
                // Other directory relationships do not create an authorization obligation.
                _ => null
            };
            if (kind is null)
                continue;

            var endsOn = link.EndsOn?.Date;
            var startsOn = link.StartsOn?.Date;
            if (startsOn is null)
            {
                // The recipient link is real, but no historical start can safely be claimed.
                // It can still participate in this annual renewal when it is not already ended.
                issues.Add(new ReleaseLinkageIssue(
                    ReleaseLinkageIssueCodes.MissingAssignmentStart,
                    $"Enter the service start date for {Display(link)} before relying on assignment-start compliance.",
                    link.LinkId,
                    link.ProviderId));
                if (endsOn is not null && endsOn.Value <= target)
                    continue;
                startsOn = target;
            }

            if (startsOn.Value >= nextTarget || endsOn is not null && endsOn.Value <= target)
                continue;

            if (kind == ReleaseAssignmentKind.ServiceOrWaiverProvider)
                hasServiceRecipient = true;

            DateTime knownOn;
            if (startsOn.Value > target)
            {
                if (link.KnownOn is not DateTime recordedKnownOn)
                {
                    issues.Add(new ReleaseLinkageIssue(
                        ReleaseLinkageIssueCodes.MissingAssignmentKnownDate,
                        $"Sati cannot determine when the assignment to {Display(link)} became known. Record the linkage before creating its mid-cycle release obligation.",
                        link.LinkId,
                        link.ProviderId));
                    continue;
                }

                knownOn = recordedKnownOn.Date;
                if (knownOn > observed)
                    throw new ArgumentException("A provider link cannot become known in the future.",
                        nameof(providerLinks));
            }
            else
            {
                // KnownOn is not used by an annual plan. Using the target as the pure-rule input
                // does not assert or persist a historical discovery date.
                knownOn = target;
            }

            assignments.Add(new ReleaseAssignmentFact(
                AssignmentKey(link.LinkId),
                kind.Value,
                startsOn.Value,
                endsOn,
                knownOn));
        }

        if (requiresServiceProvider && !hasServiceRecipient)
        {
            issues.Add(new ReleaseLinkageIssue(
                ReleaseLinkageIssueCodes.MissingServiceProvider,
                "This consumer has waiver services recorded but no waiver/service provider assignment is linked. Sati cannot invent an Agency release recipient."));
        }

        return new ReleaseAssignmentResolutionResult(assignments, issues);
    }

    public static string AssignmentKey(int providerLinkId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(providerLinkId, 0);
        return $"{AssignmentKeyPrefix}{providerLinkId}";
    }

    public static bool TryGetProviderLinkId(string? assignmentKey, out int providerLinkId)
    {
        providerLinkId = 0;
        return assignmentKey is not null &&
               assignmentKey.StartsWith(AssignmentKeyPrefix, StringComparison.Ordinal) &&
               int.TryParse(assignmentKey.AsSpan(AssignmentKeyPrefix.Length), out providerLinkId) &&
               providerLinkId > 0;
    }

    private static string Display(ReleaseProviderLinkFact link) =>
        string.IsNullOrWhiteSpace(link.RecipientDisplayName)
            ? $"provider link {link.LinkId}"
            : link.RecipientDisplayName.Trim();

    private static DateTime RequiredDate(DateTime value, string parameterName)
    {
        if (value == default)
            throw new ArgumentException("A date is required.", parameterName);
        return value.Date;
    }
}

public sealed record ReleaseObligationAttestationDto(
    long Id,
    DateTime CompletedOn,
    string Source,
    string ActorKind,
    int? ActorUserId,
    string? SignerCapacity,
    int? SignatureCompletionId,
    DateTime RecordedAtUtc,
    string? Reason,
    int? EvidenceNoteId = null,
    DateTime? RevokedAtUtc = null,
    int? RevokedByUserId = null,
    string? RevocationReason = null);

public sealed record RevokeReleaseAttestationRequest(string Reason);

public sealed record ReleaseObligationDto(
    long Id,
    Guid ObligationId,
    int PersonId,
    string StableKey,
    string Category,
    string Trigger,
    DateTime TargetEffectiveDate,
    int? RecipientProviderId,
    string? RecipientDisplayName,
    DateTime AvailableOn,
    DateTime DueOn,
    DateTime AppliesFromOn,
    DateTime? RetiredOn,
    DateTime? CompletedOn,
    DateTime? WithdrawnOn,
    bool IsAuthorizationActive,
    IReadOnlyList<ReleaseObligationAttestationDto> Attestations);

public sealed record ReleaseObligationStatusDto(
    int PersonId,
    DateTime TargetEffectiveDate,
    string RequiredSignerCapacity,
    string SignerLabel,
    IReadOnlyList<ReleaseObligationDto> Obligations,
    IReadOnlyList<ReleaseLinkageIssue> LinkageIssues);

public sealed record AttestReleaseObligationRequest(DateTime CompletedOn, string? Reason = null);

public sealed record WithdrawReleaseAuthorizationRequest(DateTime WithdrawnOn, string Reason);

public sealed record ReconcileReleaseObligationsRequest(DateTime TargetEffectiveDate);
