using Sati.Contracts.V1;

namespace Sati.Models.Billing;

/// <summary>Server-owned routing metadata; SecretReference names a server-side secret, never its value.</summary>
public sealed class ClearinghouseAccount
{
    public Guid Id { get; set; }
    public int AgencyId { get; set; }
    public TradingPartnerKind ConnectorKind { get; set; }
    public bool IsTest { get; set; }
    public string ExternalAccountNumber { get; set; } = string.Empty;
    public string? ClaimNamespace { get; set; }
    public string? SecretReference { get; set; }
    public int TradingPartnerProfileVersion { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Revision { get; set; }
}
