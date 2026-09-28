import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Room, RoomsService, ScheduleSlot, SlotStatus } from '../rooms.service';
import { BookingsService } from '../../bookings/bookings.service';
import { BookingHubService } from '../../signalr/booking-hub.service';
import { applySlotChanged } from '../schedule.reducer';
import { toIsoDate } from '../date-utils';
import { extractValidationErrors } from '../../shared/validation-problem';

/** How long the initial join is allowed to delay the first fetch — see ngOnInit. */
const JOIN_TIMEOUT_MS = 2000;

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

@Component({
  selector: 'app-room-schedule',
  imports: [RouterLink, MatDatepickerModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './room-schedule.html',
  styleUrl: './room-schedule.scss',
})
export class RoomSchedule implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly roomsService = inject(RoomsService);
  private readonly bookingsService = inject(BookingsService);
  private readonly hub = inject(BookingHubService);
  private readonly snackBar = inject(MatSnackBar);

  private roomId = 0;
  private unsubscribeSlotChanged?: () => void;
  private unsubscribeConnected?: () => void;

  protected readonly room = signal<Room | null>(null);
  protected readonly selectedDate = signal<Date>(new Date());
  protected readonly schedule = signal<ScheduleSlot[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly pendingSlotId = signal<number | null>(null);

  protected readonly minDate = new Date();
  protected readonly maxDate = addDays(new Date(), 30);

  ngOnInit(): void {
    this.roomId = Number(this.route.snapshot.paramMap.get('id'));

    this.roomsService.getRoom(this.roomId).subscribe({
      next: (room) => this.room.set(room),
      error: () => this.errorMessage.set('Could not load this room.'),
    });

    this.unsubscribeSlotChanged = this.hub.onSlotChanged((event) => {
      if (event.roomId !== this.roomId) {
        return;
      }
      this.schedule.set(applySlotChanged(this.schedule(), event, this.currentDateIso()));
    });

    // Re-join + re-fetch on every "now connected" transition - whether
    // that's the initial cold-start retry finally succeeding, or a
    // genuine reconnect. Self-heals whatever gap the bounded wait below
    // may have left.
    this.unsubscribeConnected = this.hub.onConnected(() => {
      void this.rejoinAndRefetch();
    });

    void this.joinAndFetch();
  }

  ngOnDestroy(): void {
    this.unsubscribeSlotChanged?.();
    this.unsubscribeConnected?.();
    void this.hub.leaveRoom(this.roomId);
  }

  protected onDateChange(date: Date | null): void {
    if (!date) {
      return;
    }
    this.selectedDate.set(date);
    this.fetchSchedule();
  }

  protected book(slot: ScheduleSlot): void {
    this.pendingSlotId.set(slot.timeSlotId);
    this.errorMessage.set(null);

    this.bookingsService.createBooking(slot.timeSlotId, this.currentDateIso()).subscribe({
      next: (booking) => {
        this.pendingSlotId.set(null);
        this.setSlot(slot.timeSlotId, 'Mine', booking.id);
      },
      error: (error: unknown) => {
        this.pendingSlotId.set(null);
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.snackBar.open('This slot was just booked by someone else', 'Dismiss', { duration: 5000 });
          // The winner may have already cancelled by the time this
          // response lands - re-fetch for the real current state
          // instead of assuming Booked.
          this.fetchSchedule();
        } else if (error instanceof HttpErrorResponse && error.status === 400) {
          this.errorMessage.set(extractValidationErrors(error).join(' ') || 'Could not book this slot.');
        } else {
          this.errorMessage.set('Could not book this slot.');
        }
      },
    });
  }

  protected cancel(slot: ScheduleSlot): void {
    if (slot.bookingId === null) {
      return;
    }

    this.pendingSlotId.set(slot.timeSlotId);
    this.errorMessage.set(null);

    this.bookingsService.cancelBooking(slot.bookingId).subscribe({
      next: () => {
        this.pendingSlotId.set(null);
        // Only clear to Free if the slot is still Mine locally - a
        // later-arriving event may already have moved it on.
        const current = this.schedule().find((s) => s.timeSlotId === slot.timeSlotId);
        if (current?.status === 'Mine') {
          this.setSlot(slot.timeSlotId, 'Free', null);
        }
      },
      error: (error: unknown) => {
        this.pendingSlotId.set(null);
        if (error instanceof HttpErrorResponse && error.status === 400) {
          this.errorMessage.set(extractValidationErrors(error).join(' ') || 'Could not cancel this booking.');
        } else {
          this.errorMessage.set('Could not cancel this booking.');
        }
      },
    });
  }

  private currentDateIso(): string {
    return toIsoDate(this.selectedDate());
  }

  private async joinAndFetch(): Promise<void> {
    // Never block the initial fetch on a slow/cold-starting connection -
    // race the join against a short timeout so the page always shows
    // data quickly; the onConnected subscription above heals the gap
    // once the connection actually comes up.
    await Promise.race([this.hub.joinRoom(this.roomId), delay(JOIN_TIMEOUT_MS)]);
    this.fetchSchedule();
  }

  private async rejoinAndRefetch(): Promise<void> {
    await this.hub.joinRoom(this.roomId);
    this.fetchSchedule();
  }

  private fetchSchedule(): void {
    this.loading.set(true);
    this.roomsService.getSchedule(this.roomId, this.currentDateIso()).subscribe({
      next: (schedule) => {
        this.schedule.set(schedule);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Could not load the schedule for this date.');
        this.loading.set(false);
      },
    });
  }

  private setSlot(timeSlotId: number, status: SlotStatus, bookingId: number | null): void {
    this.schedule.update((schedule) =>
      schedule.map((slot) => (slot.timeSlotId === timeSlotId ? { ...slot, status, bookingId } : slot)),
    );
  }
}

function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}
