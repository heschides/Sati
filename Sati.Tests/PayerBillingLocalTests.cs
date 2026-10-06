using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Helpers;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Sati.Services.Billing;
using Sati.TestFixtures;
using Xunit;

namespace Sati.Tests;
public class PayerBillingLocalTests
{
    [Fact] public async Task LocalPublicationUsesTheSamePermissionsRevisionAndImmutableOwner()
    {
        await using var f = await NoteEntryFixture.CreateAsync(); var service = new BillingService(f.Factory);
        var request = new PublishPayerBillingRequest(Guid.NewGuid(), 0, PayerBillingSynthetic.Configuration());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PublishPayerConfigurationAsync(f.CaseManagerOne.ToAgencyActor(), request));
        var admin = User.Create(41, "synthetic-payer-admin", "Synthetic Admin", "hash", "salt", UserRole.Admin, null, f.CaseManagerOne.AgencyId);
        admin.Permissions = UserPermissions.AllAgencyPermissions;
        await using (var db = f.Factory.CreateDbContext()) { db.Users.Add(admin); await db.SaveChangesAsync(); }
        var first = await service.PublishPayerConfigurationAsync(admin.ToAgencyActor(), request);
        var replay = await service.PublishPayerConfigurationAsync(admin.ToAgencyActor(), request); Assert.Equal(first.VersionId, replay.VersionId);
        await Assert.ThrowsAsync<PayerBillingConflictException>(() => service.PublishPayerConfigurationAsync(admin.ToAgencyActor(), request with
            { ChangeId = Guid.NewGuid(), Configuration = request.Configuration with { EffectiveOn = new(2026, 5, 1) } }));
        var own = await service.GetPayerConfigurationsAsync(admin.ToAgencyActor()); Assert.Single(own);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPayerConfigurationsAsync(admin.ToAgencyActor() with { AgencyId = f.CaseManagerTwo.AgencyId }));
        await using var verify = f.Factory.CreateDbContext(); Assert.Single(await verify.PayerBillingConfigurationVersions.ToListAsync());
        Assert.Single(await verify.AuditEvents.Where(a => a.Action == "billing-payer-configuration.published").ToListAsync());
        (await verify.Users.SingleAsync(u => u.Id == 41)).SecurityVersion++; await verify.SaveChangesAsync();
        await Assert.ThrowsAsync<SessionExpiredException>(() => service.PublishPayerConfigurationAsync(admin.ToAgencyActor(), request));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task LocalCreationFreezesAnEvidencedProfileWithoutRequiringLegacyAgencyDefaults(bool revokeDuringPreparation)
    {
        await using var f = await NoteEntryFixture.CreateAsync();
        f.CaseManagerOne.Permissions = UserPermissions.AllAgencyPermissions;
        await using (var db = f.Factory.CreateDbContext())
        {
            var actor = await db.Users.SingleAsync(u => u.Id == f.CaseManagerOne.Id); actor.Permissions = f.CaseManagerOne.Permissions;
            var person = await db.People.SingleAsync(p => p.Id == f.PersonOneId);
            person.MaineCareId = "987654321"; person.DiagnosisCode = "F89"; person.PlaceOfService = (int)PlaceOfService.Office;
            person.BillingStreet = "10 Claim Street"; person.BillingCity = "Portland"; person.BillingState = "ME"; person.BillingZip = "04101";
            person.BirthDate = new(1990, 2, 3); person.EffectiveDate = null;
            db.Settings.Add(new Settings { AgencyId = actor.AgencyId, BillingComplianceRequirements = BillingComplianceRequirements.None });
            await db.SaveChangesAsync();
        }
        var service = new BillingService(f.Factory); var actorIdentity = f.CaseManagerOne.ToAgencyActor();
        var version = await service.PublishPayerConfigurationAsync(actorIdentity, new(Guid.NewGuid(), 0, PayerBillingSynthetic.Configuration()));
        var note = Note.Create("Synthetic payer preparation", new(2026, 8, 1), NoteStatus.Approved, 15, f.PersonOneId); note.AgencyId = actorIdentity.AgencyId;
        await using (var db = f.Factory.CreateDbContext()) { db.Notes.Add(note); await db.SaveChangesAsync(); }
        var revoke = new RevokeAfterSourceLookup(f.Factory, actorIdentity.UserId);
        if (revokeDuringPreparation)
        {
            await using var seed = f.Factory.CreateDbContext();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(seed.Database.GetDbConnection()).AddInterceptors(revoke).Options;
            service = new BillingService(new NoteEntryFixture.NoteEntryContextFactory(options));
        }
        var preparation = PayerBillingSynthetic.Preparation(version, f.PersonOneId);
        var preview = await service.PreviewPayerClaimAsync(actorIdentity, note.Id, preparation);
        Assert.True(preview.IsReady, string.Join("; ", preview.Errors.Select(e => e.Message)));
        if (revokeDuringPreparation)
        {
            revoke.Armed = true;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreatePreparedClaimLineAsync(actorIdentity, note.Id, preparation));
            Assert.True(revoke.Fired);
            await using var verify = f.Factory.CreateDbContext(); Assert.Empty(await verify.ClaimLines.ToListAsync());
            Assert.DoesNotContain(await verify.AuditEvents.ToListAsync(), a => a.Action == "billing-claim-line.created");
            return;
        }
        var line = await service.CreatePreparedClaimLineAsync(actorIdentity, note.Id, preparation);
        Assert.Equal(25, line.ChargeAmount); var snapshot = ProfessionalClaimSnapshotCodec.Deserialize(line.ClaimSnapshotJson);
        Assert.Equal(version.VersionId, snapshot.PayerInputs!.ConfigurationVersion.VersionId); Assert.Equal("MEMCD", snapshot.PayerId);
        Assert.Equal(snapshot.PayerInputs.ConfigurationVersion.Configuration.RenderingProviderNpi, line.RenderingProviderNpi);
    }
    [Fact] public async Task LocalPreviewRefusesAConsumerWhoseOwnerIsOutsideTheAgency()
    {
        await using var f = await NoteEntryFixture.CreateAsync();
        f.CaseManagerOne.Permissions = UserPermissions.AllAgencyPermissions;
        var note = Note.Create("Synthetic owner-scope denial", new(2026, 8, 1), NoteStatus.Approved, 15, f.PersonOneId);
        note.AgencyId = f.CaseManagerOne.AgencyId;
        await using (var db = f.Factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(u => u.Id == f.CaseManagerOne.Id)).Permissions = f.CaseManagerOne.Permissions;
            var person = await db.People.SingleAsync(p => p.Id == f.PersonOneId);
            db.Entry(person).Property(p => p.UserId).CurrentValue = f.CaseManagerTwo.Id;
            db.Notes.Add(note); await db.SaveChangesAsync();
        }
        var service = new BillingService(f.Factory); var actor = f.CaseManagerOne.ToAgencyActor();
        var version = await service.PublishPayerConfigurationAsync(actor, new(Guid.NewGuid(), 0, PayerBillingSynthetic.Configuration()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewPayerClaimAsync(actor,
            note.Id, PayerBillingSynthetic.Preparation(version, f.PersonOneId)));
        Assert.Equal("The billing note was not found in your agency.", error.Message);
        await using var verify = f.Factory.CreateDbContext();
        Assert.DoesNotContain(await verify.AuditEvents.ToListAsync(), a => a.Action == "billing-payer-claim.previewed");
    }
    private sealed class RevokeAfterSourceLookup(IDbContextFactory<SatiContext> factory, int actorId) : DbCommandInterceptor
    {
        public bool Armed; public bool Fired;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Armed && !Fired && command.CommandText.Contains("FROM \"Notes\"", StringComparison.Ordinal))
            {
                Fired = true; await using var db = factory.CreateDbContext();
                (await db.Users.SingleAsync(u => u.Id == actorId, ct)).Permissions = UserPermissions.None;
                await db.SaveChangesAsync(ct);
            }
            return result;
        }
    }
}
