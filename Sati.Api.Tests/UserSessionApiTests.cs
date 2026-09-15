using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[CollectionDefinition(Name)]
public sealed class UserSessionApiCollection : ICollectionFixture<SatiApiFactory>
{
    public const string Name = "User session API";
}

[Collection(UserSessionApiCollection.Name)]
public sealed class UserSessionApiTests(SatiApiFactory factory)
{
    private const string Password = "Synthetic-Session-42!";
    private const string NewPassword = "New-Synthetic-Session-43!";
    private const string VersionClaim = "sati_security_version";

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"isEnabled\":null}")]
    public async Task MissingOrNullEnabledChoiceCannotDisableAnAccount(string json)
    {
        var target = await CreateUserAsync();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        int auditCount;
        await using (var beforeScope = factory.Services.CreateAsyncScope())
        {
            auditCount = await beforeScope.ServiceProvider.GetRequiredService<ApiDbContext>()
                .AuditEvents.CountAsync();
        }
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await admin.PutAsync($"/api/v1/users/{target.Id}/enabled", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var saved = await db.Users.SingleAsync(x => x.Id == target.Id);
        Assert.True(saved.IsEnabled);
        Assert.Equal(1, saved.SecurityVersion);
        Assert.Equal(auditCount, await db.AuditEvents.CountAsync());
    }

    [Theory]
    [InlineData(UserPermissions.Billing, false)]
    [InlineData(UserPermissions.Billing, true)]
    [InlineData(UserPermissions.Administration, false)]
    [InlineData(UserPermissions.Administration, true)]
    public async Task SupervisorCannotTakeOverOrEditAssignedPrivilegedAccount(UserPermissions additionalPermission, bool resetPassword)
    {
        var target = await CreateUserAsync();
        string originalHash;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var row = await db.Users.SingleAsync(x => x.Id == target.Id);
            row.Permissions = UserPermissions.CaseManagement | additionalPermission;
            row.SupervisorId = 13;
            originalHash = row.PasswordHash;
            await db.SaveChangesAsync();
        }
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var response = resetPassword
            ? await supervisor.PutAsJsonAsync($"/api/v1/users/{target.Id}/password", new ResetPasswordRequest(NewPassword))
            : await supervisor.PutAsJsonAsync($"/api/v1/users/{target.Id}", new SaveUserRequest(
                target.Username, "Unauthorized edit", UserPermissions.CaseManagement, 13, 1, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var saved = await verify.Users.SingleAsync(x => x.Id == target.Id);
        Assert.Equal(originalHash, saved.PasswordHash);
        Assert.Equal(1, saved.SecurityVersion);
        Assert.Equal(UserPermissions.CaseManagement | additionalPermission, saved.Permissions);
        Assert.Equal("Synthetic session user", saved.DisplayName);
        Assert.False(await verify.AuditEvents.AnyAsync(x => x.ResourceId == target.Id.ToString() &&
            (x.Action == AuditActions.UserPasswordReset || x.Action == AuditActions.UserUpdated)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupervisorCanStillManageAssignedCaseManagementOnlyAccount(bool resetPassword)
    {
        var target = await CreateUserAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var row = await db.Users.SingleAsync(x => x.Id == target.Id);
            row.Permissions = UserPermissions.CaseManagement;
            row.SupervisorId = 13;
            await db.SaveChangesAsync();
        }
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var response = resetPassword
            ? await supervisor.PutAsJsonAsync($"/api/v1/users/{target.Id}/password", new ResetPasswordRequest(NewPassword))
            : await supervisor.PutAsJsonAsync($"/api/v1/users/{target.Id}", new SaveUserRequest(
                target.Username, "Authorized edit", UserPermissions.CaseManagement, 13, 1, null, null));
        Assert.Equal(resetPassword ? HttpStatusCode.NoContent : HttpStatusCode.OK, response.StatusCode);
        if (resetPassword)
        {
            using var fresh = await LoginAsync(target.Username, NewPassword);
            Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/v1/me")).StatusCode);
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var rowAfter = await verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>().Users.SingleAsync(x => x.Id == target.Id);
        Assert.Equal(resetPassword ? 2 : 1, rowAfter.SecurityVersion);
        Assert.Equal(resetPassword ? "Synthetic session user" : "Authorized edit", rowAfter.DisplayName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordChangeOrResetInvalidatesEveryOldBearerAndRenewal(bool administratorReset)
    {
        var user = await CreateUserAsync();
        using var first = await LoginAsync(user.Username, Password);
        using var second = await LoginAsync(user.Username, Password);
        using var administrator = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var change = administratorReset
            ? await administrator.PutAsJsonAsync($"/api/v1/users/{user.Id}/password", new ResetPasswordRequest(NewPassword))
            : await first.PutAsJsonAsync("/api/v1/users/me/password", new ChangePasswordRequest(Password, NewPassword));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        foreach (var client in new[] { first, second })
        {
            using var read = await client.GetAsync("/api/v1/me");
            using var renewal = await client.PostAsync("/api/v1/auth/renew", null);
            Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, renewal.StatusCode);
        }
        using var anonymous = factory.CreateAnonymousClient();
        using var wrong = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(user.Username, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var current = await LoginAsync(user.Username, NewPassword);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task DisablingAndReenablingCannotResurrectOldSessionsOrRemoveRecords()
    {
        var user = await CreateUserAsync();
        using var old = await LoginAsync(user.Username, Password);
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var disabled = await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/enabled", new SetUserEnabledRequest(false));
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False((await disabled.Content.ReadFromJsonAsync<UserProfileDto>())!.IsEnabled);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.PostAsync("/api/v1/auth/renew", null)).StatusCode);
        using var anonymous = factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(user.Username, Password))).StatusCode);
        using var enabled = await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/enabled", new SetUserEnabledRequest(true));
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        Assert.True((await enabled.Content.ReadFromJsonAsync<UserProfileDto>())!.IsEnabled);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/v1/me")).StatusCode);
        using var current = await LoginAsync(user.Username, Password);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/v1/me")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.True(await db.Users.AnyAsync(x => x.Id == user.Id));
        Assert.True(await db.People.AnyAsync(x => x.UserId == user.Id));
    }

    [Fact]
    public async Task ExplicitRevocationKeepsAccountEnabledButEndsExistingSessions()
    {
        var user = await CreateUserAsync();
        using var old = await LoginAsync(user.Username, Password);
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var revoked = await admin.DeleteAsync($"/api/v1/users/{user.Id}/sessions");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.PostAsync("/api/v1/auth/renew", null)).StatusCode);
        using var current = await LoginAsync(user.Username, Password);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/v1/me")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    [InlineData("999999999999999999999999")]
    public async Task MissingOrMalformedSecurityVersionFailsClosedEvenWithValidSignature(string? version)
    {
        var user = await CreateUserAsync();
        using var current = await LoginAsync(user.Username, Password);
        var original = new JwtSecurityTokenHandler().ReadJwtToken(current.DefaultRequestHeaders.Authorization!.Parameter);
        var claims = original.Claims.Where(x => x.Type != VersionClaim).ToList();
        if (version is not null) claims.Add(new Claim(VersionClaim, version));
        var jwt = new JwtSecurityToken(original.Issuer, "Sati.Api.Tests", claims, original.ValidFrom, original.ValidTo,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                "integration-test-signing-key-that-is-at-least-32-characters")), SecurityAlgorithms.HmacSha256));
        using var altered = factory.CreateAnonymousClient();
        altered.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        Assert.Equal(HttpStatusCode.Unauthorized, (await altered.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await altered.PostAsync("/api/v1/auth/renew", null)).StatusCode);
    }

    [Theory]
    [InlineData("case-manager-one", false)]
    [InlineData("supervisor-one", false)]
    [InlineData("admin-two", true)]
    [InlineData("platform-operator", false)]
    public async Task AccountLifecycleRequiresSameAgencyAdministration(string actorName, bool foreign)
    {
        var target = await CreateUserAsync();
        using var actor = await factory.CreateAuthenticatedClientAsync(actorName);
        foreach (var revoke in new[] { false, true })
        {
            using var response = revoke ? await actor.DeleteAsync($"/api/v1/users/{target.Id}/sessions")
                : await actor.PutAsJsonAsync($"/api/v1/users/{target.Id}/enabled", new SetUserEnabledRequest(false));
            Assert.Equal(foreign ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, response.StatusCode);
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == target.Id);
        Assert.True(user.IsEnabled);
        Assert.Equal(1, user.SecurityVersion);
        Assert.False(await db.AuditEvents.AnyAsync(x => x.ResourceId == target.Id.ToString() &&
            (x.Action == "user.disabled" || x.Action == "user.sessions-revoked")));
    }

    [Fact]
    public async Task AdministratorCannotDisableSelfOrManagePlatformIdentity()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/users/11/enabled", new SetUserEnabledRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/v1/users/31/enabled", new SetUserEnabledRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/v1/users/31/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task OrdinaryUserEditsCannotReenableOrRollBackSecurityVersion()
    {
        var user = await CreateUserAsync();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/enabled", new SetUserEnabledRequest(false))).StatusCode);
        using var response = await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}", new
        {
            user.Username, displayName = "Ordinary edit", permissions = UserPermissions.CaseManagement,
            supervisorId = (int?)null, agencyId = 1, email = (string?)null, phone = (string?)null,
            isEnabled = true, securityVersion = 1
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = (await response.Content.ReadFromJsonAsync<UserProfileDto>())!;
        Assert.False(profile.IsEnabled);
        Assert.Equal(2, profile.SecurityVersion);
    }

    [Fact]
    public async Task FailedPasswordCheckLeavesTheCurrentSessionAndVersionIntact()
    {
        var user = await CreateUserAsync();
        using var client = await LoginAsync(user.Username, Password);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/users/me/password",
            new ChangePasswordRequest("incorrect", NewPassword))).StatusCode);
        var profile = await client.GetFromJsonAsync<UserProfileDto>("/api/v1/me");
        Assert.Equal(1, profile!.SecurityVersion);
        Assert.True(profile.IsEnabled);
    }

    [Fact]
    public async Task ConcurrentStaleCredentialWriteCannotUndoDisablement()
    {
        var target = await CreateUserAsync();
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var staleScope = factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var stale = staleScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var disable = await first.Users.SingleAsync(x => x.Id == target.Id);
        var password = await stale.Users.SingleAsync(x => x.Id == target.Id);
        disable.IsEnabled = false;
        disable.SecurityVersion++;
        await first.SaveChangesAsync();
        password.PasswordHash = "synthetic replacement";
        password.SecurityVersion++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var row = await verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>().Users.SingleAsync(x => x.Id == target.Id);
        Assert.False(row.IsEnabled);
        Assert.Equal(2, row.SecurityVersion);
        Assert.NotEqual("synthetic replacement", row.PasswordHash);
    }

    [Fact]
    public async Task TokenIssuedFromPreChangeCredentialSnapshotCannotAcquireNewVersion()
    {
        var target = await CreateUserAsync();
        ServerUser snapshot;
        Guid instance;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            snapshot = await db.Users.AsNoTracking().SingleAsync(x => x.Id == target.Id);
            instance = await db.DatabaseIdentities.Where(x => x.Id == 1).Select(x => x.InstanceId).SingleAsync();
        }
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/users/{target.Id}/password",
            new ResetPasswordRequest(NewPassword))).StatusCode);
        // Represents the worst timing: a password reset commits after the login's
        // final database check, immediately before it serializes the old snapshot.
        var issued = factory.Services.GetRequiredService<TokenIssuer>().Issue(snapshot, instance);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
        Assert.Equal("1", jwt.Claims.Single(x => x.Type == VersionClaim).Value);
        using var stale = factory.CreateAnonymousClient();
        stale.DefaultRequestHeaders.Authorization = new("Bearer", issued.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.PostAsync("/api/v1/auth/renew", null)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenChatLeaseClosesAfterVersionRevocationOrDisablement(bool disable)
    {
        var target = await CreateUserAsync();
        var options = factory.Services.GetRequiredService<IOptions<ChatOptions>>().Value;
        var enabledBefore = options.Enabled;
        options.Enabled = true;
        try
        {
            using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
            using var member = await LoginAsync(target.Username, Password);
            using var created = await admin.PostAsJsonAsync("/api/v1/chat/rooms",
                new CreateChatRoomRequest("Synthetic session room", null, null, [11, target.Id]));
            created.EnsureSuccessStatusCode();
            var connection = factory.Server.CreateWebSocketClient();
            connection.ConfigureRequest = request => request.Headers.Authorization = member.DefaultRequestHeaders.Authorization!.ToString();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(28));
            using var socket = await connection.ConnectAsync(new Uri("wss://localhost/api/v1/chat/stream"), timeout.Token);
            var bytes = new byte[256];
            Assert.Equal(WebSocketMessageType.Text, (await socket.ReceiveAsync(bytes, timeout.Token)).MessageType);
            using var changed = disable
                ? await admin.PutAsJsonAsync($"/api/v1/users/{target.Id}/enabled", new SetUserEnabledRequest(false))
                : await admin.DeleteAsync($"/api/v1/users/{target.Id}/sessions");
            Assert.True(changed.IsSuccessStatusCode);
            WebSocketReceiveResult result;
            do { result = await socket.ReceiveAsync(bytes, timeout.Token); }
            while (result.MessageType != WebSocketMessageType.Close);
            Assert.Equal(WebSocketCloseStatus.PolicyViolation, result.CloseStatus);
        }
        finally { options.Enabled = enabledBefore; }
    }

    [Fact]
    public async Task CurrentRenewalKeepsAuthenticationTimeAndVersionAfterFreshSignIn()
    {
        var user = await CreateUserAsync();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/users/{user.Id}/sessions")).StatusCode);
        using var client = await LoginAsync(user.Username, Password);
        var before = new JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        var response = await client.PostAsync("/api/v1/auth/renew", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renewed = (await response.Content.ReadFromJsonAsync<SessionRenewalResponse>())!;
        var after = new JwtSecurityTokenHandler().ReadJwtToken(renewed.AccessToken);
        Assert.Equal("2", after.Claims.Single(x => x.Type == VersionClaim).Value);
        Assert.Equal(before.Claims.Single(x => x.Type == "sati_auth_time").Value,
            after.Claims.Single(x => x.Type == "sati_auth_time").Value);
    }

    private async Task<(int Id, string Username)> CreateUserAsync()
    {
        using var seeded = await factory.CreateAuthenticatedClientAsync("admin-one");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var credentials = scope.ServiceProvider.GetRequiredService<PasswordVerifier>().Hash(Password);
        var id = await db.Users.MaxAsync(x => x.Id) + 1;
        var username = $"session-test-{id}";
        db.Users.Add(new ServerUser { Id = id, Username = username, DisplayName = "Synthetic session user",
            AgencyId = 1, Permissions = UserPermissions.CaseManagement | UserPermissions.Billing,
            Role = "CaseManager", PasswordHash = credentials.Hash, Salt = credentials.Salt });
        db.People.Add(new ServerPerson { UserId = id, AgencyId = 1, FirstName = "Synthetic", LastName = "Retained",
            BirthDate = new DateTime(1990, 1, 1) });
        await db.SaveChangesAsync();
        return (id, username);
    }

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = factory.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
}
