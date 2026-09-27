using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MeetingRoomBooking.Api.Data;
using MeetingRoomBooking.Api.Services;
using Microsoft.Extensions.Configuration;

namespace MeetingRoomBooking.Api.Tests;

/// <summary>Unit tests for <see cref="JwtTokenService"/> — claims, roles, and expiry.</summary>
public class JwtTokenServiceTests
{
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:SigningKey"] = "this-is-a-test-signing-key-at-least-32-bytes-long",
            })
            .Build();

    /// <summary>The generated token carries the user's id, email, and role claims.</summary>
    [Fact]
    public void CreateToken_IncludesUserAndRoleClaims()
    {
        var service = new JwtTokenService(BuildConfiguration());
        var user = new ApplicationUser { Id = "user-1", Email = "a@b.com", UserName = "a@b.com" };

        var token = service.CreateToken(user, ["Admin", "User"]);

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);

        Assert.Equal("user-1", parsed.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("a@b.com", parsed.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("test-issuer", parsed.Issuer);
        Assert.Contains("test-audience", parsed.Audiences);
        Assert.Contains(parsed.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(parsed.Claims, c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    /// <summary>The token's expiry is ~60 minutes from creation.</summary>
    [Fact]
    public void CreateToken_ExpiresInApproximately60Minutes()
    {
        var service = new JwtTokenService(BuildConfiguration());
        var user = new ApplicationUser { Id = "user-1", Email = "a@b.com", UserName = "a@b.com" };

        var before = DateTime.UtcNow;
        var token = service.CreateToken(user, []);

        Assert.InRange(token.ExpiresAtUtc, before.AddMinutes(59), before.AddMinutes(61));
    }
}
