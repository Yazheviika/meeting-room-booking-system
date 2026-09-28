using System.Security.Claims;
using MeetingRoomBooking.Api.Data;
using MeetingRoomBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Api.Controllers;

/// <summary>
/// Room and time-slot management. Viewing is open to any authenticated
/// user; creating, editing, and removing rooms/slots is Admin-only.
/// </summary>
[ApiController]
[Route("api/rooms")]
[Authorize]
public class RoomsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IOfficeClock _officeClock;

    /// <summary>Initializes a new instance of the <see cref="RoomsController"/> class.</summary>
    public RoomsController(AppDbContext dbContext, IOfficeClock officeClock)
    {
        _dbContext = dbContext;
        _officeClock = officeClock;
    }

    /// <summary>Lists all active rooms with their time slots.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoomResponse>>> GetRooms()
    {
        var rooms = await _dbContext.Rooms
            .Where(room => room.IsActive)
            .Include(room => room.TimeSlots)
            .OrderBy(room => room.Name)
            .ToListAsync();

        return Ok(rooms.Select(ToResponse).ToList());
    }

    /// <summary>Gets a single active room with its time slots.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoomResponse>> GetRoom(int id)
    {
        var room = await _dbContext.Rooms
            .Include(r => r.TimeSlots)
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);

        if (room is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(room));
    }

    /// <summary>
    /// Every slot of a room on a given date, with a status of Free, Booked,
    /// Mine, or Past — never who else booked a slot, regardless of the
    /// caller's role (Admins see that detail via GET /api/bookings instead).
    /// </summary>
    [HttpGet("{id:int}/schedule")]
    public async Task<ActionResult<IReadOnlyList<ScheduleSlotResponse>>> GetSchedule(int id, [FromQuery] DateOnly date)
    {
        var room = await _dbContext.Rooms
            .Include(r => r.TimeSlots)
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);

        if (room is null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var today = _officeClock.Today();
        var nowTimeOfDay = TimeOnly.FromDateTime(_officeClock.Now());

        var activeBookings = await _dbContext.Bookings
            .Where(b => b.TimeSlot!.RoomId == id && b.BookingDate == date && b.Status == BookingStatus.Active)
            .ToListAsync();
        var bookingsBySlot = activeBookings.ToDictionary(b => b.TimeSlotId);

        var schedule = room.TimeSlots
            .OrderBy(slot => slot.StartTime)
            .Select(slot =>
            {
                if (TimeSlotValidator.HasStarted(date, slot.StartTime, today, nowTimeOfDay))
                {
                    return new ScheduleSlotResponse(slot.Id, slot.StartTime, slot.EndTime, SlotScheduleStatus.Past, null);
                }

                if (!bookingsBySlot.TryGetValue(slot.Id, out var booking))
                {
                    return new ScheduleSlotResponse(slot.Id, slot.StartTime, slot.EndTime, SlotScheduleStatus.Free, null);
                }

                var isMine = booking.UserId == userId;
                var status = isMine ? SlotScheduleStatus.Mine : SlotScheduleStatus.Booked;
                // BookingId is only ever populated for the caller's own booking —
                // this is the caller's own id, not "who booked it," so it doesn't
                // conflict with this endpoint's never-reveal-the-booker rule.
                return new ScheduleSlotResponse(slot.Id, slot.StartTime, slot.EndTime, status, isMine ? booking.Id : null);
            })
            .ToList();

        return Ok(schedule);
    }

    /// <summary>Creates a room, optionally with its initial time slots.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    public async Task<ActionResult<RoomResponse>> CreateRoom(CreateRoomRequest request)
    {
        await ValidateNameAsync(request.Name, excludeRoomId: null);
        ValidateCapacity(request.Capacity);
        ValidateNewSlots(request.TimeSlots);

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var room = new Room
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            Capacity = request.Capacity,
            TimeSlots = request.TimeSlots
                .Select(slot => new TimeSlot { StartTime = slot.StartTime, EndTime = slot.EndTime })
                .ToList(),
        };

        _dbContext.Rooms.Add(room);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRoom), new { id = room.Id }, ToResponse(room));
    }

    /// <summary>Updates a room's name, description, and capacity. Slots are managed separately.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<RoomResponse>> UpdateRoom(int id, UpdateRoomRequest request)
    {
        var room = await _dbContext.Rooms
            .Include(r => r.TimeSlots)
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);

        if (room is null)
        {
            return NotFound();
        }

        await ValidateNameAsync(request.Name, excludeRoomId: id);
        ValidateCapacity(request.Capacity);

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        room.Name = request.Name.Trim();
        room.Description = request.Description;
        room.Capacity = request.Capacity;

        await _dbContext.SaveChangesAsync();

        return Ok(ToResponse(room));
    }

    /// <summary>Adds a time slot to a room.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("{id:int}/slots")]
    public async Task<ActionResult<TimeSlotResponse>> AddSlot(int id, CreateTimeSlotRequest request)
    {
        var room = await _dbContext.Rooms
            .Include(r => r.TimeSlots)
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);

        if (room is null)
        {
            return NotFound();
        }

        if (!TimeSlotValidator.IsValidRange(request.StartTime, request.EndTime))
        {
            ModelState.AddModelError(nameof(request.EndTime), "Start time must be before end time.");
        }
        else if (room.TimeSlots.Any(existing =>
                     TimeSlotValidator.Overlaps(request.StartTime, request.EndTime, existing.StartTime, existing.EndTime)))
        {
            ModelState.AddModelError(nameof(request.StartTime), "This slot overlaps an existing slot for this room.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var slot = new TimeSlot { RoomId = room.Id, StartTime = request.StartTime, EndTime = request.EndTime };
        _dbContext.TimeSlots.Add(slot);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRoom), new { id = room.Id }, ToResponse(slot));
    }

    /// <summary>Removes a time slot from a room.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:int}/slots/{slotId:int}")]
    public async Task<IActionResult> DeleteSlot(int id, int slotId)
    {
        var slot = await _dbContext.TimeSlots.FirstOrDefaultAsync(s => s.Id == slotId && s.RoomId == id);
        if (slot is null)
        {
            return NotFound();
        }

        if (await HasFutureActiveBookingAsync(slot))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Slot has future bookings",
                detail: "This slot has future active bookings and cannot be deleted.");
        }

        _dbContext.TimeSlots.Remove(slot);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Backstop for booking history the future-only check above
            // doesn't cover: the FK from Booking to TimeSlot is Restrict,
            // so a slot with any (even past/cancelled) booking rows can't
            // actually be removed. Caught here rather than left as a 500.
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Slot has booking history",
                detail: "This slot has booking history and cannot be deleted.");
        }

        return NoContent();
    }

    /// <summary>Soft-deletes a room. Idempotent — deleting an already-inactive room is a no-op.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var room = await _dbContext.Rooms
            .Include(r => r.TimeSlots)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (room is null)
        {
            return NotFound();
        }

        foreach (var slot in room.TimeSlots)
        {
            if (await HasFutureActiveBookingAsync(slot))
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Room has future bookings",
                    detail: "This room has future active bookings and cannot be deleted.");
            }
        }

        room.IsActive = false;
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// True if the given slot has an active booking on a date/time that
    /// hasn't happened yet. Fetches the slot's active bookings and applies
    /// the shared past/future cutoff client-side (bounded per slot — at
    /// most one active booking per calendar date, ever).
    /// </summary>
    private async Task<bool> HasFutureActiveBookingAsync(TimeSlot slot)
    {
        var activeBookingDates = await _dbContext.Bookings
            .Where(b => b.TimeSlotId == slot.Id && b.Status == BookingStatus.Active)
            .Select(b => b.BookingDate)
            .ToListAsync();

        var today = _officeClock.Today();
        var nowTimeOfDay = TimeOnly.FromDateTime(_officeClock.Now());
        return activeBookingDates.Any(date => !TimeSlotValidator.HasStarted(date, slot.StartTime, today, nowTimeOfDay));
    }

    private async Task ValidateNameAsync(string name, int? excludeRoomId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Name is required.");
            return;
        }

        var trimmedName = name.Trim();
        var nameTaken = await _dbContext.Rooms.AnyAsync(r =>
            r.IsActive && r.Name == trimmedName && (excludeRoomId == null || r.Id != excludeRoomId));

        if (nameTaken)
        {
            ModelState.AddModelError(nameof(name), $"A room named '{trimmedName}' already exists.");
        }
    }

    private void ValidateCapacity(int capacity)
    {
        if (capacity <= 0)
        {
            ModelState.AddModelError(nameof(capacity), "Capacity must be greater than zero.");
        }
    }

    private void ValidateNewSlots(IReadOnlyList<CreateTimeSlotRequest> slots)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            if (!TimeSlotValidator.IsValidRange(slots[i].StartTime, slots[i].EndTime))
            {
                ModelState.AddModelError($"{nameof(CreateRoomRequest.TimeSlots)}[{i}]", "Start time must be before end time.");
            }
        }

        for (var i = 0; i < slots.Count; i++)
        {
            for (var j = i + 1; j < slots.Count; j++)
            {
                if (TimeSlotValidator.Overlaps(slots[i].StartTime, slots[i].EndTime, slots[j].StartTime, slots[j].EndTime))
                {
                    ModelState.AddModelError(
                        $"{nameof(CreateRoomRequest.TimeSlots)}[{j}]",
                        $"Slot overlaps another slot in the same request (index {i}).");
                }
            }
        }
    }

    private static RoomResponse ToResponse(Room room) => new(
        room.Id,
        room.Name,
        room.Description,
        room.Capacity,
        room.TimeSlots.OrderBy(slot => slot.StartTime).Select(ToResponse).ToList());

    private static TimeSlotResponse ToResponse(TimeSlot slot) => new(slot.Id, slot.StartTime, slot.EndTime);
}

