using MeetingRoomBooking.Api.Data;
using MeetingRoomBooking.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeetingRoomBooking.Api.Tests;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> that runs the real
/// app (real middleware, real authorization policies) against an
/// EF Core InMemory database instead of SQL Server — no real database
/// needed for an authorization test, and InMemory doesn't support
/// migrations, so Database:MigrateOnStartup is also turned off here.
/// </summary>
public class RoomsApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Test-only JWT signing configuration, shared with <see cref="CreateTokenForRole"/>.</summary>
    private static readonly Dictionary<string, string?> TestConfiguration = new()
    {
        ["Database:MigrateOnStartup"] = "false",
        ["Jwt:Issuer"] = "test-issuer",
        ["Jwt:Audience"] = "test-audience",
        ["Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long-for-tests",
    };

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestConfiguration));

        builder.ConfigureServices(services =>
        {
            // Microsoft's documented recipe for swapping a WebApplicationFactory's
            // DbContext to a test provider: remove the existing options
            // registrations, then re-add with UseInMemoryDatabase. EF Core
            // 9/10 registers both DbContextOptions<AppDbContext> and
            // IDbContextOptionsConfiguration<AppDbContext> — both need
            // removing, or the SqlServer configuration action still runs.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            // Generate the database name once, outside the configure
            // callback: AddDbContext can invoke that callback more than
            // once per service-provider build, so a Guid.NewGuid() call
            // inline inside it produces a different name each time and
            // silently splits requests across multiple empty databases.
            var databaseName = $"RoomsApiTests-{Guid.NewGuid()}";
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
        });
    }

    /// <summary>Mints a signed JWT for a user with the given role, without a real register/login round-trip.</summary>
    public string CreateTokenForRole(string role)
    {
        using var scope = Services.CreateScope();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "test@example.com", UserName = "test@example.com" };
        return jwtTokenService.CreateToken(user, [role]).AccessToken;
    }
}
