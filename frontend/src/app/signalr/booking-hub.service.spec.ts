import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';

const {
  startMock,
  stopMock,
  invokeMock,
  onMock,
  onreconnectingMock,
  onreconnectedMock,
  oncloseMock,
  getCallbacks,
  setState,
  getState,
} = vi.hoisted(() => {
  let slotChangedCallback: ((event: unknown) => void) | undefined;
  let reconnectingCallback: (() => void) | undefined;
  let reconnectedCallback: (() => void) | undefined;
  let closeCallback: (() => void) | undefined;
  let currentState = 'Disconnected';

  const startMock = vi.fn(async () => {
    currentState = 'Connected';
  });
  const stopMock = vi.fn(async () => {
    currentState = 'Disconnected';
  });
  const invokeMock = vi.fn().mockResolvedValue(undefined);
  const onMock = vi.fn((event: string, callback: (event: unknown) => void) => {
    if (event === 'SlotChanged') {
      slotChangedCallback = callback;
    }
  });
  const onreconnectingMock = vi.fn((callback: () => void) => {
    reconnectingCallback = callback;
  });
  const onreconnectedMock = vi.fn((callback: () => void) => {
    reconnectedCallback = callback;
  });
  const oncloseMock = vi.fn((callback: () => void) => {
    closeCallback = callback;
  });

  return {
    startMock,
    stopMock,
    invokeMock,
    onMock,
    onreconnectingMock,
    onreconnectedMock,
    oncloseMock,
    getCallbacks: () => ({ slotChangedCallback, reconnectingCallback, reconnectedCallback, closeCallback }),
    setState: (value: string) => {
      currentState = value;
    },
    getState: () => currentState,
  };
});

vi.mock('@microsoft/signalr', () => {
  const connection = {
    start: startMock,
    stop: stopMock,
    invoke: invokeMock,
    on: onMock,
    onreconnecting: onreconnectingMock,
    onreconnected: onreconnectedMock,
    onclose: oncloseMock,
    get state() {
      return getState();
    },
  };

  class HubConnectionBuilder {
    withUrl(): this {
      return this;
    }
    withAutomaticReconnect(): this {
      return this;
    }
    build() {
      return connection;
    }
  }

  return {
    HubConnectionBuilder,
    HubConnectionState: { Disconnected: 'Disconnected', Connecting: 'Connecting', Connected: 'Connected', Reconnecting: 'Reconnecting' },
  };
});

import { BookingHubService } from './booking-hub.service';

describe('BookingHubService', () => {
  beforeEach(() => {
    vi.useFakeTimers();

    // mockReset() (not mockClear()) so a previous test's
    // mockRejectedValue/mockRejectedValueOnce override can't leak into the
    // next test as a persistent base implementation.
    startMock.mockReset().mockImplementation(async () => {
      setState('Connected');
    });
    stopMock.mockReset().mockImplementation(async () => {
      setState('Disconnected');
    });
    invokeMock.mockReset().mockResolvedValue(undefined);
    setState('Disconnected');

    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: { accessToken: 'test-token' } }],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('connects and reports "connected" on the first successful start', async () => {
    const service = TestBed.inject(BookingHubService);

    service.start();
    await vi.advanceTimersByTimeAsync(0);

    expect(service.connectionState()).toBe('connected');
    expect(startMock).toHaveBeenCalledOnce();
  });

  it('retries with backoff after the first start fails, and eventually connects', async () => {
    // The first retry delay is 0 (an immediate second attempt), so the
    // first two rejections both play out within the initial advance(0);
    // only the *third* attempt (unqueued, falls back to the default
    // resolving implementation) actually succeeds, after the 1s delay
    // that follows the second failure.
    startMock.mockRejectedValueOnce(new Error('cold start')).mockRejectedValueOnce(new Error('cold start'));
    const service = TestBed.inject(BookingHubService);
    const connectedHandler = vi.fn();
    service.onConnected(connectedHandler);

    service.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(service.connectionState()).toBe('disconnected');
    expect(startMock).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(1000);
    expect(service.connectionState()).toBe('connected');
    expect(startMock).toHaveBeenCalledTimes(3);
    expect(connectedHandler).toHaveBeenCalledOnce();
  });

  it('stop() cancels an in-progress retry loop', async () => {
    startMock.mockRejectedValue(new Error('still cold'));
    const service = TestBed.inject(BookingHubService);

    service.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(startMock).toHaveBeenCalledTimes(2);

    service.stop();
    expect(service.connectionState()).toBe('disconnected');

    await vi.advanceTimersByTimeAsync(60_000);
    expect(startMock).toHaveBeenCalledTimes(2);
  });

  it('joinRoom self-starts the connection before invoking JoinRoom', async () => {
    const service = TestBed.inject(BookingHubService);

    const joinPromise = service.joinRoom(5);
    await vi.advanceTimersByTimeAsync(0);
    await joinPromise;

    expect(startMock).toHaveBeenCalledOnce();
    expect(invokeMock).toHaveBeenCalledWith('JoinRoom', 5);
  });

  it('leaveRoom is a no-op when there is no live connection', async () => {
    const service = TestBed.inject(BookingHubService);

    await service.leaveRoom(5);

    expect(invokeMock).not.toHaveBeenCalled();
  });

  it('notifies onSlotChanged subscribers when the hub broadcasts SlotChanged', async () => {
    const service = TestBed.inject(BookingHubService);
    service.start();
    await vi.advanceTimersByTimeAsync(0);

    const handler = vi.fn();
    service.onSlotChanged(handler);

    const event = { roomId: 1, date: '2026-10-01', slotId: 42, isBooked: true };
    getCallbacks().slotChangedCallback?.(event);

    expect(handler).toHaveBeenCalledWith(event);
  });

  it('reflects reconnecting/reconnected transitions and fires onConnected on reconnect', async () => {
    const service = TestBed.inject(BookingHubService);
    service.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(service.connectionState()).toBe('connected');

    const connectedHandler = vi.fn();
    service.onConnected(connectedHandler);

    getCallbacks().reconnectingCallback?.();
    expect(service.connectionState()).toBe('reconnecting');

    getCallbacks().reconnectedCallback?.();
    expect(service.connectionState()).toBe('connected');
    expect(connectedHandler).toHaveBeenCalledOnce();
  });

  it('re-enters the retry loop when the connection closes after a successful connect', async () => {
    const service = TestBed.inject(BookingHubService);
    service.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(startMock).toHaveBeenCalledTimes(1);

    setState('Disconnected');
    getCallbacks().closeCallback?.();
    await vi.advanceTimersByTimeAsync(0);

    expect(startMock).toHaveBeenCalledTimes(2);
    expect(service.connectionState()).toBe('connected');
  });
});
