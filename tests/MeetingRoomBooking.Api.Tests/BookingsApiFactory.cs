using MeetingRoomBooking.Api.Data;
using MeetingRoomBooking.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRoomBooking.Api.Tests;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> pointed at a real SQL
/// Server database via a connection string override — unlike
/// <see cref="RoomsApiFactory"/>, no DbContext provider swap is needed:
/// overriding <c>ConnectionStrings:DefaultConnection</c> is enough, since
/// the app's own <c>UseSqlServer</c> registration picks it up unchanged.
/// <c>Database:MigrateOnStartup</c> is left on so the real migration path
/// in <c>Program.cs</c> runs, unmodified, against the fixture's fresh,
/// RCSI-enabled database.
/// </summary>
public class BookingsApiFactory : WebApplicationFactory<Program>
{
    private static readonly Dictionary<string, string?> TestJwtConfiguration = new()
    {
        ["Jwt:Issuer"] = "test-issuer",
        ["Jwt:Audience"] = "test-audience",
        ["Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long-for-tests",
    };

    private readonly string _connectionString;

    /// <summary>Initializes a new instance of the <see cref="BookingsApiFactory"/> class.</summary>
    public BookingsApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>(TestJwtConfiguration)
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Database:MigrateOnStartup"] = "true",
            };
            config.AddInMemoryCollection(overrides);
        });
    }

    /// <summary>Creates a real <see cref="ApplicationUser"/> with the "User" role and mints a token for them.</summary>
    public async Task<(string UserId, string Token)> CreateUserAndTokenAsync(string email)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var user = new ApplicationUser { Email = email, UserName = email };
        var result = await userManager.CreateAsync(user, "Test-Passw0rd!123");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create test user {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, "User");
        var token = jwtTokenService.CreateToken(user, ["User"]).AccessToken;
        return (user.Id, token);
    }

    /// <summary>Creates a room with one time slot directly via <see cref="AppDbContext"/> — fast and deterministic, bypassing HTTP.</summary>
    public async Task<(int RoomId, int TimeSlotId)> CreateRoomWithSlotAsync(string roomName, TimeOnly start, TimeOnly end)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var room = new Room
        {
            Name = roomName,
            Capacity = 4,
            TimeSlots = [new TimeSlot { StartTime = start, EndTime = end }],
        };

        dbContext.Rooms.Add(room);
        await dbContext.SaveChangesAsync();

        return (room.Id, room.TimeSlots[0].Id);
    }

    /// <summary>Counts active bookings for a slot/date directly via <see cref="AppDbContext"/>.</summary>
    public async Task<int> CountActiveBookingsAsync(int timeSlotId, DateOnly date)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Bookings.CountAsync(b =>
            b.TimeSlotId == timeSlotId && b.BookingDate == date && b.Status == BookingStatus.Active);
    }

    /// <summary>
    /// Tomorrow, per the app's own <see cref="IOfficeClock"/> (the same
    /// clock instance the running app uses) — race tests book tomorrow,
    /// never today, so the past-slot check can never introduce flakiness
    /// tied to the wall-clock time the tests happen to run at.
    /// </summary>
    public DateOnly Tomorrow()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOfficeClock>().Today().AddDays(1);
    }
}
