namespace Sati.Models.Billing;

/// <summary>Scheduling position only; never evidence of a physical send or account readiness.</summary>
public sealed class ClearinghouseDispatchRotation
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public int? LastAgencyId { get; set; }
    public long Revision { get; set; } = 1;
}

/// <summary>One persisted account-ordering pivot per offered agency, separate from business revisions.</summary>
public sealed class ClearinghouseAgencyDispatchRotation
{
    public int AgencyId { get; set; }
    public Guid? LastAccountId { get; set; }
    public long Revision { get; set; } = 1;
}
