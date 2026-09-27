using MeetingRoomBooking.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Api.Services;

/// <summary>The outcome of a create or cancel operation on <see cref="BookingService"/>.</summary>
public enum BookingResultKind
{
    /// <summary>A new booking was created.</summary>
    Created,

    /// <summary>The caller already holds the requested slot; no new row was created.</summary>
    AlreadyYours,

    /// <summary>Someone else holds the slot (or held it, and the gap couldn't be resolved in the caller's favor).</summary>
    Conflict,

    /// <summary>The referenced room/slot/booking doesn't exist (or the room is inactive).</summary>
    NotFound,

    /// <summary>The request violates a business rule (date out of range, slot already started).</summary>
    Invalid,

    /// <summary>The booking was cancelled.</summary>
    Cancelled,

    /// <summary>The caller may not act on this booking (not the owner, not an Admin).</summary>
    Forbidden,
}

/// <summary>Result of a <see cref="BookingService"/> operation — see <see cref="BookingResultKind"/> for the possible outcomes.</summary>
public class BookingResult
{
    private BookingResult(BookingResultKind kind, Booking? booking, string? field, string? message)
    {
        Kind = kind;
        Booking = booking;
        Field = field;
        Message = message;
    }

    /// <summary>Which outcome this is.</summary>
    public BookingResultKind Kind { get; }

    /// <summary>The affected booking, for <see cref="BookingResultKind.Created"/>, <see cref="BookingResultKind.AlreadyYours"/>, and <see cref="BookingResultKind.Cancelled"/>.</summary>
    public Booking? Booking { get; }

    /// <summary>The offending request field, for <see cref="BookingResultKind.Invalid"/> (used to build a <c>ValidationProblem</c>).</summary>
    public string? Field { get; }

    /// <summary>A human-readable message, for every non-success kind.</summary>
    public string? Message { get; }

    /// <summary>A new booking was created.</summary>
    public static BookingResult Created(Booking booking) => new(BookingResultKind.Created, booking, null, null);

    /// <summary>The caller already holds this slot.</summary>
    public static BookingResult AlreadyYours(Booking booking) => new(BookingResultKind.AlreadyYours, booking, null, null);

    /// <summary>Someone else holds the slot.</summary>
    public static BookingResult Conflict(string message) => new(BookingResultKind.Conflict, null, null, message);

    /// <summary>The room/slot/booking doesn't exist.</summary>
    public static BookingResult NotFound(string message) => new(BookingResultKind.NotFound, null, null, message);

    /// <summary>The request violates a business rule.</summary>
    public static BookingResult Invalid(string field, string message) => new(BookingResultKind.Invalid, null, field, message);

    /// <summary>The booking was cancelled.</summary>
    public static BookingResult Cancelled(Booking booking) => new(BookingResultKind.Cancelled, booking, null, null);

    /// <summary>The caller may not act on this booking.</summary>
    public static BookingResult Forbidden(string message) => new(BookingResultKind.Forbidden, null, null, message);
}

/// <summary>
/// Booking creation and cancellation, per docs/adr/0001-booking-concurrency.md.
/// The controller only maps <see cref="BookingResult"/> to HTTP — every
/// business rule and the concurrency handling itself live here.
/// </summary>
public class BookingService
{
    private const int MaxBookableDaysAhead = 30;
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;
    private const string ActiveSlotIndexName = "UX_Bookings_ActiveSlot";

    private readonly AppDbContext _dbContext;
    private readonly IOfficeClock _officeClock;
    private readonly IBookingNotifier _notifier;
    private readonly ILogger<BookingService> _logger;

