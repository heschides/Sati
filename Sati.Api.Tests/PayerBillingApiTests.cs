using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.TestFixtures;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Text.Json;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;
public class PayerBillingApiTests
{
    [Theory] [InlineData("billing-only-one")] [InlineData("case-manager-one")] [InlineData("supervisor-one")]
    public async Task NonAdministratorsCannotPublishEvenWithCompleteConfiguration(string username)
    {
        await using var f = new SatiApiFactory(); using var client = await f.CreateAuthenticatedClientAsync(username);
        using var response = await client.PostAsJsonAsync("/api/v1/billing/payer-configurations", Request());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = f.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Empty(await db.PayerBillingConfigurationVersions.ToListAsync());
        Assert.Empty(await db.AuditEvents.Where(a => a.Action == "billing-payer-configuration.published").ToListAsync());
    }
    [Fact] public async Task PublishIsIdempotentStaleUpdatesConflictAndOtherAgencyCannotReplayOrDiscover()
    {
        await using var f = new SatiApiFactory(); using var admin = await f.CreateAuthenticatedClientAsync("admin-one");
        using var foreign = await f.CreateAuthenticatedClientAsync("admin-two"); var request = Request();
        var first = await Publish(admin, request); var replay = await Publish(admin, request); Assert.Equal(first.VersionId, replay.VersionId);
        using var stale = await admin.PostAsJsonAsync("/api/v1/billing/payer-configurations", request with { ChangeId = Guid.NewGuid(), Configuration = request.Configuration with { EffectiveOn = new(2026, 5, 1) } });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(PayerBillingRules.RevisionCode, (await stale.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        using var reuse = await foreign.PostAsJsonAsync("/api/v1/billing/payer-configurations", request); Assert.Equal(HttpStatusCode.Conflict, reuse.StatusCode);
        Assert.Empty((await foreign.GetFromJsonAsync<List<PayerBillingVersionDto>>("/api/v1/billing/payer-configurations"))!);
        Assert.Single((await admin.GetFromJsonAsync<List<PayerBillingVersionDto>>("/api/v1/billing/payer-configurations"))!);
        await using var scope = f.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Single(await db.AuditEvents.Where(a => a.Action == "billing-payer-configuration.published").ToListAsync());
    }
    [Fact] public async Task AdministrationWithoutBillingCanConfigureButCannotPreviewConsumers()
    {
        await using var f = new SatiApiFactory(); var note = await f.CreateNoteInStatusAsync(6);
        using var admin = await f.CreateAuthenticatedClientAsync("admin-without-billing-one");
        var version = await Publish(admin, Request());
        Assert.Single((await admin.GetFromJsonAsync<List<PayerBillingVersionDto>>("/api/v1/billing/payer-configurations"))!);
        using var denied = await admin.PostAsJsonAsync($"/api/v1/billing/payer-claims/{note}/preview", PayerBillingSynthetic.Preparation(version, 101));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
    [Fact] public async Task PreviewAndClaimCreationFreezeFieldsAndRequireCoverageAfterProfileActivation()
    {
        await using var f = new SatiApiFactory(); var note = await f.CreateNoteInStatusAsync(6); var foreignNote = await f.CreateNoteInStatusAsync(6, 201);
        using var admin = await f.CreateAuthenticatedClientAsync("admin-one"); using var biller = await f.CreateAuthenticatedClientAsync("billing-only-one");
        var version = await Publish(admin, Request()); var preparation = PayerBillingSynthetic.Preparation(version, 101);
        using var foreign = await biller.PostAsJsonAsync($"/api/v1/billing/payer-claims/{foreignNote}/preview", preparation);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var missing = await biller.PostAsJsonAsync("/api/v1/billing/claim-lines", new CreateClaimLineRequest(note, false, null)); Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        using var noCoverage = await biller.PostAsJsonAsync($"/api/v1/billing/payer-claims/{note}/preview", preparation with { Authorization = null });
        noCoverage.EnsureSuccessStatusCode(); var invalid = (await noCoverage.Content.ReadFromJsonAsync<PayerClaimPreviewDto>())!;
        Assert.False(invalid.IsReady); Assert.Contains(invalid.Errors, e => e.Field == "Authorization.Reference");
        using var previewResponse = await biller.PostAsJsonAsync($"/api/v1/billing/payer-claims/{note}/preview", preparation);
        previewResponse.EnsureSuccessStatusCode(); var preview = (await previewResponse.Content.ReadFromJsonAsync<PayerClaimPreviewDto>())!;
        Assert.True(preview.IsReady, string.Join("; ", preview.Errors.Select(e => e.Message)));
        using var creation = await biller.PostAsJsonAsync("/api/v1/billing/claim-lines", new CreateClaimLineRequest(note, false, null) { PayerPreparation = preparation });
        Assert.True(creation.IsSuccessStatusCode, await creation.Content.ReadAsStringAsync());
        var line = (await creation.Content.ReadFromJsonAsync<ClaimLineDto>())!;
        await using var scope = f.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var json = (await db.ClaimLines.AsNoTracking().SingleAsync(c => c.Id == line.Id)).ClaimSnapshotJson;
        var snapshot = ProfessionalClaimSnapshotCodec.Deserialize(json);
        Assert.Equal(2, snapshot.Version); Assert.Equal("MEMCD", snapshot.PayerId); Assert.Equal("AB12-123", snapshot.PayerInputs!.ConfigurationVersion.Configuration.FacilityId);
        Assert.Equal(15, snapshot.PayerInputs.VerifiedByUserId);
        await Publish(admin, Request() with { ExpectedRevision = 1, Configuration = version.Configuration with { EffectiveOn = new(2026, 8, 4), FacilityId = "ZZ99-987" } });
        Assert.Equal(json, (await db.ClaimLines.SingleAsync(c => c.Id == line.Id)).ClaimSnapshotJson);
        var stored = await db.PayerBillingConfigurationVersions.FirstAsync();
        db.Remove(stored); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task ConfigurationChangedSincePreviewCannotBeUsedToCreateClaim()
    {
        await using var f = new SatiApiFactory(); var note = await f.CreateNoteInStatusAsync(6);
        using var admin = await f.CreateAuthenticatedClientAsync("admin-one"); using var biller = await f.CreateAuthenticatedClientAsync("billing-only-one");
        var first = await Publish(admin, Request());
        await Publish(admin, Request() with { ExpectedRevision = 1, Configuration = first.Configuration with { EffectiveOn = new(2026, 8, 1), FacilityId = "ZZ99-987" } });
        using var stale = await biller.PostAsJsonAsync("/api/v1/billing/claim-lines", new CreateClaimLineRequest(note, false, null) { PayerPreparation = PayerBillingSynthetic.Preparation(first, 101) });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var scope = f.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.DoesNotContain(await db.ClaimLines.ToListAsync(), l => l.NoteId == note);
        Assert.DoesNotContain(await db.AuditEvents.ToListAsync(), a => a.Action == "billing-claim-line.created" && a.ResourceId == note.ToString());
    }
    [Fact] public async Task CurrentDatabasePermissionsDefeatAStaleAdminToken()
    {
        await using var f = new SatiApiFactory(); using var admin = await f.CreateAuthenticatedClientAsync("admin-one");
        await using (var scope = f.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>(); var user = await db.Users.SingleAsync(u => u.Id == 11); user.Permissions = UserPermissions.Billing; await db.SaveChangesAsync(); }
        using var response = await admin.PostAsJsonAsync("/api/v1/billing/payer-configurations", Request()); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Fact] public async Task AuditFailureCannotLeaveAConfigurationVersionBehind()
    {
        var failure = new AuditFailure(); await using var f = new SatiApiFactory { DatabaseCommandInterceptor = failure };
        using var admin = await f.CreateAuthenticatedClientAsync("admin-one"); failure.Armed = true;
        using var response = await admin.PostAsJsonAsync("/api/v1/billing/payer-configurations", Request());
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); Assert.True(failure.Fired); failure.Armed = false;
        await using var scope = f.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Empty(await db.PayerBillingConfigurationVersions.ToListAsync());
        Assert.DoesNotContain(await db.AuditEvents.ToListAsync(), a => a.Action == "billing-payer-configuration.published");
    }
    [Theory] [InlineData("MEMCD")] [InlineData("memcd")] [InlineData("MCDME")] [InlineData(" SKME0 ")]
    public async Task MaineRoutingCannotDisablePriorAuthorizationByDeclaringAnOtherProfile(string routing)
    {
        await using var f = new SatiApiFactory(); using var admin = await f.CreateAuthenticatedClientAsync("admin-one");
        var request = Request(); request = request with { Configuration = request.Configuration with
            { Kind = PayerBillingProfileKind.OtherProfessional, RequiresAuthorization = false, PayerId = routing } };
        using var response = await admin.PostAsJsonAsync("/api/v1/billing/payer-configurations", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Kind", await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task ConfiguredReplacementRetainsStandingInputsOriginalFileAndEncryptedReceipts()
    {
        await using var database = new SyntheticPipelineDatabase();
        await database.InitializeAsync();
        await using var f = new SyntheticPipelineFactory(database) { EnableSyntheticDispatch = true, DisableDispatchWorker = true };
        var actors = await f.SeedAsync();
        var periodId = await JoinedBillingPipelineAcceptanceTests.PrepareSubmittedPeriodAsync(f, actors);
        await using (var db = f.OpenDatabase())
        {
            (await db.Users.SingleAsync(u => u.Id == actors.BillerId)).Permissions = UserPermissions.AllAgencyPermissions;
            await db.SaveChangesAsync();
        }
        using var biller = await f.SignInAsync("synthetic-biller");
        var version = await Publish(biller, Request());
        var accountId = Guid.NewGuid(); int lineId; string originalLine;
        // Private fixture setup happens before any file or receipt exists. HTTP creation of
        // configured claims is covered separately; this probe exercises retained financial history.
        await using (var db = f.OpenDatabase())
        {
            var lines = await db.ClaimLines.OrderBy(l => l.Id).ToListAsync();
            foreach (var line in lines)
            {
                var subscriber = ProfessionalClaimSnapshotCodec.Deserialize(line.ClaimSnapshotJson);
                line.ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(PayerBillingRules.Freeze(subscriber,
                    version, PayerBillingSynthetic.Preparation(version, subscriber.PersonId), actors.BillerId, DateTime.UtcNow, line.DateOfService));
            }
            lineId = lines[0].Id;
            db.ClearinghouseAccounts.Add(new ClearinghouseAccount
            {
                Id = accountId, AgencyId = actors.AgencyId, ConnectorKind = TradingPartnerKind.ClaimMd,
                IsTest = true, IsEnabled = true, ExternalAccountNumber = "ACCT123", ClaimNamespace = "DEMO01",
                TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(); originalLine = JsonSerializer.Serialize(lines[0]);
        }
        var generationRequest = new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = accountId };
        using var generated = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/edi", generationRequest);
        Assert.True(generated.IsSuccessStatusCode, await generated.Content.ReadAsStringAsync());
        var originalFile = (await generated.Content.ReadFromJsonAsync<EdiFileDto>())!;
        using var denied = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/mock-clearinghouse",
            new MockClearinghouseRequest(MockClearinghouseScenario.DeniedMissingInformation));
        Assert.True(denied.IsSuccessStatusCode, await denied.Content.ReadAsStringAsync());
        Dictionary<Guid, string> receipts;
        await using (var db = f.OpenDatabase())
        {
            receipts = (await db.ClearinghouseResponseReceipts.AsNoTracking().ToListAsync()).ToDictionary(r => r.Id, r => JsonSerializer.Serialize(r));
            Assert.NotEmpty(receipts);
            var agency = await db.Agencies.SingleAsync(a => a.Id == actors.AgencyId); agency.Npi = null;
            (await db.People.SingleAsync(p => p.Id == actors.FirstPersonId)).BillingStreet = "11 Changed Street";
            await db.SaveChangesAsync();
        }
        await Publish(biller, Request() with { ExpectedRevision = 1, Configuration = version.Configuration with
            { EffectiveOn = DateTime.Today, RenderingProviderNpi = "1234567893", FacilityId = "ZZ99-987" } });
        using var corrected = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/corrections",
            new CreateClaimCorrectionRequest(lineId, ClaimCorrectionAction.Replace, "Correct synthetic subscriber address"));
        Assert.True(corrected.IsSuccessStatusCode, await corrected.Content.ReadAsStringAsync());
        using var correctionFileResponse = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/corrections/edi",
            generationRequest with { IdempotencyKey = Guid.NewGuid().ToString("N") });
        Assert.True(correctionFileResponse.IsSuccessStatusCode, await correctionFileResponse.Content.ReadAsStringAsync());
        var correctionFile = (await correctionFileResponse.Content.ReadFromJsonAsync<EdiFileDto>())!;
        Assert.Contains("*11:B:7*", correctionFile.Content); Assert.Contains("REF*F8*MOCK", correctionFile.Content);
        Assert.Contains("REF*G2*AB12-123~", correctionFile.Content); Assert.DoesNotContain("ZZ99-987", correctionFile.Content);
        await using var finalDb = f.OpenDatabase();
        var original = await finalDb.ClaimLines.AsNoTracking().SingleAsync(l => l.Id == lineId);
        Assert.Equal(originalLine, JsonSerializer.Serialize(original));
        var correction = Assert.Single(await finalDb.ClaimCorrections.AsNoTracking().ToListAsync());
        var snapshot = ProfessionalClaimSnapshotCodec.Deserialize(correction.ClaimSnapshotJson);
        Assert.Equal(version.VersionId, snapshot.PayerInputs!.ConfigurationVersion.VersionId);
        Assert.Equal("11 Changed Street", snapshot.SubscriberStreet);
        Assert.Equal("1999999984", correction.RenderingProviderNpi);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original.ClaimSnapshotJson!))).ToLowerInvariant(), snapshot.PayerInputs.PreviousSnapshotSha256);
        Assert.Equal(originalFile.Content, (await finalDb.EdiGenerations.AsNoTracking().SingleAsync(g => g.Id == correction.CorrectsEdiGenerationId)).Content);
        foreach (var receipt in await finalDb.ClearinghouseResponseReceipts.AsNoTracking().ToListAsync()) Assert.Equal(receipts[receipt.Id], JsonSerializer.Serialize(receipt));
        using var replay = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/edi", generationRequest);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal(originalFile, await replay.Content.ReadFromJsonAsync<EdiFileDto>());
    }
    private sealed class AuditFailure : DbCommandInterceptor
    {
        public bool Armed; public bool Fired;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO \"AuditEvents\"", StringComparison.Ordinal)) { Fired = true; throw new InvalidOperationException("Synthetic payer audit failure"); }
            return base.ReaderExecutingAsync(command, data, result, ct);
        }
    }
    private static PublishPayerBillingRequest Request() => new(Guid.NewGuid(), 0, PayerBillingSynthetic.Configuration());
    private static async Task<PayerBillingVersionDto> Publish(HttpClient client, PublishPayerBillingRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/billing/payer-configurations", request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PayerBillingVersionDto>())!;
    }
}
