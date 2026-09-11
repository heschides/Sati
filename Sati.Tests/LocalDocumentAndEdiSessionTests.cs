using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Edi;
using Sati.Forms;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class LocalDocumentAndEdiSessionTests
{
    [Theory]
    [InlineData("read", false)]
    [InlineData("read", true)]
    [InlineData("publish", false)]
    [InlineData("publish", true)]
    [InlineData("edi", false)]
    [InlineData("edi", true)]
    public async Task RevokedOrDisabledActorCannotUseDocumentsOrEdi(string operation, bool disable)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var actor = fixture.CaseManagerOne;
        actor.Permissions = UserPermissions.AllAgencyPermissions;
        actor.Role = UserRole.Admin;
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var stored = await setup.Users.SingleAsync(u => u.Id == actor.Id);
            stored.Permissions = actor.Permissions;
            stored.Role = actor.Role;
            stored.IsEnabled = !disable;
            if (!disable) stored.SecurityVersion++;
            await setup.SaveChangesAsync();
        }
        var session = new SessionService();
        session.SetUser(actor);
        var service = new DocumentTemplateService(fixture.Factory, session, new DocumentTemplatePdfComposer());
        await Assert.ThrowsAsync<SessionExpiredException>(async () =>
        {
            if (operation == "read") await service.GetVersionsAsync(AnnualDocumentKind.PrivacyPractices);
            else if (operation == "publish")
                await service.PublishAsync(AnnualDocumentKind.PrivacyPractices, SatiDefaultDocumentTemplates.PrivacyPracticesBody);
            else
                // Must reject before reading/replaying an EDI generation or looking up
                // the period. No real file, billing period, or external transmission is used.
                await new EdiService(fixture.Factory, session).GenerateAndSaveAsync(-1, true, Guid.NewGuid().ToString("N"));
        });
        Assert.True(session.HasSessionEnded);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.False(await verify.DocumentTemplates.AnyAsync(t => t.AgencyId == actor.AgencyId));
        Assert.False(await verify.EdiGenerations.AnyAsync());
    }

    [Fact]
    public async Task CurrentAdministratorCanStillPublishAndReadTemplates()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var actor = fixture.CaseManagerOne;
        actor.Permissions = UserPermissions.Administration;
        actor.Role = UserRole.Admin;
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var stored = await setup.Users.SingleAsync(u => u.Id == actor.Id);
            stored.Permissions = actor.Permissions;
            stored.Role = actor.Role;
            await setup.SaveChangesAsync();
        }
        var session = new SessionService();
        session.SetUser(actor);
        var service = new DocumentTemplateService(fixture.Factory, session, new DocumentTemplatePdfComposer());
        var published = await service.PublishAsync(AnnualDocumentKind.PrivacyPractices, SatiDefaultDocumentTemplates.PrivacyPracticesBody);
        Assert.Contains(await service.GetVersionsAsync(AnnualDocumentKind.PrivacyPractices), t => t.Id == published.Id);
        Assert.False(session.HasSessionEnded);
    }

    [Fact]
    public async Task EdiExportRequiresIndependentBillingPermission()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new EdiService(fixture.Factory, session)
            .GenerateAndSaveAsync(-1, true, Guid.NewGuid().ToString("N")));
        Assert.False(session.HasSessionEnded);
    }

    [Fact]
    public async Task BillingOnlyActorReachesThePeriodLookupWithoutCaseManagementOrAdministration()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        fixture.CaseManagerOne.Permissions = UserPermissions.Billing;
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var stored = await setup.Users.SingleAsync(u => u.Id == fixture.CaseManagerOne.Id);
            stored.Permissions = UserPermissions.Billing;
            await setup.SaveChangesAsync();
        }
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EdiService(fixture.Factory, session).GenerateAndSaveAsync(-1, true, Guid.NewGuid().ToString("N")));
        Assert.Equal("Billing period -1 not found.", failure.Message);
        Assert.False(session.HasSessionEnded);
    }
}
