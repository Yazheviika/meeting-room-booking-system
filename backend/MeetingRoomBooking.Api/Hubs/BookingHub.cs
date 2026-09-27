using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MeetingRoomBooking.Api.Hubs;

/// <summary>
/// SignalR hub for real-time booking-status updates. One group per room;
/// clients join a room's group to receive its "SlotChanged" broadcasts
/// (sent by <c>SignalRBookingNotifier</c>, not from this hub directly).
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
    /// <summary>The SignalR group name for a room's real-time updates.</summary>
    public static string GroupName(int roomId) => $"room-{roomId}";

    /// <summary>
    /// Joins the caller to a room's group. No room-existence check — any
    /// authenticated user can already view any room's schedule, so joining
    /// its real-time group is equally unrestricted; joining a nonexistent
    /// room's group is harmless (no events will ever arrive for it).
    /// </summary>
    public Task JoinRoom(int roomId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(roomId));

    /// <summary>Removes the caller from a room's group.</summary>
    public Task LeaveRoom(int roomId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(roomId));
}
