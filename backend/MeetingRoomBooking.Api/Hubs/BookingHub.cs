using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MeetingRoomBooking.Api.Hubs;

/// <summary>
/// SignalR hub for real-time booking-status updates. No methods yet —
/// group membership and broadcast logic are added alongside the booking
/// feature itself.
/// </summary>
/// <remarks>
/// Requires authentication. The Angular client passes the JWT as an
/// <c>?access_token=</c> query-string parameter on connect, since a
/// WebSocket upgrade can't carry an Authorization header — see the
/// <c>JwtBearerEvents.OnMessageReceived</c> handoff in <c>Program.cs</c>.
/// </remarks>
[Authorize]
public class BookingHub : Hub
{
}
