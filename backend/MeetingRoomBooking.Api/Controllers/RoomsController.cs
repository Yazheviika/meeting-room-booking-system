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

    /// <summary>Initializes a new instance of the <see cref="RoomsController"/> class.</summary>
    public RoomsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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

        // TODO (next PR): reject with 409 if this slot has future active bookings.
        _dbContext.TimeSlots.Remove(slot);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Soft-deletes a room. Idempotent — deleting an already-inactive room is a no-op.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var room = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == id);
        if (room is null)
        {
            return NotFound();
        }

        // TODO (next PR): reject with 409 if the room has future active bookings.
        room.IsActive = false;
        await _dbContext.SaveChangesAsync();

        return NoContent();
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
