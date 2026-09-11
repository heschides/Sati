using Sati.Contracts.V1;

namespace Sati.Models.Billing;

/// <summary>Immutable encrypted source evidence committed together with its financial effects.</summary>
public sealed class ClearinghouseResponseReceipt
{
    public Guid Id { get; set; }
    public int AgencyId { get; set; }
    public int ActorUserId { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public ClaimResponseKind Kind { get; set; }
    public bool IsTest { get; set; }
    public string ParserVersion { get; set; } = string.Empty;
    public string RawSha256 { get; set; } = string.Empty;
    public string SemanticSha256 { get; set; } = string.Empty;
    public string IdentitySha256 { get; set; } = string.Empty;
    public string? PaymentIdentitySha256 { get; set; }
    public byte[] Ciphertext { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
    public byte[] WrappedDataKey { get; set; } = [];
    public string KeyId { get; set; } = string.Empty;
    public BillingSubmissionStage? StageRecorded { get; set; }
    public int ClaimOutcomesRecorded { get; set; }
    public bool DepositRecorded { get; set; }
    public List<ClearinghouseResponseMatch> Matches { get; set; } = [];
}

public sealed class ClearinghouseResponseMatch
{
    public Guid ResponseId { get; set; }
    public long EdiGenerationId { get; set; }
    public int BillingPeriodId { get; set; }
    // Empty for a group-level functional acknowledgement.
    public string ClaimReference { get; set; } = string.Empty;
}