    /// <summary>Initializes a new instance of the <see cref="BookingService"/> class.</summary>
    public BookingService(AppDbContext dbContext, IOfficeClock officeClock, IBookingNotifier notifier, ILogger<BookingService> logger)
    {
        _dbContext = dbContext;
        _officeClock = officeClock;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// Creates a booking for the given user/slot/date. Inserts directly —
    /// no pre-check — and relies on the database's unique index to reject
    /// a genuine conflict; see the ADR for why this is deliberately not a
    /// "check if free, then insert."
    /// </summary>
    public async Task<BookingResult> CreateAsync(string userId, int timeSlotId, DateOnly date)
    {
        var today = _officeClock.Today();
        if (date < today || date > today.AddDays(MaxBookableDaysAhead))
        {
            return BookingResult.Invalid("date", $"Date must be between {today:yyyy-MM-dd} and {today.AddDays(MaxBookableDaysAhead):yyyy-MM-dd}.");
        }

        var slot = await _dbContext.TimeSlots
            .Include(s => s.Room)
            .FirstOrDefaultAsync(s => s.Id == timeSlotId);

        if (slot?.Room is null || !slot.Room.IsActive)
        {
            return BookingResult.NotFound("Time slot not found.");
        }

        var nowTimeOfDay = TimeOnly.FromDateTime(_officeClock.Now());
        if (TimeSlotValidator.HasStarted(date, slot.StartTime, today, nowTimeOfDay))
        {
            return BookingResult.Invalid("timeSlotId", "This slot's start time has already passed.");
        }

        var booking = new Booking
        {
            TimeSlotId = timeSlotId,
            BookingDate = date,
            UserId = userId,
            Status = BookingStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            // Set explicitly (rather than relying on EF's change-tracker
            // fixup) so the controller can always map Room/slot details
            // off the returned booking without a second query.
            TimeSlot = slot,
        };

        _dbContext.Bookings.Add(booking);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsActiveSlotUniqueViolation(ex))
        {
            // The insert never happened — detach so a later SaveChanges in
            // this same scope (e.g. a caller retrying) can't pick this
            // half-added entity back up.
            _dbContext.Entry(booking).State = EntityState.Detached;

            var existing = await _dbContext.Bookings.FirstOrDefaultAsync(b =>
                b.TimeSlotId == timeSlotId && b.BookingDate == date && b.Status == BookingStatus.Active);

            if (existing is null)
            {
                // The winner cancelled in the gap between our violation and
                // this re-read. Not our slot either way — a clean conflict,
                // never a null-reference/500.
                return BookingResult.Conflict("This slot is no longer available.");
            }

            if (existing.UserId == userId)
            {
                existing.TimeSlot ??= slot;
                return BookingResult.AlreadyYours(existing);
            }

            return BookingResult.Conflict("This slot is already booked.");
        }

        await NotifySlotChangedAsync(slot.RoomId, date, timeSlotId, isBooked: true);
        return BookingResult.Created(booking);
    }

    /// <summary>
    /// Cancels a booking. The owner may cancel their own booking; an Admin
    /// may cancel any booking. Cancelling an already-cancelled booking is a
    /// harmless no-op (idempotent, matching the room/slot delete endpoints'
    /// convention). Past bookings cannot be cancelled.
    /// </summary>
    public async Task<BookingResult> CancelAsync(int bookingId, string requestingUserId, bool isAdmin)
    {
        var booking = await _dbContext.Bookings
            .Include(b => b.TimeSlot)
            .ThenInclude(slot => slot!.Room)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking is null)
        {
            return BookingResult.NotFound("Booking not found.");
        }

        if (booking.UserId != requestingUserId && !isAdmin)
        {
            return BookingResult.Forbidden("You can only cancel your own bookings.");
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            return BookingResult.Cancelled(booking);
        }

        var today = _officeClock.Today();
        var nowTimeOfDay = TimeOnly.FromDateTime(_officeClock.Now());
        if (TimeSlotValidator.HasStarted(booking.BookingDate, booking.TimeSlot!.StartTime, today, nowTimeOfDay))
        {
            return BookingResult.Invalid("id", "Past bookings cannot be cancelled.");
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await NotifySlotChangedAsync(booking.TimeSlot.RoomId, booking.BookingDate, booking.TimeSlotId, isBooked: false);
        return BookingResult.Cancelled(booking);
    }

    /// <summary>
    /// Calls <see cref="IBookingNotifier.SlotChangedAsync"/>, catching and
    /// logging any failure rather than letting it propagate: the booking
    /// itself already committed, so a broadcast failure must never turn a
    /// successful request into a 500.
    /// </summary>
    private async Task NotifySlotChangedAsync(int roomId, DateOnly date, int timeSlotId, bool isBooked)
    {
        try
        {
            await _notifier.SlotChangedAsync(roomId, date, timeSlotId, isBooked);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to notify slot change for room {RoomId}, slot {TimeSlotId}, date {Date}.",
                roomId,
                timeSlotId,
                date);
        }
    }

    private static bool IsActiveSlotUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        (sqlEx.Number == UniqueIndexViolation || sqlEx.Number == UniqueConstraintViolation) &&
        sqlEx.Message.Contains(ActiveSlotIndexName, StringComparison.OrdinalIgnoreCase);
}
