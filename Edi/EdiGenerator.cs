using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Helpers;

/// <summary>Desktop persistence mapping into the shared, pure 837P formatter.</summary>
public static class EdiGenerator
{
    public static string Generate(
        BillingPeriod period, bool isTest, DateTime generatedAt, string controlNumber,
        TradingPartnerProfile? profile = null) =>
        Professional837Formatter.Generate(period.Id, period.Year, period.Month,
            Claims(period), profile ?? TradingPartnerProfile.OfficeAlly,
            isTest, generatedAt, controlNumber);

    public static void ValidatePeriod(BillingPeriod period) =>
        Professional837Formatter.ValidatePeriod(period.Year, period.Month, Claims(period));

    private static IReadOnlyList<Professional837Claim> Claims(BillingPeriod period) =>
        period.Lines.Select(line => new Professional837Claim(line.NoteId,
            new ProfessionalClaimLineFacts(
                line.Id, line.DateOfService, line.ProcedureCode, line.ProcedureModifier,
                line.Units, line.ChargeAmount, line.ClientMaineCareId,
                line.RenderingProviderNpi, line.DiagnosisCode, line.PlaceOfService,
                line.ClaimSnapshotJson), "1", null)).ToList();
}
