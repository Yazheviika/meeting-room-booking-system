using Microsoft.AspNetCore.Identity;

namespace MeetingRoomBooking.Api.Data;

/// <summary>
/// Idempotent startup seeding: ensures the "Admin" and "User" roles exist,
/// and optionally provisions one admin account from configuration. Safe to
/// run on every startup — every step here is "create if missing."
/// </summary>
public static class IdentitySeeder
{
    private static readonly string[] Roles = ["Admin", "User"];

    /// <summary>Seeds roles and, if configured, the admin user.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");

        try
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var roleName in Roles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            var configuration = services.GetRequiredService<IConfiguration>();
            var adminEmail = configuration["Seed:AdminEmail"];
            var adminPassword = configuration["Seed:AdminPassword"];

            if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
            {
                // Never hardcode credentials and never fail startup over a
                // missing admin config — this is expected on a fresh clone
                // before Seed:AdminEmail/Seed:AdminPassword are set.
                logger.LogWarning(
                    "Seed:AdminEmail/Seed:AdminPassword not configured; skipping admin user seeding.");
                return;
            }

            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser is null)
            {
                adminUser = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (!result.Succeeded)
                {
                    logger.LogWarning(
                        "Failed to seed admin user {AdminEmail}: {Errors}",
                        adminEmail,
                        string.Join("; ", result.Errors.Select(e => e.Description)));
                    return;
                }
            }

            if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }
        catch (Exception ex)
        {
            // Seeding is best-effort startup convenience, not a hard
            // dependency: if the database is unreachable (e.g. Azure SQL
            // serverless still cold-starting, or migrations haven't caught
            // up yet), log it and let the app keep starting rather than
            // crash-loop. Requests that need the seeded roles will fail on
            // their own until the database is reachable.
            logger.LogError(ex, "Identity seeding failed; the database may be unavailable.");
        }
    }
}
