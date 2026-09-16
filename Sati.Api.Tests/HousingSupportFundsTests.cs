using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;
using Sati.Forms;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class HousingSupportFundsTests(SatiApiFactory factory)
{
    private const int OwnPerson = 101;
    private const int OtherAgencyPerson = 201;
    private static string Route(int id) => $"/api/v1/people/{id}/housing-support-funds.pdf";

    [Fact]
    public void Embedded_application_is_the_exact_controlled_source_supplied_for_the_feature()
    {
        using var stream = typeof(HousingSupportFundsPdfGenerator).Assembly
            .GetManifestResourceStream(HousingSupportFundsPdfGenerator.ResourceName);
        Assert.NotNull(stream);
        Assert.Equal("F44074E0FFE5096369BDCB32B30DE645155108B8A96093A75B62E9DDD8588190",
            Convert.ToHexString(SHA256.HashData(stream)));
    }

    [Fact]
    public void Generator_preserves_the_three_page_fillable_state_form_and_fills_only_applicant_fields()
    {
        var generator = new HousingSupportFundsPdfGenerator();
        var subject = Subject();
        var bytes = generator.Generate(subject, ValidRequest(),
            new DateTime(2026, 9, 13, 18, 0, 0, DateTimeKind.Utc));

        using var sourceStream = typeof(HousingSupportFundsPdfGenerator).Assembly
            .GetManifestResourceStream(HousingSupportFundsPdfGenerator.ResourceName)!;
        using var source = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Import);
        using var result = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(3, result.PageCount);
        Assert.NotNull(result.AcroForm);
        Assert.NotEmpty(result.AcroForm!.Fields.Names);
        Assert.False(result.AcroForm.Elements.GetBoolean("/NeedAppearances"));
        Assert.Equal("Housing Support Funds application - Avery Consumer", result.Info.Title);
        Assert.Equal(HousingSupportFundsRules.SourceRevision, result.Info.Subject);
        for (var page = 0; page < source.PageCount; page++)
            Assert.Equal(Digest(source.Pages[page]), Digest(result.Pages[page]));

        var fields = result.AcroForm.Fields;
        Assert.Equal("Avery Consumer", Text(fields, "Consumer Name"));
        Assert.Equal("Avery Consumer", Text(fields, "I, Consumer Name"));
        Assert.Equal("1500.00", Text(fields, "Amount requested"));
        Assert.Contains("One-time assistance is requested", Text(fields,
            "Please share any additional details regarding your request for Housing Support Funds"));
        Assert.True(NormalAppearanceLength(fields,
                "Please share any additional details regarding your request for Housing Support Funds") > 0);
        Assert.Equal("/On", Raw(fields, "Section 21"));
        Assert.Equal("/Off", Raw(fields, "Section 29"));
        Assert.Equal("/On", Raw(fields, "Housing rental"));
        Assert.Equal("/Off", Raw(fields, "Housing owned"));
        Assert.Equal("/Off", Raw(fields, "Is consumers living situation a Shared Living Situation Check One Yes"));
        Assert.Equal("/On", Raw(fields, "No"));

        foreach (var fieldName in StaffAndSignatureFields)
            Assert.True(string.IsNullOrEmpty(Raw(fields, fieldName)),
                $"'{fieldName}' must remain blank.");
    }

    [Fact]
    public void Amount_and_subsidy_rules_match_the_application_wording()
    {
        var tooLarge = HousingSupportFundsRules.Validate(ValidRequest() with { AmountRequested = 3000.01m });
        var missingSubsidyType = HousingSupportFundsRules.Validate(ValidRequest() with
        {
            ReceivesSubsidy = true,
            SubsidyType = null
        });

        Assert.Contains(nameof(HousingSupportFundsRequest.AmountRequested), tooLarge.Keys);
        Assert.Contains(nameof(HousingSupportFundsRequest.SubsidyType), missingSubsidyType.Keys);
    }

    [Fact]
    public void Eligibility_conflicts_are_prominent_review_items_not_silent_profile_inferences()
    {
        var items = HousingSupportFundsRules.FindReviewItems(
            Subject() with { IsSharedLiving = true },
            ValidRequest() with { ReceivesSubsidy = true, SubsidyType = "Section 8" });

        Assert.Contains(items, value => value.Contains("Shared Living", StringComparison.Ordinal));
        Assert.Contains(items, value => value.Contains("housing subsidy", StringComparison.Ordinal));
        Assert.Contains("Consumer or guardian signature and date", items);
    }

    [Fact]
    public async Task Anonymous_and_other_agency_callers_cannot_generate_the_application()
    {
        using var anonymous = factory.CreateAnonymousClient();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(Route(OwnPerson), ValidRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.PostAsJsonAsync(Route(OtherAgencyPerson), ValidRequest())).StatusCode);
    }

    [Fact]
    public async Task Own_application_is_non_cacheable_audited_and_recorded_as_a_versioned_draft()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await factory.DeleteDocumentArtifactsAsync(OwnPerson, AnnualDocumentKind.HousingSupportFundsApplication);
        var before = await factory.GetAuditEventsAsync("housing-support-funds.generated");
        try
        {
            var response = await client.PostAsJsonAsync(Route(OwnPerson), ValidRequest());

            response.EnsureSuccessStatusCode();
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
            Assert.True(response.Headers.TryGetValues("X-Sati-Review-Items", out var review));
            Assert.Contains("Consumer or guardian signature and date", string.Join("|", review!));
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

            var after = await factory.GetAuditEventsAsync("housing-support-funds.generated");
            Assert.Equal(before.Count + 1, after.Count);
            Assert.Equal(OwnPerson.ToString(), after[^1].ResourceId);

            var person = (await client.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload"))!
                .Single(value => value.Id == OwnPerson);
            var cycle = AnnualDocumentCycle.CurrentStart(person.EffectiveDate!.Value, DateTime.Today);
            var artifacts = await client.GetFromJsonAsync<List<DocumentArtifactDto>>(
                $"/api/v1/people/{OwnPerson}/documents?cycleStart={cycle:yyyy-MM-dd}");
            var artifact = Assert.Single(artifacts!, value =>
                value.Kind == AnnualDocumentKind.HousingSupportFundsApplication.ToString());
            Assert.Equal(DocumentArtifactOrigin.Draft.ToString(), artifact.Origin);
            Assert.Equal("Maine DHHS OADS", artifact.TemplateOwner);
            Assert.Equal("Housing-Support-Funds-Application", artifact.TemplateKey);
            Assert.Equal(20250630, artifact.TemplateVersion);
        }
        finally
        {
            await factory.DeleteDocumentArtifactsAsync(OwnPerson, AnnualDocumentKind.HousingSupportFundsApplication);
        }
    }

    private static HousingSupportFundsSubject Subject() => new(
        7, "Avery Consumer", "Section21", false, true, "Gale Guardian",
        "Case Manager", "Demo Provider", "1 Agency Way, Portland, ME 04101",
        "207-555-0101", "case.manager@example.test");

    private static HousingSupportFundsRequest ValidRequest() => new(
        ConsumerTelephone: "207-555-0100",
        ConsumerEmail: "avery@example.test",
        ConsumerAddress: "1 Demo Street, Portland, ME 04101",
        HousingType: "Rental",
        LandlordName: "Demo Landlord",
        LandlordAddress: "2 Sample Road, Portland, ME 04101",
        LandlordTelephone: "207-555-0102",
        LandlordEmail: "landlord@example.test",
        MonthlyHousingAmount: 925m,
        AmountRequested: 1500m,
        ReceivesSubsidy: false,
        GuardianAddress: "3 Guardian Lane, Portland, ME 04101",
        GuardianTelephone: "207-555-0103",
        GuardianEmail: "guardian@example.test",
        RepresentativePayeeName: "Demo Payee",
        RepresentativePayeeAddress: "4 Payee Avenue, Portland, ME 04101",
        AdditionalDetails: "One-time assistance is requested for eligible housing-related costs.",
        SupportingDocumentReady: true);

    private static readonly string[] StaffAndSignatureFields =
    [
        "Consumer Signature", "Date", "Guardian Signature", "Date_2",
        "Vendor Code", "Coding", "Requested amount", "Approved amount",
        "Address1", "Address2", "Comment", "Comment2", "Signature17", "Date approved"
    ];

    private static string Text(PdfAcroField.PdfAcroFieldCollection fields, string name) =>
        Raw(fields, name).Trim('(', ')');
    private static string Raw(PdfAcroField.PdfAcroFieldCollection fields, string name) =>
        fields[name] is { } field ? field.Value?.ToString() ?? string.Empty : string.Empty;
    private static string Digest(PdfPage page) => Convert.ToHexString(
        SHA256.HashData(page.Contents.CreateSingleContent().Stream.UnfilteredValue));
    private static int NormalAppearanceLength(
        PdfAcroField.PdfAcroFieldCollection fields,
        string name) =>
        fields[name] is { } field
            ? field.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N")?.Stream.Value.Length ?? 0
            : 0;
}
