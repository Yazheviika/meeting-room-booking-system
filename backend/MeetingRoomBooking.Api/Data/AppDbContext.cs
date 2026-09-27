using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Api.Data;

/// <summary>
/// EF Core database context for the booking system, extended with
/// ASP.NET Core Identity's user/role tables.
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

    /// <summary>User bookings of rooms' time slots.</summary>
    public DbSet<Booking> Bookings => Set<Booking>();

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

        builder.Entity<Booking>(entity =>
        {
            // Stored as its member name ("Active"/"Cancelled"), not an int,
            // so the filtered index's predicate below can read exactly as
            // it does in ADR 0001: WHERE Status = 'Active'.
            entity.Property(booking => booking.Status).HasConversion<string>().HasMaxLength(20);

            // The sole correctness guarantee for "never double-booked" —
            // see docs/adr/0001-booking-concurrency.md. Name matches the
            // ADR exactly so error messages/tests can refer to one name.
            entity.HasIndex(booking => new { booking.TimeSlotId, booking.BookingDate })
                .IsUnique()
                .HasDatabaseName("UX_Bookings_ActiveSlot")
                .HasFilter("[Status] = 'Active'");

            // Restrict (not Cascade): deleting a slot/user must never
            // silently destroy booking history. RoomsController's slot
            // -delete explicitly checks for *future* active bookings and
            // returns 409 for those; Restrict is the backstop that also
            // protects *past* booking rows the explicit check doesn't
            // cover, turning what would otherwise be an FK violation into
            // a caught, friendly 409 instead of an unhandled 500.
            entity.HasOne(booking => booking.TimeSlot)
                .WithMany()
                .HasForeignKey(booking => booking.TimeSlotId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(booking => booking.User)
                .WithMany()
                .HasForeignKey(booking => booking.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
