using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ClaimMdSandboxConnectorTests
{
    [Fact]
    public async Task UploadPostsOnlyToFixedHostAndRequiresExactD9AndClm01()
    {
        var upload = TestUpload();
        var claim = Assert.Single(ClaimResponseReader.ReadSubmission(upload.Content).Claims);
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("https://svc.claim.md/services/upload/", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("AccountKey", body);
            Assert.Contains("SYNTHETIC_KEY", body);
            Assert.Contains(upload.FileName, body);
            Assert.Contains("REF*D9*" + claim.RemoteClaimId, body);
            return Xml($"<result><claim pcn=\"{claim.ClaimReference}\" remote_claimid=\"{claim.RemoteClaimId}\" fileid=\"123456\" status=\"A\"><messages status=\"A\" mesgid=\"ACK\"/></claim></result>");
        });
        var connector = Client(handler);

        var result = await connector.UploadAsync(upload, CancellationToken.None);
        Assert.Equal("123456", result.ExternalFileId);
        Assert.Equal(ClearinghouseAttemptOutcome.Accepted, result.Outcome);
        Assert.NotNull(result.RawResponse);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MismatchedRemoteClaimIdentityIsUnknownAndRetainsVendorEvidence()
    {
        var upload = TestUpload();
        var claim = Assert.Single(ClaimResponseReader.ReadSubmission(upload.Content).Claims);
        var handler = new StubHandler(_ => Task.FromResult(Xml(
            $"<result><claim pcn=\"{claim.ClaimReference}\" remote_claimid=\"WRONG\" fileid=\"123456\" status=\"A\"/></result>")));

        var result = await Client(handler).UploadAsync(upload, CancellationToken.None);

        Assert.Equal(ClearinghouseAttemptOutcome.OutcomeUnknown, result.Outcome);
        Assert.Null(result.ExternalFileId);
        Assert.Contains("WRONG", result.RawResponse);
    }

    [Fact]
    public async Task RejectedClaimStillMeansTheFileWasReceivedNotThatItsPayerApproved()
    {
        var upload = TestUpload();
        var claim = Assert.Single(ClaimResponseReader.ReadSubmission(upload.Content).Claims);
        var handler = new StubHandler(_ => Task.FromResult(Xml(
            $"<result><claim pcn=\"{claim.ClaimReference}\" remote_claimid=\"{claim.RemoteClaimId}\" fileid=\"123456\" status=\"R\"><messages status=\"R\" /></claim></result>")));
        var result = await Client(handler).UploadAsync(upload, CancellationToken.None);
        Assert.Equal(ClearinghouseAttemptOutcome.Accepted, result.Outcome);
        Assert.Equal(0, result.AcceptedClaims);
        Assert.Equal(1, result.RejectedClaims);
    }

    [Fact]
    public async Task LaterStatusForSameClaimWinsWithinOnePage()
    {
        var handler = new StubHandler(_ => Task.FromResult(Xml(
            "<result last_responseid=\"13\">" +
            "<claim fileid=\"9\" pcn=\"123456789-77-99\" remote_claimid=\"SATI1-TEST-1-77-99\" status=\"A\"><messages responseid=\"11\" status=\"A\" /></claim>" +
            "<claim fileid=\"9\" pcn=\"123456789-77-99\" remote_claimid=\"SATI1-TEST-1-77-99\" status=\"R\"><messages responseid=\"12\" status=\"R\" /></claim>" +
            "</result>")));
        var page = await Client(handler).GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", CancellationToken.None);
        Assert.Equal("R", Assert.Single(page.Claims).Status);
        Assert.Contains("responseid=\"11\"", page.RawXml);
    }

    [Fact]
    public async Task RedirectAndProductionInterchangeNeverSucceed()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://untrusted.invalid/collect") }
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(handler).UploadAsync(TestUpload(), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        var production = TestUpload() with { Content = TestUpload().Content.Replace("*T*:", "*P*:", StringComparison.Ordinal) };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client(handler).UploadAsync(production, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task StatusAndEraReadsUseSeparateOpaqueCursorsAndNeverWriteThem()
    {
        var handler = new StubHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("AccountKey=SYNTHETIC_KEY", body);
            return request.RequestUri!.AbsolutePath switch
            {
                "/services/response/" => Xml("<result last_responseid=\"12\"><claim fileid=\"9\" pcn=\"123456789-77-99\" remote_claimid=\"SATI1-TEST-1-77-99\" status=\"R\"><messages responseid=\"12\" status=\"R\" /></claim></result>"),
                "/services/eralist/" => Xml("<result last_eraid=\"20\"><era eraid=\"19\" /></result>"),
                "/services/uploadlist/" => Xml("<result><file inboundid=\"9\" file_type=\"claim\" filename=\"claim.edi\" uploadtime=\"1720000000\" /></result>"),
                _ => throw new InvalidOperationException("Unexpected endpoint")
            };
        });
        var connector = Client(handler);

        var status = await connector.GetStatusesAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", CancellationToken.None);
        Assert.Equal("12", status.Cursor);
        Assert.Equal("R", Assert.Single(status.Claims).Status);
        var eras = await connector.GetErasAsync("CLAIMMD_SANDBOX_KEY_TEST", "0", 1, CancellationToken.None);
        Assert.Equal("20", eras.Cursor);
        Assert.Equal("19", Assert.Single(eras.Eras).EraId);
        var uploads = await connector.ListUploadsAsync("CLAIMMD_SANDBOX_KEY_TEST",
            new DateOnly(2026, 9, 26), 1, CancellationToken.None);
        Assert.Equal("9", Assert.Single(uploads).FileId);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public void SecretReferenceMustBeAnApiHostSandboxVariableName()
    {
        var source = new EnvironmentClaimMdSandboxKeySource();
        Assert.Throws<InvalidOperationException>(() => source.Resolve("https://example.invalid/key"));
        Assert.Throws<InvalidOperationException>(() => source.Resolve("CLAIMMD_SANDBOX_KEY_BAD-ALIAS"));
    }

    [Fact]
    public void RealTransportGateCannotOpenAgainstProductionIdentity()
    {
        var production = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Production", ExpectedDatabaseName = "SatiProduction",
            EnableClaimMdSandboxTransport = true
        }), new TestHostEnvironment());
        Assert.False(production.IsEnabled);
        Assert.False(production.IsRealSandboxEnabled);
    }

    private static ClaimMdSandboxConnector Client(StubHandler handler) =>
        new(new HttpClient(handler), new FakeKeySource());

    private static HttpResponseMessage Xml(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/xml")
    };

    private static ClearinghouseUpload TestUpload()
    {
        var snapshot = new ProfessionalClaimSnapshot(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            1, 101, "Alex", "Example", new DateTime(1990, 2, 3), "U", "987654321",
            "10 Claim Street", "Portland", "ME", "04101",
            "Example Agency", "1999999984", "111111111", "1 Provider Way",
            "Portland", "ME", "04101", "SATITEST1", "Billing Desk", "2075550101",
            "SYNTHETIC PAYER", "MCDME");
        var line = new ProfessionalClaimLineFacts(88, new DateTime(2026, 8, 12), "G9012", "HI",
            1.33m, 33.25m, "987654321", "1999999984", "F89", 11,
            ProfessionalClaimSnapshotCodec.Serialize(snapshot));
        var content = Professional837Formatter.Generate(77, 2026, 8,
            [new Professional837Claim(99, line, "1", null)],
            TradingPartnerProfile.ClaimMd("ACCT123", "TEST"), true,
            new DateTime(2026, 8, 29, 9, 30, 0), "123456789");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(content)));
        return new ClearinghouseUpload(Guid.NewGuid(), "claim.edi", content, hash,
            TradingPartnerKind.ClaimMd, "CLAIMMD_SANDBOX_KEY_TEST");
    }

    private sealed class FakeKeySource : IClaimMdSandboxKeySource
    {
        public string Resolve(string? reference)
        {
            Assert.Equal("CLAIMMD_SANDBOX_KEY_TEST", reference);
            return "SYNTHETIC_KEY";
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Sati.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
