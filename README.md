# Meeting Room Booking System

A meeting-room booking system where multiple users may race to book the
same time slot. A slot is never double-booked, and every viewer sees
booking status change in real time.

**Live app:** https://app-roombooking-web-vy-gwbyaxb0dab5hvb0.swedencentral-01.azurewebsites.net

- Task specification: [docs/TASK.md](docs/TASK.md)
- Development conventions: [CLAUDE.md](CLAUDE.md)
- Concurrency design record: [docs/adr/0001-booking-concurrency.md](docs/adr/0001-booking-concurrency.md)

## Architecture

Angular SPA → ASP.NET Core (.NET 10) Web API → Azure SQL Database, with
Azure SignalR Service pushing real-time slot-status updates to every
connected client. Both the API and the SPA run as separate Azure Linux Web
Apps (F1 plan, Sweden Central).

## Concurrency design

A slot is never double-booked because the database guarantees it, not
application code: a filtered unique index on
`Bookings (TimeSlotId, BookingDate) WHERE Status = 'Active'` allows at
most one active booking per slot per date, independent of instance count
or isolation level. The booking service inserts directly rather than
checking-then-inserting, and maps a unique-index violation to HTTP 409 —
except when the conflicting active booking belongs to the same user, in
which case the request returns success with that existing booking (this
covers double-clicks, client retries, and `EnableRetryOnFailure`). This
approach, the alternatives considered, and why they were rejected, are
recorded in full in
[docs/adr/0001-booking-concurrency.md](docs/adr/0001-booking-concurrency.md).
Real-time updates ride on top of this, not instead of it: the SignalR
broadcast only fires after the owning transaction commits, so a client
never gets told about a booking that then rolls back.

## Running locally

### Backend

```bash
dotnet run --project backend/MeetingRoomBooking.Api
```

Requires two local secrets first (never commit these):

```bash
dotnet user-secrets set ConnectionStrings:DefaultConnection "<your-connection-string>" --project backend/MeetingRoomBooking.Api
dotnet user-secrets set Jwt:SigningKey "<a-random-32+-byte-string>" --project backend/MeetingRoomBooking.Api
```

Health check: `GET /health`.

### Frontend

```bash
cd frontend
npm install
ng serve
```

Dev server at `http://localhost:4200`, calling the API directly via
`environment.apiBaseUrl` — no proxy.

### Tests

```bash
dotnet build && dotnet test
```

Booking concurrency tests use a real SQL Server, never EF InMemory (per
ADR 0001 — InMemory can't reproduce RCSI or real unique-index
enforcement). On Windows, plain `dotnet test` already covers this via
LocalDB, no setup needed. On macOS/Linux (or if you'd rather not use
LocalDB), start a throwaway SQL Server container first:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<a-strong-password>" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

then set the connection string as an environment variable before running
`dotnet test`:

```bash
export ConnectionStrings__DefaultConnection="Server=localhost,1433;User Id=sa;Password=<a-strong-password>;TrustServerCertificate=True"
```

Frontend tests:

```bash
cd frontend
npm test
```

Vitest, runs once and exits — no watch mode, no browser dependency. Same
command CI runs.

## Cold start

Both Azure Web Apps and the Azure SQL database are on free/serverless
tiers that idle down when unused. The first request after a period of
inactivity may take up to about a minute while everything wakes back up —
this is expected, not a bug. Subsequent requests are fast.

## How this was built

This project was built with [Claude Code](https://claude.com/claude-code)
end to end, following a deliberate, documented workflow rather than
freeform prompting:

- **[CLAUDE.md](CLAUDE.md)** is the living source of truth for
  conventions, architecture decisions, and the exact contract of every
  non-obvious subsystem (the SignalR hub protocol, the auth flow, the
  booking concurrency design) — kept up to date after every change, so
  each new step starts from an accurate picture of the codebase rather
  than re-deriving it.
- **Architecture Decision Records** (`docs/adr/`) capture the reasoning
  behind decisions with real stakes — notably
  [ADR 0001](docs/adr/0001-booking-concurrency.md) for the concurrency
  design — including the alternatives considered and why they were
  rejected, not just the choice made.
- **One feature per pull request**, each planned before any code was
  written and reviewed before merging: repo scaffolding, backend
  scaffolding, authentication, rooms/schedule, bookings with concurrency
  control, real-time SignalR updates, then the Angular frontend in four
  further PRs (skeleton + deployment, auth, booking flow, admin).
- **Atomic commits with WHY-focused bodies** within each PR — every commit
  builds and passes tests on its own, and each message explains the
  reasoning behind a change, not just a restatement of the diff.
