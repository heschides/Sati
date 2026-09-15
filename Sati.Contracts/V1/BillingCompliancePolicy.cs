namespace Sati.Contracts.V1;

public sealed record BillingCompliancePolicyVersionSnapshot(
    long VersionId,
    int AgencyId,
    DateTime EffectiveOn,
    BillingComplianceRequirements Requirements);

public sealed record BillingCompliancePolicyOptions(
    bool AllowPastEffectiveDates = false);

public sealed record BillingCompliancePolicyChangeDecision(
    bool Accepted,
    IReadOnlyList<string> Errors);

public sealed record PreviewBillingCompliancePolicyRequest(
    DateTime? EffectiveOn,
    BillingComplianceRequirements Requirements);

/// <summary>
/// The agency billing-compliance mask that applies to one exact service date.
/// This deliberately exposes only the resolved answer, not the agency's policy
/// history or the administrators who recorded it.
/// </summary>
public sealed record BillingComplianceRequirementsAtDateDto(
    DateTime ServiceDate,
    BillingComplianceRequirements Requirements);

/// <summary>
/// Counts records whose billing-compliance result would change. A record that
/// stays blocked but is blocked by a different obligation is reported
/// separately because an existing exception or recovery decision may no longer
/// cover the exact blockers.
/// </summary>
public sealed record BillingCompliancePolicyImpactBucket(
    int NewlyBlocked,
    int NewlyUnblocked,
    int ChangedWhileBlocked)
{
    public int TotalAffected => NewlyBlocked + NewlyUnblocked + ChangedWhileBlocked;
}

public sealed record BillingCompliancePolicyImpactPreviewDto(
    DateTime EffectiveOn,
    BillingComplianceRequirements Requirements,
    int NotesEvaluated,
    int ClaimRecordsEvaluated,
    BillingCompliancePolicyImpactBucket DraftOrUnsubmittedNotes,
    BillingCompliancePolicyImpactBucket SubmittedOrFinalizedNotes,
    BillingCompliancePolicyImpactBucket DraftClaimRecords,
    BillingCompliancePolicyImpactBucket SubmittedOrFinalizedClaimRecords);

/// <summary>
/// A PHI-free set of facts used to preview a policy change. The persistence
/// layers build these facts from tenant-scoped records; this shared owner makes
/// the desktop and API compare policies identically.
/// </summary>
public sealed record BillingCompliancePolicyImpactRecordSnapshot(
    int NoteId,
    int PersonId,
    DateTime ServiceDate,
    bool IsSubmittedOrFinalized,
    int DraftClaimRecordCount,
    int SubmittedOrFinalizedClaimRecordCount,
    IReadOnlyList<BillingComplianceObligationSnapshot> Obligations,
    IReadOnlyList<int>? SubmittedOrFinalizedClaimRecordIds = null);

public enum BillingCompliancePolicyImpactChangeKind
{
    NewlyBlocked,
    NewlyUnblocked,
    ChangedWhileBlocked
}

public sealed record BillingCompliancePolicyRecordImpact(
    int NoteId,
    int PersonId,
    DateTime ServiceDate,
    bool IsSubmittedOrFinalized,
    BillingCompliancePolicyImpactChangeKind ChangeKind,
    IReadOnlyList<string> PreviousBlockingObligationIds,
    IReadOnlyList<string> NewBlockingObligationIds,
    IReadOnlyList<int> SubmittedOrFinalizedClaimRecordIds);

public sealed record BillingCompliancePolicyReviewFlagDto(
    Guid FlagId,
    long PolicyVersionId,
    DateTime PolicyEffectiveOn,
    BillingComplianceRequirements PolicyRequirements,
    int PersonId,
    int NoteId,
    int? ClaimRecordId,
    DateTime ServiceDate,
    BillingCompliancePolicyImpactChangeKind ChangeKind,
    IReadOnlyList<string> PreviousBlockingObligationIds,
    IReadOnlyList<string> NewBlockingObligationIds,
    DateTime CreatedAtUtc,
    string Status = "Unresolved");

/// <summary>
/// Selects the immutable agency policy that was in force on a service date and
/// validates effective-dated policy changes.
/// </summary>
public static class BillingCompliancePolicyRules
{
    public const int ExplanationMaxLength = 1_000;

    public static BillingCompliancePolicyVersionSnapshot? ResolveForServiceDate(
        IEnumerable<BillingCompliancePolicyVersionSnapshot> versions,
        int agencyId,
        DateTime serviceDate)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(agencyId, 0);

        var applicable = versions
            .Where(version => version.AgencyId == agencyId &&
                              version.EffectiveOn.Date <= serviceDate.Date)
            .ToList();

