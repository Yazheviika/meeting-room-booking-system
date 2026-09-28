import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { Bookings } from './bookings';
import { Booking, BookingsService } from './bookings.service';

function booking(overrides: Partial<Booking> = {}): Booking {
  return {
    id: 1,
    roomId: 1,
    roomName: 'Alpha',
    timeSlotId: 1,
    startTime: '09:00:00',
    endTime: '10:00:00',
    date: '2026-10-01',
    status: 'Active',
    createdAtUtc: '2026-09-01T00:00:00Z',
    cancelledAtUtc: null,
    ...overrides,
  };
}

describe('Bookings', () => {
  let bookingsServiceStub: { getMyBookings: ReturnType<typeof vi.fn>; cancelBooking: ReturnType<typeof vi.fn> };

  function createComponent(): Bookings {
    TestBed.configureTestingModule({
      imports: [Bookings],
      providers: [{ provide: BookingsService, useValue: bookingsServiceStub }],
    });
    const fixture = TestBed.createComponent(Bookings);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    bookingsServiceStub = { getMyBookings: vi.fn(), cancelBooking: vi.fn() };
  });

  it('shows a loading state before bookings arrive', () => {
    bookingsServiceStub.getMyBookings.mockReturnValue(of([]));
    const component = createComponent();
    expect(component['loading']()).toBe(false); // of() emits synchronously
  });

  it('splits bookings into upcoming (far future, Active) and others (far past, or Cancelled)', () => {
    const upcomingBooking = booking({ id: 1, date: '2099-01-01', status: 'Active' });
    const pastBooking = booking({ id: 2, date: '2000-01-01', status: 'Active' });
    const cancelledBooking = booking({ id: 3, date: '2099-06-01', status: 'Cancelled' });
    bookingsServiceStub.getMyBookings.mockReturnValue(of([upcomingBooking, pastBooking, cancelledBooking]));

    const component = createComponent();

    expect(component['upcoming']().map((b) => b.id)).toEqual([1]);
    expect(component['others']().map((b) => b.id).sort()).toEqual([2, 3]);
  });

  it('shows an error message when bookings fail to load', () => {
    bookingsServiceStub.getMyBookings.mockReturnValue(throwError(() => new Error('network error')));
    const component = createComponent();

    expect(component['errorMessage']()).toBe('Could not load your bookings.');
  });

  it('cancel() success moves the booking from upcoming to the cancelled group', () => {
    const upcomingBooking = booking({ id: 1, date: '2099-01-01', status: 'Active' });
    bookingsServiceStub.getMyBookings.mockReturnValue(of([upcomingBooking]));
    bookingsServiceStub.cancelBooking.mockReturnValue(of(undefined));
    const component = createComponent();
    expect(component['upcoming']().map((b) => b.id)).toEqual([1]);

    component['cancel'](upcomingBooking);

    expect(component['upcoming']()).toEqual([]);
    expect(component['others']().map((b) => ({ id: b.id, status: b.status }))).toEqual([{ id: 1, status: 'Cancelled' }]);
    expect(component['cancellingId']()).toBeNull();
  });
});
