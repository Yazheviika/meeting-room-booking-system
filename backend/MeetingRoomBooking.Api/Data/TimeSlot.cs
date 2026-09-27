namespace MeetingRoomBooking.Api.Data;

/// <summary>
/// A fixed, daily bookable time range within a room, in office local time.
/// Immutable by design: to change a time, delete this slot and add a new
/// one, so an existing booking can never silently move to another time.
/// </summary>
public class TimeSlot
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Room"/>.</summary>
    public int RoomId { get; set; }

    /// <summary>Navigation property to the owning room.</summary>
    public Room? Room { get; set; }

    /// <summary>Start of the slot, office local time.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>End of the slot, office local time. Must be after <see cref="StartTime"/>.</summary>
    public TimeOnly EndTime { get; set; }
}
