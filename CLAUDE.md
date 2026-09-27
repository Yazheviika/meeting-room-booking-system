# CLAUDE.md

Guidance for Claude Code (and human contributors) working in this repository.

## Project goal

A meeting-room booking system where multiple users may race to book the same
time slot. The system must guarantee a slot is never double-booked and must
reflect booking status to all viewers in real time.

## Tech stack

- **Backend**: ASP.NET Core (.NET 10)
- **Database**: Azure SQL Database (serverless, free tier)
- **Real-time**: Azure SignalR Service (Free tier, Default mode)
- **Frontend**: Angular SPA, served on Node via `pm2 --spa`
- **Hosting**: two Azure Linux Web Apps (backend, frontend), F1 plan, Sweden Central

## Folder layout

| Folder | Purpose |
|---|---|
| `backend/` | ASP.NET Core Web API: booking/auth/SignalR hub logic |
| `frontend/` | Angular SPA: room-booking UI |
| `tests/` | Automated tests, including the required booking-concurrency test |
| `infra/` | Deployment and Azure configuration notes |
| `docs/` | Task spec and other project documentation |

## Build / run / test

- Whole repo: `dotnet build`, `dotnet test` (run from the repo root; uses `MeetingRoomBooking.slnx`)
- Run the API locally: `dotnet run --project backend/MeetingRoomBooking.Api`
- Local secrets: `dotnet user-secrets set ConnectionStrings:DefaultConnection "..." --project backend/MeetingRoomBooking.Api`
  (also set `Jwt:SigningKey` the same way — a random 32+ byte string; without
  it, any authenticated request 500s)
- Health check: `GET /health`
- Frontend (not yet scaffolded): `npm install`, `ng serve`, `npm test` (run from `frontend/`)
- Booking concurrency tests use a real SQL Server, never EF InMemory (per
  ADR 0001 — InMemory can't reproduce RCSI or real unique-index
  enforcement). Plain `dotnet test` already covers them on Windows via
  LocalDB, no setup needed. On macOS/Linux (or if you'd rather not use
  LocalDB), start a throwaway SQL Server container first:
  `docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<a-strong-password>" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest`,
  then set `ConnectionStrings__DefaultConnection` (environment variable) to
  point at it, e.g.
  `Server=localhost,1433;User Id=sa;Password=<a-strong-password>;TrustServerCertificate=True`,
  before running `dotnet test`. CI uses the same mechanism via the SQL
  Server service container in `backend.yml`. Each test class run creates
  its own uniquely-named database and drops it afterward.

## Coding conventions

- **C#**: nullable reference types enabled, `async`/`await` for all I/O, EF Core
  migrations for every schema change (never edit the database out-of-band).
- **Angular**: standalone components, strict TypeScript.
- **Documentation**: XML doc comments on public types/methods. Comment the
  *why*, not the *what*, especially around concurrency decisions.

## Concurrency (critical)

Double-booking is prevented by a filtered unique index —
`Bookings (TimeSlotId, BookingDate) WHERE Status = 'Active'` — the database
guarantees one active booking per slot per date, independent of app code,
instance count, or isolation level. The booking service inserts directly; a
unique-index violation is mapped to HTTP 409, except when the conflicting
active booking belongs to the same user, in which case the request returns
success with that existing booking (covers `EnableRetryOnFailure` retries,
double-clicks, and client retries). See
[docs/adr/0001-booking-concurrency.md](docs/adr/0001-booking-concurrency.md)
for the full rationale and rejected alternatives. Any change touching the
booking flow must keep the automated concurrency test green. A conflicting
booking request must return HTTP 409 with a clear message — never a silent
overwrite, never a 500. Implemented in `BookingService` — see the Bookings
section below for how it's wired up and tested.

## Azure SQL specifics

- `READ_COMMITTED_SNAPSHOT` is ON by default on Azure SQL — factor this into
  the chosen concurrency strategy.
- Use `EnableRetryOnFailure` on the EF Core provider.
- Wrap manual transactions in `Database.CreateExecutionStrategy()` so retries
  and transactions compose correctly.
- The serverless tier may cold-start; expect occasional first-request latency.

## SignalR

- One SignalR group per resource (room).
- Broadcast a slot-status change only *after* the owning DB transaction
  commits — never before, to avoid announcing a booking that then rolls back.

### Hub contract (`/hubs/booking`, `BookingHub`)

- `JoinRoom(int roomId)` — joins the caller to that room's group. No
  room-existence check: any authenticated user can already view any room's
  schedule (`GET /api/rooms/{id}/schedule`), so joining its real-time group
  is equally unrestricted; joining a nonexistent room's group is harmless
  (no events ever arrive for it).
