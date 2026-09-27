using MeetingRoomBooking.Api.Data;

namespace MeetingRoomBooking.Api.Services;

/// <summary>Generates signed JWT access tokens for authenticated users.</summary>
public interface IJwtTokenService
{
    /// <summary>Creates a signed access token for the given user and their current roles.</summary>
    JwtToken CreateToken(ApplicationUser user, IEnumerable<string> roles);
}

/// <summary>A generated access token and its absolute expiry, in UTC.</summary>
public record JwtToken(string AccessToken, DateTime ExpiresAtUtc);
