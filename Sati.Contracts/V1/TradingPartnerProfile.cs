namespace Sati.Contracts.V1;

public enum TradingPartnerKind
{
    OfficeAlly = 1,
    ClaimMd = 2
}

/// <summary>
/// Versioned, non-secret 837P routing values. The API will own selection and account
/// configuration in a later phase; this profile does not authorize transmission.
/// </summary>
public sealed class TradingPartnerProfile
{
    public const int CurrentVersion = 1;

    private TradingPartnerProfile(TradingPartnerKind kind, string? accountNumber, string? claimNamespace)
    {
        Version = CurrentVersion;
        Kind = kind;
        AccountNumber = accountNumber;
        ClaimNamespace = claimNamespace;
    }

    public int Version { get; }
    public TradingPartnerKind Kind { get; }
    public string? AccountNumber { get; }
    public string? ClaimNamespace { get; }

    public static TradingPartnerProfile OfficeAlly { get; } = new(TradingPartnerKind.OfficeAlly, null, null);

    public static TradingPartnerProfile ClaimMd(string accountNumber, string claimNamespace)
    {
        if (!BillingRules.IsSafeX12Element(accountNumber, 15) ||
            !string.Equals(accountNumber, accountNumber.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A Claim.MD account number must be a safe, nonblank X12 identifier of at most 15 characters.", nameof(accountNumber));
        if (string.IsNullOrEmpty(claimNamespace) || claimNamespace.Length > 8 ||
            claimNamespace.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9')))
            throw new ArgumentException("The stable claim namespace must contain 1 to 8 uppercase letters or digits.", nameof(claimNamespace));
        return new(TradingPartnerKind.ClaimMd, accountNumber, claimNamespace);
    }

    internal string SenderId(ProfessionalClaimSnapshot snapshot) => Kind switch
    {
        TradingPartnerKind.OfficeAlly => snapshot.SubmitterId,
        TradingPartnerKind.ClaimMd => AccountNumber!,
        _ => throw new InvalidOperationException("Unsupported trading-partner profile.")
    };

    internal string ReceiverId => Kind switch
    {
        TradingPartnerKind.OfficeAlly => "330897513",
        TradingPartnerKind.ClaimMd => "CLAIMMD",
        _ => throw new InvalidOperationException("Unsupported trading-partner profile.")
    };

    internal string ReceiverName => Kind switch
    {
        TradingPartnerKind.OfficeAlly => "OFFICE ALLY",
        TradingPartnerKind.ClaimMd => "CLAIM.MD",
        _ => throw new InvalidOperationException("Unsupported trading-partner profile.")
    };
}
