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
- Health check: `GET /health`
- Frontend (not yet scaffolded): `npm install`, `ng serve`, `npm test` (run from `frontend/`)

## Coding conventions

- **C#**: nullable reference types enabled, `async`/`await` for all I/O, EF Core
  migrations for every schema change (never edit the database out-of-band).
- **Angular**: standalone components, strict TypeScript.
- **Documentation**: XML doc comments on public types/methods. Comment the
  *why*, not the *what*, especially around concurrency decisions.

## Concurrency (critical)

Double-booking must be prevented by an explicit database-level mechanism —
a unique constraint, explicit locking, or optimistic concurrency via a
rowversion/concurrency token. A naive "check if free, then insert" as two
unprotected steps is forbidden. Any change touching the booking flow must
keep the automated concurrency test green. A conflicting booking request must
return HTTP 409 with a clear message — never a silent overwrite, never a 500.

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

## Configuration keys

- `ConnectionStrings:DefaultConnection`
- `Azure:SignalR:ConnectionString`
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
