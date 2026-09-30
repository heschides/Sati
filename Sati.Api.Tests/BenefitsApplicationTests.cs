using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;
using Sati.Forms;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class BenefitsApplicationTests(SatiApiFactory factory)
{
    private static string Route(int id) => $"/api/v1/people/{id}/benefits-application.pdf";

    [Fact]
    public void Controlled_source_and_twenty_page_draft_are_preserved()
    {
        using var resource = typeof(BenefitsApplicationPdfGenerator).Assembly
            .GetManifestResourceStream("Sati.Forms.OFI-Application-for-Benefits-2024-04-30.pdf");
        Assert.NotNull(resource);
        Assert.Equal(BenefitsApplicationRules.SourceSha256,
            Convert.ToHexString(SHA256.HashData(resource)));
        var request = new BenefitsApplicationRequest(new Dictionary<string, string>
        {
            ["program.snap"] = "True",
            ["initial.medicalBills"] = "No",
            ["person1.homeAddress"] = "1 Demo Street, Portland ME 04101",
            ["person2.name"] = "Example Household Member",
            ["employment.1.employer"] = "Example Employer",
            ["expense.rent.amount"] = "800",
            ["insurance.company"] = "Example Health Plan"
        });
        var pdf = new BenefitsApplicationPdfGenerator().Generate(
            new BenefitsApplicationSubject("Demo Consumer", new DateTime(1985, 4, 3), null),
            request, new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));
        var qaDirectory = Environment.GetEnvironmentVariable("SATI_DOCUMENT_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qaDirectory))
        {
            Directory.CreateDirectory(qaDirectory);
            File.WriteAllBytes(Path.Combine(qaDirectory, "benefits-application-sample.pdf"), pdf);
        }
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal(20, document.PageCount);
        Assert.Equal(BenefitsApplicationRules.SourceRevision, document.Info.Subject);
        Assert.True(pdf.Length > 300_000);
    }

    [Fact]
    public void Unknown_fields_and_choice_forgery_are_rejected()
    {
        var invalid = new BenefitsApplicationRequest(new Dictionary<string, string>
        {
            ["signature"] = "Applicant",
            ["person1.ssn"] = "123-45-6789",
            ["program.snap"] = "Approved",
            ["initial.medicalBills"] = "Sometimes"
        });
        var errors = BenefitsApplicationRules.Validate(invalid);
        Assert.Contains("signature", errors.Keys);
        Assert.Contains("person1.ssn", errors.Keys);
        Assert.Contains("program.snap", errors.Keys);
        Assert.Contains("initial.medicalBills", errors.Keys);
    }

    [Fact]
    public void Every_wizard_field_can_be_written_to_the_source_revision()
    {
        var answers = BenefitsApplicationRules.Fields.ToDictionary(
            item => item.Key,
            item => item.Kind switch
            {
                BenefitsAnswerKind.Text => "X",
                BenefitsAnswerKind.YesNo => "Yes",
                _ => "True"
            }, StringComparer.Ordinal);
        answers["person1.noHomeAddress"] = "False";
        for (var index = 1; index <= 6; index++)
        {
            foreach (var suffix in new[] { "female", "nonbinary", "married", "notHispanic" })
                answers[$"person{index}.{suffix}"] = "False";
        }
        var pdf = new BenefitsApplicationPdfGenerator().Generate(
            new BenefitsApplicationSubject("Demo Consumer", new DateTime(1985, 4, 3), null),
            new BenefitsApplicationRequest(answers),
            new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));
        var qaDirectory = Environment.GetEnvironmentVariable("SATI_DOCUMENT_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qaDirectory))
            File.WriteAllBytes(Path.Combine(qaDirectory, "benefits-application-all-fields.pdf"), pdf);
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal(20, document.PageCount);
    }

    [Fact]
    public async Task Only_current_caseload_owner_can_generate_a_non_cacheable_audited_draft()
    {
        var request = new BenefitsApplicationRequest(new Dictionary<string, string>
        {
            ["program.maineCare"] = "True"
        });
        using var anonymous = factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(Route(101), request)).StatusCode);
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.PostAsJsonAsync(Route(201), request)).StatusCode);

        await factory.DeleteDocumentArtifactsAsync(101, AnnualDocumentKind.BenefitsApplication);
        var before = await factory.GetAuditEventsAsync("benefits-application.generated");
        try
        {
            var response = await owner.PostAsJsonAsync(Route(101), request);
            response.EnsureSuccessStatusCode();
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes[..4]));
            var after = await factory.GetAuditEventsAsync("benefits-application.generated");
            Assert.Equal(before.Count + 1, after.Count);
            Assert.Equal("101", after[^1].ResourceId);
        }
        finally
        {
            await factory.DeleteDocumentArtifactsAsync(101, AnnualDocumentKind.BenefitsApplication);
        }
    }
}
