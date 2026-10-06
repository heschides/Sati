using Sati.Contracts.V1;
using Sati.TestFixtures;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Helpers;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Tests;
public class PayerBillingRulesTests
{
    [Fact] public void EffectiveDatesAreInclusiveAndExpiredNewVersionNeverFallsBack()
    {
        var first = PayerBillingSynthetic.Version();
        var second = first with { VersionId = Guid.NewGuid(), Revision = 2, Configuration = first.Configuration with { EffectiveOn = new(2026, 9, 1), ExpiresOn = new(2026, 9, 30) } };
        Assert.Null(PayerBillingRules.Resolve([first, second], 1, first.Configuration.ProfileKey, new(2026, 4, 27)));
        Assert.Equal(first, PayerBillingRules.Resolve([second, first], 1, first.Configuration.ProfileKey, new(2026, 4, 28)));
        Assert.Equal(first, PayerBillingRules.Resolve([second, first], 1, first.Configuration.ProfileKey, new(2026, 8, 31, 23, 59, 59)));
        Assert.Equal(second, PayerBillingRules.Resolve([first, second], 1, first.Configuration.ProfileKey, new(2026, 9, 1)));
        Assert.Equal(second, PayerBillingRules.Resolve([first, second], 1, first.Configuration.ProfileKey, new(2026, 9, 30)));
        Assert.Null(PayerBillingRules.Resolve([first, second], 1, first.Configuration.ProfileKey, new(2026, 10, 1)));
        Assert.Null(PayerBillingRules.Resolve([first], 2, first.Configuration.ProfileKey, new(2026, 8, 1)));
    }
    [Theory] [InlineData("facility")] [InlineData("taxonomy")] [InlineData("routing")] [InlineData("modifier")] [InlineData("evidence")] [InlineData("delimiter")] [InlineData("nonascii")]
    public void MissingOrIncompatibleConfigurationReportsSpecificFields(string defect)
    {
        var c = PayerBillingSynthetic.Configuration();
        c = defect switch { "facility" => c with { FacilityId = "1234567" }, "taxonomy" => c with { RenderingTaxonomy = "" },
            "routing" => c with { PayerId = "MCDME" }, "modifier" => c with { Modifiers = ["XX"] },
            "evidence" => c with { RequirementsReviewed = false }, "delimiter" => c with { FacilityName = "A~NM1*" },
            _ => c with { BillingProviderName = "Café" } };
        Assert.NotEmpty(PayerBillingRules.Validate(c));
        Assert.All(PayerBillingRules.Validate(c), e => Assert.False(string.IsNullOrWhiteSpace(e.Field)));
    }
    [Theory] [InlineData("missing")] [InlineData("scope")] [InlineData("dates")] [InlineData("review")] [InlineData("stale")]
    public void AuthorizationReferenceAloneAndStaleConfigurationCannotPrepareClaims(string defect)
    {
        var v = PayerBillingSynthetic.Version(); var p = PayerBillingSynthetic.Preparation(v, 101);
        p = defect switch { "missing" => p with { Authorization = null }, "scope" => p with { Authorization = p.Authorization! with { PersonId = 102 } },
            "dates" => p with { Authorization = p.Authorization! with { ExpiresOn = new(2026, 7, 31) } },
            "review" => p with { Authorization = p.Authorization! with { CoverageReviewed = false } }, _ => p with { ExpectedVersionId = Guid.NewGuid() } };
        Assert.NotEmpty(PayerBillingRules.ValidatePreparation(v, p, 1, 101, new(2026, 8, 1)));
    }
    [Fact] public void VersionsCannotBeDowngradedAndMalformedFrozenInputsFailReadiness()
    {
        var v2 = PayerBillingSynthetic.Frozen();
        Assert.Throws<InvalidOperationException>(() => ProfessionalClaimSnapshotCodec.Deserialize(ProfessionalClaimSnapshotCodec.Serialize(v2 with { Version = 1 })));
        Assert.Throws<InvalidOperationException>(() => ProfessionalClaimSnapshotCodec.Deserialize(ProfessionalClaimSnapshotCodec.Serialize(v2 with { PayerInputs = null })));
        var invalid = v2 with { PayerInputs = v2.PayerInputs! with { ConfigurationVersion = v2.PayerInputs.ConfigurationVersion with { Configuration = v2.PayerInputs.ConfigurationVersion.Configuration with { FacilityId = "" } } } };
        var readiness = ProfessionalClaimReadiness.EvaluatePeriod(2026, 8, [PayerBillingSynthetic.Line(ProfessionalClaimSnapshotCodec.Serialize(invalid))]);
        Assert.False(readiness.IsReady); Assert.Contains("FacilityId", readiness.ExplainFailure());
    }
    [Fact] public void CorrectionRetainsOriginalConfigurationAndAuthorizationWithHashProvenance()
    {
        var frozen = PayerBillingSynthetic.Frozen(); var json = ProfessionalClaimSnapshotCodec.Serialize(frozen);
        var changed = PayerBillingSynthetic.Legacy() with { SubscriberStreet = "11 Changed Street", BillingProviderNpi = "1234567893", PayerId = "DIFFERENT" };
        var corrected = PayerBillingRules.PreserveCorrection(changed, json, new(2026, 8, 1));
        Assert.Equal("11 Changed Street", corrected.SubscriberStreet);
        Assert.Equal(frozen.BillingProviderNpi, corrected.BillingProviderNpi); Assert.Equal(frozen.PayerId, corrected.PayerId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(frozen.PayerInputs!.ConfigurationVersion), System.Text.Json.JsonSerializer.Serialize(corrected.PayerInputs!.ConfigurationVersion));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(frozen.PayerInputs.Authorization), System.Text.Json.JsonSerializer.Serialize(corrected.PayerInputs.Authorization)); Assert.Equal(64, corrected.PayerInputs.PreviousSnapshotSha256!.Length);
        Assert.Throws<InvalidOperationException>(() => PayerBillingRules.PreserveCorrection(changed, json, new(2027, 5, 1)));
        Assert.Equal(json, ProfessionalClaimSnapshotCodec.Serialize(frozen));
    }
    [Fact] public void LocalAndApiRenderIdenticalConfiguredClaimsAndGoldenLoops()
    {
        var json = ProfessionalClaimSnapshotCodec.Serialize(PayerBillingSynthetic.Frozen());
        var d = new BillingPeriod { Id = 77, Year = 2026, Month = 8 };
        d.Lines.Add(new() { Id = 1, NoteId = 99, DateOfService = new(2026, 8, 1), ProcedureCode = "G9012", ProcedureModifier = "HI", Units = 1.33m, ChargeAmount = 33.25m,
            ClientMaineCareId = "987654321", RenderingProviderNpi = "1999999984", DiagnosisCode = "F89", PlaceOfService = 11, ClaimSnapshotJson = json });
        var s = new ServerBillingPeriod { Id = 77, Year = 2026, Month = 8 };
        s.Lines.Add(new() { Id = 1, NoteId = 99, DateOfService = new(2026, 8, 1), ProcedureCode = "G9012", ProcedureModifier = "HI", Units = 1.33m, ChargeAmount = 33.25m,
            ClientMaineCareId = "987654321", RenderingProviderNpi = "1999999984", DiagnosisCode = "F89", PlaceOfService = 11, ClaimSnapshotJson = json });
        var profile = TradingPartnerProfile.ClaimMd("ACCT123", "DEMO01"); var at = new DateTime(2026, 8, 29, 9, 30, 0);
        var content = EdiGenerator.Generate(d, true, at, "123456789", profile);
        var golden = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "PayerBilling-MaineCare.837"));
        Assert.Equal(golden.Replace("\r\n", "\n").TrimEnd(), content.Replace("\r\n", "\n").TrimEnd());
        Assert.Equal(content, ServerEdiGenerator.Generate(s, true, at, "123456789", profile));
        var segments = content.Split('~', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        Assert.Equal(106, segments[0].Length + 1);
        Assert.Equal(segments.FindIndex(x => x.StartsWith("SE*")) - segments.FindIndex(x => x.StartsWith("ST*")) + 1, int.Parse(segments.Single(x => x.StartsWith("SE*")).Split('*')[1]));
        Assert.Contains("REF*G1*SYNTHPA123~" + Environment.NewLine + "HI*ABK:F89~" + Environment.NewLine + "NM1*82*2*Synthetic Rendering*****XX*1999999984~", content);
        Assert.Contains("PRV*PE*PXC*251B00000X~" + Environment.NewLine + "NM1*77*2*Synthetic Facility~" + Environment.NewLine + "N3*3 Synthetic Way~" + Environment.NewLine + "N4*Portland*ME*04101~" + Environment.NewLine + "REF*G2*AB12-123~" + Environment.NewLine + "LX*1~", content);
        Assert.Contains("REF*D9*SATI1-DEMO01-1-77-99~", content); Assert.Contains("SV1*HC:G9012:HI*33.25*UN*1.33*11", content);
        Assert.Throws<InvalidOperationException>(() => EdiGenerator.Generate(d, true, at, "123456789", TradingPartnerProfile.OfficeAlly));
    }
    [Theory] [InlineData("1", null)] [InlineData("7", "PAYERICN")] [InlineData("8", "PAYERICN")]
    public void CorrectionFrequencyRetainsStableRemoteIdentityAndNewClaimReference(string frequency, string? payerNumber)
    {
        var line = PayerBillingSynthetic.Line(ProfessionalClaimSnapshotCodec.Serialize(PayerBillingSynthetic.Frozen()));
        var file = Professional837Formatter.Generate(77, 2026, 8, [new(99, line, frequency, payerNumber)], TradingPartnerProfile.ClaimMd("ACCT123", "DEMO01"), true, new(2026,8,29,9,30,0), "123456790");
        Assert.Contains("CLM*123456790-77-99", file); Assert.Contains("REF*D9*SATI1-DEMO01-1-77-99~", file);
        Assert.Equal(payerNumber is not null, file.Contains("REF*F8*PAYERICN~"));
    }
    [Fact] public void MultipleClaimsPreserveSubscriberVersionsAndEachSingleLineStartsAtOne()
    {
        var first = PayerBillingSynthetic.Frozen(); var second = first with { SubscriberStreet = "11 Changed Street" };
        var a = PayerBillingSynthetic.Line(ProfessionalClaimSnapshotCodec.Serialize(first));
        var b = PayerBillingSynthetic.Line(ProfessionalClaimSnapshotCodec.Serialize(second)) with { LineId = 2 };
        var file = Professional837Formatter.Generate(77, 2026, 8, [new(99, a, "1", null), new(100, b, "1", null)], TradingPartnerProfile.ClaimMd("ACCT123", "DEMO01"), true, new(2026, 8, 29), "123456789");
        Assert.Contains("N3*10 Claim Street~", file); Assert.Contains("N3*11 Changed Street~", file);
        Assert.Equal(2, file.Split('~').Count(s => s.Trim() == "LX*1"));
        Assert.Equal(2, file.Split('~').Count(s => s.Trim().StartsWith("NM1*IL*")));
    }
    [Fact] public void OtherProfilesCarryOrderedModifiersAndAnExplicitCommercialIndicator()
    {
        var v = PayerBillingSynthetic.Version(); v = v with { Configuration = v.Configuration with
            { Kind = PayerBillingProfileKind.OtherProfessional, PayerId = "TESTPAY", ClaimFilingIndicator = "CI", Modifiers = ["HI", "GT", "95"] } };
        var snapshot = PayerBillingRules.Freeze(PayerBillingSynthetic.Legacy(), v, PayerBillingSynthetic.Preparation(v, 101), 11,
            new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), new(2026, 8, 1));
        var file = Professional837Formatter.Generate(77, 2026, 8, [new(99, PayerBillingSynthetic.Line(ProfessionalClaimSnapshotCodec.Serialize(snapshot)), "1", null)], TradingPartnerProfile.ClaimMd("ACCT123", "DEMO01"), true, new(2026, 8, 29), "123456789");
        Assert.Contains("SBR*P*18*******CI~", file); Assert.Contains("SV1*HC:G9012:HI:GT:95*", file);
    }
    [Theory] [InlineData("*")] [InlineData("~")] [InlineData(":")] [InlineData("^")] [InlineData("\n")] [InlineData("\t")]
    public void FrozenDelimitersAndControlsCannotProduceAClaimFile(string unsafeValue)
    {
        var s = PayerBillingSynthetic.Frozen(); var inputs = s.PayerInputs!;
        s = s with { PayerInputs = inputs with { Authorization = inputs.Authorization! with { Reference = "AUTH" + unsafeValue } } };
        Assert.Throws<InvalidOperationException>(() => Professional837Formatter.Generate(77, 2026, 8,
            [new(99, PayerBillingSynthetic.Line(ProfessionalClaimSnapshotCodec.Serialize(s)), "1", null)],
            TradingPartnerProfile.ClaimMd("ACCT123", "DEMO01"), true, new(2026, 8, 29), "123456789"));
    }
}
