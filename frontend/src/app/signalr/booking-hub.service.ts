import { Injectable, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../environments/environment';
import { AuthService } from '../auth/auth.service';

export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export interface SlotChangedEvent {
  roomId: number;
  date: string;
  slotId: number;
  isBooked: boolean;
}

type Unsubscribe = () => void;

/** 0, 1s, 2s, 5s, 10s, then repeats at 10s — deliberately never gives up. */
const RETRY_DELAYS_MS = [0, 1000, 2000, 5000, 10000];

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * One SignalR connection for the whole app, per CLAUDE.md's SignalR client
 * protocol (JoinRoom before fetching; re-join + re-fetch on reconnect).
 *
 * `withAutomaticReconnect()` only takes over once a connection has
 * succeeded at least once — it does nothing for a first `start()` that
 * fails outright, which is exactly what happens against a cold,
 * still-waking-up Azure backend. So this service owns its own indefinite
 * retry loop (with backoff) around the first connect, and re-enters that
 * same loop if SignalR's own automatic reconnect eventually gives up
 * (`onclose`) — callers never see a terminal "gave up" state short of an
 * explicit `stop()`.
 */
@Injectable({ providedIn: 'root' })
export class BookingHubService {
  private readonly authService = inject(AuthService);

  private connection: signalR.HubConnection | null = null;
  private connectPromise: Promise<void> | null = null;
  private stopRequested = false;

  private readonly slotChangedHandlers = new Set<(event: SlotChangedEvent) => void>();
  private readonly connectedHandlers = new Set<() => void>();

  private readonly state = signal<ConnectionState>('disconnected');
  readonly connectionState = this.state.asReadonly();

  /** Starts connecting (with indefinite retry) if not already started. Safe to call more than once. */
  start(): void {
    if (this.connection) {
      return;
    }

    this.stopRequested = false;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}/hubs/booking`, {
        accessTokenFactory: () => this.authService.accessToken ?? '',
      })
      .withAutomaticReconnect()
      .build();

    connection.on('SlotChanged', (event: SlotChangedEvent) => {
      this.slotChangedHandlers.forEach((handler) => handler(event));
    });
    connection.onreconnecting(() => this.state.set('reconnecting'));
    connection.onreconnected(() => {
      this.state.set('connected');
      this.connectedHandlers.forEach((handler) => handler());
    });
    connection.onclose(() => {
      this.state.set('disconnected');
      if (!this.stopRequested && this.connection === connection) {
        this.connectPromise = this.connectWithRetry(connection);
      }
    });

    this.connection = connection;
    this.connectPromise = this.connectWithRetry(connection);
  }

  /** Stops the connection (or cancels an in-progress connect retry loop) — the only thing that ends auto-retry. */
  stop(): void {
    this.stopRequested = true;
    const connection = this.connection;
    this.connection = null;
    this.connectPromise = null;
    this.state.set('disconnected');
    connection?.stop();
  }

  /** Fires on every "SlotChanged" broadcast. Returns an unsubscribe function. */
  onSlotChanged(handler: (event: SlotChangedEvent) => void): Unsubscribe {
    this.slotChangedHandlers.add(handler);
    return () => this.slotChangedHandlers.delete(handler);
  }

  /** Fires whenever the connection reaches Connected — the retry loop's first success, or SignalR's own reconnect. */
  onConnected(handler: () => void): Unsubscribe {
    this.connectedHandlers.add(handler);
    return () => this.connectedHandlers.delete(handler);
  }

  /** Self-starts the connection if needed, then joins the room's group. */
  async joinRoom(roomId: number): Promise<void> {
    await this.ensureStarted();
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('JoinRoom', roomId);
    }
  }

  /** Leaves the room's group — a no-op if there's no live connection to leave it on. */
  async leaveRoom(roomId: number): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('LeaveRoom', roomId);
    }
  }

  private ensureStarted(): Promise<void> {
    if (!this.connection) {
      this.start();
    }
    return this.connectPromise ?? Promise.resolve();
  }

  private async connectWithRetry(connection: signalR.HubConnection): Promise<void> {
    let attempt = 0;
    while (!this.stopRequested && this.connection === connection) {
      this.state.set('connecting');
      try {
        await connection.start();
        if (this.stopRequested || this.connection !== connection) {
          return;
        }
        this.state.set('connected');
        this.connectedHandlers.forEach((handler) => handler());
        return;
      } catch {
        if (this.stopRequested || this.connection !== connection) {
          return;
        }
        this.state.set('disconnected');
        const waitMs = RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)];
        attempt++;
        await delay(waitMs);
      }
    }
  }
}
