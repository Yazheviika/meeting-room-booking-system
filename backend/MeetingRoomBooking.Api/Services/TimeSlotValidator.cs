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
}
