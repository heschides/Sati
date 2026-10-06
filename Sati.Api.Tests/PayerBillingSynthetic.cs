using Sati.Contracts.V1;
namespace Sati.TestFixtures;
internal static class PayerBillingSynthetic
{
    public static PayerBillingConfiguration Configuration() => new()
    {
        ProfileKey = "mainecare-section13", EffectiveOn = new(2026, 4, 28),
        BillingProviderName = "Synthetic Agency", BillingProviderNpi = "1999999984", BillingProviderTaxId = "111111111",
        BillingTaxonomy = "251B00000X", BillingStreet = "1 Synthetic Way", BillingCity = "Portland", BillingState = "ME", BillingZip = "04101",
        RenderingProviderName = "Synthetic Rendering", RenderingProviderNpi = "1999999984", RenderingTaxonomy = "251B00000X",
        FacilityName = "Synthetic Facility", FacilityStreet = "3 Synthetic Way", FacilityCity = "Portland", FacilityState = "ME", FacilityZip = "04101",
        FacilityId = "AB12-123", FacilityIdQualifier = "G2", PayerName = "MAINE MEDICAID", PayerId = "MEMCD", SubmitterId = "SATITEST1",
        ContactName = "Synthetic Billing", ContactPhone = "2075550101", ProcedureCode = "G9012", Modifiers = ["HI"], UnitRate = 25,
        RequirementsReviewed = true, RequirementsEvidenceReference = "synthetic:agency-review-no-live-authority"
    };
    public static PayerBillingVersionDto Version(int agencyId = 1) => new(new Guid("11111111-1111-1111-1111-111111111111"), agencyId, 1, Configuration(), 11, new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));
    public static PayerClaimPreparation Preparation(PayerBillingVersionDto v, int personId) => new(v.Configuration.ProfileKey, v.VersionId,
        new(personId, v.Configuration.ProfileKey, v.Configuration.ProcedureCode, v.Configuration.RenderingProviderNpi,
            "SYNTHPA123", new(2026, 4, 28), new(2027, 4, 27), "synthetic:authorization-evidence", true)
            { AuthorizedOn = new(2026, 4, 28), Modifiers = v.Configuration.Modifiers });
    public static ProfessionalClaimSnapshot Legacy(int agencyId = 1, int personId = 101) => new(1, agencyId, personId,
        "Alex", "Example", new(1990, 2, 3), "U", "987654321", "10 Claim Street", "Portland", "ME", "04101",
        "Example Agency", "1999999984", "111111111", "1 Provider Way", "Portland", "ME", "04101", "SATITEST1", "Billing Desk", "2075550101", "SYNTHETIC PAYER", "MCDME");
    public static ProfessionalClaimSnapshot Frozen() { var v = Version(); return PayerBillingRules.Freeze(Legacy(), v, Preparation(v, 101), 11, new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), new(2026, 8, 1)); }
    public static ProfessionalClaimLineFacts Line(string json) => new(1, new(2026, 8, 1), "G9012", "HI", 1.33m, 33.25m, "987654321", "1999999984", "F89", 11, json);
}
