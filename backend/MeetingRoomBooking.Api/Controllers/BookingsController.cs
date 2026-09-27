using MeetingRoomBooking.Api.Data;
using MeetingRoomBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Api.Controllers;

/// <summary>
/// Booking creation, cancellation, and listing. The controller only maps
/// <see cref="BookingResult"/> to HTTP status codes — all business logic
/// and the concurrency handling live in <see cref="BookingService"/>.
/// </summary>
[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly BookingService _bookingService;
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary>Initializes a new instance of the <see cref="BookingsController"/> class.</summary>
    public BookingsController(AppDbContext dbContext, BookingService bookingService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _bookingService = bookingService;
        _userManager = userManager;
    }

    /// <summary>
    /// Books a time slot for a date. Same-user retries of an already-held
    /// slot return 200 with the existing booking rather than a conflict;
    /// a different user's active booking on the same slot/date returns 409.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateBooking(CreateBookingRequest request)
    {
        var userId = _userManager.GetUserId(User)!;
        var result = await _bookingService.CreateAsync(userId, request.TimeSlotId, request.Date);

        return result.Kind switch
        {
            BookingResultKind.Created => StatusCode(StatusCodes.Status201Created, ToResponse(result.Booking!)),
            BookingResultKind.AlreadyYours => Ok(ToResponse(result.Booking!)),
            BookingResultKind.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Booking conflict", detail: result.Message),
            BookingResultKind.NotFound => NotFound(),
            BookingResultKind.Invalid => InvalidResult(result),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Cancels a booking (soft cancel). The owner may cancel their own
    /// booking; an Admin may cancel any booking. Past bookings cannot be
    /// cancelled.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> CancelBooking(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var isAdmin = User.IsInRole("Admin");
        var result = await _bookingService.CancelAsync(id, userId, isAdmin);

        return result.Kind switch
        {
            BookingResultKind.Cancelled => NoContent(),
            BookingResultKind.NotFound => NotFound(),
            BookingResultKind.Forbidden => Forbid(),
            BookingResultKind.Invalid => InvalidResult(result),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>The caller's own bookings — active and cancelled (full history), newest first.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<BookingResponse>>> GetMyBookings()
    {
        var userId = _userManager.GetUserId(User)!;
        var bookings = await _dbContext.Bookings
            .Where(b => b.UserId == userId)
            .Include(b => b.TimeSlot)
            .ThenInclude(slot => slot!.Room)
            .OrderByDescending(b => b.BookingDate)
            .ThenBy(b => b.TimeSlot!.StartTime)
            .ToListAsync();

        return Ok(bookings.Select(ToResponse).ToList());
    }

    /// <summary>All bookings across all users, with the booking user's email. Admin-only.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminBookingResponse>>> GetAllBookings([FromQuery] DateOnly? date, [FromQuery] int? roomId)
    {
        var query = _dbContext.Bookings
            .Include(b => b.TimeSlot)
            .ThenInclude(slot => slot!.Room)
            .Include(b => b.User)
            .AsQueryable();

        if (date is not null)
        {
            query = query.Where(b => b.BookingDate == date);
        }

        if (roomId is not null)
        {
            query = query.Where(b => b.TimeSlot!.RoomId == roomId);
        }

        var bookings = await query
            .OrderByDescending(b => b.BookingDate)
            .ThenBy(b => b.TimeSlot!.StartTime)
            .ToListAsync();

        return Ok(bookings.Select(ToAdminResponse).ToList());
    }

    private IActionResult InvalidResult(BookingResult result)
    {
        ModelState.AddModelError(result.Field ?? string.Empty, result.Message ?? "Invalid request.");
        return ValidationProblem(ModelState);
    }

    private static BookingResponse ToResponse(Booking booking) => new(
        booking.Id,
        booking.TimeSlot!.RoomId,
        booking.TimeSlot.Room!.Name,
        booking.TimeSlotId,
        booking.TimeSlot.StartTime,
        booking.TimeSlot.EndTime,
        booking.BookingDate,
        booking.Status,
        booking.CreatedAtUtc,
        booking.CancelledAtUtc);

    private static AdminBookingResponse ToAdminResponse(Booking booking) => new(
        booking.Id,
        booking.UserId,
        booking.User!.Email!,
        booking.TimeSlot!.RoomId,
        booking.TimeSlot.Room!.Name,
        booking.TimeSlotId,
        booking.TimeSlot.StartTime,
        booking.TimeSlot.EndTime,
        booking.BookingDate,
        booking.Status,
        booking.CreatedAtUtc,
        booking.CancelledAtUtc);
}

/// <summary>Request body for <see cref="BookingsController.CreateBooking"/>.</summary>
public record CreateBookingRequest(int TimeSlotId, DateOnly Date);

/// <summary>A booking, as returned to the booking owner.</summary>
public record BookingResponse(
    int Id,
    int RoomId,
    string RoomName,
    int TimeSlotId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly Date,
    BookingStatus Status,
    DateTime CreatedAtUtc,
    DateTime? CancelledAtUtc);

/// <summary>A booking, as returned to an Admin — includes the booking user's identity.</summary>
public record AdminBookingResponse(
    int Id,
    string UserId,
    string UserEmail,
    int RoomId,
    string RoomName,
    int TimeSlotId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly Date,
    BookingStatus Status,
    DateTime CreatedAtUtc,
    DateTime? CancelledAtUtc);
