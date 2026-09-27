namespace MeetingRoomBooking.Api.Services;

/// <summary>
/// Notifies viewers that a slot's booking status changed. Called only
/// after the owning DB transaction commits (see CLAUDE.md's SignalR rule)
/// — never before, to avoid announcing a booking that then rolls back.
/// A no-op for now; the next PR swaps this for a real SignalR broadcast.
/// </summary>
public interface IBookingNotifier
{
    /// <summary>A slot's booking status changed for the given room/date/slot.</summary>
    Task SlotChangedAsync(int roomId, DateOnly date, int timeSlotId, bool isBooked);
}
