namespace Sati.Models.Billing;

public enum ClearinghouseFeedKind
{
    Status = 1,
    Era = 2,
    Modifications = 3
}

/// <summary>Account/feed-specific opaque cursor, advanced only with committed receipt effects.</summary>
public sealed class ClearinghouseFeedCheckpoint
{
    public Guid Id { get; set; }
    public int AgencyId { get; set; }
    public Guid AccountId { get; set; }
    public ClearinghouseFeedKind FeedKind { get; set; }
    public string? Cursor { get; set; }
    public Guid? LastReceiptId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Revision { get; set; }
}