- `LeaveRoom(int roomId)` — removes the caller from that group.
- Group name format: `"room-{roomId}"` (`BookingHub.GroupName`) — the single
  source of truth both the hub and `SignalRBookingNotifier` use.
- Server → client event `"SlotChanged"`, payload:
  ```json
  { "roomId": 1, "date": "2026-10-01", "slotId": 42, "isBooked": true }
  ```
  Sent once per successful create (`isBooked: true`) or cancel
  (`isBooked: false`). Note the field is `slotId`, not `timeSlotId` — this
  is the deliberate wire-contract name, independent of the C# parameter
  name used elsewhere in this codebase. **Never includes who booked the
  slot** — same rule as the schedule endpoint.
- A broadcast failure (e.g. Azure SignalR hiccup) is logged and swallowed
  in `BookingService`, not the notifier itself — it never turns an
  already-committed booking/cancellation into a 500.
- The hub requires authentication; see the Authentication section above for
  the `?access_token=` query-string handoff WebSocket connections need.

### Client protocol

Join the room's group **before** fetching its schedule, not after —
fetching first and joining second leaves a window where a booking made in
between is silently missed (the fetch predates it, the join postdates the
event). On reconnect, SignalR's automatic reconnect does not restore group
membership, so the client must **re-join the group and re-fetch the
schedule**, in that same order, as if starting over.

### Test coverage note

