using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using System.Security;
using Xunit;

namespace Sati.Tests;

public sealed class LocalPrivilegedUserTargetTests
{
    [Theory]
    [InlineData(UserPermissions.Billing, false)]
    [InlineData(UserPermissions.Billing, true)]
    [InlineData(UserPermissions.Administration, false)]
    [InlineData(UserPermissions.Administration, true)]
    [InlineData(UserPermissions.Supervision, false)]
    [InlineData(UserPermissions.Supervision, true)]
    [InlineData(UserPermissions.AgencyWideSupervision, false)]
    [InlineData(UserPermissions.AgencyWideSupervision, true)]
    [InlineData(UserPermissions.None, false)]
    [InlineData(UserPermissions.None, true)]
    public async Task SupervisorCanManageOnlyAnAssignedCaseManagementOnlyTarget(
        UserPermissions additionalPermission, bool resetPassword)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var supervisor = fixture.CaseManagerOne;
        supervisor.Role = UserRole.Supervisor;
        supervisor.Permissions = UserPermissions.Supervision;
        using var originalPassword = Secure("Original-synthetic-password-907");
        using var replacementPassword = Secure("Replacement-synthetic-password-908");
        var hasher = new PasswordHasher();
        var (hash, salt) = hasher.HashPassword(originalPassword);
        var target = User.Create(809, "assigned-target", "Assigned target", hash, salt,
            UserRole.CaseManager, supervisor.Id, supervisor.AgencyId);
        target.Permissions = UserPermissions.CaseManagement | additionalPermission;
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var stored = await setup.Users.SingleAsync(u => u.Id == supervisor.Id);
            stored.Role = supervisor.Role;
            stored.Permissions = supervisor.Permissions;
            setup.Users.Add(target);
            await setup.SaveChangesAsync();
        }
        var session = new SessionService();
        session.SetUser(supervisor);
        var service = new UserService(fixture.Factory, hasher, session);
        // A harmless-looking requested permission set must not hide the stronger
        // stored target. Previously this could strip its powers, then reset its password.
        var proposed = User.Create(target.Id, target.Username, target.DisplayName, "", "",
            UserRole.CaseManager, supervisor.Id, supervisor.AgencyId);
        proposed.Permissions = UserPermissions.CaseManagement;
        proposed.Email = "synthetic-change@example.invalid";
        Task Act() => resetPassword
            ? service.ResetPasswordAsync(supervisor.ToAgencyActor(), target, replacementPassword)
            : service.UpdateAsync(supervisor.ToAgencyActor(), proposed);

        if (additionalPermission == UserPermissions.None) await Act();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(Act);

        await using var verify = fixture.Factory.CreateDbContext();
        var persisted = await verify.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id);
        if (additionalPermission != UserPermissions.None)
        {
            Assert.Equal(UserPermissions.CaseManagement | additionalPermission, persisted.Permissions);
            Assert.Equal(hash, persisted.PasswordHash);
            Assert.Equal(salt, persisted.Salt);
            Assert.Equal(1L, persisted.SecurityVersion);
            Assert.Null(persisted.Email);
            Assert.False(await verify.AuditEvents.AnyAsync(e => e.Action == "user.password-reset"));
        }
        else if (resetPassword)
        {
            Assert.True(hasher.Verify(replacementPassword, persisted.PasswordHash, persisted.Salt));
            Assert.False(hasher.Verify(originalPassword, persisted.PasswordHash, persisted.Salt));
            Assert.Equal(2L, persisted.SecurityVersion);
        }
        else Assert.Equal(proposed.Email, persisted.Email);
    }

    private static SecureString Secure(string value)
    {
        var result = new SecureString();
        foreach (var character in value) result.AppendChar(character);
        result.MakeReadOnly();
        return result;
    }
}
