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
overwrite, never a 500.

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

## Configuration keys

- `ConnectionStrings:DefaultConnection`
- `Azure:SignalR:ConnectionString`
- `Jwt:Issuer`, `Jwt:Audience` (not secret — defaulted in `appsettings.json`)
- `Jwt:SigningKey` (secret, min 32 bytes; empty by default, must be set via
  user-secrets locally / an Azure App Setting in production)
- `Seed:AdminEmail`, `Seed:AdminPassword` (optional; admin seeding is
  skipped with a startup warning if either is absent)
- `Database:MigrateOnStartup` (bool, defaults to `true`)
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
