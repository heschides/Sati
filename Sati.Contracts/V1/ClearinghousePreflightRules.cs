using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Sati.Contracts.V1;

public enum ClearinghousePreflightDisposition { Ready = 1, Deferred = 2, Held = 3 }

public sealed record ClearinghousePreflightState(ClearinghousePreflightDisposition Disposition,
    int FailureCount, Guid RecoveryCycleId, DateTime? NextEligibleAtUtc,
    DateTime? LastFailureAtUtc, string? SafeFailureCode)
{
    public static ClearinghousePreflightState Ready { get; } = new(
        ClearinghousePreflightDisposition.Ready, 0, Guid.Empty, null, null, null);
}

/// <summary>Known-unsent account preparation only. Never grants permission to replay an upload.</summary>
public static class ClearinghousePreflightRules
{
    public const int MaximumFailures = 5;
    public const string MissingAccountKey = "account_key_unavailable";
    private static readonly int[] DelayMinutes = [1, 5, 15, 60];

    public static bool CanReopen(ClearinghousePreflightState state)
    {
        Validate(state);
        return state.Disposition is ClearinghousePreflightDisposition.Deferred or ClearinghousePreflightDisposition.Held;
    }

    public static bool IsEligible(ClearinghousePreflightState? state, DateTime nowUtc)
    {
        if (state is null) return true;
        Validate(state);
        return state.Disposition == ClearinghousePreflightDisposition.Ready ||
            state.Disposition == ClearinghousePreflightDisposition.Deferred && state.NextEligibleAtUtc <= nowUtc;
    }

    public static ClearinghousePreflightState MissingKey(Guid accountId,
        ClearinghousePreflightState? current, Guid newCycleId, DateTime nowUtc)
    {
        if (accountId == Guid.Empty || newCycleId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Preflight recovery requires account/cycle identities and a UTC clock.");
        if (!IsEligible(current, nowUtc))
            throw new InvalidOperationException("This account is not due for another preflight probe.");
        var failures = (current?.FailureCount ?? 0) + 1;
        var cycle = current is { FailureCount: > 0 } ? current.RecoveryCycleId : newCycleId;
        if (failures == MaximumFailures)
            return new(ClearinghousePreflightDisposition.Held, failures, cycle, null, nowUtc, MissingAccountKey);
        var delay = TimeSpan.FromMinutes(DelayMinutes[failures - 1]);
        var seed = SHA256.HashData(Encoding.ASCII.GetBytes($"{accountId:N}:{cycle:N}:{failures}"));
        // Stable across hosts/restart, inclusive 0–10% extra delay. No random runtime state.
        var fraction = BinaryPrimitives.ReadUInt32LittleEndian(seed) % 10001;
        var jitter = TimeSpan.FromTicks(delay.Ticks / 10 * fraction / 10000);
        return new(ClearinghousePreflightDisposition.Deferred, failures, cycle,
            nowUtc.Add(delay).Add(jitter), nowUtc, MissingAccountKey);
    }

    public static void Validate(ClearinghousePreflightState state)
    {
        var valid = state.Disposition switch
        {
            ClearinghousePreflightDisposition.Ready => state.FailureCount == 0 &&
                state.RecoveryCycleId == Guid.Empty && state.NextEligibleAtUtc is null &&
                state.LastFailureAtUtc is null && state.SafeFailureCode is null,
            ClearinghousePreflightDisposition.Deferred => state.FailureCount is >= 1 and < MaximumFailures &&
                state.RecoveryCycleId != Guid.Empty && state.NextEligibleAtUtc is { } next &&
                state.LastFailureAtUtc is { } last && next > last && state.SafeFailureCode == MissingAccountKey,
            ClearinghousePreflightDisposition.Held => state.FailureCount == MaximumFailures &&
                state.RecoveryCycleId != Guid.Empty && state.NextEligibleAtUtc is null &&
                state.LastFailureAtUtc is not null && state.SafeFailureCode == MissingAccountKey,
            _ => false
        };
        if (!valid) throw new InvalidOperationException("Invalid clearinghouse preflight recovery state.");
    }
}
