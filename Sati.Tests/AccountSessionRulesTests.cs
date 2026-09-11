using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class AccountSessionRulesTests
{
    [Fact]
    public void EnabledStateRequestMustExpressTheIntendedStateExplicitly()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<SetUserEnabledRequest>("{}", options));
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<SetUserEnabledRequest>("{\"isEnabled\":false}", options)!.IsEnabled);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<SetUserEnabledRequest>("{\"isEnabled\":true}", options)!.IsEnabled);
    }

    [Theory]
    [InlineData(true, 1L, 1L, true)]
    [InlineData(true, 8L, 8L, true)]
    [InlineData(false, 8L, 8L, false)]
    [InlineData(true, 8L, 7L, false)]
    [InlineData(true, 8L, 9L, false)]
    [InlineData(true, 0L, 0L, false)]
    [InlineData(true, -1L, -1L, false)]
    public void OnlyEnabledCurrentPositiveVersionsAuthenticate(
        bool enabled, long current, long session, bool expected) =>
        Assert.Equal(expected, AccountSessionRules.IsCurrentSession(enabled, current, session));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void InvalidOrExhaustedVersionCannotWrapAndReviveAnOldSession(long version) =>
        Assert.Throws<InvalidOperationException>(() => AccountSessionRules.NextSecurityVersion(version));

    [Fact]
    public void RevocationAdvancesVersionMonotonically() =>
        Assert.Equal(12L, AccountSessionRules.NextSecurityVersion(11));

    [Theory]
    [InlineData(UserPermissions.None)]
    [InlineData(UserPermissions.CaseManagement)]
    [InlineData(UserPermissions.Supervision)]
    [InlineData(UserPermissions.Billing)]
    [InlineData(UserPermissions.Administration | (UserPermissions)1024)]
    public void AccountLifecycleRequiresSupportedAdministration(UserPermissions permissions)
    {
        var refusal = AccountSessionRules.DescribeManagementRefusal(
            new AgencyActor(1, 2, permissions), 3, 2, "CaseManager", false);
        Assert.Equal(AccountSessionRules.RequiresAdministration, refusal?.Message);
    }

    [Fact]
    public void AdministratorCannotDisableSelfButCanRevokeOwnSessions()
    {
        var actor = new AgencyActor(1, 2, UserPermissions.Administration);
        Assert.Equal(AccountSessionRules.CannotDisableSelf,
            AccountSessionRules.DescribeManagementRefusal(actor, 1, 2, "Admin", false)?.Message);
        Assert.Null(AccountSessionRules.DescribeManagementRefusal(actor, 1, 2, "Admin", null));
    }

    [Theory]
    [InlineData(3, "CaseManager")]
    [InlineData(2, "PlatformOperator")]
    public void AdministratorCannotManageForeignOrPlatformIdentity(int agency, string role) =>
        Assert.NotNull(AccountSessionRules.DescribeManagementRefusal(
            new AgencyActor(1, 2, UserPermissions.Administration), 3, agency, role, false));

    [Fact]
    public void AgencyActorCarriesTheCapturedSecurityVersion()
    {
        var user = User.Create(1, "synthetic", "Synthetic", "", "", UserRole.CaseManager, null, 2);
        user.SecurityVersion = 9;
        var actor = user.ToAgencyActor();
        user.SecurityVersion = 10;
        Assert.Equal(9L, actor.SecurityVersion);
    }

    [Fact]
    public void MigrationAddsRetainedStateWithoutChangingCredentialsOrRecords()
    {
        var migration = new Migrations.AddAccountSessionLifecycle();
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(2, migration.UpOperations.Count);
        Assert.Equal(true, Assert.Single(columns, c => c.Name == "IsEnabled").DefaultValue);
        Assert.Equal(1L, Assert.Single(columns, c => c.Name == "SecurityVersion").DefaultValue);
        Assert.All(columns, c => { Assert.Equal("Users", c.Table); Assert.False(c.IsNullable); });
    }

    [Fact]
    public void SqlServerUpgradeScriptAddsStateWithoutConnectingToAnyDatabase()
    {
        var options = new DbContextOptionsBuilder<Sati.Data.SatiContext>()
            .UseSqlServer("Server=synthetic.invalid;Database=ModelOnly;Integrated Security=true;Encrypt=true")
            .Options;
        using var context = new Sati.Data.SatiContext(options);
        var script = context.GetService<IMigrator>().GenerateScript(
            "20260911022820_AddClearinghouseResponseIntake", "20260911120000_AddAccountSessionLifecycle");
        Assert.Contains("ADD [IsEnabled] bit NOT NULL DEFAULT CAST(1 AS bit)", script);
        Assert.Contains("ADD [SecurityVersion] bigint NOT NULL DEFAULT CAST(1 AS bigint)", script);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [Users]", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledStatePersistsAndConcurrentRevocationsCannotLoseAnIncrement()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await using var first = fixture.Factory.CreateDbContext();
        await using var stale = fixture.Factory.CreateDbContext();
        var current = await first.Users.SingleAsync(u => u.Id == fixture.CaseManagerOne.Id);
        var captured = await stale.Users.SingleAsync(u => u.Id == current.Id);
        current.IsEnabled = false;
        current.SecurityVersion = AccountSessionRules.NextSecurityVersion(current.SecurityVersion);
        await first.SaveChangesAsync();

        captured.SecurityVersion = AccountSessionRules.NextSecurityVersion(captured.SecurityVersion);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());

        await using var verify = fixture.Factory.CreateDbContext();
        var stored = await verify.Users.AsNoTracking().SingleAsync(u => u.Id == current.Id);
        Assert.False(stored.IsEnabled);
        Assert.Equal(2L, stored.SecurityVersion);
        Assert.True(await verify.People.AnyAsync(p => p.Id == fixture.PersonOneId));
    }

    [Fact]
    public async Task ExplicitlyDisabledNewAccountDoesNotBecomeEnabledByTheDatabaseDefault()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await using var db = fixture.Factory.CreateDbContext();
        var disabled = User.Create(801, "disabled-synthetic", "Disabled synthetic", "", "",
            UserRole.CaseManager, null, fixture.CaseManagerOne.AgencyId);
        disabled.IsEnabled = false;
        db.Users.Add(disabled);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.False((await db.Users.SingleAsync(u => u.Id == disabled.Id)).IsEnabled);
    }
}
