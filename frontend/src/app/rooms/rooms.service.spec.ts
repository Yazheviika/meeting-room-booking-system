import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { RoomsService } from './rooms.service';

describe('RoomsService', () => {
  let service: RoomsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RoomsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getRooms() calls GET /api/rooms', () => {
    service.getRooms().subscribe();
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/rooms`);
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('getRoom(id) calls GET /api/rooms/{id}', () => {
    service.getRoom(5).subscribe();
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/rooms/5`);
    expect(request.request.method).toBe('GET');
    request.flush({ id: 5, name: 'Room', description: null, capacity: 4, timeSlots: [] });
  });

  it('getSchedule(roomId, date) calls GET /api/rooms/{id}/schedule with the date query param', () => {
    service.getSchedule(5, '2026-10-01').subscribe();
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/rooms/5/schedule?date=2026-10-01`);
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });
});
