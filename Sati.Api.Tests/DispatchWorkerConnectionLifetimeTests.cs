using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;
using Xunit.Sdk;

namespace Sati.Api.Tests;

public sealed partial class ClearinghouseDispatchApiTests
{
    [SqlServerFact]
    public Task DispatchLifetimeNormalAcceptedUploadPreservesDistinctAccountAndVendorIntervals() => DispatchLifetimeAsync("normal");
    [SqlServerFact]
    public Task DispatchLifetimeMissingKeyDefersWithoutTransportAndClosesDecisionConnections() => DispatchLifetimeAsync("missing-key");
    [SqlServerFact]
    public Task DispatchLifetimePreflightCancellationLeavesQueuedWithoutPhysicalAttempt() => DispatchLifetimeAsync("preflight-cancel");
    [SqlServerFact]
    public Task DispatchLifetimeCancelledUploadRetainsUnknownEvidenceWithoutResend() => DispatchLifetimeAsync("upload-cancel");
    [SqlServerFact]
    public Task DispatchLifetimeExtraConnectionFailsUploadIntervalAssertion() => DispatchLifetimeAsync("extra");

    private static async Task DispatchLifetimeAsync(string scenario)
    {
        var probe = new WorkerConnectionProbe();
        var saves = new DispatchLifetimeSaves(probe);
        var wrapping = new DispatchLifetimeWrapping();
        var keys = new DispatchLifetimeKeys();
        await using var fixture = await Fixture.CreateAsync(sqlServer: true, interceptor: probe,
            keyWrapper: wrapping, keySource: keys, additionalInterceptors: [saves]);
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var account = await db.ClearinghouseAccounts.SingleAsync();
            account.SecretReference = "CLAIMMD_SANDBOX_KEY_TEST"; account.Revision++;
            await db.SaveChangesAsync();
        }
        var file = await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
        var claims = ClaimResponseReader.ReadSubmission(file.Content).Claims;
        Assert.NotEmpty(claims);
        var contexts = fixture.Factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>();
        var options = Options.Create(new SatiApiOptions { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo", EnableClaimMdSandboxTransport = true });
        var gate = new ClearinghouseDispatchGate(options, new DispatchLifetimeEnvironment());
        var coordination = new SqlClaimMdSandboxCoordination(contexts);
        var reset = new SqlDemoWorkerResetCoordination(contexts, options);
        using var cancellation = new CancellationTokenSource();
        var keyCalls = 0;
        keys.BeforeResolve = () =>
        {
            Assert.Equal(++keyCalls == 1 ? 3 : 2, probe.Held);
            if (scenario == "missing-key") throw new ClaimMdAccountKeyUnavailableException();
            if (scenario == "preflight-cancel") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
        };
        var wraps = 0;
        wrapping.BeforeWrap = () => Assert.Equal(++wraps == 1 ? 3 : 2, probe.Held);
        var httpCalls = 0;
        var adverseDetected = false;
        using var http = new HttpClient(new DispatchLifetimeHttp(async token =>
        {
            httpCalls++; Assert.Equal(3, probe.Held); // reset + dispatch + vendor; account/decision already disposed
            if (scenario == "extra")
            {
                await using (var extra = new ApiDbContext(fixture.Database.Options(probe)))
                {
                    await extra.Database.OpenConnectionAsync(token);
                    Assert.Throws<EqualException>(() => Assert.Equal(3, probe.Held));
                    Assert.Equal(4, probe.Held); adverseDetected = true;
                }
                Assert.Equal(3, probe.Held);
            }
            if (scenario == "upload-cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<result>" + string.Concat(claims.Select(claim =>
                    $"<claim pcn=\"{claim.ClaimReference}\" remote_claimid=\"{claim.RemoteClaimId}\" fileid=\"123456\" status=\"A\"><messages status=\"A\" mesgid=\"ACK\"/></claim>")) + "</result>", Encoding.UTF8, "application/xml")
            };
        }));
        ClaimMdSandboxConnector.ConfigureHttpClient(http);
        var connector = new ClaimMdSandboxConnector(http, keys, coordination, TimeProvider.System);
        var worker = new ClearinghouseDispatchWorker(contexts, connector, gate, new EnvelopeProtector(wrapping), keys,
            reset, NullLogger<ClearinghouseDispatchWorker>.Instance, TimeProvider.System,
            fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchSchedule>(), new ClearinghouseDispatchSelector(contexts, gate, TimeProvider.System));
        probe.ResetEvidence(); saves.Armed = true;
        if (scenario == "preflight-cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ProcessOneAsync(cancellation.Token));
        else Assert.True(await worker.ProcessOneAsync(cancellation.Token));
        Assert.Equal(0, probe.Held);
        Assert.Equal(4, probe.Peak); // preflight SQL reads also overlap all three admission sessions
        Assert.Equal(scenario is "missing-key" or "preflight-cancel" ? 0 : 1, httpCalls);
        Assert.Equal(scenario is "missing-key" or "preflight-cancel" ? 0 : 1, saves.Sending);
        Assert.Equal(scenario is "missing-key" or "preflight-cancel" ? 0 : 1, saves.Attempts);
        Assert.Equal(scenario is "missing-key" or "preflight-cancel" ? 0 : scenario == "upload-cancel" ? 1 : 2, wraps);
        if (scenario == "extra") Assert.True(adverseDetected);
        await using (var evidence = new ApiDbContext(fixture.Database.Options()))
        {
            var dispatch = await evidence.ClearinghouseDispatches.SingleAsync();
            if (scenario is "missing-key" or "preflight-cancel")
            {
                Assert.Equal(ClearinghouseDispatchState.Queued, dispatch.State);
                Assert.Empty(await evidence.ClearinghouseDispatchAttempts.ToListAsync());
                if (scenario == "missing-key")
                {
                    var readiness = await evidence.ClearinghouseDispatchReadiness.SingleAsync();
                    Assert.Equal(1, readiness.FailureCount); Assert.NotNull(readiness.NextEligibleAtUtc);
                    Assert.Equal("account_key_unavailable", readiness.SafeFailureCode);
                }
            }
            else
            {
                var attempt = await evidence.ClearinghouseDispatchAttempts.SingleAsync();
                Assert.Equal(dispatch.Id, attempt.DispatchId); Assert.Equal(file.FileName, attempt.FileName);
                Assert.Equal(scenario == "upload-cancel" ? ClearinghouseDispatchState.OutcomeUnknown : ClearinghouseDispatchState.AcceptedByClearinghouse, dispatch.State);
                Assert.Equal(scenario == "upload-cancel" ? ClearinghouseAttemptOutcome.OutcomeUnknown : ClearinghouseAttemptOutcome.Accepted, attempt.Outcome);
                if (scenario != "upload-cancel")
                { Assert.Equal("123456", dispatch.ExternalFileId); Assert.NotNull(attempt.ResponseCiphertext); }
            }
        }
        if (scenario == "upload-cancel")
        {
            // A later pass can inspect selection metadata, but cannot repeat the physical upload.
            Assert.False(await worker.ProcessOneAsync(CancellationToken.None));
            Assert.Equal(1, httpCalls); Assert.Equal(0, probe.Held);
            await using var evidence = new ApiDbContext(fixture.Database.Options());
            Assert.Single(await evidence.ClearinghouseDispatchAttempts.ToListAsync());
        }
    }

    private sealed class DispatchLifetimeSaves(WorkerConnectionProbe probe) : SaveChangesInterceptor
    {
        internal bool Armed;
        internal int Sending, Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                if (data.Context!.ChangeTracker.Entries<ClearinghouseDispatch>().Any(x => x.State == EntityState.Modified && x.Entity.State == ClearinghouseDispatchState.Sending))
                { Sending++; Assert.Equal(4, probe.Held); Assert.NotNull(data.Context.Database.CurrentTransaction); }
                if (data.Context!.ChangeTracker.Entries<ClearinghouseDispatchAttempt>().Any(x => x.State == EntityState.Added))
                { Attempts++; Assert.Equal(3, probe.Held); Assert.NotNull(data.Context.Database.CurrentTransaction); Assert.False(cancellationToken.CanBeCanceled); }
            }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class DispatchLifetimeWrapping : IKeyWrapper
    {
        private readonly TestKeyWrapper inner = new();
        internal Action? BeforeWrap;
        public Task<WrappedDataKey> WrapAsync(byte[] key, CancellationToken cancellationToken = default)
        { BeforeWrap?.Invoke(); return inner.WrapAsync(key, cancellationToken); }
        public Task<byte[]> UnwrapAsync(byte[] key, string id, CancellationToken cancellationToken = default) => inner.UnwrapAsync(key, id, cancellationToken);
    }
    private sealed class DispatchLifetimeKeys : IClaimMdSandboxKeySource
    {
        internal Action? BeforeResolve;
        public string Resolve(string? reference) { BeforeResolve?.Invoke(); Assert.Equal("CLAIMMD_SANDBOX_KEY_TEST", reference); return "SYNTHETIC_KEY"; }
    }
    private sealed class DispatchLifetimeHttp(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Assert.Equal("https://svc.claim.md/services/upload/", request.RequestUri!.AbsoluteUri); return respond(cancellationToken); }
    }
    private sealed class DispatchLifetimeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Demo";
        public string ApplicationName { get; set; } = "Sati.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
