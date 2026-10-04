using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ClaimMdStatusProcessorTests
{
    [Fact]
    public async Task StatusReceiptOutcomesAndCursorCommitTogetherAndReplayAddsNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = fixture.Status("R");
        await using (var db = fixture.Factory.OpenDatabase())
            Assert.True(await new ClaimMdStatusProcessor(db, fixture.Protector)
                .ProcessAsync(fixture.AccountId, "0", page, CancellationToken.None));

        await using (var db = fixture.Factory.OpenDatabase())
        {
            var receipt = await db.ClearinghouseResponseReceipts.Include(row => row.Matches).SingleAsync();
            Assert.Equal(ClearinghouseReceiptSource.Connector, receipt.Source);
            Assert.Equal(ClaimResponseKind.ClaimMdApiStatus, receipt.Kind);
            Assert.Equal("status:12", receipt.ExternalArtifactId);
            Assert.Single(receipt.Matches);
            Assert.Equal("12", (await db.ClearinghouseFeedCheckpoints.SingleAsync()).Cursor);
            var outcome = await db.ClaimAcknowledgementOutcomes.SingleAsync();
            Assert.Equal(ClaimAcknowledgementDisposition.Rejected, outcome.Disposition);
            Assert.Equal(fixture.ClaimReference, outcome.ClaimReference);
        }
        await using (var db = fixture.Factory.OpenDatabase())
            Assert.False(await new ClaimMdStatusProcessor(db, fixture.Protector)
                .ProcessAsync(fixture.AccountId, "0", page, CancellationToken.None));
        await using (var db = fixture.Factory.OpenDatabase())
        {
            Assert.Single(await db.ClearinghouseResponseReceipts.ToListAsync());
            Assert.Single(await db.ClaimAcknowledgementOutcomes.ToListAsync());
        }
    }

    [Fact]
    public async Task AlteredRemoteIdentityCannotAdvanceCursorOrWriteAReceipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var bad = fixture.Status("A") with
        {
            Claims = [fixture.Status("A").Claims[0] with { RemoteClaimId = "WRONG" }]
        };
        await using (var db = fixture.Factory.OpenDatabase())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ClaimMdStatusProcessor(db, fixture.Protector)
                    .ProcessAsync(fixture.AccountId, "0", bad, CancellationToken.None));
        await using (var db = fixture.Factory.OpenDatabase())
        {
            Assert.Empty(await db.ClearinghouseResponseReceipts.ToListAsync());
            Assert.Empty(await db.ClearinghouseFeedCheckpoints.ToListAsync());
            Assert.Empty(await db.ClaimAcknowledgementOutcomes.ToListAsync());
        }
    }

    [Fact]
    public async Task EraUsesTheExisting835FinancialRulesAndCommitsItsOwnCursor()
    {
        await using var fixture = await Fixture.CreateAsync();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var intake = scope.ServiceProvider.GetRequiredService<ClaimResponseIngestion>();
            Assert.True(await intake.ImportConnectorEraAsync(fixture.AccountId, "20", "0",
                fixture.Era835(), CancellationToken.None));
        }
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var receipt = await db.ClearinghouseResponseReceipts.Include(row => row.Matches).SingleAsync();
            Assert.Equal(ClearinghouseReceiptSource.Connector, receipt.Source);
            Assert.Equal(ClearinghouseFeedKind.Era, receipt.FeedKind);
            Assert.Equal("era:20", receipt.ExternalArtifactId);
            Assert.Equal(2, receipt.Matches.Count);
            Assert.Equal(2, await db.RemittanceClaimOutcomes.CountAsync());
            Assert.Single(await db.RemittanceDeposits.ToListAsync());
            Assert.Equal("20", (await db.ClearinghouseFeedCheckpoints.SingleAsync()).Cursor);
        }
        using (var scope = fixture.Factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<ClaimResponseIngestion>()
                .ImportConnectorEraAsync(fixture.AccountId, "20", "0", fixture.Era835(), CancellationToken.None));
    }

    [Fact]
    public async Task UnmatchedEraLeavesPaymentAndCheckpointAbsent()
    {
        await using var fixture = await Fixture.CreateAsync();
        using (var scope = fixture.Factory.Services.CreateScope())
            await Assert.ThrowsAsync<ClaimResponseRejected>(() =>
                scope.ServiceProvider.GetRequiredService<ClaimResponseIngestion>()
                    .ImportConnectorEraAsync(fixture.AccountId, "20", "0",
                        fixture.Era835().Replace(fixture.ClaimReference, "999999999-1-1", StringComparison.Ordinal),
                        CancellationToken.None));
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Empty(await db.ClearinghouseResponseReceipts.ToListAsync());
        Assert.Empty(await db.ClearinghouseFeedCheckpoints.ToListAsync());
        Assert.Empty(await db.RemittanceDeposits.ToListAsync());
    }

    [Fact]
    public async Task ForeignAccountCannotClaimThisAgencysStatusFile()
    {
        await using var fixture = await Fixture.CreateAsync();
        var foreignId = Guid.NewGuid();
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var foreign = new ServerAgency { Name = "Foreign Synthetic Agency" };
            db.Agencies.Add(foreign);
            await db.SaveChangesAsync();
            db.ClearinghouseAccounts.Add(new ClearinghouseAccount
            {
                Id = foreignId, AgencyId = foreign.Id,
                ConnectorKind = TradingPartnerKind.ClaimMd, IsTest = true, IsEnabled = true,
                ExternalAccountNumber = "FOREIGNTEST", ClaimNamespace = "FOREIGN",
                TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Factory.OpenDatabase())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ClaimMdStatusProcessor(db, fixture.Protector)
                    .ProcessAsync(foreignId, "0", fixture.Status("A"), CancellationToken.None));
        await using (var db = fixture.Factory.OpenDatabase())
        {
            Assert.Empty(await db.ClearinghouseResponseReceipts.ToListAsync());
            Assert.Empty(await db.ClearinghouseFeedCheckpoints.ToListAsync());
        }
    }

    [Fact]
    public async Task StatusAndEraCursorsAreIndependentForTheSameAccount()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Factory.OpenDatabase())
            Assert.True(await new ClaimMdStatusProcessor(db, fixture.Protector)
                .ProcessAsync(fixture.AccountId, "0", fixture.Status("A"), CancellationToken.None));
        using (var scope = fixture.Factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ClaimResponseIngestion>()
                .ImportConnectorEraAsync(fixture.AccountId, "20", "0", fixture.Era835(), CancellationToken.None));
        await using var finalDb = fixture.Factory.OpenDatabase();
        var cursors = await finalDb.ClearinghouseFeedCheckpoints.ToDictionaryAsync(row => row.FeedKind);
        Assert.Equal("12", cursors[ClearinghouseFeedKind.Status].Cursor);
        Assert.Equal("20", cursors[ClearinghouseFeedKind.Era].Cursor);
        Assert.NotEqual(cursors[ClearinghouseFeedKind.Status].LastReceiptId,
            cursors[ClearinghouseFeedKind.Era].LastReceiptId);
    }

    [Fact]
    public async Task EraPollingReadsEveryListingPageBeforeProcessingAndAdvancing()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var account = await db.ClearinghouseAccounts.SingleAsync();
            account.SecretReference = "CLAIMMD_SANDBOX_KEY_TEST";
            account.Revision++;
            db.ClearinghouseFeedCheckpoints.AddRange(
                new ClearinghouseFeedCheckpoint { Id = Guid.NewGuid(), AccountId = fixture.AccountId,
                    AgencyId = account.AgencyId, FeedKind = ClearinghouseFeedKind.Status,
                    Cursor = "0", UpdatedAtUtc = DateTime.UtcNow },
                new ClearinghouseFeedCheckpoint { Id = Guid.NewGuid(), AccountId = fixture.AccountId,
                    AgencyId = account.AgencyId, FeedKind = ClearinghouseFeedKind.Era,
                    Cursor = "0", UpdatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var listedPages = new List<int>();
        string? firstEraRequested = null;
        using var http = new HttpClient(new PageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            if (request.RequestUri!.AbsolutePath == "/services/response/")
                return Xml("<result last_responseid=\"0\" />");
            if (request.RequestUri.AbsolutePath == "/services/eralist/")
            {
                var page = body.Contains("Page=2", StringComparison.Ordinal) ? 2 : 1;
                listedPages.Add(page);
                var ids = page == 1 ? Enumerable.Range(101, 100) : [100];
                return Xml("<result last_eraid=\"200\">" +
                    string.Concat(ids.Select(id => $"<era eraid=\"{id}\" />")) + "</result>");
            }
            if (request.RequestUri.AbsolutePath == "/services/era835/")
            {
                firstEraRequested = body.Contains("eraid=100", StringComparison.Ordinal) ? "100" : "other";
                return Xml("<result error=\"stop before importing\" />");
            }
            throw new InvalidOperationException("Unexpected Claim.MD test request.");
        }));
        var connector = new ClaimMdSandboxConnector(http, new PageKeySource(), new TestClaimMdCoordination());
        var gate = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo",
            EnableClaimMdSandboxTransport = true
        }), new DemoHostEnvironment());
        var poller = new ClaimMdSandboxPoller(
            fixture.Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
            fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), gate,
            fixture.Protector, new TestClaimMdCoordination(), new TestDemoWorkerResetCoordination(),
            NullLogger<ClaimMdSandboxPoller>.Instance);

        Assert.Equal(0, await poller.PollOnceAsync(CancellationToken.None));
        Assert.Equal([1, 2], listedPages);
        Assert.Equal("100", firstEraRequested);
        await using var finalDb = fixture.Factory.OpenDatabase();
        Assert.All(await finalDb.ClearinghouseFeedCheckpoints.ToListAsync(), row => Assert.Equal("0", row.Cursor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-cursor")]
    public async Task UninitializedFeedCursorsNeverTriggerVendorRequests(string? invalidCursor)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var account = await db.ClearinghouseAccounts.SingleAsync();
            account.SecretReference = "CLAIMMD_SANDBOX_KEY_TEST";
            account.Revision++;
            if (invalidCursor is not null)
                db.ClearinghouseFeedCheckpoints.AddRange(
                    new ClearinghouseFeedCheckpoint { Id = Guid.NewGuid(), AccountId = fixture.AccountId,
                        AgencyId = account.AgencyId, FeedKind = ClearinghouseFeedKind.Status,
                        Cursor = invalidCursor, UpdatedAtUtc = DateTime.UtcNow },
                    new ClearinghouseFeedCheckpoint { Id = Guid.NewGuid(), AccountId = fixture.AccountId,
                        AgencyId = account.AgencyId, FeedKind = ClearinghouseFeedKind.Era,
                        Cursor = invalidCursor, UpdatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var requests = 0;
        using var http = new HttpClient(new PageHandler(_ =>
        {
            requests++;
            return Task.FromResult(Xml("<result />"));
        }));
        var connector = new ClaimMdSandboxConnector(http, new PageKeySource(), new TestClaimMdCoordination());
        var gate = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo",
            EnableClaimMdSandboxTransport = true
        }), new DemoHostEnvironment());
        var poller = new ClaimMdSandboxPoller(
            fixture.Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
            fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), gate,
            fixture.Protector, new TestClaimMdCoordination(), new TestDemoWorkerResetCoordination(),
            NullLogger<ClaimMdSandboxPoller>.Instance);

        Assert.Equal(0, await poller.PollOnceAsync(CancellationToken.None));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ResetLeaseBlocksFeedReadsBeforeAnyVendorRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var account = await db.ClearinghouseAccounts.SingleAsync();
            account.SecretReference = "CLAIMMD_SANDBOX_KEY_TEST";
            account.Revision++;
            foreach (var feed in new[] { ClearinghouseFeedKind.Status, ClearinghouseFeedKind.Era })
                db.ClearinghouseFeedCheckpoints.Add(new ClearinghouseFeedCheckpoint
                {
                    Id = Guid.NewGuid(), AccountId = account.Id, AgencyId = account.AgencyId,
                    FeedKind = feed, Cursor = "0", UpdatedAtUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }
        var requests = 0;
        using var http = new HttpClient(new PageHandler(_ =>
        {
            requests++;
            return Task.FromResult(Xml("<result />"));
        }));
        var connector = new ClaimMdSandboxConnector(http, new PageKeySource(), new TestClaimMdCoordination());
        var gate = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo",
            EnableClaimMdSandboxTransport = true
        }), new DemoHostEnvironment());
        var reset = new BlockedResetLeaseCoordination();
        var poller = new ClaimMdSandboxPoller(
            fixture.Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
            fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), gate,
            fixture.Protector, new TestClaimMdCoordination(), reset, NullLogger<ClaimMdSandboxPoller>.Instance);

        Assert.Equal(0, await poller.PollOnceAsync(CancellationToken.None));
        Assert.Equal(1, reset.Calls);
        Assert.Equal(0, requests);
    }

    private static HttpResponseMessage Xml(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/xml")
    };

    private sealed class PageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            respond(request);
    }

    private sealed class PageKeySource : IClaimMdSandboxKeySource
    {
        public string Resolve(string? reference) => "SYNTHETIC_KEY";
    }

    private sealed class DemoHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Demo";
        public string ApplicationName { get; set; } = "Sati.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SyntheticPipelineDatabase _database;
        public SyntheticPipelineFactory Factory { get; }
        public EnvelopeProtector Protector { get; }
        public Guid AccountId { get; }
        public string ClaimReference { get; }
        public string RemoteClaimId { get; }
        public string GenerationContent { get; }

        private Fixture(SyntheticPipelineDatabase database, SyntheticPipelineFactory factory,
            Guid accountId, string claimReference, string remoteClaimId, string generationContent)
        {
            _database = database; Factory = factory; AccountId = accountId;
            ClaimReference = claimReference; RemoteClaimId = remoteClaimId;
            GenerationContent = generationContent;
            Protector = new EnvelopeProtector(factory.Vault);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var database = new SyntheticPipelineDatabase();
            await database.InitializeAsync();
            var factory = new SyntheticPipelineFactory(database)
            {
                EnableSyntheticDispatch = true, DisableDispatchWorker = true
            };
            var actors = await factory.SeedAsync();
            var periodId = await JoinedBillingPipelineAcceptanceTests.PrepareSubmittedPeriodAsync(factory, actors);
            var accountId = Guid.NewGuid();
            await using (var db = factory.OpenDatabase())
            {
                db.ClearinghouseAccounts.Add(new ClearinghouseAccount
                {
                    Id = accountId, AgencyId = actors.AgencyId,
                    ConnectorKind = TradingPartnerKind.ClaimMd,
                    IsTest = true, IsEnabled = true, ExternalAccountNumber = "CLAIMMDTEST",
                    ClaimNamespace = "TEST", TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            using var biller = await factory.SignInAsync("synthetic-biller");
            var generated = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/edi",
                new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = accountId });
            generated.EnsureSuccessStatusCode();
            var file = (await generated.Content.ReadFromJsonAsync<EdiFileDto>())!;
            var claim = ClaimResponseReader.ReadSubmission(file.Content).Claims[0];
            long generationId;
            await using (var db = factory.OpenDatabase())
                generationId = await db.EdiGenerations.Select(row => row.Id).SingleAsync();
            (await biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
                new QueueClearinghouseDispatchRequest(generationId, accountId))).EnsureSuccessStatusCode();
            await using (var db = factory.OpenDatabase())
            {
                var dispatch = await db.ClearinghouseDispatches.SingleAsync();
                dispatch.State = ClearinghouseDispatchState.Sending;
                dispatch.Revision++;
                await db.SaveChangesAsync();
                dispatch.State = ClearinghouseDispatchState.AcceptedByClearinghouse;
                dispatch.ExternalFileId = "123456";
                dispatch.Revision++;
                await db.SaveChangesAsync();
            }
            return new Fixture(database, factory, accountId, claim.ClaimReference,
                claim.RemoteClaimId!, file.Content);
        }

        public ClaimMdStatusPage Status(string status) => new("12",
            [new ClaimMdStatusClaim("12", "123456", ClaimReference, RemoteClaimId, status)],
            $"<result last_responseid=\"12\"><claim pcn=\"{ClaimReference}\" remote_claimid=\"{RemoteClaimId}\" fileid=\"123456\" status=\"{status}\"><messages responseid=\"12\" status=\"{status}\" /></claim></result>");

        public string Era835()
        {
            var submission = ClaimResponseReader.ReadSubmission(GenerationContent);
            var claims = submission.Claims;
            var total = claims.Sum(claim => claim.BilledAmount).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            var body = $"BPR*I*{total}*C*CHK************20260901~TRN*1*SYNTHETIC-PAYMENT-1*1234567890~" +
                $"N1*PR*TEST PAYER*XV*MCDME~N1*PE*SYNTHETIC AGENCY*XX*{submission.BillingProviderNpi}~";
            var index = 0;
            foreach (var claim in claims)
            {
                index++;
                var amount = claim.BilledAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                body += $"LX*{index}~CLP*{claim.ClaimReference}*1*{amount}*{amount}*0*MC*PAYER-{index}*11*1~" +
                    $"NM1*QC*1*PERSON*TEST****MI*123456789~SVC*HC:G9012*{amount}*{amount}~" +
                    $"REF*6R*{claim.ServiceLineReferences[0]}~";
            }
            const string control = "555555555";
            const string transaction = "0001";
            var count = body.Count(character => character == '~') + 2;
            return $"ISA*00*          *00*          *ZZ*CLAIMMD        *ZZ*CLAIMMDTEST    *260901*1200*^*00501*{control}*0*T*:~" +
                $"GS*HP*CLAIMMD*CLAIMMDTEST*20260901*1200*{control}*X*005010X221A1~" +
                $"ST*835*{transaction}*005010X221A1~{body}SE*{count}*{transaction}~GE*1*{control}~IEA*1*{control}~";
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