/// <summary>A room and its time slots.</summary>
public record RoomResponse(int Id, string Name, string? Description, int Capacity, IReadOnlyList<TimeSlotResponse> TimeSlots);

/// <summary>A single bookable time slot.</summary>
public record TimeSlotResponse(int Id, TimeOnly StartTime, TimeOnly EndTime);

/// <summary>Request body for <see cref="RoomsController.CreateRoom"/>.</summary>
public record CreateRoomRequest(string Name, string? Description, int Capacity, IReadOnlyList<CreateTimeSlotRequest> TimeSlots);

/// <summary>Request body for <see cref="RoomsController.UpdateRoom"/>.</summary>
public record UpdateRoomRequest(string Name, string? Description, int Capacity);

/// <summary>Request body for <see cref="RoomsController.AddSlot"/>, and for each slot in <see cref="CreateRoomRequest"/>.</summary>
public record CreateTimeSlotRequest(TimeOnly StartTime, TimeOnly EndTime);

/// <summary>A slot's booking status on a given date, for the schedule view. Never reveals who booked it.</summary>
public enum SlotScheduleStatus
{
    /// <summary>Nobody has booked this slot on this date.</summary>
    Free,

    /// <summary>Someone other than the caller has booked this slot on this date.</summary>
    Booked,

    /// <summary>The caller has booked this slot on this date.</summary>
    Mine,

    /// <summary>This date/slot combination has already happened; it cannot be booked or cancelled.</summary>
    Past,
}

/// <summary>
/// One slot's schedule entry, returned by <see cref="RoomsController.GetSchedule"/>.
/// <see cref="BookingId"/> is populated only when <see cref="Status"/> is
/// <see cref="SlotScheduleStatus.Mine"/> (null otherwise) — it's the
/// caller's own booking id, needed to call <c>DELETE /api/bookings/{id}</c>,
/// not an identity disclosure about anyone else's booking.
/// </summary>
public record ScheduleSlotResponse(int TimeSlotId, TimeOnly StartTime, TimeOnly EndTime, SlotScheduleStatus Status, int? BookingId);
