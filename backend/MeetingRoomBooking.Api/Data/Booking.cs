namespace MeetingRoomBooking.Api.Data;

/// <summary>A booking's lifecycle state. Persisted as its member name (see ADR 0001).</summary>
public enum BookingStatus
{
    /// <summary>The booking holds its slot.</summary>
    Active,

    /// <summary>The booking was cancelled and no longer holds its slot.</summary>
    Cancelled,
}

/// <summary>
/// A user's claim on a room's time slot for a specific date. Double-booking
/// is prevented by a filtered unique index on (TimeSlotId, BookingDate)
/// WHERE Status = 'Active' — see docs/adr/0001-booking-concurrency.md.
/// </summary>
public class Booking
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the booked <see cref="TimeSlot"/>.</summary>
    public int TimeSlotId { get; set; }

    /// <summary>Navigation property to the booked slot.</summary>
    public TimeSlot? TimeSlot { get; set; }

    /// <summary>The calendar date this booking occupies the slot on.</summary>
    public DateOnly BookingDate { get; set; }

    /// <summary>Foreign key to the booking user.</summary>
    public required string UserId { get; set; }

    /// <summary>Navigation property to the booking user.</summary>
    public ApplicationUser? User { get; set; }

    /// <summary>Active or Cancelled. Only Active bookings hold their slot.</summary>
    public BookingStatus Status { get; set; } = BookingStatus.Active;

    /// <summary>When the booking was created, in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the booking was cancelled, in UTC, or null if still active.</summary>
    public DateTime? CancelledAtUtc { get; set; }
}
