using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MeetingRoomBooking.Api.Tests;

/// <summary>
/// Integration tests proving the "AdminOnly" policy actually rejects
/// non-admins at the HTTP level. A pure policy-evaluation unit test can
/// prove the policy itself succeeds/fails, but not the resulting status
/// code — that's the authorization *middleware's* behavior, so this uses
/// the real ASP.NET Core pipeline via <see cref="RoomsApiFactory"/>. Covers
/// all three cases: a 403-only test would also pass if the endpoint simply
/// rejected everyone.
/// </summary>
public class RoomsAuthorizationTests : IClassFixture<RoomsApiFactory>
{
    private readonly RoomsApiFactory _factory;

    /// <summary>Initializes a new instance of the <see cref="RoomsAuthorizationTests"/> class.</summary>
    public RoomsAuthorizationTests(RoomsApiFactory factory)
    {
        _factory = factory;
    }

    private static object NewRoomPayload(string name) => new
    {
        name,
        description = (string?)null,
        capacity = 4,
        timeSlots = Array.Empty<object>(),
    };

    /// <summary>No bearer token at all is unauthenticated, not just unauthorized.</summary>
    [Fact]
    public async Task CreateRoom_NoToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/rooms", NewRoomPayload("No Token Room"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>An authenticated "User"-role token is not enough for an Admin-only endpoint.</summary>
    [Fact]
    public async Task CreateRoom_UserRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateTokenForRole("User"));

        var response = await client.PostAsJsonAsync("/api/rooms", NewRoomPayload("User Role Room"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An "Admin"-role token succeeds, and the room is actually persisted.</summary>
    [Fact]
    public async Task CreateRoom_AdminRole_Returns201AndPersistsRoom()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateTokenForRole("Admin"));

        var response = await client.PostAsJsonAsync("/api/rooms", NewRoomPayload("Admin Role Room"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var getResponse = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("Admin Role Room", body);
    }
}
