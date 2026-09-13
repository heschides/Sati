using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;
using Sati.Forms;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class CwicPacketTests(SatiApiFactory factory)
{
    private const int OwnPerson = 101;
    private const int OtherAgencyPerson = 201;
    private static string Route(int id) => $"/api/v1/people/{id}/cwic-referral.pdf";

    [Fact]
    public void Embedded_packet_is_the_exact_controlled_source_supplied_for_the_feature()
    {
        using var stream = typeof(CwicPacketPdfGenerator).Assembly
            .GetManifestResourceStream("Sati.Forms.BCS-Referral-Packet-2020.pdf");
        Assert.NotNull(stream);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        Assert.Equal("07450B91D9756AD4EC26D0B7F570FCE9DEBE152722A2D981ACE4CB195D3A1013", hash);
    }

    [Fact]
    public void Generator_keeps_all_ten_source_pages_and_labels_the_revision()
    {
        var generator = new CwicPacketPdfGenerator();
        var bytes = generator.Generate(
            new CwicPacketSubject(7, "Demo Consumer", new DateTime(1985, 4, 3), "123-45-6789"),
            ValidRequest(),
            new DateTime(2026, 9, 13, 14, 0, 0, DateTimeKind.Utc));

        using var stream = new MemoryStream(bytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(10, document.PageCount);
        Assert.Equal("CWIC referral packet - Demo Consumer", document.Info.Title);
        Assert.Equal(CwicPacketRules.SourceRevision, document.Info.Subject);
        Assert.True(bytes.Length > 100_000);
    }

    [Fact]
    public void Release_dates_cannot_be_reversed_or_exceed_one_year()
    {
        var reversed = CwicPacketRules.Validate(ValidRequest() with
        {
            DolReleaseStart = new DateOnly(2026, 9, 13),
            DolReleaseEnd = new DateOnly(2026, 9, 12)
        });
        var tooLong = CwicPacketRules.Validate(ValidRequest() with
        {
            DolReleaseStart = new DateOnly(2026, 9, 13),
            DolReleaseEnd = new DateOnly(2027, 9, 14)
        });

        Assert.Contains(nameof(CwicPacketRequest.DolReleaseEnd), reversed.Keys);
        Assert.Contains(nameof(CwicPacketRequest.DolReleaseEnd), tooLong.Keys);
    }

    [Fact]
    public async Task Anonymous_and_other_agency_callers_cannot_generate_the_packet()
    {
        using var anonymous = factory.CreateAnonymousClient();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(Route(OwnPerson), ValidRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.PostAsJsonAsync(Route(OtherAgencyPerson), ValidRequest())).StatusCode);
    }

    [Fact]
    public async Task Own_packet_is_non_cacheable_audited_and_recorded_as_a_versioned_draft()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await factory.DeleteDocumentArtifactsAsync(OwnPerson, AnnualDocumentKind.CwicReferralPacket);
        var before = await factory.GetAuditEventsAsync("cwic-packet.generated");
        try
        {
            var response = await client.PostAsJsonAsync(Route(OwnPerson), ValidRequest());

            response.EnsureSuccessStatusCode();
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
            Assert.True(response.Headers.TryGetValues("X-Sati-Unfilled-Fields", out var unfilled));
            Assert.Contains("Required signatures and signature dates", string.Join("|", unfilled!));
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

            var after = await factory.GetAuditEventsAsync("cwic-packet.generated");
            Assert.Equal(before.Count + 1, after.Count);
            Assert.Equal(OwnPerson.ToString(), after[^1].ResourceId);

            var person = (await client.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload"))!
                .Single(value => value.Id == OwnPerson);
            var cycle = AnnualDocumentCycle.CurrentStart(person.EffectiveDate!.Value, DateTime.Today);
            var artifacts = await client.GetFromJsonAsync<List<DocumentArtifactDto>>(
                $"/api/v1/people/{OwnPerson}/documents?cycleStart={cycle:yyyy-MM-dd}");
            var artifact = Assert.Single(artifacts!, value => value.Kind == AnnualDocumentKind.CwicReferralPacket.ToString());
            Assert.Equal(DocumentArtifactOrigin.Draft.ToString(), artifact.Origin);
            Assert.Equal("MaineHealth", artifact.TemplateOwner);
            Assert.Equal("BCS-Referral-Packet", artifact.TemplateKey);
            Assert.Equal(202012, artifact.TemplateVersion);
        }
        finally
        {
            await factory.DeleteDocumentArtifactsAsync(OwnPerson, AnnualDocumentKind.CwicReferralPacket);
        }
    }

    private static CwicPacketRequest ValidRequest() => new(
        MailingAddress: "1 Demo Street",
        City: "Portland",
        State: "ME",
        Zip: "04101",
        County: "Cumberland",
        HomePhone: "207-555-0100",
        Email: "demo@example.test",
        MaritalStatus: "Single",
        Gender: "Nonbinary",
        SpouseReceivesDisabilityBenefits: false,
        ReferringOrganization: "Demo Agency",
        ScheduleWithConsumer: true,
        MeetingMethods: ["Virtual (Zoom)"],
        HasRepresentativePayee: false,
        HasLegalGuardian: false,
        EmploymentSituations: ["Working"],
        WorkingHoursPerWeek: "20",
        WorkingHourlyWage: "18.50",
        WorkBeganOn: new DateOnly(2026, 8, 1),
        JobSatisfaction: "Satisfied",
        Benefits: ["SSI", "MaineCare"],
        ChildrenUnder21ReceiveMaineCare: false,
        BenefitsQuestion: "How will an increase in hours affect benefits?",
        InPersonLocation: "Portland",
        HasVrCounselor: true,
        VrCounselorName: "Demo Counselor",
        VrStatus: "Service",
        EstimatedReturnToWork: "Next two months",
        HasIpe: true,
        IpeGoal: "Maintain community employment.",
        EstimatedHoursPerWeek: "20",
        VrDivision: "Vocational Rehabilitation",
        DolReleaseStart: new DateOnly(2026, 9, 13),
        DolReleaseEnd: new DateOnly(2027, 9, 13));
}