`tests/MeetingRoomBooking.Api.Tests/BookingSignalRTests.cs` verifies this
end-to-end against a real SQL Server database via
`WebApplicationFactory` + `Microsoft.AspNetCore.SignalR.Client`, no Azure
SignalR involved. Most scenarios connect over LongPolling, which is enough
to prove group-scoped delivery, payload content, cross-room isolation, and
exactly-one-event-on-a-race. One scenario specifically connects over real
WebSockets (`TestServer.CreateWebSocketClient()` +
`HttpConnectionOptions.WebSocketFactory`) to prove the `?access_token=`
query-string handoff actually works — LongPolling's `AccessTokenProvider`
sends the token as a normal `Authorization` header on every poll, so a
LongPolling-only suite would never exercise that fallback at all. This
combination needed one adjustment worth knowing if you touch that test: the
SignalR client only auto-appends the access token to the connect URL when
it owns the WebSocket connection itself — once a custom `WebSocketFactory`
is set (required to route through `TestServer`), that responsibility shifts
to the factory, so the test appends the token to the query string itself
inside it. This is genuinely equivalent to what a real browser client does
(a WebSocket upgrade can't carry an Authorization header either way), so
the request that reaches the server — and the `OnMessageReceived` code path
it exercises — is identical. This path is therefore automated-tested, not
just a documented gap.

## Authentication

ASP.NET Core Identity (EF Core stores, `AppDbContext : IdentityDbContext<ApplicationUser>`)
provides user/role storage. Two fixed roles: "Admin" and "User". Auth is
stateless JWT bearer — no cookies, no server-side session, since the
frontend is a separate Angular SPA origin. `POST /api/auth/login` issues a
60-minute token; **refresh tokens are explicitly out of scope** — once a
token expires, the client logs in again. `POST /api/auth/register` always
assigns "User"; there is no self-service path to "Admin" — the only admin
account is optionally seeded at startup from `Seed:AdminEmail`/
`Seed:AdminPassword`. The `"AdminOnly"` authorization policy
(`RequireRole("Admin")`) is registered for future admin-only endpoints.

SignalR's `BookingHub` requires authentication. Since a WebSocket upgrade
can't carry an `Authorization` header, the client sends the JWT as an
`access_token` query-string parameter, read back out in
`JwtBearerEvents.OnMessageReceived` — scoped to `/hubs` paths only, so
ordinary REST calls still require the header.

`Database:MigrateOnStartup` defaults to `true` and applies pending
migrations automatically at boot, wrapped in
`Database.CreateExecutionStrategy()` so retries compose with
`EnableRetryOnFailure`. Fine for the current single-instance F1 App Service
Plan; a genuinely multi-instance deployment should instead migrate as a
CI/CD step, to avoid concurrent instances racing to migrate the same
database at once. Startup seeding (`IdentitySeeder`) is best-effort: if the
database is unreachable, it logs an error and lets the app keep starting
rather than crash-looping.

## Rooms and time slots

A room has a fixed daily set of bookable time slots (`Room` → `TimeSlot`,
one-to-many). `TimeSlot` is immutable — there is no update endpoint for a
slot's times; changing a time means deleting the slot and adding a new one,
so an existing booking can never silently move to another time. `Room`
soft-deletes via `IsActive` rather than a real delete, since slots (and
later, bookings) must keep referencing a real row; deleting an
already-inactive room is a no-op, not a 404, so the delete endpoint stays
idempotent.

Room-name uniqueness "among active rooms" is a filtered unique index
(`WHERE IsActive = 1`), the same technique the booking-concurrency ADR uses
for one-active-booking-per-slot — but unlike booking conflicts, this is
*not* a stated concurrency requirement (room creation is Admin-only,
low-contention), so it's an application-level pre-check with the index only
as a schema-level backstop, not exception-to-409 mapping. Slot overlap and
start<end have no database constraint at all — SQL Server has no native
exclusion constraint for interval overlap without triggers, which would be
overkill here — so both are checked in `TimeSlotValidator`, a small,
DB-free, directly unit-testable helper.

`RoomsController`'s slot/room delete endpoints return 409 when a slot has a
future active booking — see the Bookings section below for the shared
past/future cutoff this reuses, and why deleting a slot with only
historical (past/cancelled) bookings is *also* blocked, via a different
mechanism.

`Office:TimeZone` (IANA id, default `Europe/Bucharest`) backs `IOfficeClock`,
which resolves it once and exposes `Now()`/`Today()` in office local time —
used throughout the booking feature below for bookable-date and past-slot
checks.

## Bookings

`Booking` (`TimeSlotId`, `BookingDate`, `UserId`, `Status` Active/Cancelled,
`CreatedAtUtc`, `CancelledAtUtc`) implements the concurrency design from the
section above and [ADR 0001](docs/adr/0001-booking-concurrency.md): insert
directly, catch the unique-index violation, map to 409 or same-user
idempotent success. `Status` is persisted via `HasConversion<string>()`
specifically so the filtered index's predicate reads `WHERE Status =
'Active'`, matching the ADR literally rather than an opaque int.

Bookable dates run from today through today + 30 days, in office time
(`IOfficeClock`); a slot whose start time has already passed cannot be
booked. That single comparison —
`TimeSlotValidator.HasStarted(date, slotStart, today, nowTimeOfDay)` — is
reused three ways: rejecting a new booking for an already-started slot,
rejecting cancellation of an already-started (i.e. past) booking, and
`RoomsController`'s slot/room delete 409s, which block on any *future*
active booking. A slot/room can still have `Restrict`-FK-protected
*historical* booking rows the future-only check doesn't cover; deleting
such a slot hits the FK constraint, which `RoomsController.DeleteSlot`
catches and turns into a 409 too, rather than a 500.

`BookingService` returns a single shared result type (`BookingResult`/
`BookingResultKind`: `Created`, `AlreadyYours`, `Conflict`, `NotFound`,
`Invalid`, plus `Cancelled`/`Forbidden` for cancellation) — the controller
only maps this to HTTP status codes. After a successful create or cancel,
`IBookingNotifier.SlotChangedAsync` is called (a no-op today; real SignalR
comes in a later PR), only after the transaction commits, per this file's
SignalR rule above.

`GET /api/rooms/{id}/schedule` reports each slot as Free, Booked, Mine, or
Past — **never** who booked it, for any caller including Admins (Admins get
that detail from `GET /api/bookings` instead, which is Admin-only and
explicitly for that purpose).

## Configuration keys

- `ConnectionStrings:DefaultConnection`
- `Azure:SignalR:ConnectionString`
- `Jwt:Issuer`, `Jwt:Audience` (not secret — defaulted in `appsettings.json`)
- `Jwt:SigningKey` (secret, min 32 bytes; empty by default, must be set via
  user-secrets locally / an Azure App Setting in production)
- `Seed:AdminEmail`, `Seed:AdminPassword` (optional; admin seeding is
  skipped with a startup warning if either is absent)
- `Database:MigrateOnStartup` (bool, defaults to `true`)
- `Office:TimeZone` (IANA id, not secret, defaults to `Europe/Bucharest`)
- CORS must allow the specific frontend origin and set `AllowCredentials`
  (required for the SignalR connection).

## Secrets

Secrets never go into git. For local backend development, use
`dotnet user-secrets`. In deployed environments, use Azure App Service
Application Settings. See [.gitignore](.gitignore) for the patterns this
enforces.

## Commit convention

- Small, atomic commits — one logical change per commit.
- [Conventional Commits](https://www.conventionalcommits.org/) style:
  `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`, etc.
- Every message explains **what** changed and **why**, not just what.

## Workflow

- Propose a plan before larger changes.
- Run the relevant build/tests before committing.
- Never commit without the user's review.
- Keep the "Build / run / test" section above up to date as real projects are
  scaffolded.
