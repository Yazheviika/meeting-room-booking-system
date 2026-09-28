import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { CreateTimeSlotInput, Room, RoomsService } from '../rooms/rooms.service';
import { AdminBooking, BookingsService } from '../bookings/bookings.service';
import { extractValidationErrors } from '../shared/validation-problem';

function errorMessages(error: unknown): string[] {
  if (error instanceof HttpErrorResponse && error.status === 400) {
    const messages = extractValidationErrors(error);
    return messages.length > 0 ? messages : ['Request failed.'];
  }
  return ['Something went wrong. Please try again.'];
}

function toMinutes(hhmm: string): number {
  const [hours, minutes] = hhmm.split(':').map(Number);
  return hours * 60 + minutes;
}

function fromMinutes(total: number): string {
  const hours = String(Math.floor(total / 60)).padStart(2, '0');
  const minutes = String(total % 60).padStart(2, '0');
  return `${hours}:${minutes}:00`;
}

/** Generates fixed-length slots covering [start, end) — the last slot is dropped if it would run past `end`. */
function generateSlots(start: string, end: string, slotMinutes: number): CreateTimeSlotInput[] {
  const slots: CreateTimeSlotInput[] = [];
  const endMinutes = toMinutes(end);
  for (let cursor = toMinutes(start); cursor + slotMinutes <= endMinutes; cursor += slotMinutes) {
    slots.push({ startTime: fromMinutes(cursor), endTime: fromMinutes(cursor + slotMinutes) });
  }
  return slots;
}

@Component({
  selector: 'app-admin',
  imports: [ReactiveFormsModule, MatTabsModule, MatTableModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './admin.html',
  styleUrl: './admin.scss',
})
export class Admin implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly roomsService = inject(RoomsService);
  private readonly bookingsService = inject(BookingsService);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly roomColumns = ['name', 'description', 'capacity', 'slots', 'actions'];
  protected readonly bookingColumns = ['userEmail', 'room', 'time', 'date', 'status'];

  protected readonly rooms = signal<Room[]>([]);
  protected readonly roomsError = signal<string | null>(null);
  protected readonly editingRoomId = signal<number | null>(null);
  protected readonly editErrors = signal<string[]>([]);

  protected readonly bookings = signal<AdminBooking[]>([]);
  protected readonly bookingsError = signal<string | null>(null);

  protected readonly createForm = this.fb.nonNullable.group({
    name: ['', Validators.required],
    description: [''],
    capacity: [4, [Validators.required, Validators.min(1)]],
    startTime: ['09:00', Validators.required],
    endTime: ['18:00', Validators.required],
    slotMinutes: [60, [Validators.required, Validators.min(5)]],
  });
  protected readonly createErrors = signal<string[]>([]);
  protected readonly creating = signal(false);

  protected readonly editForm = this.fb.nonNullable.group({
    name: ['', Validators.required],
    description: [''],
    capacity: [1, [Validators.required, Validators.min(1)]],
  });

  protected readonly filterForm = this.fb.nonNullable.group({
    date: [''],
    roomId: [''],
  });

  ngOnInit(): void {
    this.roomsService.getRooms().subscribe({
      next: (rooms) => this.rooms.set(rooms),
      error: () => this.roomsError.set('Could not load rooms.'),
    });
    this.loadBookings();
  }

  protected loadBookings(): void {
    const { date, roomId } = this.filterForm.getRawValue();
    this.bookingsService.getAllBookings(date || undefined, roomId ? Number(roomId) : undefined).subscribe({
      next: (bookings) => this.bookings.set(bookings),
      error: () => this.bookingsError.set('Could not load bookings.'),
    });
  }

  protected createRoom(): void {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }

    const { name, description, capacity, startTime, endTime, slotMinutes } = this.createForm.getRawValue();
    const timeSlots = generateSlots(startTime, endTime, slotMinutes);
    if (timeSlots.length === 0) {
      this.createErrors.set(['No slots could be generated from that start/end time and slot length.']);
      return;
    }

    this.creating.set(true);
    this.createErrors.set([]);
    this.roomsService.createRoom({ name, description: description || null, capacity, timeSlots }).subscribe({
      next: (room) => {
        this.creating.set(false);
        this.rooms.update((rooms) => [...rooms, room]);
        this.createForm.reset({
          name: '',
          description: '',
          capacity: 4,
          startTime: '09:00',
          endTime: '18:00',
          slotMinutes: 60,
        });
      },
      error: (error: unknown) => {
        this.creating.set(false);
        this.createErrors.set(errorMessages(error));
      },
    });
  }

  protected startEdit(room: Room): void {
    this.editingRoomId.set(room.id);
    this.editErrors.set([]);
    this.editForm.setValue({ name: room.name, description: room.description ?? '', capacity: room.capacity });
  }

  protected cancelEdit(): void {
    this.editingRoomId.set(null);
  }

  protected saveEdit(room: Room): void {
    if (this.editForm.invalid) {
      this.editForm.markAllAsTouched();
      return;
    }

    const { name, description, capacity } = this.editForm.getRawValue();
    this.roomsService.updateRoom(room.id, { name, description: description || null, capacity }).subscribe({
      next: (updated) => {
        this.rooms.update((rooms) => rooms.map((r) => (r.id === room.id ? updated : r)));
        this.editingRoomId.set(null);
      },
      error: (error: unknown) => this.editErrors.set(errorMessages(error)),
    });
  }

  protected deleteRoom(room: Room): void {
    this.roomsService.deleteRoom(room.id).subscribe({
      next: () => this.rooms.update((rooms) => rooms.filter((r) => r.id !== room.id)),
      error: (error: unknown) => {
        const message =
          error instanceof HttpErrorResponse && error.status === 409
            ? 'Room has future bookings'
            : 'Could not delete this room.';
        this.snackBar.open(message, 'Dismiss', { duration: 5000 });
      },
    });
  }
}
