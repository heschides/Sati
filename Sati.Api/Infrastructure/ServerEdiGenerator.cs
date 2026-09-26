using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

/// <summary>Server persistence mapping into the shared, pure 837P formatter.</summary>
internal static class ServerEdiGenerator
{
    public static string Generate(
        ServerBillingPeriod period, bool isTest, DateTime generatedAt, string controlNumber,
        TradingPartnerProfile? profile = null) =>
        Professional837Formatter.Generate(period.Id, period.Year, period.Month,
            Originals(period), profile ?? TradingPartnerProfile.OfficeAlly,
            isTest, generatedAt, controlNumber);

    public static string GenerateCorrections(
        ServerBillingPeriod period,
        IReadOnlyList<EdiClaim> claims,
        bool isTest,
        DateTime generatedAt,
        string controlNumber,
        TradingPartnerProfile? profile = null)
    {
        ClaimSubmissionIdentity.RequireControlNumber(controlNumber);
        if (claims.Count == 0)
            throw new InvalidOperationException("There are no corrections waiting to be sent for this billing period.");
        if (claims.Any(claim => claim.FrequencyCode is not ("1" or "7" or "8") ||
                (claim.FrequencyCode is "7" or "8" && string.IsNullOrWhiteSpace(claim.PayerClaimControlNumber))))
            throw new InvalidOperationException("A replacement or void claim must cite the payer's claim number.");

        return Professional837Formatter.Generate(period.Id, period.Year, period.Month,
            claims.Select(ToSharedClaim).ToList(), profile ?? TradingPartnerProfile.OfficeAlly,
            isTest, generatedAt, controlNumber);
    }

    public static void ValidatePeriod(ServerBillingPeriod period) =>
        Professional837Formatter.ValidatePeriod(period.Year, period.Month, Originals(period));

    private static IReadOnlyList<Professional837Claim> Originals(ServerBillingPeriod period) =>
        period.Lines.Select(line => ToSharedClaim(new EdiClaim(line, "1", null))).ToList();

    private static Professional837Claim ToSharedClaim(EdiClaim claim) =>
        new(claim.Line.NoteId, ContractMapper.ToReadinessFacts(claim.Line),
            claim.FrequencyCode, claim.PayerClaimControlNumber);
}

/// <summary>One persisted correction with its claim frequency and optional payer number.</summary>
internal sealed record EdiClaim(ServerClaimLine Line, string FrequencyCode, string? PayerClaimControlNumber);
