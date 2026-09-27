namespace MeetingRoomBooking.Api.Services;

/// <summary>
/// Pure validation rules for time slots. Kept free of EF Core/DB access so
/// they're directly unit-testable — slot overlap has no database-level
/// guarantee (SQL Server has no native exclusion constraint for interval
/// overlap without triggers, which would be overkill for an admin-only,
/// non-concurrent operation).
/// </summary>
public static class TimeSlotValidator
{
    /// <summary>True if <paramref name="start"/> is strictly before <paramref name="end"/>.</summary>
    public static bool IsValidRange(TimeOnly start, TimeOnly end) => start < end;

    /// <summary>
    /// True if the two ranges overlap. Touching endpoints (one range's end
    /// equals the other's start) do not count as overlapping — back-to-back
    /// slots are allowed.
    /// </summary>
    public static bool Overlaps(TimeOnly startA, TimeOnly endA, TimeOnly startB, TimeOnly endB) =>
        startA < endB && startB < endA;

    /// <summary>
    /// True if a slot starting at <paramref name="slotStart"/> on
    /// <paramref name="date"/> has already started, relative to
    /// <paramref name="today"/>/<paramref name="nowTimeOfDay"/> in office
    /// time. The single source of truth for "is this future or past,"
    /// reused for the booking-creation past-slot check, the
    /// cancel-past-booking check, and the room/slot delete future-booking
    /// check (all three are really the same date/time comparison).
    /// </summary>
    public static bool HasStarted(DateOnly date, TimeOnly slotStart, DateOnly today, TimeOnly nowTimeOfDay) =>
        date < today || (date == today && slotStart <= nowTimeOfDay);
}
