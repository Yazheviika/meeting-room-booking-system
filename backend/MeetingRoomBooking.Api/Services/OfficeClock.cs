namespace MeetingRoomBooking.Api.Services;

/// <inheritdoc cref="IOfficeClock" />
public class OfficeClock : IOfficeClock
{
    private readonly TimeZoneInfo _timeZone;

    /// <summary>Initializes a new instance of the <see cref="OfficeClock"/> class.</summary>
    public OfficeClock(IConfiguration configuration)
    {
        var timeZoneId = configuration["Office:TimeZone"] ?? "Europe/Bucharest";
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    /// <inheritdoc />
    public DateTime Now() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

    /// <inheritdoc />
    public DateOnly Today() => DateOnly.FromDateTime(Now());
}
