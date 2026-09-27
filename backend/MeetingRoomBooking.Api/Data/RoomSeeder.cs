using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Api.Data;

/// <summary>
/// Seeds a handful of demo rooms with typical office hours so a reviewer
/// (or a fresh local environment) sees data immediately, without needing to
/// create rooms by hand first. Unlike the optional admin user, this is
/// non-secret sample data, so it runs unconditionally in every environment
/// — not gated by any configuration flag.
/// </summary>
public static class RoomSeeder
{
    /// <summary>Seeds demo rooms and their time slots if no rooms exist yet.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        var dbContext = services.GetRequiredService<AppDbContext>();

        // "Any room already exists" (real or previously seeded) is enough
        // reason to skip — once real data exists, demo rows shouldn't keep
        // reappearing on every restart.
        if (await dbContext.Rooms.AnyAsync())
        {
            return;
        }

        dbContext.Rooms.AddRange(
            CreateRoom("Boardroom", "Main boardroom with projector and video conferencing", capacity: 12),
            CreateRoom("Focus Room 1", "Small room for 1:1s and focused work", capacity: 4),
            CreateRoom("Focus Room 2", "Small room for 1:1s and focused work", capacity: 4));

        await dbContext.SaveChangesAsync();
    }

    private static Room CreateRoom(string name, string description, int capacity)
    {
        var room = new Room { Name = name, Description = description, Capacity = capacity };

        for (var hour = 9; hour < 18; hour++)
        {
            room.TimeSlots.Add(new TimeSlot
            {
                StartTime = new TimeOnly(hour, 0),
                EndTime = new TimeOnly(hour + 1, 0),
            });
        }

        return room;
    }
}