        if (applicable.Any(version => !BillingComplianceGate.IsSupported(version.Requirements)))
            throw new InvalidOperationException("A billing-compliance policy contains unsupported requirements.");

        if (applicable.Count == 0)
            return null;

        return applicable
            .OrderByDescending(version => version.EffectiveOn.Date)
            .ThenByDescending(version => version.VersionId)
            .First();
    }

    public static BillingCompliancePolicyChangeDecision ValidateChange(
        BillingComplianceRequirements requirements,
        DateTime? effectiveOn,
        DateTime agencyToday,
        BillingCompliancePolicyOptions? options = null,
        string? explanation = null)
    {
        options ??= new BillingCompliancePolicyOptions();
        var errors = new List<string>();

        if (!BillingComplianceGate.IsSupported(requirements))
            errors.Add("The billing-compliance policy contains unsupported requirements.");

        if (explanation?.Trim().Length > ExplanationMaxLength)
            errors.Add($"The explanation must not exceed {ExplanationMaxLength:N0} characters.");

        if (effectiveOn is null)
        {
            errors.Add("An enforcement date is required.");
        }
        else if (effectiveOn.Value.Date < agencyToday.Date)
        {
            if (!options.AllowPastEffectiveDates)
                errors.Add("A past enforcement date is not allowed by agency settings.");
            else if (string.IsNullOrWhiteSpace(explanation))
                errors.Add("An explanation is required for a past enforcement date.");
        }

        return new BillingCompliancePolicyChangeDecision(errors.Count == 0, errors);
    }
}

public static class BillingCompliancePolicyImpactRules
{
    public static BillingCompliancePolicyImpactPreviewDto Preview(
        int agencyId,
        BillingComplianceRequirements fallbackRequirements,
        IEnumerable<BillingCompliancePolicyVersionSnapshot> existingVersions,
        DateTime effectiveOn,
        BillingComplianceRequirements proposedRequirements,
        IEnumerable<BillingCompliancePolicyImpactRecordSnapshot> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var recordList = records.ToArray();
        var impacts = Analyze(
            agencyId,
            fallbackRequirements,
            existingVersions,
            effectiveOn,
            proposedRequirements,
            recordList);

        var draftNotes = new MutableBucket();
        var finalizedNotes = new MutableBucket();
        var draftClaims = new MutableBucket();
        var finalizedClaims = new MutableBucket();
        foreach (var impact in impacts)
        {
            var record = recordList.Single(item => item.NoteId == impact.NoteId);
            (record.IsSubmittedOrFinalized ? finalizedNotes : draftNotes)
                .Add(impact.ChangeKind, 1);
            draftClaims.Add(impact.ChangeKind, record.DraftClaimRecordCount);
            finalizedClaims.Add(
                impact.ChangeKind, record.SubmittedOrFinalizedClaimRecordCount);
        }

        return new BillingCompliancePolicyImpactPreviewDto(
            effectiveOn.Date,
            proposedRequirements,
            recordList.Length,
            recordList.Sum(record => record.DraftClaimRecordCount +
                                     record.SubmittedOrFinalizedClaimRecordCount),
            draftNotes.ToContract(),
            finalizedNotes.ToContract(),
            draftClaims.ToContract(),
            finalizedClaims.ToContract());
    }

    public static IReadOnlyList<BillingCompliancePolicyRecordImpact> Analyze(
        int agencyId,
        BillingComplianceRequirements fallbackRequirements,
        IEnumerable<BillingCompliancePolicyVersionSnapshot> existingVersions,
        DateTime effectiveOn,
        BillingComplianceRequirements proposedRequirements,
        IEnumerable<BillingCompliancePolicyImpactRecordSnapshot> records)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(agencyId, 0);
        ArgumentNullException.ThrowIfNull(existingVersions);
        ArgumentNullException.ThrowIfNull(records);
        if (!BillingComplianceGate.IsSupported(fallbackRequirements) ||
            !BillingComplianceGate.IsSupported(proposedRequirements))
        {
            throw new ArgumentException(
                "The billing-compliance policy contains unsupported requirements.");
        }

        var baseline = new[]
            {
                new BillingCompliancePolicyVersionSnapshot(
                    long.MinValue, agencyId, DateTime.MinValue, fallbackRequirements)
            }
            .Concat(existingVersions)
            .ToArray();
        var proposed = baseline
            .Append(new BillingCompliancePolicyVersionSnapshot(
                long.MaxValue, agencyId, effectiveOn.Date, proposedRequirements))
            .ToArray();

        var impacts = new List<BillingCompliancePolicyRecordImpact>();
        var seenNoteIds = new HashSet<int>();

