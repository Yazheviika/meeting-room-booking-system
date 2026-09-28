import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export type BookingStatus = 'Active' | 'Cancelled';

export interface Booking {
  id: number;
  roomId: number;
  roomName: string;
  timeSlotId: number;
  startTime: string;
  endTime: string;
  date: string;
  status: BookingStatus;
  createdAtUtc: string;
  cancelledAtUtc: string | null;
}

/** Same shape as `Booking` plus the booking owner's identity — Admin-only, per CLAUDE.md's Bookings section. */
export interface AdminBooking extends Booking {
  userId: string;
  userEmail: string;
}

@Injectable({ providedIn: 'root' })
export class BookingsService {
  private readonly http = inject(HttpClient);

  createBooking(timeSlotId: number, date: string): Observable<Booking> {
    return this.http.post<Booking>(`${environment.apiBaseUrl}/api/bookings`, { timeSlotId, date });
  }

  cancelBooking(id: number): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/api/bookings/${id}`);
  }

  getMyBookings(): Observable<Booking[]> {
    return this.http.get<Booking[]>(`${environment.apiBaseUrl}/api/bookings/mine`);
  }

  /** Admin-only. `date`/`roomId` are optional server-side filters. */
  getAllBookings(date?: string, roomId?: number): Observable<AdminBooking[]> {
    let params = new HttpParams();
    if (date) {
      params = params.set('date', date);
    }
    if (roomId !== undefined) {
      params = params.set('roomId', roomId);
    }
    return this.http.get<AdminBooking[]>(`${environment.apiBaseUrl}/api/bookings`, { params });
  }
}
