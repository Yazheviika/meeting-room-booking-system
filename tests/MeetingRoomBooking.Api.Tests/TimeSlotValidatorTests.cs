using MeetingRoomBooking.Api.Services;

namespace MeetingRoomBooking.Api.Tests;

/// <summary>Unit tests for <see cref="TimeSlotValidator"/>.</summary>
public class TimeSlotValidatorTests
{
    /// <summary>Start strictly before end is a valid range.</summary>
    [Fact]
    public void IsValidRange_StartBeforeEnd_ReturnsTrue()
    {
        Assert.True(TimeSlotValidator.IsValidRange(new TimeOnly(9, 0), new TimeOnly(10, 0)));
    }

    /// <summary>Start equal to or after end is not a valid range.</summary>
    [Theory]
    [InlineData(10, 0, 10, 0)]
    [InlineData(10, 0, 9, 0)]
    public void IsValidRange_StartNotBeforeEnd_ReturnsFalse(int startHour, int startMinute, int endHour, int endMinute)
    {
        Assert.False(TimeSlotValidator.IsValidRange(
            new TimeOnly(startHour, startMinute),
            new TimeOnly(endHour, endMinute)));
    }

    /// <summary>Two ranges that share time overlap.</summary>
    [Fact]
    public void Overlaps_OverlappingRanges_ReturnsTrue()
    {
        Assert.True(TimeSlotValidator.Overlaps(
            new TimeOnly(9, 0), new TimeOnly(10, 0),
            new TimeOnly(9, 30), new TimeOnly(10, 30)));
    }

    /// <summary>Back-to-back ranges (one ends exactly when the other starts) do not overlap.</summary>
    [Fact]
    public void Overlaps_TouchingRanges_ReturnsFalse()
    {
        Assert.False(TimeSlotValidator.Overlaps(
            new TimeOnly(9, 0), new TimeOnly(10, 0),
            new TimeOnly(10, 0), new TimeOnly(11, 0)));
    }

    /// <summary>Disjoint ranges with a gap between them do not overlap.</summary>
    [Fact]
    public void Overlaps_DisjointRanges_ReturnsFalse()
    {
        Assert.False(TimeSlotValidator.Overlaps(
            new TimeOnly(9, 0), new TimeOnly(10, 0),
            new TimeOnly(11, 0), new TimeOnly(12, 0)));
    }
}
