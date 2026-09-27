using MeetingRoomBooking.Api.Data;
using MeetingRoomBooking.Api.Hubs;
using Microsoft.EntityFrameworkCore;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure()));

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

app.UseAuthorization();

app.MapControllers();
app.MapHub<BookingHub>("/hubs/booking");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
