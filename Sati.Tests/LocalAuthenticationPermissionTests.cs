using System.Security;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class LocalAuthenticationPermissionTests
{
    private const string SyntheticPassword = "synthetic-auth-permission-test-only";

    [Theory]
    [InlineData(UserRole.CaseManager, UserPermissions.Billing)]
    [InlineData(UserRole.Admin, UserPermissions.Billing)]
    [InlineData(UserRole.Admin, UserPermissions.None)]
    [InlineData(UserRole.CaseManager, UserPermissions.CaseManagement | UserPermissions.Billing)]
    [InlineData(UserRole.CaseManager, UserPermissions.Supervision)]
    [InlineData(UserRole.Admin, (UserPermissions)128)]
    public async Task LoginPreservesPersistedPermissionsInsteadOfRegrantingLegacyRoleDefaults(
        UserRole legacyRole, UserPermissions permissions)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PrepareLoginAsync(fixture, legacyRole, permissions);
        using var password = Secure(SyntheticPassword);

        var result = await new AuthService(fixture.Factory).AuthenticateAsync("cm-one", password);

        Assert.NotNull(result);
        Assert.Equal(permissions, result.Permissions);
        Assert.Equal(UserPermissionRules.HasCaseManagerPermissions(permissions), result.HasCaseManagerPermissions);
        Assert.Equal(UserPermissionRules.HasSupervisorPermissions(permissions), result.HasSupervisorPermissions);
        Assert.Equal(UserPermissionRules.HasAdminPermissions(permissions), result.HasAdminPermissions);
        Assert.Equal(UserPermissionRules.HasBillingPermissions(permissions), result.HasBillingPermissions);
        Assert.Equal(legacyRole, result.Role);
        Assert.Equal(fixture.CaseManagerOne.Id, result.Id);
        Assert.Equal(fixture.CaseManagerOne.AgencyId, result.AgencyId);
        Assert.Equal("synthetic@example.invalid", result.Email);
        Assert.Equal("555-0100", result.Phone);
    }

    [Fact]
    public async Task SuccessfulLoginDoesNotCopyPasswordVerifierIntoSessionOrChangeStoredCredentials()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var expected = await PrepareLoginAsync(fixture, UserRole.CaseManager, UserPermissions.CaseManagement);
        using var password = Secure(SyntheticPassword);

        var result = await new AuthService(fixture.Factory).AuthenticateAsync("cm-one", password);

        Assert.NotNull(result);
        Assert.Empty(result.PasswordHash);
        Assert.Empty(result.Salt);
        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.Users.AsNoTracking().SingleAsync(user => user.Id == result.Id);
        Assert.Equal(expected.Hash, stored.PasswordHash);
        Assert.Equal(expected.Salt, stored.Salt);
        Assert.Single(await db.AuditEvents.Where(item => item.Action == "authentication.succeeded").ToListAsync());
    }

    [Theory]
    [InlineData("cm-one")]
    [InlineData("missing-synthetic-user")]
    public async Task InvalidCredentialsReturnNoSessionOrSuccessAudit(string username)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await PrepareLoginAsync(fixture, UserRole.Admin, UserPermissions.Billing);
        using var password = Secure("incorrect-synthetic-password");

        Assert.Null(await new AuthService(fixture.Factory).AuthenticateAsync(username, password));

        await using var db = fixture.Factory.CreateDbContext();
        Assert.False(await db.AuditEvents.AnyAsync(item => item.Action == "authentication.succeeded"));
    }

    private static async Task<(string Hash, string Salt)> PrepareLoginAsync(
        NoteEntryFixture fixture, UserRole role, UserPermissions permissions)
    {
        using var password = Secure(SyntheticPassword);
        var credentials = new PasswordHasher().HashPassword(password);
        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.Users.SingleAsync(user => user.Id == fixture.CaseManagerOne.Id);
        stored.SetPassword(credentials.Hash, credentials.Salt);
        stored.Role = role;
        stored.Permissions = permissions;
        stored.Email = "synthetic@example.invalid";
        stored.Phone = "555-0100";
        await db.SaveChangesAsync();
        return credentials;
    }

    private static SecureString Secure(string value)
    {
        var result = new SecureString();
        foreach (var character in value)
            result.AppendChar(character);
        result.MakeReadOnly();
        return result;
    }
}
