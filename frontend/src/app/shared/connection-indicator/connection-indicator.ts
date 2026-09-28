import { Component, computed, inject } from '@angular/core';
import { BookingHubService } from '../../signalr/booking-hub.service';

type DisplayState = 'live' | 'reconnecting' | 'offline';

/** Small Live/Reconnecting/Offline badge off BookingHubService's connection state. */
@Component({
  selector: 'app-connection-indicator',
  imports: [],
  templateUrl: './connection-indicator.html',
  styleUrl: './connection-indicator.scss',
})
export class ConnectionIndicator {
  private readonly hub = inject(BookingHubService);

  // The task only asks for three UI states — "connecting" (the brief
  // window before the very first successful connect) reads the same as
  // "disconnected" (Offline); the two don't need separate treatment here.
  protected readonly display = computed<DisplayState>(() => {
    switch (this.hub.connectionState()) {
      case 'connected':
        return 'live';
      case 'reconnecting':
        return 'reconnecting';
      default:
        return 'offline';
    }
  });
}
