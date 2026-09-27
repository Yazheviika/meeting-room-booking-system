using System.Text;
using MeetingRoomBooking.Api.Data;
using MeetingRoomBooking.Api.Hubs;
using MeetingRoomBooking.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

const string FrontendCorsPolicy = "Frontend";
const string AdminOnlyPolicy = "AdminOnly";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure()));

// AddIdentityCore (not AddIdentity/AddDefaultIdentity) — this is a
// stateless JWT API for a separate Angular SPA, so we skip Identity's
// cookie authentication scheme entirely and only take the password
// hashing / user & role management pieces (UserManager/RoleManager).
builder.Services
    .AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"] ?? string.Empty)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        // SignalR's JS client can't set an Authorization header on a
        // WebSocket upgrade request, so the Angular app sends the JWT as an
        // ?access_token= query-string parameter instead. Only accept that
        // fallback for hub paths — ordinary REST calls still require the
        // header.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminOnlyPolicy, policy => policy.RequireRole("Admin"));
});

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services.AddSingleton<IOfficeClock, OfficeClock>();

// SignalR always runs; it only talks to Azure SignalR when a connection
// string is configured, so the app still runs locally without Azure.
var signalRBuilder = builder.Services.AddSignalR();
var azureSignalRConnectionString = builder.Configuration["Azure:SignalR:ConnectionString"];
if (!string.IsNullOrEmpty(azureSignalRConnectionString))
{
    signalRBuilder.AddAzureSignalR(azureSignalRConnectionString);
}

// CORS is locked to a configured allow-list (never AllowAnyOrigin) with
// AllowCredentials, which SignalR's client requires.
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Applying migrations at startup only ever happens on this single F1 App
// Service instance (see infra/README.md); a genuinely multi-instance
// deployment should migrate as a CI/CD step instead, to avoid concurrent
// instances racing to migrate the same database at once. Wrapped in the
// execution strategy so retries compose with EnableRetryOnFailure — this
// is EF Core's own documented pattern for applying migrations against a
// provider configured with retry-on-failure.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var migrationScope = app.Services.CreateScope();
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var strategy = dbContext.Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(() => dbContext.Database.MigrateAsync());
}

using (var seedScope = app.Services.CreateScope())
{
    await IdentitySeeder.SeedAsync(seedScope.ServiceProvider);
    await RoomSeeder.SeedAsync(seedScope.ServiceProvider);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Azure App Service terminates TLS in front of the container and only ever
// forwards plain HTTP (port 8080) to the app, so UseHttpsRedirection there
// just warns "Failed to determine the https port" on every request. HTTPS
// is enforced instead by the App Service "HTTPS Only" setting (see
// infra/README.md). Only redirect locally, where Kestrel really does serve
// both http and https endpoints.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<BookingHub>("/hubs/booking");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

/// <summary>
/// Top-level statements generate an internal <c>Program</c> class; this
/// partial declaration makes it public so <c>WebApplicationFactory&lt;Program&gt;</c>
/// in the test project can see it. No runtime effect otherwise.
/// </summary>
public partial class Program;
