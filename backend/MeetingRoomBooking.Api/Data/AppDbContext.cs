using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Api.Data;

/// <summary>
/// EF Core database context for the booking system, extended with
/// ASP.NET Core Identity's user/role tables. Bookings aren't modeled yet —
/// that comes with the booking-concurrency feature.
/// </summary>
/// <remarks>
/// If a later change overrides <c>OnModelCreating</c>, it must call
/// <c>base.OnModelCreating(builder)</c> first — <see cref="IdentityDbContext{TUser}"/>
/// uses that override to configure Identity's own tables.
/// </remarks>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    /// <summary>Initializes a new instance of the <see cref="AppDbContext"/> class.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>Bookable meeting rooms.</summary>
    public DbSet<Room> Rooms => Set<Room>();

    /// <summary>Rooms' fixed daily time slots.</summary>
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Room>(entity =>
        {
            entity.Property(room => room.Name).IsRequired().HasMaxLength(200);

            // "Unique among active rooms" is exactly what a filtered index
            // expresses — same technique as the booking-concurrency ADR's
            // filtered unique index, but without exception-to-409 mapping:
            // room creation is admin-only and low-contention, not a stated
            // concurrency requirement, so this is a schema backstop behind
            // an application-level pre-check, not a race-safety guarantee.
            entity.HasIndex(room => room.Name).IsUnique().HasFilter("[IsActive] = 1");
        });

        builder.Entity<TimeSlot>(entity =>
        {
            entity.HasOne(slot => slot.Room)
                .WithMany(room => room.TimeSlots)
                .HasForeignKey(slot => slot.RoomId);
        });
    }
}
