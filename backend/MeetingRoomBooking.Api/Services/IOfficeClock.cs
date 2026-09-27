namespace MeetingRoomBooking.Api.Services;

/// <summary>
/// Provides "today"/"now" in office local time, for bookable-date and
/// past-slot checks. All room/slot times are stored as office-local
/// <see cref="TimeOnly"/> values with no time zone of their own, so
/// converting "what time is it right now, at the office" needs a single,
/// shared source of truth rather than each caller reading
/// <see cref="DateTime.UtcNow"/> and converting it ad hoc.
/// </summary>
public interface IOfficeClock
{
    /// <summary>The current date and time in office local time.</summary>
    DateTime Now();

    /// <summary>The current date in office local time.</summary>
    DateOnly Today();
}