        foreach (var record in records)
        {
            if (record.NoteId <= 0 || record.PersonId <= 0)
                throw new ArgumentException("Every preview record needs a note and consumer id.");
            if (record.DraftClaimRecordCount < 0 ||
                record.SubmittedOrFinalizedClaimRecordCount < 0)
            {
                throw new ArgumentException("Claim-record counts cannot be negative.");
            }
            if (!seenNoteIds.Add(record.NoteId))
                throw new ArgumentException("Preview note ids must be unique.");
            var finalizedClaimIds = record.SubmittedOrFinalizedClaimRecordIds ?? [];
            if (finalizedClaimIds.Any(id => id <= 0) ||
                finalizedClaimIds.Distinct().Count() != finalizedClaimIds.Count)
            {
                throw new ArgumentException(
                    "Submitted or finalized claim-record ids must be positive and unique.");
            }
            if (finalizedClaimIds.Count > 0 &&
                finalizedClaimIds.Count != record.SubmittedOrFinalizedClaimRecordCount)
            {
                throw new ArgumentException(
                    "Submitted or finalized claim-record ids must match their count.");
            }
            if (record.Obligations.Any(obligation =>
                    string.IsNullOrWhiteSpace(obligation.ObligationId)))
            {
                throw new ArgumentException(
                    "Every billing-compliance obligation needs an id.");
            }

            var currentPolicy = BillingCompliancePolicyRules.ResolveForServiceDate(
                baseline, agencyId, record.ServiceDate)!;
            var proposedPolicy = BillingCompliancePolicyRules.ResolveForServiceDate(
                proposed, agencyId, record.ServiceDate)!;
            var currentBlockers = ResolveBlockerIds(
                record, currentPolicy.Requirements);
            var proposedBlockers = ResolveBlockerIds(
                record, proposedPolicy.Requirements);
            var change = Classify(currentBlockers, proposedBlockers);
            if (change is null)
                continue;

            impacts.Add(new BillingCompliancePolicyRecordImpact(
                record.NoteId,
                record.PersonId,
                record.ServiceDate.Date,
                record.IsSubmittedOrFinalized,
                change.Value,
                currentBlockers,
                proposedBlockers,
                finalizedClaimIds.ToArray()));
        }

        return impacts;
    }

    private static string[] ResolveBlockerIds(
        BillingCompliancePolicyImpactRecordSnapshot record,
        BillingComplianceRequirements requirements) => record.Obligations
        .Where(obligation => obligation.PersonId == record.PersonId &&
                             (requirements & obligation.Requirement) == obligation.Requirement &&
                             (obligation.AppliesFromDate is null ||
                              record.ServiceDate.Date >= obligation.AppliesFromDate.Value.Date) &&
                             (obligation.RetiredDate is null ||
                              record.ServiceDate.Date < obligation.RetiredDate.Value.Date) &&
                             BillingComplianceGate.IsWithinBlockedInterval(
                                 obligation.DueDate,
                                 obligation.CompletedDate,
                                 record.ServiceDate))
        .Select(obligation => obligation.ObligationId.Trim())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static BillingCompliancePolicyImpactChangeKind? Classify(
        IReadOnlyList<string> currentBlockers,
        IReadOnlyList<string> proposedBlockers)
    {
        if (currentBlockers.Count == 0 && proposedBlockers.Count > 0)
            return BillingCompliancePolicyImpactChangeKind.NewlyBlocked;
        if (currentBlockers.Count > 0 && proposedBlockers.Count == 0)
            return BillingCompliancePolicyImpactChangeKind.NewlyUnblocked;
        if (currentBlockers.Count > 0 &&
            !currentBlockers.SequenceEqual(proposedBlockers, StringComparer.Ordinal))
        {
            return BillingCompliancePolicyImpactChangeKind.ChangedWhileBlocked;
        }

        return null;
    }

    private sealed class MutableBucket
    {
        private int _newlyBlocked;
        private int _newlyUnblocked;
        private int _changedWhileBlocked;

        public void Add(BillingCompliancePolicyImpactChangeKind change, int count)
        {
            if (count == 0)
                return;
            switch (change)
            {
                case BillingCompliancePolicyImpactChangeKind.NewlyBlocked:
                    _newlyBlocked += count;
                    break;
                case BillingCompliancePolicyImpactChangeKind.NewlyUnblocked:
                    _newlyUnblocked += count;
                    break;
                case BillingCompliancePolicyImpactChangeKind.ChangedWhileBlocked:
                    _changedWhileBlocked += count;
                    break;
            }
        }

        public BillingCompliancePolicyImpactBucket ToContract() =>
            new(_newlyBlocked, _newlyUnblocked, _changedWhileBlocked);
    }
}
