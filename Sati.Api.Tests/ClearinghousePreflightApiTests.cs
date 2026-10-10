using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClearinghouseDispatchApiTests
{
    [Fact]
    public async Task PreflightFifthFailureHoldsWithoutCancellingOrAutomaticallyResending()
    {
        var keys = new ControlledPreflightKeys();
        await using var fixture = await CreatePreflightFixtureAsync(keys);
        var clock = new PreflightTestClock(); var connector = new IsolationRecordingConnector();
        var worker = CreateIsolationWorker(fixture.Factory, keys, connector, clock);
        for (var failure = 1; failure <= 5; failure++)
        {
            Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
            await using var db = fixture.Factory.OpenDatabase();
            var readiness = await db.ClearinghouseDispatchReadiness.SingleAsync();
            Assert.Equal(failure, readiness.FailureCount); Assert.Equal(failure, readiness.Revision);
            Assert.Equal(ClearinghouseDispatchState.Queued, (await db.ClearinghouseDispatches.SingleAsync()).State);
            Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
            Assert.Equal(failure, await db.AuditEvents.CountAsync(x => x.Action.StartsWith("billing-clearinghouse.preflight-")));
            if (readiness.NextEligibleAtUtc is { } due)
            {
                clock.SetUtc(due.AddTicks(-1));
                Assert.False(await worker.ProcessOneAsync(CancellationToken.None));
                Assert.Equal(failure, keys.Calls);
                clock.SetUtc(due);
            }
            else Assert.Equal(ClearinghousePreflightDisposition.Held, readiness.Disposition);
        }
        keys.Available = true;
        clock.SetUtc(clock.GetUtcNow().UtcDateTime.AddYears(1));
        Assert.False(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.Equal(5, keys.Calls); Assert.Empty(connector.Dispatches);
        await using var retained = fixture.Factory.OpenDatabase();
        Assert.All(await retained.AuditEvents.Where(x => x.Action.StartsWith("billing-clearinghouse.preflight-")).ToListAsync(),
            row => { Assert.Equal(SystemActor.UserId, row.ActorUserId); Assert.DoesNotContain("CLAIMMD_SANDBOX_KEY", row.MetadataJson); });
        using var reopen = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId), await ReadReopenRequestAsync(fixture));
        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.Single(connector.Dispatches);
        await using var recovered = fixture.Factory.OpenDatabase();
        Assert.Equal(fixture.Actors.BillerId, (await recovered.AuditEvents.SingleAsync(row =>
            row.Action == "billing-clearinghouse.preflight-reopened")).ActorUserId);
    }

    [Fact]
    public async Task PreflightCancellationDoesNotConsumeFailureOrCreateFalseSendEvidence()
    {
        var keys = new ControlledPreflightKeys();
        await using var fixture = await CreatePreflightFixtureAsync(keys);
        using var cancellation = new CancellationTokenSource(); keys.BeforeResolve = cancellation.Cancel;
        var connector = new IsolationRecordingConnector();
        var worker = CreateIsolationWorker(fixture.Factory, keys, connector);
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ProcessOneAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        await AssertNoPreflightEffectsAsync(fixture, connector);
    }

    [Theory]
    [InlineData("unknown-key")] [InlineData("shared-wrap")]
    public async Task PreflightSharedAndUnknownFaultsAreNotAccountFailures(string fault)
    {
        var keys = new ControlledPreflightKeys { Available = true };
        var wrapper = new ControlledPreflightWrapper();
        await using var fixture = await CreatePreflightFixtureAsync(keys, wrapper);
        if (fault == "unknown-key") keys.UnknownFault = true;
        else wrapper.Fail = true;
        var connector = new IsolationRecordingConnector();
        await Assert.ThrowsAnyAsync<Exception>(() => CreateIsolationWorker(fixture.Factory, keys, connector)
            .ProcessOneAsync(CancellationToken.None));
        await AssertNoPreflightEffectsAsync(fixture, connector);
    }

    [Fact]
    public async Task PreflightFailedCommitRollsBackDispositionAndItsAuditTogether()
    {
        var keys = new ControlledPreflightKeys(); var fault = new FailReleaseCommit();
        await using var fixture = await CreatePreflightFixtureAsync(keys, interceptor: fault);
        var connector = new IsolationRecordingConnector(); fault.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateIsolationWorker(fixture.Factory, keys, connector)
            .ProcessOneAsync(CancellationToken.None));
        Assert.True(fault.Fired);
        await AssertNoPreflightEffectsAsync(fixture, connector);
        Assert.True(await CreateIsolationWorker(fixture.Factory, keys, connector).ProcessOneAsync(CancellationToken.None));
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Equal(1, (await db.ClearinghouseDispatchReadiness.SingleAsync()).FailureCount);
    }

    [Fact]
    public async Task PreflightWorkspaceIsScopedAndContainsOnlySafeSchedulingFacts()
    {
        var keys = new ControlledPreflightKeys();
        await using var fixture = await CreatePreflightFixtureAsync(keys);
        Assert.True(await CreateIsolationWorker(fixture.Factory, keys, new IsolationRecordingConnector()).ProcessOneAsync(CancellationToken.None));
        var foreignId = await fixture.AddAccountAsync("FOREIGN", "FOREIGN", foreign: true);
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var foreign = await db.ClearinghouseAccounts.SingleAsync(row => row.Id == foreignId);
            var hidden = new ClearinghouseDispatchReadiness { AgencyId = foreign.AgencyId, AccountId = foreignId };
            hidden.Apply(ClearinghousePreflightRules.MissingKey(foreignId, null, Guid.NewGuid(), DateTime.UtcNow), foreign.Revision);
            db.ClearinghouseDispatchReadiness.Add(hidden); await db.SaveChangesAsync();
        }
        using var response = await fixture.Biller.GetAsync("/api/v1/billing/clearinghouse");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var workspace = (await response.Content.ReadFromJsonAsync<ClearinghouseWorkspaceDto>())!;
        var readiness = Assert.Single(workspace.Accounts).Readiness;
        Assert.NotNull(readiness); Assert.Equal("Deferred", readiness.Disposition);
        Assert.Equal(1, readiness.FailureCount); Assert.NotNull(readiness.NextEligibleAtUtc);
        Assert.DoesNotContain("CLAIMMD_SANDBOX_KEY", json); Assert.DoesNotContain("SYNTHETIC_ONLY_KEY", json);
        Assert.DoesNotContain(foreignId.ToString(), json);
        Assert.Equal("Queued", Assert.Single(workspace.Dispatches).State);
    }

    [Fact]
    public async Task PreflightReopenClearsOnlyReadinessAndRequiresARefreshForRepeat()
    {
        var keys = new ControlledPreflightKeys();
        await using var fixture = await CreatePreflightFixtureAsync(keys);
        var connector = new IsolationRecordingConnector();
        Assert.True(await CreateIsolationWorker(fixture.Factory, keys, connector).ProcessOneAsync(CancellationToken.None));
        var request = await ReadReopenRequestAsync(fixture);
        keys.Available = true;
        using var response = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId), request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ready", (await response.Content.ReadFromJsonAsync<ClearinghouseAccountReadinessDto>())!.Disposition);
        using var repeat = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId), request);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Empty(connector.Dispatches);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Action == "billing-clearinghouse.preflight-reopened"));
        Assert.Equal(ClearinghouseDispatchState.Queued, (await db.ClearinghouseDispatches.SingleAsync()).State);
        Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
        Assert.True(await CreateIsolationWorker(fixture.Factory, keys, connector).ProcessOneAsync(CancellationToken.None));
        Assert.Single(connector.Dispatches);
    }

    [Fact]
    public async Task PreflightReopenRefusesAMissingKeyOrStaleRevisionWithoutMutation()
    {
        var keys = new ControlledPreflightKeys();
        await using var fixture = await CreatePreflightFixtureAsync(keys);
        Assert.True(await CreateIsolationWorker(fixture.Factory, keys, new IsolationRecordingConnector()).ProcessOneAsync(CancellationToken.None));
        var request = await ReadReopenRequestAsync(fixture);
        using var missing = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId), request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missing.StatusCode);
        var calls = keys.Calls;
        using var stale = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId),
            request with { ExpectedReadinessRevision = request.ExpectedReadinessRevision - 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal(calls, keys.Calls);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Equal(1, (await db.ClearinghouseDispatchReadiness.SingleAsync()).FailureCount);
        Assert.Empty(await db.AuditEvents.Where(x => x.Action == "billing-clearinghouse.preflight-reopened").ToListAsync());
    }

    private static string PreflightReopenRoute(Guid accountId) => $"/api/v1/admin/clearinghouse/accounts/{accountId}/preflight/reopen";

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PreflightLostLeaseRefusesBothDeferredAndSendingCommits(bool keyAvailable)
    {
        var keys = new ControlledPreflightKeys { Available = keyAvailable };
        await using var fixture = await CreatePreflightFixtureAsync(keys);
        var connector = new IsolationRecordingConnector();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateIsolationWorker(fixture.Factory, keys, connector,
            coordination: new LostPreflightCoordination()).ProcessOneAsync(CancellationToken.None));
        await AssertNoPreflightEffectsAsync(fixture, connector);
    }

    [Fact]
    public async Task PreflightStaleMissingKeyDoesNotDeferAnAccountThatChangedDuringPreparation()
    {
        var keys = new ControlledPreflightKeys();
        await using var fixture = await CreatePreflightFixtureAsync(keys);
        keys.BeforeResolve = () =>
        {
            using var db = fixture.Factory.OpenDatabase();
            var account = db.ClearinghouseAccounts.Single();
            account.SecretReference = "CLAIMMD_SANDBOX_KEY_NEW_BINDING"; account.Revision++;
            db.SaveChanges();
        };
        var connector = new IsolationRecordingConnector();
        Assert.False(await CreateIsolationWorker(fixture.Factory, keys, connector).ProcessOneAsync(CancellationToken.None));
        await AssertNoPreflightEffectsAsync(fixture, connector);
    }

    private sealed class LostPreflightCoordination : IDemoWorkerResetCoordination, IAccountPreflightLease
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token) => operation(token);
        public Task<T> RunDispatchAsync<T>(Guid id, Func<CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token) => operation(token);
        public Task<T> RunAccountPreflightAsync<T>(int agencyId, Guid accountId, Func<IAccountPreflightLease, CancellationToken, Task<T>> operation,
            T unavailableResult, CancellationToken token) => operation(this, token);
        public Task VerifyAsync(CancellationToken token) => throw new InvalidOperationException("Synthetic lost preflight lease.");
    }

    [Fact]
    public async Task PreflightReopenRejectsBillingOnlyAndForeignIdsBeforeResolvingKeys()
    {
        var keys = new ControlledPreflightKeys();
        await using var fixture = await CreatePreflightFixtureAsync(keys);
        Assert.True(await CreateIsolationWorker(fixture.Factory, keys, new IsolationRecordingConnector()).ProcessOneAsync(CancellationToken.None));
        var request = await ReadReopenRequestAsync(fixture);
        var foreign = await fixture.AddAccountAsync("FOREIGN", "FOREIGN", foreign: true);
        var calls = keys.Calls;
        using var foreignResponse = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(foreign), request);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var biller = await db.Users.SingleAsync(row => row.Id == fixture.Actors.BillerId);
            biller.Permissions = UserPermissions.Billing;
            await db.SaveChangesAsync();
        }
        using var forbidden = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId), request);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(calls, keys.Calls);
    }

    [Theory]
    [InlineData("actor", HttpStatusCode.Unauthorized)]
    [InlineData("account", HttpStatusCode.Conflict)]
    public async Task PreflightReopenRechecksAuthorityAndBindingAfterPreparation(string change, HttpStatusCode expected)
    {
        var keys = new ControlledPreflightKeys(); var wrapper = new ControlledPreflightWrapper();
        await using var fixture = await CreatePreflightFixtureAsync(keys, wrapper);
        Assert.True(await CreateIsolationWorker(fixture.Factory, keys, new IsolationRecordingConnector()).ProcessOneAsync(CancellationToken.None));
        var request = await ReadReopenRequestAsync(fixture); keys.Available = true;
        wrapper.BeforeWrap = async () =>
        {
            await using var db = fixture.Factory.OpenDatabase();
            if (change == "actor") (await db.Users.SingleAsync(row => row.Id == fixture.Actors.BillerId)).Permissions = UserPermissions.Billing;
            else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClearinghouseAccounts SET SecretReference = {"CLAIMMD_SANDBOX_KEY_CHANGED"} WHERE Id = {fixture.AccountId}");
            await db.SaveChangesAsync();
        };
        using var response = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId), request);
        Assert.Equal(expected, response.StatusCode);
        await using var saved = fixture.Factory.OpenDatabase();
        Assert.Equal(1, (await saved.ClearinghouseDispatchReadiness.SingleAsync()).Revision);
        Assert.Empty(await saved.AuditEvents.Where(row => row.Action == "billing-clearinghouse.preflight-reopened").ToListAsync());
    }

    [Theory]
    [InlineData(ClearinghouseDispatchState.Sending)]
    [InlineData(ClearinghouseDispatchState.OutcomeUnknown)]
    public async Task PreflightReopenNeverReplaysAnUncertainDispatch(ClearinghouseDispatchState state)
    {
        var keys = new ControlledPreflightKeys();
        await using var fixture = await CreatePreflightFixtureAsync(keys);
        var connector = new IsolationRecordingConnector();
        Assert.True(await CreateIsolationWorker(fixture.Factory, keys, connector).ProcessOneAsync(CancellationToken.None));
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var dispatch = await db.ClearinghouseDispatches.SingleAsync();
            dispatch.State = ClearinghouseDispatchState.Sending; dispatch.Revision++; await db.SaveChangesAsync();
            if (state == ClearinghouseDispatchState.OutcomeUnknown)
            { dispatch.State = state; dispatch.Revision++; await db.SaveChangesAsync(); }
        }
        keys.Available = true;
        using var response = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId), await ReadReopenRequestAsync(fixture));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await CreateIsolationWorker(fixture.Factory, keys, connector).ProcessOneAsync(CancellationToken.None));
        Assert.Empty(connector.Dispatches);
        await using var saved = fixture.Factory.OpenDatabase();
        Assert.Equal(state, (await saved.ClearinghouseDispatches.SingleAsync()).State);
        Assert.Empty(await saved.ClearinghouseDispatchAttempts.ToListAsync());
    }

    private static async Task<ReopenClearinghousePreflightRequest> ReadReopenRequestAsync(Fixture fixture)
    {
        await using var db = fixture.Factory.OpenDatabase();
        var readiness = await db.ClearinghouseDispatchReadiness.SingleAsync();
        var account = await db.ClearinghouseAccounts.SingleAsync();
        return new(readiness.Revision, account.Revision);
    }

    private static async Task<Fixture> CreatePreflightFixtureAsync(ControlledPreflightKeys keys,
        IKeyWrapper? wrapper = null, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null,
        bool sqlServer = false, IDemoWorkerResetCoordination? coordination = null)
    {
        var fixture = await Fixture.CreateAsync(sqlServer: sqlServer, interceptor: interceptor,
            keyWrapper: wrapper, keySource: keys, coordination: coordination);
        try
        {
            await using (var db = fixture.Factory.OpenDatabase())
            {
                var account = await db.ClearinghouseAccounts.SingleAsync();
                account.SecretReference = "CLAIMMD_SANDBOX_KEY_PREFLIGHT"; account.Revision++;
                await db.SaveChangesAsync();
            }
            await fixture.GenerateAsync(fixture.AccountId);
            (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
                new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private static async Task AssertNoPreflightEffectsAsync(Fixture fixture, IsolationRecordingConnector connector)
    {
        Assert.Empty(connector.Dispatches);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Empty(await db.ClearinghouseDispatchReadiness.ToListAsync());
        Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
        Assert.Empty(await db.AuditEvents.Where(x => x.Action.StartsWith("billing-clearinghouse.preflight-")).ToListAsync());
        Assert.Equal(ClearinghouseDispatchState.Queued, (await db.ClearinghouseDispatches.SingleAsync()).State);
    }

    private sealed class ControlledPreflightKeys : IClaimMdSandboxKeySource
    {
        public bool Available { get; set; }
        public bool UnknownFault { get; set; }
        public Action? BeforeResolve { get; set; }
        public int Calls { get; private set; }
        public string Resolve(string? reference)
        {
            Calls++; BeforeResolve?.Invoke();
            if (UnknownFault) throw new InvalidOperationException("Synthetic unclassified preparation fault.");
            if (!Available) throw new ClaimMdAccountKeyUnavailableException();
            return "SYNTHETIC_ONLY_KEY";
        }
    }

    private sealed class ControlledPreflightWrapper : IKeyWrapper
    {
        private readonly TestKeyWrapper _inner = new();
        public bool Fail { get; set; }
        public Func<Task>? BeforeWrap { get; set; }
        public async Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default)
        {
            if (BeforeWrap is { } before) await before();
            if (Fail) throw new CryptographicException("Synthetic shared wrapping fault.");
            return await _inner.WrapAsync(dataKey, cancellationToken);
        }
        public Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken cancellationToken = default) =>
            _inner.UnwrapAsync(wrappedKey, keyId, cancellationToken);
    }
}
