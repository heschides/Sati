namespace Sati.Models.Billing;

/// <summary>Shared polling scheduling position only; never a receipt/feed checkpoint.</summary>
public sealed class ClearinghousePollRotation
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public int? LastAgencyId { get; set; }
    public long Revision { get; set; } = 1;
}

public sealed class ClearinghouseAgencyPollRotation
{
    public int AgencyId { get; set; }
    public Guid? LastAccountId { get; set; }
    public long Revision { get; set; } = 1;
}

public sealed class ClearinghouseAccountPollRotation
{
    public int AgencyId { get; set; }
    public Guid AccountId { get; set; }
    public ClearinghouseFeedKind? LastFeedKind { get; set; }
    public long Revision { get; set; } = 1;
}
