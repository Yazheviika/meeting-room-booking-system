import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Booking, BookingsService } from './bookings.service';

/**
 * Upcoming-vs-past/cancelled here is a display nicety, not an authority
 * claim: comparing a booking's date+end time against the *browser's*
 * local clock is only for sorting this list — the backend's own
 * office-time check still actually decides whether a Cancel succeeds.
 */
@Component({
  selector: 'app-bookings',
  imports: [MatButtonModule],
  templateUrl: './bookings.html',
  styleUrl: './bookings.scss',
})
export class Bookings implements OnInit {
  private readonly bookingsService = inject(BookingsService);

  protected readonly bookings = signal<Booking[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly cancellingId = signal<number | null>(null);

  protected readonly upcoming = computed(() =>
    this.bookings()
      .filter((booking) => booking.status === 'Active' && !this.isPast(booking))
      .sort((a, b) => this.startInstant(a) - this.startInstant(b)),
  );

  protected readonly others = computed(() =>
    this.bookings()
      .filter((booking) => booking.status === 'Cancelled' || this.isPast(booking))
      .sort((a, b) => this.startInstant(b) - this.startInstant(a)),
  );

  ngOnInit(): void {
    this.bookingsService.getMyBookings().subscribe({
      next: (bookings) => {
        this.bookings.set(bookings);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Could not load your bookings.');
        this.loading.set(false);
      },
    });
  }

  protected cancel(booking: Booking): void {
    this.cancellingId.set(booking.id);
    this.errorMessage.set(null);

    this.bookingsService.cancelBooking(booking.id).subscribe({
      next: () => {
        this.cancellingId.set(null);
        this.bookings.update((bookings) =>
          bookings.map((b) => (b.id === booking.id ? { ...b, status: 'Cancelled' as const } : b)),
        );
      },
      error: () => {
        this.cancellingId.set(null);
        this.errorMessage.set('Could not cancel this booking.');
      },
    });
  }

  private isPast(booking: Booking): boolean {
    return this.toLocalDate(booking.date, booking.endTime).getTime() <= Date.now();
  }

  private startInstant(booking: Booking): number {
    return this.toLocalDate(booking.date, booking.startTime).getTime();
  }

  // Builds a local Date from year/month/day/hour/minute parts directly,
  // rather than string-concatenating into something passed to `new
  // Date(string)` — browsers disagree on parsing startTime/endTime's
  // "HH:mm:ss.fffffff" (7 fractional digits) tacked onto a date string.
  private toLocalDate(dateIso: string, timeHHmmss: string): Date {
    const [year, month, day] = dateIso.split('-').map(Number);
    const [hour, minute] = timeHHmmss.split(':').map(Number);
    return new Date(year, month - 1, day, hour, minute);
  }
}
