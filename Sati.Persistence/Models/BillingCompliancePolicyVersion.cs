using Sati.Contracts.V1;

namespace Sati.Models;

/// <summary>
/// Append-only agency billing policy. Updating or deleting an existing row would
/// change the meaning of historical service dates, so callers add a new version.
/// </summary>
public sealed class BillingCompliancePolicyVersion
{
    public long Id { get; private set; }
    public Guid VersionId { get; private set; }
    public int AgencyId { get; private set; }
    public DateTime EffectiveOn { get; private set; }
    public BillingComplianceRequirements Requirements { get; private set; }
    public int CreatedByUserId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string? Explanation { get; private set; }

    private BillingCompliancePolicyVersion() { }

    public static BillingCompliancePolicyVersion Create(
        int agencyId,
        BillingComplianceRequirements requirements,
        DateTime effectiveOn,
        DateTime agencyToday,
        int createdByUserId,
        DateTime recordedAtUtc,
        bool allowPastEffectiveDates = false,
        string? explanation = null,
        Guid? changeId = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(agencyId, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(createdByUserId, 0);
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The recorded timestamp must be UTC.", nameof(recordedAtUtc));
        if (changeId == Guid.Empty)
            throw new ArgumentException("The policy change id cannot be empty.", nameof(changeId));

        var decision = BillingCompliancePolicyRules.ValidateChange(
            requirements,
            effectiveOn,
            agencyToday,
            new BillingCompliancePolicyOptions(allowPastEffectiveDates),
            explanation);
        if (!decision.Accepted)
            throw new ArgumentException(string.Join(" ", decision.Errors), nameof(effectiveOn));

        return new BillingCompliancePolicyVersion
        {
            VersionId = changeId ?? Guid.NewGuid(),
            AgencyId = agencyId,
            EffectiveOn = effectiveOn.Date,
            Requirements = requirements,
            CreatedByUserId = createdByUserId,
            RecordedAtUtc = recordedAtUtc,
            Explanation = string.IsNullOrWhiteSpace(explanation) ? null : explanation.Trim()
        };
    }

    public BillingCompliancePolicyVersionSnapshot ToSnapshot() =>
        new(Id, AgencyId, EffectiveOn, Requirements);
}
