using System.Text.Json;
using Sati.Contracts.V1;

namespace Sati.Models;

/// <summary>
/// Append-only review signal created when a billing-policy version changes the
/// compliance blockers on a submitted or finalized record. It never changes
/// the note or claim line it points to.
/// </summary>
public sealed class BillingCompliancePolicyReviewFlag
{
    public long Id { get; private set; }
    public Guid FlagId { get; private set; }
    public int AgencyId { get; private set; }
    public long PolicyVersionId { get; private set; }
    public BillingCompliancePolicyVersion PolicyVersion { get; private set; } = null!;
    public int PersonId { get; private set; }
    public int NoteId { get; private set; }
    public int? ClaimLineId { get; private set; }
    public string RecordKey { get; private set; } = string.Empty;
    public DateTime ServiceDate { get; private set; }
    public BillingCompliancePolicyImpactChangeKind ChangeKind { get; private set; }
    public string PreviousBlockingObligationIdsJson { get; private set; } = "[]";
    public string NewBlockingObligationIdsJson { get; private set; } = "[]";
    public DateTime CreatedAtUtc { get; private set; }

    private BillingCompliancePolicyReviewFlag() { }

    public static BillingCompliancePolicyReviewFlag ForNote(
        BillingCompliancePolicyVersion policyVersion,
        BillingCompliancePolicyRecordImpact impact,
        DateTime createdAtUtc) => Create(policyVersion, impact, null, createdAtUtc);

    public static BillingCompliancePolicyReviewFlag ForClaimLine(
        BillingCompliancePolicyVersion policyVersion,
        BillingCompliancePolicyRecordImpact impact,
        int claimLineId,
        DateTime createdAtUtc)
    {
        if (!impact.SubmittedOrFinalizedClaimRecordIds.Contains(claimLineId))
            throw new ArgumentException(
                "The claim line is not a submitted or finalized record in this impact.",
                nameof(claimLineId));
        return Create(policyVersion, impact, claimLineId, createdAtUtc);
    }

    public BillingCompliancePolicyReviewFlagDto ToContract() => new(
        FlagId,
        PolicyVersionId,
        PolicyVersion.EffectiveOn,
        PolicyVersion.Requirements,
        PersonId,
        NoteId,
        ClaimLineId,
        ServiceDate,
        ChangeKind,
        Deserialize(PreviousBlockingObligationIdsJson),
        Deserialize(NewBlockingObligationIdsJson),
        DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc));

    private static BillingCompliancePolicyReviewFlag Create(
        BillingCompliancePolicyVersion policyVersion,
        BillingCompliancePolicyRecordImpact impact,
        int? claimLineId,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(policyVersion);
        ArgumentNullException.ThrowIfNull(impact);
        if (!impact.IsSubmittedOrFinalized)
            throw new ArgumentException(
                "Only submitted or finalized notes receive durable review flags.",
                nameof(impact));
        if (impact.NoteId <= 0 || impact.PersonId <= 0)
            throw new ArgumentException("The impacted record identity is invalid.", nameof(impact));
        if (claimLineId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(claimLineId));
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The review-flag timestamp must be UTC.", nameof(createdAtUtc));

        return new BillingCompliancePolicyReviewFlag
        {
            FlagId = Guid.NewGuid(),
            AgencyId = policyVersion.AgencyId,
            PolicyVersion = policyVersion,
            PersonId = impact.PersonId,
            NoteId = impact.NoteId,
            ClaimLineId = claimLineId,
            RecordKey = claimLineId is int id ? $"claim:{id}" : $"note:{impact.NoteId}",
            ServiceDate = impact.ServiceDate.Date,
            ChangeKind = impact.ChangeKind,
            PreviousBlockingObligationIdsJson = Serialize(
                impact.PreviousBlockingObligationIds),
            NewBlockingObligationIdsJson = Serialize(
                impact.NewBlockingObligationIds),
            CreatedAtUtc = createdAtUtc
        };
    }

    private static string Serialize(IEnumerable<string> values) => JsonSerializer.Serialize(
        values.Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());

    private static IReadOnlyList<string> Deserialize(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];
}
