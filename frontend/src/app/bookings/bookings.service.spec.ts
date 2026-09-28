import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { BookingsService } from './bookings.service';

describe('BookingsService', () => {
  let service: BookingsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BookingsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('createBooking() posts timeSlotId and date to /api/bookings', () => {
    service.createBooking(5, '2026-10-01').subscribe();
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/bookings`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ timeSlotId: 5, date: '2026-10-01' });
    request.flush({});
  });

  it('cancelBooking(id) deletes /api/bookings/{id}', () => {
    service.cancelBooking(42).subscribe();
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/bookings/42`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
  });

  it('getMyBookings() calls GET /api/bookings/mine', () => {
    service.getMyBookings().subscribe();
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/bookings/mine`);
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });
});
