namespace Sati.Models.Billing;

public enum ClearinghouseDispatchState
{
    Queued = 1,
    Sending = 2,
    AcceptedByClearinghouse = 3,
    RejectedByClearinghouse = 4,
    OutcomeUnknown = 5,
    CancelledBeforeSend = 6,
    /// <summary>Manual, audited finding of authoritative vendor non-receipt; not a rejection.</summary>
    ConfirmedNotReceived = 7
}

/// <summary>Durable outbox identity for one exact, immutable EDI generation.</summary>
public sealed class ClearinghouseDispatch
{
    public Guid Id { get; set; }
    public int AgencyId { get; set; }
    public Guid AccountId { get; set; }
    public long EdiGenerationId { get; set; }
    public int RequestingUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public ClearinghouseDispatchState State { get; set; }
    public int TradingPartnerProfileVersion { get; set; }
    public string? ExternalFileId { get; set; }
    public int? AcceptedClaimCount { get; set; }
    public int? RejectedClaimCount { get; set; }
    public string? SafeErrorCode { get; set; }
    public long Revision { get; set; }
}

public enum ClearinghouseAttemptOutcome
{
    Accepted = 1,
    Rejected = 2,
    TransportFailure = 3,
    OutcomeUnknown = 4
}

/// <summary>Append-only evidence of one completed upload attempt. A Sending dispatch without an attempt is uncertain.</summary>
public sealed class ClearinghouseDispatchAttempt
{
    public Guid Id { get; set; }
    public Guid DispatchId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public ClearinghouseAttemptOutcome Outcome { get; set; }
    public string? VendorCode { get; set; }
    public string? CorrelationId { get; set; }
    public string? ResponseSha256 { get; set; }
    public byte[]? ResponseCiphertext { get; set; }
    public byte[]? ResponseNonce { get; set; }
    public byte[]? ResponseTag { get; set; }
    public byte[]? ResponseWrappedDataKey { get; set; }
    public string? ResponseKeyId { get; set; }
}
