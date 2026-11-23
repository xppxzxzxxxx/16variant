using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DrivingSchool.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace DrivingSchool.Api;

internal sealed class SessionTokens(SymmetricSecurityKey key)
{
    public const string Issuer = "DrivingSchool";
    public const string Audience = "DrivingSchool.Client";

    public string Issue(UserInfo user)
    {
        var claims = new[]
        {
            new Claim("sub", user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim("jti", Guid.NewGuid().ToString("N")),
            new Claim("login", user.Login),
            new Claim("display_name", user.DisplayName),
            new Claim("role", user.Role)
        };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience, claims, now, now.AddHours(4),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static UserInfo User(ClaimsPrincipal principal) => new(
        int.Parse(principal.FindFirst("sub")!.Value, CultureInfo.InvariantCulture),
        principal.FindFirst("login")!.Value, principal.FindFirst("display_name")!.Value,
        principal.FindFirst("role")!.Value);

    public static void Revoke(ClaimsPrincipal user, IMemoryCache cache)
    {
        var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(user.FindFirst("exp")!.Value,
            CultureInfo.InvariantCulture));
        cache.Set(user.FindFirst("jti")!.Value, true, expires);
    }
}
