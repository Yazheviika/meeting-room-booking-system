using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingRoomBooking.Api.Controllers;

namespace MeetingRoomBooking.Api.Tests;

/// <summary>
/// <c>GET /api/rooms/{id}/schedule</c> had no test coverage before this —
/// covers all four <see cref="SlotScheduleStatus"/> values, with particular
/// attention to <see cref="ScheduleSlotResponse.BookingId"/>: populated for
/// the caller's own ("Mine") slot, and explicitly null everywhere else,
/// including "Booked" — proving a caller never learns anyone else's
/// booking id, only their own.
/// </summary>
public class ScheduleEndpointTests : IClassFixture<BookingSqlServerFixture>
{
    private readonly BookingSqlServerFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="ScheduleEndpointTests"/> class.</summary>
    public ScheduleEndpointTests(BookingSqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>An unbooked slot reports "Free" with no booking id.</summary>
    [Fact]
    public async Task FreeSlot_HasNullBookingId()
    {
        var (_, token) = await _fixture.Factory.CreateUserAndTokenAsync($"free-{Guid.NewGuid():N}@example.com");
        var (roomId, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Schedule Free Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var schedule = await GetScheduleAsync(client, roomId, date);
        var slot = Assert.Single(schedule, s => s.TimeSlotId == slotId);

        Assert.Equal(SlotScheduleStatus.Free, slot.Status);
        Assert.Null(slot.BookingId);
    }

    /// <summary>The caller's own active booking reports "Mine" with that booking's real id.</summary>
    [Fact]
    public async Task MineSlot_HasTheCallersOwnBookingId()
    {
        var (_, token) = await _fixture.Factory.CreateUserAndTokenAsync($"mine-{Guid.NewGuid():N}@example.com");
        var (roomId, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Schedule Mine Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var bookResponse = await client.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
        var booking = await bookResponse.Content.ReadFromJsonAsync<BookingIdOnly>();

        var schedule = await GetScheduleAsync(client, roomId, date);
        var slot = Assert.Single(schedule, s => s.TimeSlotId == slotId);

        Assert.Equal(SlotScheduleStatus.Mine, slot.Status);
        Assert.Equal(booking!.Id, slot.BookingId);
    }

    /// <summary>Another user's active booking reports "Booked" with a null booking id — never leaks their id.</summary>
    [Fact]
    public async Task BookedByAnotherUser_HasNullBookingId()
    {
        var (_, ownerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"owner-{Guid.NewGuid():N}@example.com");
        var (_, viewerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"viewer-{Guid.NewGuid():N}@example.com");
        var (roomId, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Schedule Booked Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();

        var ownerClient = _fixture.Factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        await ownerClient.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });

        var viewerClient = _fixture.Factory.CreateClient();
        viewerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);

        var schedule = await GetScheduleAsync(viewerClient, roomId, date);
        var slot = Assert.Single(schedule, s => s.TimeSlotId == slotId);

        Assert.Equal(SlotScheduleStatus.Booked, slot.Status);
        Assert.Null(slot.BookingId);
    }

    /// <summary>A slot on a past date reports "Past" with no booking id.</summary>
    [Fact]
    public async Task PastSlot_HasNullBookingId()
    {
        var (_, token) = await _fixture.Factory.CreateUserAndTokenAsync($"past-{Guid.NewGuid():N}@example.com");
        var (roomId, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Schedule Past Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Yesterday is unconditionally "Past" regardless of what time this
        // test happens to run at (unlike "today", where whether a given
        // slot has started yet depends on the current time of day) — no
        // booking can even be created for a past date via the API, so this
        // only exercises the no-booking-at-all path.
        var yesterday = _fixture.Factory.Tomorrow().AddDays(-2);

        var schedule = await GetScheduleAsync(client, roomId, yesterday);
        var slot = Assert.Single(schedule, s => s.TimeSlotId == slotId);

        Assert.Equal(SlotScheduleStatus.Past, slot.Status);
        Assert.Null(slot.BookingId);
    }

    // The app serializes SlotScheduleStatus as a string (a global
    // JsonStringEnumConverter registered in Program.cs) — HttpClient's
    // default ReadFromJsonAsync options don't know that, so this test's
    // own read needs the same converter to deserialize the enum back.
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task<List<ScheduleSlotResponse>> GetScheduleAsync(HttpClient client, int roomId, DateOnly date)
    {
        var response = await client.GetAsync($"/api/rooms/{roomId}/schedule?date={date:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ScheduleSlotResponse>>(ResponseOptions) ?? [];
    }

    private sealed record BookingIdOnly(int Id);
}
