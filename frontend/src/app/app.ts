import { Component, effect, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from './auth/auth.service';
import { HealthIndicator } from './shared/health-indicator/health-indicator';
import { ConnectionIndicator } from './shared/connection-indicator/connection-indicator';
import { BookingHubService } from './signalr/booking-hub.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, MatToolbarModule, MatButtonModule, HealthIndicator, ConnectionIndicator],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly bookingHub = inject(BookingHubService);

  constructor() {
    // The one place that starts/stops the shared hub connection — pages
    // don't need to know about connection lifecycle at all, since
    // BookingHubService.joinRoom/leaveRoom self-start if needed anyway.
    effect(() => {
      if (this.authService.isLoggedIn()) {
        this.bookingHub.start();
      } else {
        this.bookingHub.stop();
      }
    });
  }

  protected logout(): void {
    this.authService.logout();
    this.router.navigateByUrl('/login');
  }
}
