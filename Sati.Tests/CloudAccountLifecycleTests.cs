using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class CloudAccountLifecycleTests
{
    [Fact]
    public void ProfileMappingPreservesDisabledStateAndSecurityVersion()
    {
        var user = CloudContractMapper.ToUser(new(2, "synthetic", "Synthetic", "CaseManager",
            UserPermissions.CaseManagement, null, 1, null, null, false, 27));
        Assert.False(user.IsEnabled);
        Assert.Equal(27, user.SecurityVersion);
    }

    [Theory]
    [InlineData("disable", false, "/api/v1/users/2/enabled", "PUT")]
    [InlineData("enable", false, "/api/v1/users/2/enabled", "PUT")]
    [InlineData("revoke", false, "/api/v1/users/2/sessions", "DELETE")]
    [InlineData("revoke", true, "/api/v1/users/1/sessions", "DELETE")]
    [InlineData("password", true, "/api/v1/users/me/password", "PUT")]
    [InlineData("reset", true, "/api/v1/users/1/password", "PUT")]
    [InlineData("reset", false, "/api/v1/users/2/password", "PUT")]
    public async Task LifecycleUsesDedicatedRouteAndEndsOnlySuccessfulOwnSession(
        string operation, bool own, string path, string method)
    {
        var handler = new CaptureHandler();
        var api = new CloudApiClient(new HttpClient(handler) { BaseAddress = new("https://synthetic.invalid") });
        api.SetAccessToken("SYNTHETIC", DateTimeOffset.UtcNow.AddHours(1));
        IUserService service = new CloudUserService(api);
        var actor = new AgencyActor(1, 1, UserPermissions.Administration);
        var target = User.Create(own ? 1 : 2, "synthetic", "Synthetic", "", "", UserRole.CaseManager, null, 1);
        using var password = new SecureString();
        foreach (var character in "Synthetic-Test-Password") password.AppendChar(character);
        switch (operation)
        {
            case "enable": await service.SetEnabledAsync(actor, target, true); break;
            case "disable": await service.SetEnabledAsync(actor, target, false); break;
            case "revoke": await service.RevokeSessionsAsync(actor, target); break;
            case "password": await service.ChangePasswordAsync(target, password, password); break;
            default: await service.ResetPasswordAsync(actor, target, password); break;
        }
        Assert.Equal(path, handler.Path);
        Assert.Equal(method, handler.Method);
        Assert.Equal(own, api.HasSessionEnded);
        if (operation is "enable" or "disable")
            Assert.Equal(operation == "enable", System.Text.Json.JsonDocument.Parse(handler.Body!).RootElement.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task GeneralProfileUpdateDoesNotSendLifecycleFields()
    {
        var handler = new CaptureHandler();
        var api = new CloudApiClient(new HttpClient(handler) { BaseAddress = new("https://synthetic.invalid") });
        api.SetAccessToken("SYNTHETIC");
        var user = User.Create(2, "synthetic", "Synthetic", "", "", UserRole.CaseManager, null, 1);
        user.IsEnabled = false;
        user.SecurityVersion = 999;
        await new CloudUserService(api).UpdateAsync(new(1, 1, UserPermissions.Administration), user);
        Assert.DoesNotContain("isEnabled", handler.Body);
        Assert.DoesNotContain("securityVersion", handler.Body);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Path, Method, Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Method = request.Method.Method;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.NoContent);
        }
    }
}
