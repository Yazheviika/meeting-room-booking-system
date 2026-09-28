import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface TimeSlot {
  id: number;
  startTime: string;
  endTime: string;
}

export interface Room {
  id: number;
  name: string;
  description: string | null;
  capacity: number;
  timeSlots: TimeSlot[];
}

export type SlotStatus = 'Free' | 'Booked' | 'Mine' | 'Past';

/**
 * One slot's schedule entry for a given date. `bookingId` is populated
 * only when `status` is `"Mine"` (null otherwise) — see CLAUDE.md's
 * Bookings section for why the backend exposes it that way.
 */
export interface ScheduleSlot {
  timeSlotId: number;
  startTime: string;
  endTime: string;
  status: SlotStatus;
  bookingId: number | null;
}

@Injectable({ providedIn: 'root' })
export class RoomsService {
  private readonly http = inject(HttpClient);

  getRooms(): Observable<Room[]> {
    return this.http.get<Room[]>(`${environment.apiBaseUrl}/api/rooms`);
  }

  getRoom(id: number): Observable<Room> {
    return this.http.get<Room>(`${environment.apiBaseUrl}/api/rooms/${id}`);
  }

  getSchedule(roomId: number, date: string): Observable<ScheduleSlot[]> {
    const params = new HttpParams().set('date', date);
    return this.http.get<ScheduleSlot[]>(`${environment.apiBaseUrl}/api/rooms/${roomId}/schedule`, { params });
  }
}
