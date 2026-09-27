using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRoomBooking.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRoomBooking.Api.Tests;

/// <summary>
/// The concurrency test required by TASK.md item 6, plus the additional
/// scenarios ADR 0001 calls for — all against a real SQL Server
/// (<see cref="BookingSqlServerFixture"/>), never EF InMemory, since the
/// point is proving real unique-index enforcement under RCSI.
/// </summary>
public class BookingConcurrencyTests : IClassFixture<BookingSqlServerFixture>
{
    private const int DifferentUserCount = 20;
    private const int RaceRounds = 5;
    private const int SameUserConcurrency = 10;

    private readonly BookingSqlServerFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="BookingConcurrencyTests"/> class.</summary>
    public BookingConcurrencyTests(BookingSqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// 20 different users, tokens, and HttpClients prepared up front; all
    /// requests released at the same moment via a shared
    /// TaskCompletionSource gate. Exactly one 201, the other 19 409, never
    /// a 5xx, exactly one active row — repeated for 5 rounds on fresh
    /// slots each time, since a single lucky pass proves much less than
    /// five independent ones.
    /// </summary>
    [Fact]
    public async Task TwentyDifferentUsers_RaceForSameSlot_ExactlyOneWinsEveryRound()
    {
        var clients = new List<HttpClient>();
        for (var i = 0; i < DifferentUserCount; i++)
        {
            var (_, token) = await _fixture.Factory.CreateUserAndTokenAsync($"race-{Guid.NewGuid():N}@example.com");
            var client = _fixture.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            clients.Add(client);
        }

        var date = _fixture.Factory.Tomorrow();

        for (var round = 1; round <= RaceRounds; round++)
        {
            var (_, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
                $"Race Room {round}-{Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));

            var gate = new TaskCompletionSource();
            var tasks = clients
                .Select(async client =>
                {
                    await gate.Task;
                    return await client.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
                })
                .ToList();

            // Best-effort: give every task a moment to reach the gate
            // before releasing it, so the requests actually fire together
            // rather than trickling out as tasks get scheduled.
            await Task.Delay(50);
            gate.SetResult();
            var responses = await Task.WhenAll(tasks);

            Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(DifferentUserCount - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

            var activeCount = await _fixture.Factory.CountActiveBookingsAsync(slotId, date);
            Assert.Equal(1, activeCount);
        }
    }

    /// <summary>
    /// One user, several parallel requests for the same slot/date — every
    /// response succeeds (201 the first time, 200 idempotent replay for
    /// the rest), never a 409, and exactly one row exists afterward.
    /// </summary>
    [Fact]
    public async Task SameUser_ConcurrentRequests_AllSucceedExactlyOneRow()
    {
        var (_, token) = await _fixture.Factory.CreateUserAndTokenAsync($"same-user-{Guid.NewGuid():N}@example.com");
        var (_, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"SameUser Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();

        var clients = Enumerable.Range(0, SameUserConcurrency)
            .Select(_ =>
            {
                var client = _fixture.Factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return client;
            })
            .ToList();

        var gate = new TaskCompletionSource();
        var tasks = clients
            .Select(async client =>
            {
                await gate.Task;
                return await client.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
            })
            .ToList();

        await Task.Delay(50);
        gate.SetResult();
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r =>
            Assert.True(
                r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                $"expected 201 or 200, got {r.StatusCode}"));

        var activeCount = await _fixture.Factory.CountActiveBookingsAsync(slotId, date);
        Assert.Equal(1, activeCount);
    }

    /// <summary>
    /// Inserts a duplicate active booking directly via <see cref="AppDbContext"/>,
    /// bypassing <c>BookingService</c> entirely — proves the unique index
    /// itself rejects the duplicate, independent of application logic, per
    /// ADR 0001's Consequences section.
    /// </summary>
    [Fact]
    public async Task DuplicateActiveBooking_DirectDbContextInsert_ThrowsDbUpdateException()
    {
        var (userId1, _) = await _fixture.Factory.CreateUserAndTokenAsync($"idx-1-{Guid.NewGuid():N}@example.com");
        var (userId2, _) = await _fixture.Factory.CreateUserAndTokenAsync($"idx-2-{Guid.NewGuid():N}@example.com");
        var (_, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Index Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        dbContext.Bookings.Add(new Booking
        {
            TimeSlotId = slotId,
            BookingDate = date,
            UserId = userId1,
            Status = BookingStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        dbContext.Bookings.Add(new Booking
        {
            TimeSlotId = slotId,
            BookingDate = date,
            UserId = userId2,
            Status = BookingStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    /// <summary>
    /// Book, cancel, then book the same slot/date again (a different user)
    /// — succeeds, since the filtered unique index only constrains
    /// <em>active</em> rows.
    /// </summary>
    [Fact]
    public async Task CancelThenRebook_SameSlot_Succeeds()
    {
        var (_, tokenA) = await _fixture.Factory.CreateUserAndTokenAsync($"cancel-a-{Guid.NewGuid():N}@example.com");
        var (_, tokenB) = await _fixture.Factory.CreateUserAndTokenAsync($"cancel-b-{Guid.NewGuid():N}@example.com");
        var (_, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Cancel Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();

        var clientA = _fixture.Factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var clientB = _fixture.Factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var createResponse = await clientA.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BookingIdOnly>();

        var cancelResponse = await clientA.DeleteAsync($"/api/bookings/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

        var rebookResponse = await clientB.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
        Assert.Equal(HttpStatusCode.Created, rebookResponse.StatusCode);

        var activeCount = await _fixture.Factory.CountActiveBookingsAsync(slotId, date);
        Assert.Equal(1, activeCount);
    }

    private sealed record BookingIdOnly(int Id);
}
