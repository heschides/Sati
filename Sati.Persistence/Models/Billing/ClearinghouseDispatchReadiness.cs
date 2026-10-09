using Sati.Contracts.V1;

namespace Sati.Models.Billing;

/// <summary>Account dispatch scheduling, independent of immutable physical claim evidence.</summary>
public sealed class ClearinghouseDispatchReadiness
{
    public int AgencyId { get; set; }
    public Guid AccountId { get; set; }
    public ClearinghousePreflightDisposition Disposition { get; set; }
    public int FailureCount { get; set; }
    public Guid RecoveryCycleId { get; set; }
    public DateTime? NextEligibleAtUtc { get; set; }
    public DateTime? LastFailureAtUtc { get; set; }
    public string? SafeFailureCode { get; set; }
    public long ValidatedAccountRevision { get; set; }
    public long Revision { get; set; }

    public ClearinghousePreflightState Snapshot() => new(Disposition, FailureCount, RecoveryCycleId,
        NextEligibleAtUtc, LastFailureAtUtc, SafeFailureCode);

    public void Apply(ClearinghousePreflightState state, long accountRevision)
    {
        ClearinghousePreflightRules.Validate(state);
        Disposition = state.Disposition; FailureCount = state.FailureCount;
        RecoveryCycleId = state.RecoveryCycleId; NextEligibleAtUtc = state.NextEligibleAtUtc;
        LastFailureAtUtc = state.LastFailureAtUtc; SafeFailureCode = state.SafeFailureCode;
        ValidatedAccountRevision = accountRevision; Revision++;
    }
}
