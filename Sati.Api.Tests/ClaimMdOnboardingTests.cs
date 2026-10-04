using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ClaimMdOnboardingTests
{
    private const string Route = "/api/v1/admin/clearinghouse/claimmd-test-accounts";
    private const string Action = "billing-clearinghouse.claimmd-test-account-onboarded";

    [Fact]
    public async Task DemoAdminCreatesAccountAndBothReviewedCursorsAtomicallyBeforeTransportIsEnabled()
    {
        await using var factory = new SatiApiFactory();
        using var signedIn = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var demo = DemoHost(factory);
        using var client = Client(demo, signedIn);
        var request = ValidRequest();

        using var created = await client.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = (await created.Content.ReadFromJsonAsync<ClaimMdTestAccountOnboardingDto>())!;
        Assert.Equal(request.AccountId, result.AccountId);
        Assert.Equal("0", result.StatusCursor);
        Assert.Equal("52", result.EraCursor);
        Assert.Equal(0, result.Revision);
        Assert.False(result.AlreadyProvisioned);
        Assert.DoesNotContain("SecretReference", await created.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var replay = await client.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<ClaimMdTestAccountOnboardingDto>())!.AlreadyProvisioned);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var account = await db.ClearinghouseAccounts.SingleAsync();
        Assert.Equal(1, account.AgencyId);
        Assert.True(account.IsEnabled && account.IsTest);
        Assert.Equal(TradingPartnerKind.ClaimMd, account.ConnectorKind);
        Assert.Equal(request.SecretReference, account.SecretReference);
        Assert.Equal(TradingPartnerProfile.CurrentVersion, account.TradingPartnerProfileVersion);
        var cursors = await db.ClearinghouseFeedCheckpoints.OrderBy(row => row.FeedKind).ToListAsync();
        Assert.Equal(2, cursors.Count);
        Assert.Equal(ClearinghouseFeedKind.Status, cursors[0].FeedKind);
        Assert.Equal("0", cursors[0].Cursor);
        Assert.Equal(ClearinghouseFeedKind.Era, cursors[1].FeedKind);
        Assert.Equal("52", cursors[1].Cursor);
        Assert.All(cursors, row => Assert.Equal(account.Id, row.AccountId));
        var audit = Assert.Single(await factory.GetAuditEventsAsync(Action));
        Assert.Equal(account.AgencyId, audit.AgencyId);
        Assert.DoesNotContain(request.SecretReference, audit.MetadataJson);
        Assert.Contains(request.StatusFeed.EvidenceReference, audit.MetadataJson);
        Assert.Contains(request.EraFeed.EvidenceReference, audit.MetadataJson);
    }

    [Fact]
    public async Task NonAdminAndRevokedAdminCannotOnboardOrLeavePartialRows()
    {
        await using var factory = new SatiApiFactory();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var demo = DemoHost(factory);
        using var supervisorClient = Client(demo, supervisor);
        using var adminClient = Client(demo, admin);
        var request = ValidRequest();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await supervisorClient.PostAsJsonAsync(Route, request)).StatusCode);
        await factory.ChangeUserPermissionsAsync(11, UserPermissions.None);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await adminClient.PostAsJsonAsync(Route, request)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Empty(await db.ClearinghouseAccounts.ToListAsync());
        Assert.Empty(await db.ClearinghouseFeedCheckpoints.ToListAsync());
        Assert.Empty(await factory.GetAuditEventsAsync(Action));
    }

    [Fact]
    public async Task ForeignAgencyAccountNumberNamespaceOrSecretReferenceCannotBeReused()
    {
        await using var factory = new SatiApiFactory();
        using var signedIn = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var demo = DemoHost(factory);
        using var client = Client(demo, signedIn);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var agency = new ServerAgency { Name = "Other Synthetic Agency" };
            db.Agencies.Add(agency);
            await db.SaveChangesAsync();
            db.ClearinghouseAccounts.Add(new ClearinghouseAccount
            {
                Id = Guid.NewGuid(), AgencyId = agency.Id, ConnectorKind = TradingPartnerKind.ClaimMd,
                IsTest = true, IsEnabled = false, ExternalAccountNumber = "VENDORACCOUNT",
                ClaimNamespace = "FOREIGN", SecretReference = "CLAIMMD_SANDBOX_KEY_FOREIGN",
                TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var basis = ValidRequest();
        foreach (var request in new[]
        {
            basis with { ExternalAccountNumber = "VENDORACCOUNT" },
            basis with { ClaimNamespace = "FOREIGN" },
            basis with { SecretReference = "CLAIMMD_SANDBOX_KEY_FOREIGN" }
        })
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Route, request)).StatusCode);
        await using var readScope = factory.Services.CreateAsyncScope();
        var read = readScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Single(await read.ClearinghouseAccounts.ToListAsync());
        Assert.Empty(await read.ClearinghouseFeedCheckpoints.ToListAsync());
        Assert.Empty(await factory.GetAuditEventsAsync(Action));
    }

    [Fact]
    public async Task ChangedReplayAndUnsupportedEvidenceAreRefusedWithoutNewRows()
    {
        await using var factory = new SatiApiFactory();
        using var signedIn = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var demo = DemoHost(factory);
        using var client = Client(demo, signedIn);
        var request = ValidRequest();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Route, request)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Route,
            request with { EraFeed = request.EraFeed with { Cursor = "53" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Route,
            request with { SecretReference = "CLAIMMD_SANDBOX_KEY_ROTATED" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route,
            ValidRequest() with { StatusFeed = new("0", ClaimMdFeedReviewKind.ExistingReconciled, "REVIEW-STATUS-2026") })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route,
            ValidRequest() with { EraFeed = new("052", ClaimMdFeedReviewKind.ExistingReconciled, "REVIEW-ERA-2026") })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route,
            ValidRequest() with { RemoteClaimIdOnlyDuplicateFieldConfirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route,
            ValidRequest() with { ExpectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route,
            ValidRequest() with { DedicatedTestAccountConfirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route,
            ValidRequest() with { EraFeed = new("52", ClaimMdFeedReviewKind.ExistingReconciled,
                request.StatusFeed.EvidenceReference) })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Single(await db.ClearinghouseAccounts.ToListAsync());
        Assert.Equal(2, await db.ClearinghouseFeedCheckpoints.CountAsync());
        Assert.Single(await factory.GetAuditEventsAsync(Action));

        // Even the identical original request stops being an idempotent replay after
        // an authorized account revision; setup must not overwrite newer routing state.
        var account = await db.ClearinghouseAccounts.SingleAsync();
        account.Revision++;
        account.SecretReference = "CLAIMMD_SANDBOX_KEY_ROTATED";
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Route, request)).StatusCode);
    }

    [Fact]
    public async Task TestingAndProductionProfilesCannotOnboard()
    {
        await using var factory = new SatiApiFactory();
        using var signedIn = await factory.CreateAuthenticatedClientAsync("admin-one");
        var request = ValidRequest();
        Assert.Equal(HttpStatusCode.NotFound, (await signedIn.PostAsJsonAsync(Route, request)).StatusCode);
        using var production = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sati:ExpectedEnvironment"] = "Production", ["Sati:ExpectedDatabaseName"] = "SatiProduction"
            })));
        using var client = Client(production, signedIn);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(Route, request)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApiDbContext>().ClearinghouseAccounts.ToListAsync());
    }

    [SqlServerFact]
    public async Task SqlGlobalLeaseSerializesTwoHostsAndExactReplayAfterRelease()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        await using var firstFactory = new SyntheticPipelineFactory(database);
        await using var secondFactory = new SyntheticPipelineFactory(database);
        await firstFactory.SeedAsync();
        using var signedIn = await firstFactory.SignInAsync("synthetic-biller");
        using var firstHost = DemoHost(firstFactory);
        using var secondHost = DemoHost(secondFactory);
        using var firstClient = Client(firstHost, signedIn);
        using var secondClient = Client(secondHost, signedIn);
        var request = ValidRequest();

        await using (var holdingDb = new ApiDbContext(database.Options()))
        {
            await using var holdingTransaction = await holdingDb.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            await using (var command = holdingDb.Database.GetDbConnection().CreateCommand())
            {
                command.Transaction = holdingTransaction.GetDbTransaction();
                command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
                    "@Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', " +
                    "@LockTimeout = 0; SELECT @result;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@resource";
                parameter.Value = ClaimMdOnboardingCoordination.Resource;
                command.Parameters.Add(parameter);
                Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 0);
            }
            using var busy = await secondClient.PostAsJsonAsync(Route, request);
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
            Assert.Contains("claimmd_onboarding_busy", await busy.Content.ReadAsStringAsync());
            await using var check = new ApiDbContext(database.Options());
            Assert.Empty(await check.ClearinghouseAccounts.ToListAsync());
            Assert.Empty(await check.ClearinghouseFeedCheckpoints.ToListAsync());
            Assert.Empty(await check.AuditEvents.Where(row => row.Action == Action).ToListAsync());
            await holdingTransaction.RollbackAsync();
        }

        Assert.Equal(HttpStatusCode.Created,
            (await firstClient.PostAsJsonAsync(Route, request)).StatusCode);
        using var replay = await secondClient.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<ClaimMdTestAccountOnboardingDto>())!.AlreadyProvisioned);
        await using var read = new ApiDbContext(database.Options());
        Assert.Single(await read.ClearinghouseAccounts.ToListAsync());
        Assert.Equal(2, await read.ClearinghouseFeedCheckpoints.CountAsync());
        Assert.Single(await read.AuditEvents.Where(row => row.Action == Action).ToListAsync());
    }

    private static ClaimMdTestAccountOnboardingRequest ValidRequest() => new(
        Guid.NewGuid(), 0, "VENDORNEW", "SATITEST", "CLAIMMD_SANDBOX_KEY_SATI_TEST",
        true, "TESTACCOUNT-20261003", true, "DUPLICATE-SETTING-20261003",
        new("0", ClaimMdFeedReviewKind.VerifiedEmpty, "REVIEW-STATUS-20261003"),
        new("52", ClaimMdFeedReviewKind.ExistingReconciled, "REVIEW-ERA-20261003"));

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> DemoHost(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sati:ExpectedEnvironment"] = "Demo", ["Sati:ExpectedDatabaseName"] = "SatiDemo"
            })));

    private static HttpClient Client(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,
        HttpClient signedIn)
    {
        var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        return client;
    }
}
