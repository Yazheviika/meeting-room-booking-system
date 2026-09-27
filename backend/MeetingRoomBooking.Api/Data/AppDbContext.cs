using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Api.Data;

/// <summary>
/// EF Core database context for the booking system. No entities yet —
/// resources and bookings are added once the domain model is designed.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="AppDbContext"/> class.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}
