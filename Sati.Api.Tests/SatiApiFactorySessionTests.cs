using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

public sealed class SatiApiFactorySessionTests
{
    [Fact]
    public async Task NewFixtureClientSignsInAgainAfterCachedTokenExpiresWithoutChangingExistingClient()
    {
        await using var factory = new SatiApiFactory();
        using var existing = await factory.CreateAuthenticatedClientAsync("admin-one");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(existing.DefaultRequestHeaders.Authorization!.Parameter);
        var options = factory.Services.GetRequiredService<IOptions<ApiAuthenticationOptions>>().Value;
        var now = DateTime.UtcNow;
        var expired = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            options.Issuer, options.Audience,
            jwt.Claims.Where(claim => claim.Type is not "exp" and not "nbf" and not "iat"),
            now.AddMinutes(-10), now.AddMinutes(-5),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                SecurityAlgorithms.HmacSha256)));
        // Only this private, single-test factory's cache is injected; no product clock or lifetime changes.
        var cache = (Dictionary<string, string>)typeof(SatiApiFactory)
            .GetField("_tokens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(factory)!;
        cache["admin-one"] = expired;
        existing.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        using var oldDenied = await existing.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldDenied.StatusCode);

        using var fresh = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var allowed = await fresh.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.NotEqual(expired, fresh.DefaultRequestHeaders.Authorization!.Parameter);
        Assert.Equal(expired, existing.DefaultRequestHeaders.Authorization!.Parameter);
        using var oldStillDenied = await existing.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldStillDenied.StatusCode);
    }
}
