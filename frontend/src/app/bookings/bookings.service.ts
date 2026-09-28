import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
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
}
