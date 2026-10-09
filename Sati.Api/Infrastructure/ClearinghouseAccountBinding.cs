using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

internal static class ClearinghouseAccountBinding
{
    // Recheck the complete staged binding as well as its concurrency revision.
    public static bool Unchanged(ClearinghouseAccount staged, ClearinghouseAccount current) =>
        staged.Id == current.Id && staged.AgencyId == current.AgencyId && staged.Revision == current.Revision &&
        staged.ConnectorKind == current.ConnectorKind && staged.IsTest == current.IsTest &&
        staged.IsEnabled == current.IsEnabled && staged.ExternalAccountNumber == current.ExternalAccountNumber &&
        staged.ClaimNamespace == current.ClaimNamespace && staged.SecretReference == current.SecretReference &&
        staged.TradingPartnerProfileVersion == current.TradingPartnerProfileVersion;
}
