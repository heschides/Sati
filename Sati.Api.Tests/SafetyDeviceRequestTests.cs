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
public sealed class SafetyDeviceRequestTests(SatiApiFactory factory)
{
    private static string Route(int id) => $"/api/v1/people/{id}/safety-device-request.pdf";

    [Fact]
    public void Generator_keeps_the_exact_six_page_OADS_source_and_leaves_signatures_blank()
    {
        using var resource = typeof(SafetyDevicePdfGenerator).Assembly
            .GetManifestResourceStream(SafetyDevicePdfGenerator.ResourceName);
        Assert.NotNull(resource);
        Assert.Equal("F33AA99B6166FCE926627C98BA04058D457FA38489A709F94033F219AB01927F",
            Convert.ToHexString(SHA256.HashData(resource)));
        resource.Position = 0;
        var result = new SafetyDevicePdfGenerator().Generate(
            new SafetyDeviceSubject(7, "Avery Consumer", new DateTime(1990, 4, 2),
                "EG-77", "MC-88", "1 Main St", "Gale Guardian", "Case Manager", "cm@example.test"),
            new SafetyDeviceRequest(
                MedicalProviderName: "Dr. Example",
                Devices: [new SafetyDeviceEntry("Bed alarm", "Prevent falls", "Overnight", "2"),
                    new(), new(), new(), new(),
                    new SafetyDeviceEntry("Helmet", "Prevent injury", "During seizures", "2")],
                LessRestrictiveStrategies: "Verbal prompts were tried.",
                PlanningTeamMeetingDate: new DateOnly(2026, 9, 20)),
            new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc));
        var qaDirectory = Environment.GetEnvironmentVariable("SATI_DOCUMENT_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qaDirectory))
        {
            Directory.CreateDirectory(qaDirectory);
            File.WriteAllBytes(Path.Combine(qaDirectory, "safety-device-sample.pdf"), result);
        }

        using var source = PdfReader.Open(resource, PdfDocumentOpenMode.Import);
        using var filled = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(6, filled.PageCount);
        for (var page = 0; page < 6; page++)
            Assert.Equal(Digest(source.Pages[page]), Digest(filled.Pages[page]));
        var fields = filled.AcroForm!.Fields;
        Assert.Equal("Avery Consumer", Text(fields, "Member Legal Name"));
        Assert.Equal("MC-88", Text(fields, "MaineCare"));
        Assert.Equal("Bed alarm", Text(fields, "Safety Devices.0.0"));
        Assert.Equal("2", Text(fields, "Safety Devices.0.3.0"));
        Assert.Equal("Helmet", Text(fields, "Medical Providers Recommendations.0.0"));
        Assert.Equal("09/20/2026", Text(fields, "recommended by the Medical Provider"));
        Assert.Equal("", Text(fields, "Safety Devices.0.3.1"));
        Assert.Equal("", Text(fields, "Medical Providers Recommendations.0.3.1"));
        Assert.Equal("", Text(fields, "Text91"));
        Assert.Equal("", Text(fields, "Signature93"));
    }

    [Fact]
    public void Validation_rejects_unknown_level_and_more_than_ten_devices()
    {
        var errors = SafetyDeviceRules.Validate(new SafetyDeviceRequest(Devices:
            Enumerable.Repeat(new SafetyDeviceEntry(Level: "3"), 11).ToArray()));
        Assert.Contains(nameof(SafetyDeviceRequest.Devices), errors.Keys);
        Assert.Contains("Devices[0].Level", errors.Keys);
        var overflow = SafetyDeviceRules.Validate(new SafetyDeviceRequest(
            LessRestrictiveStrategies: new string('x', 200),
            Devices: [new SafetyDeviceEntry(Purpose: "An answer that cannot fit in this fixed cell")]));
        Assert.Contains(nameof(SafetyDeviceRequest.LessRestrictiveStrategies), overflow.Keys);
        Assert.Contains("Devices[0].Purpose", overflow.Keys);
    }

    [Fact]
    public async Task Route_requires_a_current_caseload_member_and_returns_no_store_pdf()
    {
        using var anonymous = factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(Route(101), new SafetyDeviceRequest())).StatusCode);
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.PostAsJsonAsync(Route(201), new SafetyDeviceRequest())).StatusCode);

        await factory.DeleteDocumentArtifactsAsync(101, AnnualDocumentKind.SafetyDeviceRequest);
        var auditBefore = await factory.GetAuditEventsAsync("safety-device-request.generated");
        try
        {
            var response = await owner.PostAsJsonAsync(Route(101),
                new SafetyDeviceRequest(Devices: [new SafetyDeviceEntry("Alarm", "Safety", "Night", "2")]));
            response.EnsureSuccessStatusCode();
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(
                (await response.Content.ReadAsByteArrayAsync())[..4]));
            var auditAfter = await factory.GetAuditEventsAsync("safety-device-request.generated");
            Assert.Equal(auditBefore.Count + 1, auditAfter.Count);
            var person = (await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload"))!
                .Single(value => value.Id == 101);
            var cycle = AnnualDocumentCycle.CurrentStart(person.EffectiveDate!.Value, DateTime.Today);
            var artifacts = await owner.GetFromJsonAsync<List<DocumentArtifactDto>>(
                $"/api/v1/people/101/documents?cycleStart={cycle:yyyy-MM-dd}");
            var artifact = Assert.Single(artifacts!, value =>
                value.Kind == AnnualDocumentKind.SafetyDeviceRequest.ToString());
            Assert.Equal(DocumentArtifactOrigin.Draft.ToString(), artifact.Origin);
            Assert.Equal("Maine DHHS OADS", artifact.TemplateOwner);
            Assert.Equal("Safety-Device-Request-Form", artifact.TemplateKey);
            Assert.Equal(202604, artifact.TemplateVersion);
        }
        finally
        {
            await factory.DeleteDocumentArtifactsAsync(101, AnnualDocumentKind.SafetyDeviceRequest);
        }
    }

    private static string Text(PdfAcroField.PdfAcroFieldCollection fields, string name) =>
        fields[name]?.Value?.ToString()?.Trim('(', ')') ?? "";
    private static string Digest(PdfPage page) => Convert.ToHexString(
        SHA256.HashData(page.Contents.CreateSingleContent().Stream.UnfilteredValue));
}
