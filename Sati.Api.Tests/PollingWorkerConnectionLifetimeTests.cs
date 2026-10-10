using System.Data.Common;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;
using Xunit.Sdk;

namespace Sati.Api.Tests;

public sealed partial class ClaimMdStatusProcessorTests
{
    [SqlServerFact]
    public Task PollingLifetimeNormalCommitsIndependentReceiptsWithoutVendorReceiptOverlap() => PollingLifetimeAsync("normal");
    [SqlServerFact]
    public Task PollingLifetimeStatusWrapFailureLeavesCursorAndAllowsEraThenRetry() => PollingLifetimeAsync("wrap-failure");
    [SqlServerFact]
    public Task PollingLifetimeHttpCancellationClosesLeasesAndLeavesBothFeedsDue() => PollingLifetimeAsync("cancel");
    [SqlServerFact]
    public Task PollingLifetimeEraHttpFailurePreservesStatusThenLaterCompletesEra() => PollingLifetimeAsync("era-failure");
    [SqlServerFact]
    public Task PollingLifetimeExtraConnectionIsDetectedAtHttpBoundary() => PollingLifetimeAsync("extra");
    [SqlServerFact]
    public Task PollingLifetimeSecondCallerAdmissionIsChargedAndCannotEnterHttp() => PollingLifetimeAsync("second-caller");

