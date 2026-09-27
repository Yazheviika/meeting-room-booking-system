using MeetingRoomBooking.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MeetingRoomBooking.Api.Services;

/// <inheritdoc cref="IBookingNotifier" />
/// <remarks>
/// No try/catch here — a broadcast failure being non-fatal is
/// <see cref="BookingService"/>'s responsibility (it wraps every call to
/// this interface), not this specific implementation's, so the guarantee
/// holds for any <see cref="IBookingNotifier"/>, not just this one.
/// </remarks>
public class SignalRBookingNotifier : IBookingNotifier
{
    private readonly IHubContext<BookingHub> _hubContext;

    /// <summary>Initializes a new instance of the <see cref="SignalRBookingNotifier"/> class.</summary>
    public SignalRBookingNotifier(IHubContext<BookingHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc />
    public Task SlotChangedAsync(int roomId, DateOnly date, int timeSlotId, bool isBooked) =>
        _hubContext.Clients
            .Group(BookingHub.GroupName(roomId))
            // "slotId", not "timeSlotId": this is the documented wire
            // contract (CLAUDE.md) the Angular client is built against,
            // deliberately named differently from the C# parameter.
            .SendAsync("SlotChanged", new { roomId, date, slotId = timeSlotId, isBooked });
}
