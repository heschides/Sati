using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services.Billing;
using Sati.Models;
using System.Security;
using Xunit;

namespace Sati.Tests;

public sealed class LocalAccountSessionTests
{
    public static IEnumerable<object[]> ProtectedOperations()
    {
        string[] operations = ["people", "journal-write", "notes", "scratchpad-today", "scratchpad-history",
            "scratchpad-save", "scratchpad-comment", "settings-read", "settings-save", "provider-read",
            "provider-create", "exempt-read", "exempt-create", "incentive-read", "incentive-create",
            "productivity", "billing-loss", "users", "supervisees", "review", "billing", "own-profile"];
        foreach (var operation in operations)
            foreach (var disable in new[] { false, true })
                yield return [operation, disable];
    }

    [Theory]
    [MemberData(nameof(ProtectedOperations))]
    public async Task RevocationStopsProtectedLocalReadsAndWritesAndEndsCapturedSession(string operation, bool disable)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PromoteAsync(fixture, fixture.CaseManagerOne, UserPermissions.AllAgencyPermissions);
        var session = Session(fixture.CaseManagerOne);
        var actor = session.CurrentUser!;
        var scratchpads = new ScratchpadService(fixture.Factory, session);
        var scratchpad = await scratchpads.LoadTodayAsync(actor.Id);
        var editableSettings = await new SettingsService(fixture.Factory, session).LoadAsync();
        var historical = new Scratchpad { UserId = actor.Id, Date = DateTime.Today.AddDays(-1), Content = "synthetic history" };
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            setup.Scratchpad.Add(historical);
            var stored = await setup.Users.SingleAsync(u => u.Id == actor.Id);
            if (disable) stored.IsEnabled = false;
            else stored.SecurityVersion++;
            await setup.SaveChangesAsync();
        }
        var ended = 0;
        session.SessionEnded += (_, _) => ended++;
        var people = new PersonService(fixture.Factory, new StubSettingsService(), session);
        var settings = new SettingsService(fixture.Factory, session);
        var providers = new ProviderService(fixture.Factory, session);
        var exempt = new ExemptDateService(fixture.Factory, session);
        var incentives = new IncentiveService(fixture.Factory, new StubSettingsService(), session);
        var users = new UserService(fixture.Factory, new PasswordHasher(), session);
        await Assert.ThrowsAsync<SessionExpiredException>(async () =>
        {
            switch (operation)
            {
                case "people": await people.GetAllPeopleAsync(actor.Id); break;
                case "journal-write": await people.SaveJournalAsync(fixture.PersonOneId, "rejected synthetic write"); break;
                case "notes": await new NoteService(fixture.Factory, session).GetAllByPersonAsync(fixture.PersonOneId); break;
                case "scratchpad-today": await scratchpads.LoadTodayAsync(actor.Id); break;
                case "scratchpad-history": await scratchpads.GetHistoryAsync(actor.Id); break;
                case "scratchpad-save": scratchpad.Content = "rejected synthetic write"; await scratchpads.SaveAsync(scratchpad); break;
                case "scratchpad-comment": await scratchpads.AddCommentAsync(historical.Id, actor.Id, "spoofed", "rejected"); break;
                case "settings-read": await settings.LoadAsync(); break;
                case "settings-save": await settings.SaveAsync(editableSettings); break;
                case "provider-read": await providers.GetAllAsync(); break;
                case "provider-create": await providers.AddAsync(new Provider { Name = "rejected synthetic provider" }); break;
                case "exempt-read": await exempt.GetByYearAsync(actor.Id, DateTime.Today.Year); break;
                case "exempt-create": await exempt.AddAsync(actor.Id, DateTime.Today); break;
                case "incentive-read": await incentives.GetHistoryAsync(actor.Id); break;
                case "incentive-create": await incentives.GetOrCreateAsync(actor.Id, 9, 2026); break;
                case "productivity": await new ProductivityReportService(fixture.Factory, session).GetUnitsAsync(DateTime.Today.AddMonths(-1), DateTime.Today); break;
                case "billing-loss": await new ConsumerBillingLossReportService(fixture.Factory, session).GetAsync(actor.Id, DateTime.Today.AddMonths(-1), DateTime.Today); break;
                case "users": await users.GetAllAsync(); break;
                case "supervisees": await users.GetSuperviseesAsync(actor.Id); break;
                case "review": await new SupervisorService(fixture.Factory, session).GetPendingNotesAsync(actor.Id); break;
                case "billing": await new BillingService(fixture.Factory, session).GetAllBillingPeriodsAsync(actor.ToAgencyActor()); break;
                case "own-profile": await users.UpdateOwnContactDetailsAsync(actor.ToAgencyActor(), actor); break;
                default: throw new InvalidOperationException(operation);
            }
        });
        Assert.True(session.HasSessionEnded);
        Assert.Equal(1, ended);
        await Assert.ThrowsAsync<SessionExpiredException>(() => scratchpads.GetHistoryAsync(actor.Id));
        Assert.Equal(1, ended);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.DoesNotContain("rejected", (await verify.People.SingleAsync(p => p.Id == fixture.PersonOneId)).Journal ?? "");
        Assert.DoesNotContain("rejected", (await verify.Scratchpad.SingleAsync(p => p.Id == scratchpad.Id)).Content ?? "");
        Assert.Empty(await verify.ScratchpadComments.ToListAsync());
        Assert.Empty(await verify.ExemptDates.ToListAsync());
        Assert.Empty(await verify.Providers.ToListAsync());
        Assert.Empty(await verify.Incentives.ToListAsync());
    }

    [Fact]
    public async Task DisabledLoginIsDeniedAndSuccessfulLoginCopiesOnlySafeCurrentIdentity()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        using var password = Password("Synthetic current password 123!");
        await SetPasswordAsync(fixture, fixture.CaseManagerOne.Id, password);
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var stored = await setup.Users.SingleAsync(u => u.Id == fixture.CaseManagerOne.Id);
            stored.SecurityVersion = 7;
            stored.IsEnabled = false;
            await setup.SaveChangesAsync();
        }
        var auth = new AuthService(fixture.Factory);
        Assert.Null(await auth.AuthenticateAsync(fixture.CaseManagerOne.Username, password));
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var stored = await setup.Users.SingleAsync(u => u.Id == fixture.CaseManagerOne.Id);
            stored.IsEnabled = true;
            await setup.SaveChangesAsync();
        }
        var authenticated = Assert.IsType<User>(await auth.AuthenticateAsync(fixture.CaseManagerOne.Username, password));
        Assert.True(authenticated.IsEnabled);
        Assert.Equal(7, authenticated.SecurityVersion);
        Assert.Empty(authenticated.PasswordHash);
        Assert.Empty(authenticated.Salt);
    }

    [Fact]
    public async Task PasswordChangeAdvancesVersionAuditsAndImmediatelyEndsTheOldSession()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        using var oldPassword = Password("Synthetic old password 123!");
        using var newPassword = Password("Synthetic new password 456!");
        await SetPasswordAsync(fixture, fixture.CaseManagerOne.Id, oldPassword);
        var session = Session(fixture.CaseManagerOne);
        var captured = session.CurrentUser!;
        var service = new UserService(fixture.Factory, new PasswordHasher(), session);
        await service.ChangePasswordAsync(captured, oldPassword, newPassword);
        Assert.True(session.HasSessionEnded);
        Assert.Equal(1, captured.SecurityVersion);
        Assert.Empty(captured.PasswordHash);
        Assert.Empty(captured.Salt);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(2, (await verify.Users.SingleAsync(u => u.Id == captured.Id)).SecurityVersion);
        Assert.Single(await verify.AuditEvents.Where(a => a.Action == "user.password-changed").ToListAsync());
        var auth = new AuthService(fixture.Factory);
        Assert.Null(await auth.AuthenticateAsync(captured.Username, oldPassword));
        var replacement = Assert.IsType<User>(await auth.AuthenticateAsync(captured.Username, newPassword));
        Assert.Equal(2, replacement.SecurityVersion);
        session.SetUser(replacement);
        session.Invalidate(captured);
        Assert.False(session.HasSessionEnded);
        await new ScratchpadService(fixture.Factory, session).LoadTodayAsync(captured.Id);
    }

    [Fact]
    public async Task WrongCurrentPasswordDoesNotChangeVersionOrEndSession()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        using var oldPassword = Password("Synthetic old password 123!");
        using var wrong = Password("Incorrect password 789!");
        await SetPasswordAsync(fixture, fixture.CaseManagerOne.Id, oldPassword);
        var session = Session(fixture.CaseManagerOne);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new UserService(fixture.Factory, new PasswordHasher(), session).ChangePasswordAsync(session.CurrentUser!, wrong, oldPassword));
        Assert.False(session.HasSessionEnded);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(1, (await verify.Users.SingleAsync(u => u.Id == fixture.CaseManagerOne.Id)).SecurityVersion);
        Assert.Empty(await verify.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task DisableReenableAndResetRetainCaseloadButNeverReviveOldSignIn()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var admin = await AddAdministratorAsync(fixture);
        var adminSession = Session(admin);
        var targetSession = Session(fixture.CaseManagerOne);
        var manager = new UserService(fixture.Factory, new PasswordHasher(), adminSession);
        var target = fixture.CaseManagerOne;
        await manager.SetEnabledAsync(admin.ToAgencyActor(), target, false);
        Assert.False(target.IsEnabled);
        Assert.Equal(2, target.SecurityVersion);
        await manager.SetEnabledAsync(admin.ToAgencyActor(), target, false);
        Assert.Equal(2, target.SecurityVersion);
        var listed = await manager.GetAllAsync();
        Assert.Contains(listed, u => u.Id == target.Id && !u.IsEnabled);
        Assert.All(listed, u => { Assert.Empty(u.PasswordHash); Assert.Empty(u.Salt); });
        Assert.Equal(2, (await new PersonService(fixture.Factory, new StubSettingsService(), adminSession).GetAllPeopleAsync(target.Id)).Count);
        await manager.SetEnabledAsync(admin.ToAgencyActor(), target, true);
        Assert.Equal(3, target.SecurityVersion);
        await Assert.ThrowsAsync<SessionExpiredException>(() =>
            new ScratchpadService(fixture.Factory, targetSession).LoadTodayAsync(target.Id));
        using var password = Password("Synthetic reset password 123!");
        await manager.ResetPasswordAsync(admin.ToAgencyActor(), target, password);
        var authenticated = Assert.IsType<User>(await new AuthService(fixture.Factory).AuthenticateAsync(target.Username, password));
        Assert.Equal(4, authenticated.SecurityVersion);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(2, await verify.People.CountAsync(p => p.UserId == target.Id));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(a => a.Action == "user.disabled"));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(a => a.Action == "user.enabled"));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(a => a.Action == "user.password-reset"));
    }

    [Fact]
    public async Task SelfRevokeEndsSessionWithoutRefreshingItsStampAndSelfDisableIsRefused()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PromoteAsync(fixture, fixture.CaseManagerOne, UserPermissions.AllAgencyPermissions);
        var session = Session(fixture.CaseManagerOne);
        var actor = session.CurrentUser!;
        var service = new UserService(fixture.Factory, new PasswordHasher(), session);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetEnabledAsync(actor.ToAgencyActor(), actor, false));
        Assert.False(session.HasSessionEnded);
        await service.RevokeSessionsAsync(actor.ToAgencyActor(), actor);
        Assert.True(session.HasSessionEnded);
        Assert.Equal(1, actor.SecurityVersion);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(2, (await verify.Users.SingleAsync(u => u.Id == actor.Id)).SecurityVersion);
        Assert.Single(await verify.AuditEvents.Where(a => a.Action == "user.sessions-revoked").ToListAsync());
    }

    [Fact]
    public async Task ProfileSaveCannotReplaceLifecycleStateOrPasswordMaterial()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var admin = await AddAdministratorAsync(fixture);
        var service = new UserService(fixture.Factory, new PasswordHasher(), Session(admin));
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var stored = await setup.Users.SingleAsync(u => u.Id == fixture.CaseManagerOne.Id);
            stored.IsEnabled = false;
            stored.SecurityVersion = 9;
            await setup.SaveChangesAsync();
        }
        var stale = fixture.CaseManagerOne;
        stale.Email = "synthetic@example.invalid";
        stale.SetPassword("forged hash", "forged salt");
        await service.UpdateAsync(admin.ToAgencyActor(), stale);
        await using var verify = fixture.Factory.CreateDbContext();
        var saved = await verify.Users.SingleAsync(u => u.Id == stale.Id);
        Assert.False(saved.IsEnabled);
        Assert.Equal(9, saved.SecurityVersion);
        Assert.Equal("hash", saved.PasswordHash);
        Assert.Equal("salt", saved.Salt);
        Assert.Equal(stale.Email, saved.Email);
    }

    [Fact]
    public async Task ExplicitActorBillingAndUserWritesRejectStaleVersionWithoutSessionInjection()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PromoteAsync(fixture, fixture.CaseManagerOne, UserPermissions.AllAgencyPermissions);
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            (await setup.Users.SingleAsync(u => u.Id == fixture.CaseManagerOne.Id)).SecurityVersion++;
            await setup.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<SessionExpiredException>(() => new BillingService(fixture.Factory)
            .GetAllBillingPeriodsAsync(fixture.CaseManagerOne.ToAgencyActor()));
        await Assert.ThrowsAsync<SessionExpiredException>(() => new UserService(fixture.Factory, new PasswordHasher())
            .UpdateOwnContactDetailsAsync(fixture.CaseManagerOne.ToAgencyActor(), fixture.CaseManagerOne));
    }

    [Fact]
    public void SessionSnapshotCannotBeChangedByManagementRowAndOldResultCannotEndNewLogin()
    {
        var original = User.Create(1, "synthetic", "Synthetic", "secret hash", "secret salt", UserRole.CaseManager, null, 1);
        var session = Session(original);
        var old = session.CurrentUser!;
        original.SecurityVersion = 123;
        Assert.Equal(1, old.SecurityVersion);
        Assert.Empty(old.PasswordHash);
        var ended = 0;
        session.SessionEnded += (_, _) => ended++;
        session.Invalidate(old);
        session.Invalidate(old);
        Assert.Equal(1, ended);
        session.SetUser(original);
        session.Invalidate(old);
        Assert.False(session.HasSessionEnded);
        Assert.Equal(1, ended);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedOrDisabledSessionCannotChangeOrResetPasswords(bool disable)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PromoteAsync(fixture, fixture.CaseManagerOne, UserPermissions.AllAgencyPermissions);
        using var password = Password("Synthetic current password 123!");
        await SetPasswordAsync(fixture, fixture.CaseManagerOne.Id, password);
        var session = Session(fixture.CaseManagerOne);
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var actor = await setup.Users.SingleAsync(u => u.Id == fixture.CaseManagerOne.Id);
            if (disable) actor.IsEnabled = false;
            else actor.SecurityVersion++;
            await setup.SaveChangesAsync();
        }
        var service = new UserService(fixture.Factory, new PasswordHasher(), session);
        await Assert.ThrowsAsync<SessionExpiredException>(() => service.ChangePasswordAsync(session.CurrentUser!, password, password));
        await Assert.ThrowsAsync<SessionExpiredException>(() => service.ResetPasswordAsync(session.CurrentUser!.ToAgencyActor(), fixture.CaseManagerOne, password));
        Assert.True(session.HasSessionEnded);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Empty(await verify.AuditEvents.ToListAsync());
    }

    private static SessionService Session(User user)
    {
        var session = new SessionService();
        session.SetUser(user);
        return session;
    }

    private static async Task PromoteAsync(NoteEntryFixture fixture, User user, UserPermissions permissions)
    {
        user.Permissions = permissions;
        user.Role = Enum.Parse<UserRole>(UserPermissionRules.LegacyLabel(permissions));
        await using var setup = fixture.Factory.CreateDbContext();
        var stored = await setup.Users.SingleAsync(u => u.Id == user.Id);
        stored.Permissions = user.Permissions;
        stored.Role = user.Role;
        await setup.SaveChangesAsync();
    }

    private static async Task<User> AddAdministratorAsync(NoteEntryFixture fixture)
    {
        var user = User.Create(41, "synthetic-admin", "Synthetic Administrator", "hash", "salt", UserRole.Admin, null, fixture.CaseManagerOne.AgencyId);
        user.Permissions = UserPermissions.AllAgencyPermissions;
        await using var setup = fixture.Factory.CreateDbContext();
        setup.Users.Add(user);
        await setup.SaveChangesAsync();
        return user;
    }

    private static async Task SetPasswordAsync(NoteEntryFixture fixture, int userId, SecureString password)
    {
        var (hash, salt) = new PasswordHasher().HashPassword(password);
        await using var setup = fixture.Factory.CreateDbContext();
        (await setup.Users.SingleAsync(u => u.Id == userId)).SetPassword(hash, salt);
        await setup.SaveChangesAsync();
    }

    private static SecureString Password(string value)
    {
        var secure = new SecureString();
        foreach (var character in value) secure.AppendChar(character);
        secure.MakeReadOnly();
        return secure;
    }
}
