using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sati.Api.Data;

namespace Sati.Api.Security;

internal sealed class TokenIssuer(IOptions<Api.Infrastructure.ApiAuthenticationOptions> options)
{
    internal const string AuthenticatedAtClaim = "sati_auth_time";
    internal const string DatabaseInstanceClaim = "sati_demo_instance";
    internal const string SecurityVersionClaim = "sati_security_version";
    private readonly Api.Infrastructure.ApiAuthenticationOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
        ServerUser user,
        Guid databaseInstanceId,
        DateTimeOffset? authenticatedAtUtc = null)
    {
        if (!Sati.Contracts.V1.AccountSessionRules.IsCurrentSession(
                user.IsEnabled, user.SecurityVersion, user.SecurityVersion))
            throw new UnauthorizedAccessException("The account cannot start a session.");
        var issuedAt = DateTimeOffset.UtcNow;
        var authenticatedAt = authenticatedAtUtc ?? issuedAt;
        var expiresAt = issuedAt.AddMinutes(_options.TokenMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(AuthenticatedAtClaim, authenticatedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(DatabaseInstanceClaim, databaseInstanceId.ToString("D")),
            new Claim(SecurityVersionClaim, user.SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("agency_id", user.AgencyId.ToString())
        };

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt);
    }
}
