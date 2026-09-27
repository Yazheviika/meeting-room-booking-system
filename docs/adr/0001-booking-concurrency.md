# ADR 0001: Booking concurrency control

## Status

Accepted

## Context

Data model (to be implemented): `Room` (resource), `TimeSlot` (a fixed daily
slot of a room, e.g. 09:00-10:00), `Booking` (`TimeSlotId`, `BookingDate`,
`UserId`, `Status` Active/Cancelled).

Per [docs/TASK.md](../TASK.md): when two or more requests target the same
slot at effectively the same time, exactly one must succeed and the rest
must receive a clear conflict response — never a silent overwrite, never a
server error. The chosen mechanism must be a deliberate, explained design
decision, not incidental ORM/database behavior. A naive "check if free, then
insert" as two unprotected steps explicitly does not satisfy this.

This is sharper on Azure SQL than it first appears: `READ_COMMITTED_SNAPSHOT`
is ON by default there, so a plain `SELECT` inside a transaction reads a
row-versioned snapshot and does not block on another transaction's
uncommitted insert. Two concurrent requests can each run "check if free" at
default isolation, both see the slot as free, and both proceed to insert —
RCSI makes the forbidden check-then-insert pattern fail silently rather than
just being theoretically racy.

## Decision

A filtered **unique index**:

```sql
CREATE UNIQUE INDEX UX_Bookings_ActiveSlot
    ON Bookings (TimeSlotId, BookingDate)
    WHERE Status = 'Active';
```

This is the sole correctness guarantee: the database enforces "at most one
active booking per slot per date," independent of application code, number
of backend instances, or isolation level. It cannot be bypassed by a future
code path (a new endpoint, a data import, a manual row insert) the way an
application-level check can.

Conflict handling: the booking service inserts directly — no pre-check. A
unique-index violation (SQL Server error 2601 or 2627, detected specifically
for this index) is caught and mapped to HTTP 409 with a clear message,
never a 500.

Idempotency: on a unique violation, the service re-reads the conflicting
active booking. If it belongs to the *same* user, the request returns
success with that existing booking instead of 409. This covers three cases
that are not "someone else took the slot": EF's `EnableRetryOnFailure`
re-executing an `INSERT` whose commit actually succeeded (the connection
dropped after commit, before acknowledgment), a double-click, and a client
retry after a timeout. A 409 is returned only when the existing active
booking belongs to a *different* user.

## Alternatives considered

- **Check-then-insert in a transaction at default isolation** — rejected:
  broken specifically by Azure SQL's default RCSI (see Context); both
  requests observe the slot as free.
- **Check-then-insert under `SERIALIZABLE`** — rejected: does prevent double
  booking, but under contention SQL Server resolves the conflict via
  deadlock (error 1205) rather than a clean, predictable 409. Turning a
  deadlock into a correct HTTP response requires its own detection and
  retry/translation logic, which is more moving parts than the alternative
  below for no extra correctness benefit.
- **`UPDLOCK, HOLDLOCK` pessimistic locking** — valid, and would also
  satisfy the requirement, but the guarantee would then live in application
  code (raw SQL or `FromSqlRaw` with table hints, a manual transaction,
  wrapped in `Database.CreateExecutionStrategy()` to compose with
  `EnableRetryOnFailure`) rather than in the schema. That is meaningfully
  more code to write, test, and explain in review for a rule the unique
  index already expresses directly and enforces unconditionally.
- **Optimistic concurrency via a `rowversion` column** — rejected: this
  pattern concurrency-checks *updates* to an existing row, which requires
  one pre-materialized row per (slot, date) to update. That means
  pre-generating and storing a "free" row for every slot on every future
  date, duplicating booking state (a `rowversion` row plus the `Booking`
  row it supposedly represents) instead of simply not having a row until a
  booking exists.

## Consequences

- The guarantee is schema-level, so it survives code changes that the
  original author didn't anticipate — a new endpoint, a bulk import script,
  or a manual `INSERT` all still hit the same index.
- Conflict *detection* in application code is coupled to SQL Server error
  numbers 2601/2627. That coupling must stay scoped to catching violations
  of this specific index (e.g. checking the index name in the error message
  or re-querying for the conflicting row) so it doesn't accidentally swallow
  unrelated constraint violations as 409s.
- The automated concurrency test (required by TASK.md item 6) fires N
  parallel booking requests for the same slot/date and asserts exactly one
  succeeds and the rest come back 409 — this exercises the full
  insert-and-catch path, including the idempotency branch when a caller's
  own retry lands as one of the "duplicates."
- A second, narrower test inserts a duplicate active booking for the same
  slot/date directly through `DbContext.SaveChangesAsync`, bypassing the
  booking service entirely. This proves the index itself rejects the
  duplicate — that the guarantee holds even when application logic is
  skipped — independent of whatever the service-layer conflict handling
  does.
