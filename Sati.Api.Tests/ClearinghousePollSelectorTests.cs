using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClearinghousePollSelectorTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(null)]
    public void PollingCancellationWinsOverUnavailableLockResults(int? result)
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => ClearinghousePollSelector.OwnsLease(result, stop.Token));
    }
    [Fact]
    public void PollingBothModelsPreserveAccountUniquenessIndexesAndScopedSchedulingRevisions()
    {
        using var api = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer("Server=unused;Database=SatiSyntheticDesignOnly;Integrated Security=true").Options);
        using var local = new Sati.Data.SatiContext(new DbContextOptionsBuilder<Sati.Data.SatiContext>().UseSqlServer("Server=unused;Database=SatiSyntheticDesignOnly;Integrated Security=true").Options);
        foreach (var model in new[] { api.Model, local.Model })
        {
            foreach (var type in new[] { typeof(ClearinghousePollRotation), typeof(ClearinghouseAgencyPollRotation), typeof(ClearinghouseAccountPollRotation) })
            {
                var entity = model.FindEntityType(type)!;
                Assert.True(entity.FindProperty("Revision")!.IsConcurrencyToken);
                Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
            }
            var unique = Assert.Single(model.FindEntityType(typeof(ClearinghouseAccount))!.GetIndexes(), x =>
                x.Properties.Select(p => p.Name).SequenceEqual(new[] { "AgencyId", "ConnectorKind", "IsTest" }));
            Assert.True(unique.IsUnique); Assert.Equal("[IsEnabled] = 1", unique.GetFilter());
            Assert.Contains(model.FindEntityType(typeof(ClearinghouseFeedCheckpoint))!.GetIndexes(), x =>
                x.Properties.Select(p => p.Name).SequenceEqual(new[] { "AgencyId", "AccountId" }) && !x.IsUnique);
            Assert.Equal(2, model.FindEntityType(typeof(ClearinghouseAccountPollRotation))!.FindPrimaryKey()!.Properties.Count);
        }
    }
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(-1, false)]
    public void PollingKnownLockResultsHaveExactOwnership(int result, bool owned) =>
        Assert.Equal(owned, ClearinghousePollSelector.OwnsLease(result, CancellationToken.None));

    [Theory]
    [InlineData(-2)]
    [InlineData(-3)]
    [InlineData(-999)]
    [InlineData(2)]
    [InlineData(null)]
    public void PollingUnknownLockResultsNeverGrantOwnership(int? result) =>
        Assert.Throws<InvalidOperationException>(() => ClearinghousePollSelector.OwnsLease(result, CancellationToken.None));
    [Fact]
    public Task PollingRotationGivesAgencyAccountAndFeedOpportunitiesAcrossRestart() => RotationAsync(false);

    [SqlServerFact]
    public Task ClaimReleaseSqlPollingRotationGivesAgencyAccountAndFeedOpportunitiesAcrossRestart() => RotationAsync(true);

    private static async Task RotationAsync(bool sql)
    {
        var reads = new SelectorReads();
        await using var fixture = await PollFixture.CreateAsync(sql, reads);
        var offers = new List<PollSelection>();
        for (var i = 0; i < 24; i++)
        {
            reads.Commands.Clear();
            // Recreating both factory/selector each time rules out a process-local pivot.
            var selector = fixture.Selector();
            offers.Add(await selector.SelectAsync([], CancellationToken.None));
            Assert.InRange(reads.Commands.Count, 6, 9);
            Assert.All(reads.Commands, text => Assert.True(text.Contains(sql ? "TOP(" : "LIMIT", StringComparison.Ordinal), text));
        }
        Assert.Equal(Enumerable.Range(0, 24).Select(i => i % 2 == 0 ? fixture.A : fixture.B), offers.Select(x => x.AgencyId));
        var agencyA = offers.Where(x => x.AgencyId == fixture.A).ToArray();
        Assert.Single(agencyA.Select(x => x.AccountId).Distinct());
        foreach (var group in agencyA.GroupBy(x => x.AccountId))
            Assert.Equal(Enumerable.Range(0, 12).Select(i => i % 2 == 0 ? ClearinghouseFeedKind.Status : ClearinghouseFeedKind.Era), group.Select(x => x.FeedKind));
        await using var db = fixture.Open();
        var providerOrder = await db.ClearinghouseAccounts.Where(x => x.AgencyId == fixture.A && x.IsEnabled).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
        Assert.Equal(providerOrder, agencyA.Take(1).Select(x => x.AccountId));
        Assert.All(await db.ClearinghouseFeedCheckpoints.ToListAsync(), x => { Assert.Equal("0", x.Cursor); Assert.Equal(0, x.Revision); Assert.Null(x.LastReceiptId); });
        Assert.All(await db.ClearinghouseAccounts.ToListAsync(), x => Assert.Equal(0, x.Revision));
        Assert.Empty(await db.ClearinghouseResponseReceipts.ToListAsync());
        Assert.Equal(25, (await db.ClearinghousePollRotation.SingleAsync()).Revision);
        Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
    }

    [Fact]
    public async Task PollingVisitedFeedsAreExcludedWithoutCheckpointWrites()
    {
        await using var fixture = await PollFixture.CreateAsync();
        var visited = new HashSet<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var offer = await fixture.Selector().SelectAsync(visited, CancellationToken.None);
            Assert.Equal(PollSelectionKind.Selected, offer.Kind); Assert.True(visited.Add(offer.CheckpointId));
        }
        Assert.Equal(PollSelectionKind.Empty, (await fixture.Selector().SelectAsync(visited, CancellationToken.None)).Kind);
        await using var db = fixture.Open(); Assert.Equal(5, (await db.ClearinghousePollRotation.SingleAsync()).Revision);
        Assert.All(await db.ClearinghouseFeedCheckpoints.ToListAsync(), x => Assert.Equal("0", x.Cursor));
        Assert.Equal(PollSelectionKind.Selected, (await fixture.Selector().SelectAsync([], CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task PollingEligibilityPreservesScopeAndUnsupportedFeedsNeverOffer()
    {
        await using var fixture = await PollFixture.CreateAsync();
        await using (var db = fixture.Open())
        {
            var accounts = await db.ClearinghouseAccounts.OrderBy(x => x.Id).ToListAsync();
            var activeA = accounts.Single(x => x.AgencyId == fixture.A && x.IsEnabled);
            activeA.IsEnabled = false; activeA.Revision++;
            var dormant = accounts.First(x => x.AgencyId == fixture.A && x.Id != activeA.Id);
            dormant.SecretReference = null; dormant.Revision++;
            var otherFeeds = await db.ClearinghouseFeedCheckpoints.Where(x => x.AccountId == dormant.Id).ToListAsync();
            foreach (var feed in otherFeeds) { feed.Cursor = null; feed.Revision++; }
            db.ClearinghouseFeedCheckpoints.Add(new ClearinghouseFeedCheckpoint
            { Id = Guid.NewGuid(), AgencyId = fixture.B, AccountId = accounts.Single(x => x.AgencyId == fixture.B).Id, FeedKind = ClearinghouseFeedKind.Modifications, Cursor = "0" });
            await db.SaveChangesAsync();
        }
        var visited = new HashSet<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var offer = await fixture.Selector().SelectAsync(visited, CancellationToken.None);
            Assert.Equal(PollSelectionKind.Selected, offer.Kind); Assert.True(visited.Add(offer.CheckpointId));
            Assert.Equal(fixture.B, offer.AgencyId);
        }
        Assert.Equal(PollSelectionKind.Empty, (await fixture.Selector().SelectAsync(visited, CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task PollingMissingSeedDisabledGateCancellationAndExclusionBoundsFailClosed()
    {
        await using var fixture = await PollFixture.CreateAsync();
        fixture.Options = new SatiApiOptions { ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests" };
        Assert.Equal(PollSelectionKind.Disabled, (await fixture.Selector().SelectAsync([], CancellationToken.None)).Kind);
        fixture.Options = new SatiApiOptions { ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests", EnableClaimMdSandboxTransport = true };
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Selector().SelectAsync([], stop.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Selector().SelectAsync(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray(), CancellationToken.None));
        await using var db = fixture.Open();
        Assert.Equal(1, (await db.ClearinghousePollRotation.SingleAsync()).Revision);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ClearinghousePollRotation");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Selector().SelectAsync([], CancellationToken.None));
        Assert.Empty(await db.ClearinghouseAgencyPollRotation.ToListAsync());
    }

    [Fact]
    public async Task PollingPivotsRejectDeletionBadFeedAndMissingRevisionAndConcurrentOverwrite()
    {
        await using var fixture = await PollFixture.CreateAsync();
        await fixture.Selector().SelectAsync([], CancellationToken.None);
        foreach (var kind in new[] { "global", "agency", "account" })
        {
            await using var db = fixture.Open();
            object row = kind switch
            {
                "global" => await db.ClearinghousePollRotation.SingleAsync(),
                "agency" => await db.ClearinghouseAgencyPollRotation.SingleAsync(),
                _ => await db.ClearinghouseAccountPollRotation.SingleAsync()
            };
            db.Remove(row); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var bad = fixture.Open())
        {
            var row = await bad.ClearinghouseAccountPollRotation.SingleAsync();
            row.LastFeedKind = ClearinghouseFeedKind.Modifications; row.Revision++;
            await Assert.ThrowsAsync<InvalidOperationException>(() => bad.SaveChangesAsync());
        }
        await using (var bad = fixture.Open())
        {
            (await bad.ClearinghousePollRotation.SingleAsync()).LastAgencyId = fixture.B;
            await Assert.ThrowsAsync<InvalidOperationException>(() => bad.SaveChangesAsync());
        }
        await using var first = fixture.Open(); await using var stale = fixture.Open();
        var a = await first.ClearinghousePollRotation.SingleAsync(); var b = await stale.ClearinghousePollRotation.SingleAsync();
        a.LastAgencyId = fixture.B; a.Revision++; await first.SaveChangesAsync();
        b.LastAgencyId = fixture.B; b.Revision++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    private sealed class SelectorReads : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)) Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PollFixture : IAsyncDisposable
    {
        public SyntheticPipelineDatabase Database { get; }
        private readonly IInterceptor[] interceptors;
        public int A { get; private set; }
        public int B { get; private set; }
        public SatiApiOptions Options { get; set; } = new()
        { ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests", EnableClaimMdSandboxTransport = true };
        private PollFixture(bool sql, IInterceptor[] hooks) { Database = new(sql); interceptors = hooks; }
        public ApiDbContext Open() => new(Database.Options(interceptors));
        public ClearinghousePollSelector Selector() => new(new PollContexts(Database.Options(interceptors)),
            new ClearinghouseDispatchGate(Microsoft.Extensions.Options.Options.Create(Options), new PollEnvironment()));
        public static async Task<PollFixture> CreateAsync(bool sql = false, params IInterceptor[] hooks)
        {
            var fixture = new PollFixture(sql, hooks);
            try
            {
                await fixture.Database.InitializeAsync();
                await using var db = fixture.Open();
                var a = new ServerAgency { Name = "Synthetic poll A" }; var b = new ServerAgency { Name = "Synthetic poll B" };
                db.Agencies.AddRange(a, b); await db.SaveChangesAsync(); fixture.A = a.Id; fixture.B = b.Id;
                var ids = new[] { Guid.Parse("ffffffff-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    Guid.Parse("00000001-0000-0000-0000-000000000000"), Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") };
                for (var i = 0; i < 4; i++)
                {
                    var agency = i == 3 ? b.Id : a.Id;
                    db.ClearinghouseAccounts.Add(new ClearinghouseAccount
                    { Id = ids[i], AgencyId = agency, IsEnabled = i is 0 or 3, IsTest = true, ConnectorKind = TradingPartnerKind.ClaimMd,
                        ClaimNamespace = "POLL" + i, ExternalAccountNumber = "POLLTEST" + i,
                        TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion, SecretReference = "CLAIMMD_SANDBOX_KEY_POLL" + i });
                    foreach (var feed in new[] { ClearinghouseFeedKind.Status, ClearinghouseFeedKind.Era })
                        db.ClearinghouseFeedCheckpoints.Add(new ClearinghouseFeedCheckpoint
                        { Id = Guid.NewGuid(), AgencyId = agency, AccountId = ids[i], FeedKind = feed, Cursor = "0" });
                }
                await db.SaveChangesAsync(); return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class PollContexts(DbContextOptions<ApiDbContext> options) : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext CreateDbContext() => new(options);
        public ValueTask<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(CreateDbContext());
    }
    private sealed class PollEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Sati.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
