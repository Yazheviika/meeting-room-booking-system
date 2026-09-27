using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Api.Data;

/// <summary>
/// EF Core database context for the booking system, extended with
/// ASP.NET Core Identity's user/role tables. No domain entities yet —
/// resources and bookings are added once that domain model is designed.
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
}
