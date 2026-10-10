using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClaimMdStatusProcessorTests
{
    [Theory]
    [InlineData("missing-key")]
    [InlineData("cancel")]
    [InlineData("disable")]
    [InlineData("stale-account")]
    [InlineData("slow-status")]
    public async Task PollingFailureAndStopBoundariesPreserveHealthyAgencyAndReceiptCursors(string scenario)
    {
        await using var database = new SyntheticPipelineDatabase(); await database.InitializeAsync();
        var change = new DisableOfferedAccount(database);
        await using var factory = new SyntheticPipelineFactory(database, null, change) { DisableDispatchWorker = true };
        await using (var db = factory.OpenDatabase())
        {
            for (var i = 0; i < 2; i++)
            {
                var agency = new ServerAgency { Name = "Synthetic boundary agency " + i };
                db.Agencies.Add(agency); await db.SaveChangesAsync();
                var account = new ClearinghouseAccount
                { Id = Guid.NewGuid(), AgencyId = agency.Id, IsEnabled = true, IsTest = true, ConnectorKind = TradingPartnerKind.ClaimMd,
                    ClaimNamespace = "BOUND" + i, ExternalAccountNumber = "BOUNDTEST" + i,
                    TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion, SecretReference = "CLAIMMD_SANDBOX_KEY_AGENCY" + (i == 0 ? "A" : "B") };
                db.ClearinghouseAccounts.Add(account);
                foreach (var feed in new[] { ClearinghouseFeedKind.Status, ClearinghouseFeedKind.Era })
                    db.ClearinghouseFeedCheckpoints.Add(new ClearinghouseFeedCheckpoint
                    { Id = Guid.NewGuid(), AgencyId = agency.Id, AccountId = account.Id, FeedKind = feed, Cursor = "0" });
                if (i == 1 && scenario == "stale-account") change.Target = account.Id;
            }
            await db.SaveChangesAsync();
        }
        var values = new MutablePollingOptions();
        var gate = new ClearinghouseDispatchGate(values, factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>());
        var calls = new List<string>(); using var stop = new CancellationTokenSource();
        var clock = new ManualTimeProvider();
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new TokenPageHandler(async (request, requestToken) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            var agency = body.Contains("AGENCYB", StringComparison.Ordinal) ? "B" : "A";
            var status = request.RequestUri!.AbsolutePath == "/services/response/";
            calls.Add(agency + (status ? ":Status" : ":Era"));
            if (scenario == "cancel") stop.Cancel();
            if (scenario == "disable") values.Value = new SatiApiOptions { ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests" };
            if (scenario == "slow-status" && agency == "A" && status)
            {
                stalled.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, requestToken);
            }
            return Xml(status ? "<result last_responseid=\"0\"/>" : "<result last_eraid=\"0\"/>");
        }));
        ClaimMdSandboxConnector.ConfigureHttpClient(http);
        var connector = new ClaimMdSandboxConnector(http, new SelectivePollingKeys(scenario == "missing-key"), new TestClaimMdCoordination(), clock);
        var poller = new ClaimMdSandboxPoller(factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
            factory.Services.GetRequiredService<IServiceScopeFactory>(), gate, factory.Services.GetRequiredService<EnvelopeProtector>(),
            new TestClaimMdCoordination(), new TestDemoWorkerResetCoordination(), NullLogger<ClaimMdSandboxPoller>.Instance,
            new ClearinghousePollSelector(factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), gate));
        var run = poller.PollOnceAsync(stop.Token);
        if (scenario == "slow-status")
        { await stalled.Task.WaitAsync(TimeSpan.FromSeconds(15)); clock.Advance(TimeSpan.FromSeconds(45)); }
        if (scenario == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        else Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(20)));
        if (scenario is "cancel" or "disable") Assert.Equal(["A:Status"], calls);
        else if (scenario == "missing-key") Assert.Equal(["B:Status", "B:Era"], calls);
        else if (scenario == "stale-account") { Assert.Equal(["A:Status", "A:Era"], calls); Assert.True(change.Fired); }
        else Assert.Equal(["A:Status", "B:Status", "A:Era", "B:Era"], calls);
        await using var after = factory.OpenDatabase();
        Assert.All(await after.ClearinghouseFeedCheckpoints.ToListAsync(), x => { Assert.Equal("0", x.Cursor); Assert.Equal(0, x.Revision); });
        Assert.Empty(await after.ClearinghouseResponseReceipts.ToListAsync());
        Assert.Empty(await after.ClearinghouseDispatchReadiness.ToListAsync());
        if (scenario is "cancel" or "disable") Assert.Equal(2, (await after.ClearinghousePollRotation.SingleAsync()).Revision);
    }

    private sealed class MutablePollingOptions : IOptions<SatiApiOptions>
    { public SatiApiOptions Value { get; set; } = new() { ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests", EnableClaimMdSandboxTransport = true }; }
    private sealed class TokenPageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request, token); }
    private sealed class SelectivePollingKeys(bool failA) : IClaimMdSandboxKeySource
    {
        public string Resolve(string? reference) => failA && reference!.EndsWith("AGENCYA", StringComparison.Ordinal)
            ? throw new ClaimMdAccountKeyUnavailableException() : reference!;
    }
    private sealed class DisableOfferedAccount(SyntheticPipelineDatabase database) : DbTransactionInterceptor
    {
        public Guid? Target { get; set; } public bool Fired { get; private set; }
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Fired || Target is null || eventData.Context?.ChangeTracker.Entries<ClearinghouseAccountPollRotation>().Any(x => x.Entity.AccountId == Target) != true) return;
            Fired = true;
            await using var db = new ApiDbContext(database.Options());
            var account = await db.ClearinghouseAccounts.SingleAsync(x => x.Id == Target, cancellationToken);
            account.IsEnabled = false; account.Revision++; await db.SaveChangesAsync(cancellationToken);
        }
    }
    [Fact]
    public async Task PollingPassCapsDistinctOffersAndContinuesFromDurablePositionWithNoHttpForDamagedCursors()
    {
        await using var database = new SyntheticPipelineDatabase(); await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database) { DisableDispatchWorker = true };
        await using (var db = factory.OpenDatabase())
        {
            for (var i = 0; i < 53; i++)
            {
                var agency = new ServerAgency { Name = "Synthetic cap agency " + i };
                db.Agencies.Add(agency); await db.SaveChangesAsync();
                var account = new ClearinghouseAccount
                { Id = Guid.NewGuid(), AgencyId = agency.Id, IsEnabled = true, IsTest = true, ConnectorKind = TradingPartnerKind.ClaimMd,
                    ClaimNamespace = "CAP" + i, ExternalAccountNumber = "CAPTEST" + i,
                    TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion, SecretReference = "CLAIMMD_SANDBOX_KEY_CAP" + i };
                db.ClearinghouseAccounts.Add(account);
                foreach (var feed in new[] { ClearinghouseFeedKind.Status, ClearinghouseFeedKind.Era })
                    db.ClearinghouseFeedCheckpoints.Add(new ClearinghouseFeedCheckpoint
                    { Id = Guid.NewGuid(), AgencyId = agency.Id, AccountId = account.Id, FeedKind = feed, Cursor = "x" });
            }
            await db.SaveChangesAsync();
        }
        var requests = 0;
        using var http = new HttpClient(new PageHandler(_ => { requests++; throw new InvalidOperationException("Damaged cursor must not issue HTTP."); }));
        var connector = new ClaimMdSandboxConnector(http, new ReferenceKeySource(), new TestClaimMdCoordination(), TimeProvider.System);
        var gate = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        { ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests", EnableClaimMdSandboxTransport = true }),
            factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>());
        var poller = new ClaimMdSandboxPoller(factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
            factory.Services.GetRequiredService<IServiceScopeFactory>(), gate, factory.Services.GetRequiredService<EnvelopeProtector>(),
            new TestClaimMdCoordination(), new TestDemoWorkerResetCoordination(), NullLogger<ClaimMdSandboxPoller>.Instance, TestPollSelector(factory));
        Assert.Equal(0, await poller.PollOnceAsync(CancellationToken.None));
        await using (var db = factory.OpenDatabase())
        {
            Assert.Equal(101, (await db.ClearinghousePollRotation.SingleAsync()).Revision);
            Assert.Equal(100, await db.ClearinghouseAccountPollRotation.SumAsync(x => x.Revision));
            Assert.Equal(53, await db.ClearinghouseAccountPollRotation.CountAsync());
        }
        Assert.Equal(0, await poller.PollOnceAsync(CancellationToken.None));
        Assert.Equal(0, requests);
        await using var after = factory.OpenDatabase();
        Assert.Equal(201, (await after.ClearinghousePollRotation.SingleAsync()).Revision);
        Assert.All(await after.ClearinghouseFeedCheckpoints.ToListAsync(), x => { Assert.Equal("x", x.Cursor); Assert.Equal(0, x.Revision); });
        Assert.Empty(await after.ClearinghouseResponseReceipts.ToListAsync());
    }

    [Fact]
    public async Task PollingAgencyOpportunityPrecedesSecondFeedOfEarlierAgency()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var a = await db.ClearinghouseAccounts.SingleAsync();
            a.SecretReference = "CLAIMMD_SANDBOX_KEY_AGENCYA"; a.Revision++;
            var agencyB = new ServerAgency { Name = "Synthetic polling agency B" };
            db.Agencies.Add(agencyB); await db.SaveChangesAsync();
            var b = new ClearinghouseAccount
            {
                Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), AgencyId = agencyB.Id,
                IsEnabled = true, IsTest = true, ConnectorKind = TradingPartnerKind.ClaimMd,
                ExternalAccountNumber = "POLLTESTB", ClaimNamespace = "POLLB",
                TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
                SecretReference = "CLAIMMD_SANDBOX_KEY_AGENCYB"
            };
            db.ClearinghouseAccounts.Add(b);
            foreach (var account in new[] { a, b })
                foreach (var feed in new[] { ClearinghouseFeedKind.Status, ClearinghouseFeedKind.Era })
                    db.ClearinghouseFeedCheckpoints.Add(new ClearinghouseFeedCheckpoint
                    { Id = Guid.NewGuid(), AgencyId = account.AgencyId, AccountId = account.Id, FeedKind = feed, Cursor = "0" });
            await db.SaveChangesAsync();
        }
        var calls = new List<string>();
        using var http = new HttpClient(new PageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            var agency = body.Contains("AGENCYB", StringComparison.Ordinal) ? "B" : "A";
            var status = request.RequestUri!.AbsolutePath == "/services/response/";
            calls.Add(agency + (status ? ":Status" : ":Era"));
            return Xml(status ? "<result last_responseid=\"0\"/>" : "<result last_eraid=\"0\"/>");
        }));
        var connector = new ClaimMdSandboxConnector(http, new ReferenceKeySource(), new TestClaimMdCoordination(), TimeProvider.System);
        var gate = new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo", EnableClaimMdSandboxTransport = true }), new DemoHostEnvironment());
        var poller = new ClaimMdSandboxPoller(
            fixture.Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(), connector,
            fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), gate, fixture.Protector,
            new TestClaimMdCoordination(), new TestDemoWorkerResetCoordination(), NullLogger<ClaimMdSandboxPoller>.Instance, TestPollSelector(fixture.Factory));
        Assert.Equal(0, await poller.PollOnceAsync(CancellationToken.None));
        Assert.Equal(["A:Status", "B:Status", "A:Era", "B:Era"], calls);
        await using var read = fixture.Factory.OpenDatabase();
        Assert.All(await read.ClearinghouseFeedCheckpoints.ToListAsync(), row => Assert.Equal("0", row.Cursor));
        Assert.Empty(await read.ClearinghouseResponseReceipts.ToListAsync());
    }

    private sealed class ReferenceKeySource : IClaimMdSandboxKeySource
    { public string Resolve(string? reference) => reference!; }

    private static ClearinghousePollSelector TestPollSelector(SyntheticPipelineFactory factory) => new(
        factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>(),
        new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        { ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests", EnableClaimMdSandboxTransport = true }),
        factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>()));
}
