namespace MeetingRoomBooking.Api.Services;

/// <inheritdoc cref="IBookingNotifier" />
public class NoOpBookingNotifier : IBookingNotifier
{
    /// <inheritdoc />
    public Task SlotChangedAsync(int roomId, DateOnly date, int timeSlotId) => Task.CompletedTask;
}
