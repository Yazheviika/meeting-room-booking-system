# infra

Azure resources this project runs on, and their non-secret configuration.
Names and settings only — never connection strings, keys, or other secret
values (see [../CLAUDE.md](../CLAUDE.md)'s Secrets section; actual values
live in `dotnet user-secrets` locally and Azure App Service Application
Settings / Connection strings in the deployed environment).

All resources below are in resource group `rg-roombooking`, region Sweden
Central. (The resource group's own ARM metadata happens to be stored in a
different region — that's just where its record lives, not where any
resource actually runs.)

## Azure SQL

- Server: `sql-roombooking-vy`
- Database: `sqldb-roombooking`
- Tier: free offer, serverless, auto-pause enabled
- Firewall: "Allow Azure services" on, plus a rule for the current
  developer's IP (added ad hoc when connecting from a new location)
- Maps to the backend's `ConnectionStrings:DefaultConnection` — set via the
  App Service **Connection strings** blade, name `DefaultConnection`, type
  `SQLAzure` (see Backend app below)

## Azure SignalR

- Name: `sigr-roombooking-vy`
- Tier: Free, Mode: Default

## App Service Plan

- Name: `asp-roombooking`
- Tier: F1 (Free), Linux

## Backend app (`app-roombooking-api-vy`)

ASP.NET Core (.NET 10) Web API.

- **Startup Command**: `dotnet MeetingRoomBooking.Api.dll`. Root cause as
  observed in the App Service log stream: Oryx's default startup script
  scans `/home/site/wwwroot` for `*.runtimeconfig.json` and expects exactly
  one; with two present (the API's and the xUnit test project's, left over
  from the very first, whole-solution publish before `backend.yml` existed)
  it couldn't choose and fell back to Azure's placeholder app
  (`hostingstart.dll`) instead of the API. `clean: true` in
  [../.github/workflows/backend.yml](../.github/workflows/backend.yml) now
  stops stale files from accumulating, but the Startup Command stays
  explicit anyway so startup is deterministic regardless of what ends up in
  `wwwroot`.
- **HTTPS Only**: On. TLS terminates at the platform edge; see the comment
  next to `UseHttpsRedirection` in
  [../backend/MeetingRoomBooking.Api/Program.cs](../backend/MeetingRoomBooking.Api/Program.cs).
- **Connection strings** blade: `DefaultConnection` (type `SQLAzure`) →
  read by the app as `ConnectionStrings:DefaultConnection`.
- **Application settings**: `Azure__SignalR__ConnectionString`,
  `Cors__AllowedOrigins__0` (append `__1`, `__2`, ... for additional allowed
  origins), `Jwt__SigningKey`, `Seed__AdminEmail`, `Seed__AdminPassword` →
  read by the app as `Azure:SignalR:ConnectionString`, `Cors:AllowedOrigins`,
  `Jwt:SigningKey`, `Seed:AdminEmail`, `Seed:AdminPassword` (names only here
  — actual values are set directly in the Azure Portal, never committed).
  `Jwt:Issuer`/`Jwt:Audience` and `Database:MigrateOnStartup` all have
  working non-secret defaults in `appsettings.json` and don't need an
  App Setting unless overriding those defaults (`Jwt__Issuer`,
  `Jwt__Audience`, `Database__MigrateOnStartup`).

## Frontend app (`app-roombooking-web-vy`)

Angular SPA on Node.

- **Startup Command**: `pm2 serve /home/site/wwwroot --no-daemon --spa`
- **HTTPS Only**: On
