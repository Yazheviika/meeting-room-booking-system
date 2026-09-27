namespace MeetingRoomBooking.Api.Data;

/// <summary>
/// A bookable meeting room. <see cref="IsActive"/> is a soft-delete flag —
/// rooms are never hard-deleted, since existing time slots and (later)
/// bookings must keep referring to a real row.
/// </summary>
public class Room
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Display name. Must be unique among active rooms.</summary>
    public required string Name { get; set; }

    /// <summary>Optional free-text description.</summary>
    public string? Description { get; set; }

    /// <summary>Maximum number of people the room seats.</summary>
    public int Capacity { get; set; }

    /// <summary>Soft-delete flag. Inactive rooms are hidden, never removed.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The room's fixed daily set of bookable time slots.</summary>
    public List<TimeSlot> TimeSlots { get; set; } = [];
}
