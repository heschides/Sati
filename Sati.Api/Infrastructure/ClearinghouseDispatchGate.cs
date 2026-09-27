using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

/// <summary>Transport is only possible for an exact Demo/Testing identity and an explicit server opt-in.</summary>
internal sealed class ClearinghouseDispatchGate(IOptions<SatiApiOptions> options, IHostEnvironment environment)
{
    public bool IsSyntheticEnvironment =>
        options.Value.ExpectedEnvironment == "Demo" && options.Value.ExpectedDatabaseName == "SatiDemo" ||
        environment.IsEnvironment("Testing") && options.Value.ExpectedEnvironment == "Testing" &&
        options.Value.ExpectedDatabaseName == "SatiApiTests";

    public bool IsRealSandboxEnabled => IsSyntheticEnvironment && options.Value.EnableClaimMdSandboxTransport &&
        !options.Value.EnableSyntheticClearinghouseDispatch;

    public bool IsEnabled => IsSyntheticEnvironment &&
        (options.Value.EnableSyntheticClearinghouseDispatch || IsRealSandboxEnabled);

    public bool CanUseAccount(ClearinghouseAccount account) => account.IsEnabled && account.IsTest &&
        (options.Value.EnableSyntheticClearinghouseDispatch ||
         IsRealSandboxEnabled && account.ConnectorKind == TradingPartnerKind.ClaimMd &&
         EnvironmentClaimMdSandboxKeySource.IsValidReference(account.SecretReference));
}

internal sealed class ClearinghouseSelectionRejected(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal static class ClearinghouseAccountSelection
{
    public static async Task<TradingPartnerProfile> ForGenerationAsync(ApiDbContext db, int agencyId,
        GenerateEdiRequest request, ClearinghouseDispatchGate gate, CancellationToken token)
    {
        if (request.ClearinghouseAccountId is not { } accountId)
            return TradingPartnerProfile.OfficeAlly;
        if (!gate.IsEnabled || !request.IsTest)
            throw new ClearinghouseSelectionRejected("dispatch_unavailable",
                "Server-managed clearinghouse generation is available only in an enabled test environment.");
        var account = await db.ClearinghouseAccounts.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == accountId && row.AgencyId == agencyId && row.IsEnabled && row.IsTest, token);
        if (account is null || !gate.CanUseAccount(account))
            throw new ClearinghouseSelectionRejected("account_unavailable",
                "The selected test clearinghouse account is unavailable for this agency.");
        return Profile(account);
    }

    public static TradingPartnerProfile Profile(ClearinghouseAccount account)
    {
        if (account.TradingPartnerProfileVersion != TradingPartnerProfile.CurrentVersion)
            throw new ClearinghouseSelectionRejected("profile_unsupported", "The configured trading-partner profile is unsupported.");
        try
        {
            return account.ConnectorKind switch
            {
                TradingPartnerKind.OfficeAlly => TradingPartnerProfile.OfficeAlly,
                TradingPartnerKind.ClaimMd => TradingPartnerProfile.ClaimMd(
                    account.ExternalAccountNumber, account.ClaimNamespace ?? string.Empty),
                _ => throw new ClearinghouseSelectionRejected("profile_unsupported", "The configured trading partner is unsupported.")
            };
        }
        catch (ArgumentException)
        {
            throw new ClearinghouseSelectionRejected("account_invalid", "The test clearinghouse account needs administrator review.");
        }
    }

    /// <summary>Validate the retained bytes, including Claim.MD's stable per-claim D9, against this account.</summary>
    public static bool Matches(string content, ClearinghouseAccount account, int billingPeriodId)
    {
        try { return Matches(content, Profile(account), account.AgencyId, billingPeriodId, true); }
        catch (ClearinghouseSelectionRejected) { return false; }
    }

    public static bool Matches(string content, TradingPartnerProfile profile, int agencyId,
        int billingPeriodId, bool isTest)
    {
        try
        {
            var submitted = ClaimResponseReader.ReadSubmission(content);
            var envelope = submitted.Envelope;
            if (envelope.IsTestInterchange != isTest || envelope.SenderQualifier != "ZZ" ||
                envelope.ReceiverQualifier != "ZZ" ||
                envelope.SenderId != envelope.GroupSenderId ||
                envelope.ReceiverId != envelope.GroupReceiverId)
                return false;
            if (profile.Kind == TradingPartnerKind.OfficeAlly)
                return envelope.ReceiverId == "330897513" &&
                    submitted.Claims.All(claim => claim.RemoteClaimId is null);
            if (profile.Kind != TradingPartnerKind.ClaimMd ||
                envelope.SenderId != profile.AccountNumber || envelope.ReceiverId != "CLAIMMD" ||
                profile.ClaimNamespace is null)
                return false;
            var prefix = $"SATI1-{profile.ClaimNamespace}-{agencyId}-{billingPeriodId}-";
            return submitted.Claims.All(claim => claim.ServiceLineReferences.Count == 1 &&
                claim.RemoteClaimId == prefix + claim.ServiceLineReferences[0]);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