    private static async Task PollingLifetimeAsync(string scenario)
    {
        var probe = new WorkerConnectionProbe();
        var saves = new PollingLifetimeSaves(probe);
        var wrapping = new PollingLifetimeWrapping();
        await using var fixture = await Fixture.CreateAsync(true, [probe, saves], wrapping);
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var account = await db.ClearinghouseAccounts.SingleAsync();
            account.SecretReference = "CLAIMMD_SANDBOX_KEY_TEST"; account.Revision++;
            foreach (var kind in new[] { ClearinghouseFeedKind.Status, ClearinghouseFeedKind.Era })
                db.ClearinghouseFeedCheckpoints.Add(new ClearinghouseFeedCheckpoint
                { Id = Guid.NewGuid(), AgencyId = account.AgencyId, AccountId = account.Id, FeedKind = kind, Cursor = "0" });
            await db.SaveChangesAsync();
        }
        var contexts = fixture.Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>();
        var options = Options.Create(new SatiApiOptions { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo", EnableClaimMdSandboxTransport = true });
        var gate = new ClearinghouseDispatchGate(options, new DemoHostEnvironment());
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wraps = 0;
        var faultEnabled = true;
        wrapping.BeforeWrap = () =>
        {
            Assert.Equal(2, probe.Held); // reset + poller, prior decision SQL closed before key access
            if (++wraps == 1 && scenario == "wrap-failure" && faultEnabled) throw new IOException("Synthetic receipt wrapping failure.");
        };
        var paths = new List<string>();
        var extraDetected = false;
        using var http = new HttpClient(new PollingLifetimeHttp(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path); Assert.Equal(3, probe.Held); // reset + poller + request, no receipt SQL
            if (paths.Count == 1)
            {
                if (scenario == "second-caller") { entered.TrySetResult(); await release.Task.WaitAsync(token); }
                if (scenario == "cancel" && faultEnabled) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                if (scenario == "extra")
                {
                    await using (var extra = new ApiDbContext(fixture.Database.Options(probe)))
                    {
                        await extra.Database.OpenConnectionAsync(token);
                        Assert.Throws<EqualException>(() => Assert.Equal(3, probe.Held));
                        Assert.Equal(4, probe.Held); extraDetected = true;
                    }
                    Assert.Equal(3, probe.Held);
                }
            }
            var body = await request.Content!.ReadAsStringAsync(token);
            if (path == "/services/response/")
                return Xml(body.Contains("ResponseID=12", StringComparison.OrdinalIgnoreCase)
                    ? "<result last_responseid=\"12\"/>" : fixture.Status("R").RawXml);
            if (path == "/services/eralist/")
                return Xml(body.Contains("ERAID=7", StringComparison.OrdinalIgnoreCase)
                    ? "<result last_eraid=\"7\"/>" : "<result last_eraid=\"7\"><era eraid=\"7\"/></result>");
            if (path == "/services/era835/")
            {
                if (scenario == "era-failure" && faultEnabled) throw new IOException("Synthetic ERA exchange failure.");
                return Xml(new XElement("result", new XAttribute("eraid", "7"), new XElement("data", fixture.Era835())).ToString(SaveOptions.DisableFormatting));
            }
            throw new InvalidOperationException("Unexpected synthetic polling endpoint.");
        }));
        ClaimMdSandboxConnector.ConfigureHttpClient(http);
        ClaimMdSandboxPoller Poller()
        {
            var coordination = new SqlClaimMdSandboxCoordination(contexts);
            var connector = new ClaimMdSandboxConnector(http, new PageKeySource(), coordination, TimeProvider.System);
            return new ClaimMdSandboxPoller(contexts, connector, fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
                gate, fixture.Protector, coordination, new SqlDemoWorkerResetCoordination(contexts, options),
                NullLogger<ClaimMdSandboxPoller>.Instance, new ClearinghousePollSelector(contexts, gate));
        }
        probe.ResetEvidence(); saves.Armed = true;
        var first = Poller();
        var run = first.PollOnceAsync(cancellation.Token);
        if (scenario == "second-caller")
        {
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(3, probe.Held);
                Assert.Equal(0, await Poller().PollOnceAsync(CancellationToken.None));
                Assert.Single(paths); Assert.Equal(3, probe.Held); Assert.Equal(5, probe.Peak);
            }
            finally { release.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(30)); }
        }
        if (scenario == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        else Assert.Equal(scenario is "wrap-failure" or "era-failure" ? 1 : 2, await run);
        Assert.Equal(0, probe.Held);
        Assert.Equal(scenario == "second-caller" ? 5 : scenario == "extra" ? 4 : 3, probe.Peak);
        if (scenario == "extra") Assert.True(extraDetected);
        await AssertPollingLifetimeEffectsAsync(fixture,
            scenario is "cancel" or "wrap-failure" ? "0" : "12",
            scenario is "cancel" or "era-failure" ? "0" : "7");
        faultEnabled = false;
        Assert.Equal(scenario == "cancel" ? 2 : scenario is "wrap-failure" or "era-failure" ? 1 : 0,
            await Poller().PollOnceAsync(CancellationToken.None));
        Assert.Equal(0, probe.Held);
        await AssertPollingLifetimeEffectsAsync(fixture, "12", "7");
        Assert.Equal(2, saves.Receipts);
    }

    private static async Task AssertPollingLifetimeEffectsAsync(Fixture fixture, string status, string era)
    {
        await using var evidence = new ApiDbContext(fixture.Database.Options());
        var checkpoints = await evidence.ClearinghouseFeedCheckpoints.ToDictionaryAsync(row => row.FeedKind);
        Assert.Equal(status, checkpoints[ClearinghouseFeedKind.Status].Cursor); Assert.Equal(era, checkpoints[ClearinghouseFeedKind.Era].Cursor);
        Assert.Equal(status == "0" ? 0 : 1, await evidence.ClearinghouseResponseReceipts.CountAsync(row => row.Kind == ClaimResponseKind.ClaimMdApiStatus));
        Assert.Equal(era == "0" ? 0 : 1, await evidence.ClearinghouseResponseReceipts.CountAsync(row => row.Kind == ClaimResponseKind.RemittanceAdvice));
        Assert.Equal(status == "0" ? 0 : 1, await evidence.ClaimAcknowledgementOutcomes.CountAsync());
        Assert.Equal(era == "0" ? 0 : ClaimResponseReader.ReadSubmission(fixture.GenerationContent).Claims.Count,
            await evidence.RemittanceClaimOutcomes.CountAsync());
        foreach (var checkpoint in checkpoints.Values)
        {
            if (checkpoint.Cursor == "0") Assert.Null(checkpoint.LastReceiptId);
            else Assert.True(await evidence.ClearinghouseResponseReceipts.AnyAsync(row => row.Id == checkpoint.LastReceiptId && row.FeedKind == checkpoint.FeedKind));
        }
    }

    private sealed class PollingLifetimeSaves(WorkerConnectionProbe probe) : SaveChangesInterceptor
    {
        internal bool Armed;
        internal int Receipts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<ClearinghouseResponseReceipt>().Any(row => row.State == EntityState.Added))
            { Receipts++; Assert.Equal(3, probe.Held); Assert.NotNull(data.Context.Database.CurrentTransaction); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class PollingLifetimeWrapping : IKeyWrapper
    {
        private readonly TestKeyWrapper inner = new();
        internal Action? BeforeWrap;
        public Task<WrappedDataKey> WrapAsync(byte[] key, CancellationToken cancellationToken = default)
        { BeforeWrap?.Invoke(); return inner.WrapAsync(key, cancellationToken); }
        public Task<byte[]> UnwrapAsync(byte[] key, string id, CancellationToken cancellationToken = default) => inner.UnwrapAsync(key, id, cancellationToken);
    }
    private sealed class PollingLifetimeHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Assert.Equal("svc.claim.md", request.RequestUri!.Host); return respond(request, cancellationToken); }
    }
}
