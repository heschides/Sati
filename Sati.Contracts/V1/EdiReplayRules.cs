namespace Sati.Contracts.V1;

public enum EdiRequestKind { Original, Correction }

public readonly record struct EdiRequestIdentity(int BillingPeriodId, bool IsTest, EdiRequestKind Kind);

/// <summary>
/// Owns retained EDI request identity. Authorization, key scope, routing and current
/// release eligibility remain with callers; frequency 1 does not identify request kind.
/// </summary>
public static class EdiReplayRules
{
    public static bool Matches(EdiRequestIdentity retained, EdiRequestIdentity requested) =>
        retained.BillingPeriodId > 0 && requested.BillingPeriodId > 0 &&
        Enum.IsDefined(retained.Kind) && Enum.IsDefined(requested.Kind) && retained == requested;
}
