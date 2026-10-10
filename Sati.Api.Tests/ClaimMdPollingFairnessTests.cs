using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClaimMdStatusProcessorTests
{
    [Fact]
    public async Task PollingEraOfferImportsOnlyOldestAndLeavesNextArtifactForLater()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var account = await db.ClearinghouseAccounts.SingleAsync();
            account.SecretReference = "CLAIMMD_SANDBOX_KEY_TEST"; account.Revision++;
            db.ClearinghouseFeedCheckpoints.AddRange(
                new ClearinghouseFeedCheckpoint { Id = Guid.NewGuid(), AccountId = account.Id,
                    AgencyId = account.AgencyId, FeedKind = ClearinghouseFeedKind.Status, Cursor = "0" },
                new ClearinghouseFeedCheckpoint { Id = Guid.NewGuid(), AccountId = account.Id,
                    AgencyId = account.AgencyId, FeedKind = ClearinghouseFeedKind.Era, Cursor = "0" });
            await db.SaveChangesAsync();
        }
        var downloads = new List<string>();
        using var http = new HttpClient(new PageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            if (request.RequestUri!.AbsolutePath == "/services/response/") return Xml("<result last_responseid=\"0\" />");
            if (request.RequestUri.AbsolutePath == "/services/eralist/")
                return Xml("<result last_eraid=\"21\"><era eraid=\"21\"/><era eraid=\"20\"/></result>");
            var id = body.Contains("eraid=20", StringComparison.Ordinal) ? "20" : "21";
            downloads.Add(id);
            // A valid second document deliberately conflicts with retained payment identity.
            // Its download alone proves the old worker drained past the first committed artifact.
            return Xml(new XElement("result", new XAttribute("eraid", id),
                new XElement("data", fixture.Era835())).ToString());
        }));
        var connector = new ClaimMdSandboxConnector(http, new PageKeySource(), new TestClaimMdCoordination(), TimeProvider.System);
        var gate = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo", EnableClaimMdSandboxTransport = true
        }), new DemoHostEnvironment());
        var poller = new ClaimMdSandboxPoller(
            fixture.Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
            fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), gate, fixture.Protector,
            new TestClaimMdCoordination(), new TestDemoWorkerResetCoordination(), NullLogger<ClaimMdSandboxPoller>.Instance, TestPollSelector(fixture.Factory));

        var processed = await poller.PollOnceAsync(CancellationToken.None);
        Assert.Equal(["20"], downloads);
        Assert.Equal(1, processed);
        await using var retained = fixture.Factory.OpenDatabase();
        Assert.Equal("20", (await retained.ClearinghouseFeedCheckpoints.SingleAsync(x => x.FeedKind == ClearinghouseFeedKind.Era)).Cursor);
        Assert.Equal("0", (await retained.ClearinghouseFeedCheckpoints.SingleAsync(x => x.FeedKind == ClearinghouseFeedKind.Status)).Cursor);
        Assert.Single(await retained.ClearinghouseResponseReceipts.ToListAsync());
        Assert.Single(await retained.RemittanceDeposits.ToListAsync());
    }
}
