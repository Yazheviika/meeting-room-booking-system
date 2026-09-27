using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using MeetingRoomBooking.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeetingRoomBooking.Api.Tests;

/// <summary>
/// Proves <c>BookingHub</c>/<c>SignalRBookingNotifier</c> end-to-end: real
/// SignalR clients against the real HTTP pipeline (via
/// <see cref="BookingsApiFactory"/> + <c>Microsoft.AspNetCore.SignalR.Client</c>),
/// no Azure SignalR involved. Reuses <see cref="BookingSqlServerFixture"/>
/// for the real-database guarantees the booking flow itself needs.
/// </summary>
public class BookingSignalRTests : IClassFixture<BookingSqlServerFixture>
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoEventTimeout = TimeSpan.FromSeconds(2);

    private readonly BookingSqlServerFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="BookingSignalRTests"/> class.</summary>
    public BookingSignalRTests(BookingSqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// A client joined to a room's group receives "SlotChanged" (isBooked:
    /// true, correct roomId/date/slotId) when someone else books a slot in
    /// that room — over LongPolling, the simplest transport to assert
    /// group membership and payload content with.
    /// </summary>
    [Fact]
    public async Task RoomMember_ReceivesSlotChanged_WhenSlotIsBooked_OverLongPolling()
    {
        var (roomId, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"LP Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();
        var (_, listenerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"lp-listener-{Guid.NewGuid():N}@example.com");
        var (_, bookerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"lp-booker-{Guid.NewGuid():N}@example.com");

        await using var connection = await ConnectViaLongPollingAsync(listenerToken);
        var events = AttachChannel(connection);
        await connection.InvokeAsync("JoinRoom", roomId);

        var bookerClient = _fixture.Factory.CreateClient();
        bookerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bookerToken);
        var response = await bookerClient.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await ReadOneAsync(events);
        Assert.Equal(roomId, payload.RoomId);
        Assert.Equal(date, payload.Date);
        Assert.Equal(slotId, payload.SlotId);
        Assert.True(payload.IsBooked);
    }

    /// <summary>
    /// The same scenario as above, but the listener connects over real
    /// WebSockets specifically — LongPolling's <c>AccessTokenProvider</c>
    /// sends the token as a normal Authorization header on every poll, so a
    /// LongPolling-only test suite would never actually exercise the
    /// <c>?access_token=</c> query-string fallback in
    /// <c>JwtBearerEvents.OnMessageReceived</c>, despite that being this
    /// codebase's documented reason the fallback exists (a WebSocket
    /// upgrade can't carry an Authorization header). The SignalR .NET
    /// client's WebSocket transport always sends the access token via query
    /// string, regardless of platform — this test genuinely proves the
    /// query-string handoff works, not just that some transport does.
    /// </summary>
    [Fact]
    public async Task RoomMember_ReceivesSlotChanged_WhenSlotIsBooked_OverWebSockets()
    {
        var (roomId, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"WS Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();
        var (_, listenerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"ws-listener-{Guid.NewGuid():N}@example.com");
        var (_, bookerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"ws-booker-{Guid.NewGuid():N}@example.com");

        await using var connection = await ConnectViaWebSocketsAsync(listenerToken);
        var events = AttachChannel(connection);
        await connection.InvokeAsync("JoinRoom", roomId);

        var bookerClient = _fixture.Factory.CreateClient();
        bookerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bookerToken);
        var response = await bookerClient.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await ReadOneAsync(events);
        Assert.Equal(roomId, payload.RoomId);
        Assert.Equal(date, payload.Date);
        Assert.Equal(slotId, payload.SlotId);
        Assert.True(payload.IsBooked);
    }

    /// <summary>Cancelling a booking sends a second "SlotChanged" (isBooked: false) on the same connection.</summary>
    [Fact]
    public async Task RoomMember_ReceivesSlotChanged_WhenBookingIsCancelled()
    {
        var (roomId, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Cancel-Notify Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();
        var (_, listenerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"cn-listener-{Guid.NewGuid():N}@example.com");
        var (_, bookerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"cn-booker-{Guid.NewGuid():N}@example.com");

        await using var connection = await ConnectViaLongPollingAsync(listenerToken);
        var events = AttachChannel(connection);
        await connection.InvokeAsync("JoinRoom", roomId);

        var bookerClient = _fixture.Factory.CreateClient();
        bookerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bookerToken);
        var createResponse = await bookerClient.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
        var created = await createResponse.Content.ReadFromJsonAsync<BookingIdOnly>();

        var bookedEvent = await ReadOneAsync(events);
        Assert.True(bookedEvent.IsBooked);

        var cancelResponse = await bookerClient.DeleteAsync($"/api/bookings/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

        var cancelledEvent = await ReadOneAsync(events);
        Assert.Equal(roomId, cancelledEvent.RoomId);
        Assert.Equal(date, cancelledEvent.Date);
        Assert.Equal(slotId, cancelledEvent.SlotId);
        Assert.False(cancelledEvent.IsBooked);
    }

    /// <summary>A client joined to a different room's group receives nothing when another room's slot changes.</summary>
    [Fact]
    public async Task Client_InDifferentRoomGroup_ReceivesNothing()
    {
        var (bookedRoomId, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Isolation Room A {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var (otherRoomId, _) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Isolation Room B {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();
        var (_, listenerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"iso-listener-{Guid.NewGuid():N}@example.com");
        var (_, bookerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"iso-booker-{Guid.NewGuid():N}@example.com");

        await using var connection = await ConnectViaLongPollingAsync(listenerToken);
        var events = AttachChannel(connection);
        await connection.InvokeAsync("JoinRoom", otherRoomId);

        var bookerClient = _fixture.Factory.CreateClient();
        bookerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bookerToken);
        var response = await bookerClient.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(bookedRoomId, otherRoomId);

        await AssertNoEventAsync(events);
    }

    /// <summary>
    /// Two users race for the same slot — exactly one "SlotChanged" arrives
    /// (the winner's), and no second one shows up shortly after: the 409
    /// loser never reaches <c>BookingService</c>'s notify call at all.
    /// </summary>
    [Fact]
    public async Task RacingUsers_ProduceExactlyOneSlotChanged()
    {
        var (roomId, slotId) = await _fixture.Factory.CreateRoomWithSlotAsync(
            $"Race-Notify Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = _fixture.Factory.Tomorrow();
        var (_, listenerToken) = await _fixture.Factory.CreateUserAndTokenAsync($"race-listener-{Guid.NewGuid():N}@example.com");
        var (_, tokenA) = await _fixture.Factory.CreateUserAndTokenAsync($"race-a-{Guid.NewGuid():N}@example.com");
        var (_, tokenB) = await _fixture.Factory.CreateUserAndTokenAsync($"race-b-{Guid.NewGuid():N}@example.com");

        await using var connection = await ConnectViaLongPollingAsync(listenerToken);
        var events = AttachChannel(connection);
        await connection.InvokeAsync("JoinRoom", roomId);

        var clientA = _fixture.Factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var clientB = _fixture.Factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var gate = new TaskCompletionSource();
        var taskA = RaceRequestAsync(clientA, gate.Task, slotId, date);
        var taskB = RaceRequestAsync(clientB, gate.Task, slotId, date);

        await Task.Delay(50);
        gate.SetResult();
        var responses = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, responses.Count(r => r == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r == HttpStatusCode.Conflict));

        var onlyEvent = await ReadOneAsync(events);
        Assert.Equal(roomId, onlyEvent.RoomId);
        Assert.True(onlyEvent.IsBooked);

        await AssertNoEventAsync(events);
    }

    /// <summary>
    /// A throwing <see cref="IBookingNotifier"/>, substituted via DI on a
    /// second factory pointed at the same already-migrated database, still
    /// returns 201 for a normal booking request — the broadcast failure is
    /// logged and swallowed, never turning a committed booking into a 500.
    /// </summary>
    [Fact]
    public async Task ThrowingNotifier_DoesNotFailTheBookingRequest()
    {
        await using var throwingFactory = new ThrowingNotifierApiFactory(_fixture.ConnectionString);
        var (_, token) = await throwingFactory.CreateUserAndTokenAsync($"throw-{Guid.NewGuid():N}@example.com");
        var (_, slotId) = await throwingFactory.CreateRoomWithSlotAsync(
            $"Throw Room {Guid.NewGuid():N}", new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = throwingFactory.Tomorrow();

        var client = throwingFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var activeCount = await throwingFactory.CountActiveBookingsAsync(slotId, date);
        Assert.Equal(1, activeCount);
    }

    private static async Task<HttpStatusCode> RaceRequestAsync(HttpClient client, Task gate, int slotId, DateOnly date)
    {
        await gate;
        var response = await client.PostAsJsonAsync("/api/bookings", new { timeSlotId = slotId, date });
        return response.StatusCode;
    }

    private async Task<HubConnection> ConnectViaLongPollingAsync(string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_fixture.Factory.Server.BaseAddress, "hubs/booking"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _fixture.Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    /// <summary>
    /// Connects over real WebSockets against the in-process <c>TestServer</c>,
    /// via <see cref="HttpConnectionOptions.WebSocketFactory"/> +
    /// <c>TestServer.CreateWebSocketClient()</c> — this combination does
    /// work end-to-end (see CLAUDE.md's SignalR section for the honest
    /// verification note this was checked against).
    ///
    /// <para>
    /// The SignalR client only auto-appends <c>?access_token=</c> to the
    /// connect URL when it owns the WebSocket connection itself; once a
    /// custom <see cref="HttpConnectionOptions.WebSocketFactory"/> is set,
    /// that responsibility shifts to the factory (the client assumes the
    /// factory may handle auth its own way, e.g. via headers on a real
    /// <c>ClientWebSocket</c>). <c>TestServer</c>'s in-memory
    /// <c>WebSocketClient</c> has no header-setting equivalent, so this
    /// helper appends the token to the query string itself — exactly what
    /// the production JS client does automatically for a real browser
    /// WebSocket (which also can't carry an Authorization header on the
    /// upgrade request), so the request that reaches the server, and the
    /// <c>OnMessageReceived</c> code path it exercises, is identical either
    /// way.
    /// </para>
    /// </summary>
    private async Task<HubConnection> ConnectViaWebSocketsAsync(string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_fixture.Factory.Server.BaseAddress, "hubs/booking"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => _fixture.Factory.Server.CreateHandler();
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var webSocketClient = _fixture.Factory.Server.CreateWebSocketClient();
                    var uriWithToken = AppendAccessToken(context.Uri, token);
                    return await webSocketClient.ConnectAsync(uriWithToken, cancellationToken);
                };
                // Negotiate is a normal HTTP POST (not a WebSocket upgrade),
                // so it can and does carry a real Authorization header —
                // only the WebSocket upgrade itself needs the query-string
                // workaround, applied above.
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    private static Uri AppendAccessToken(Uri uri, string token)
    {
        var separator = uri.Query.Length > 0 ? "&" : "?";
        return new Uri($"{uri}{separator}access_token={Uri.EscapeDataString(token)}");
    }

    private static Channel<SlotChangedPayload> AttachChannel(HubConnection connection)
    {
        var channel = Channel.CreateUnbounded<SlotChangedPayload>();
        connection.On<SlotChangedPayload>("SlotChanged", payload => channel.Writer.TryWrite(payload));
        return channel;
    }

    private static async Task<SlotChangedPayload> ReadOneAsync(Channel<SlotChangedPayload> channel)
    {
        using var cts = new CancellationTokenSource(EventTimeout);
        return await channel.Reader.ReadAsync(cts.Token);
    }

    private static async Task AssertNoEventAsync(Channel<SlotChangedPayload> channel)
    {
        using var cts = new CancellationTokenSource(NoEventTimeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await channel.Reader.ReadAsync(cts.Token));
    }

    private sealed record SlotChangedPayload(int RoomId, DateOnly Date, int SlotId, bool IsBooked);

    private sealed record BookingIdOnly(int Id);

    /// <summary>A <see cref="BookingsApiFactory"/> with <see cref="IBookingNotifier"/> swapped for one that always throws.</summary>
    private sealed class ThrowingNotifierApiFactory : BookingsApiFactory
    {
        /// <summary>Initializes a new instance of the <see cref="ThrowingNotifierApiFactory"/> class.</summary>
        public ThrowingNotifierApiFactory(string connectionString) : base(connectionString)
        {
        }

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBookingNotifier>();
                services.AddSingleton<IBookingNotifier, ThrowingBookingNotifier>();
            });
        }
    }

    private sealed class ThrowingBookingNotifier : IBookingNotifier
    {
        public Task SlotChangedAsync(int roomId, DateOnly date, int timeSlotId, bool isBooked) =>
            throw new InvalidOperationException("Simulated notifier failure for testing.");
    }
}
