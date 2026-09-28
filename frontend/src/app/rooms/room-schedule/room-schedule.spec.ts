import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNativeDateAdapter } from '@angular/material/core';
import { Subject, of, throwError } from 'rxjs';
import { RoomSchedule } from './room-schedule';
import { RoomsService, ScheduleSlot } from '../rooms.service';
import { BookingsService, Booking } from '../../bookings/bookings.service';
import { BookingHubService, SlotChangedEvent } from '../../signalr/booking-hub.service';

const ROOM_ID = 7;

function scheduleSlot(overrides: Partial<ScheduleSlot> = {}): ScheduleSlot {
  return { timeSlotId: 1, startTime: '09:00:00', endTime: '10:00:00', status: 'Free', bookingId: null, ...overrides };
}

describe('RoomSchedule', () => {
  let roomsServiceStub: { getRoom: ReturnType<typeof vi.fn>; getSchedule: ReturnType<typeof vi.fn> };
  let bookingsServiceStub: { createBooking: ReturnType<typeof vi.fn>; cancelBooking: ReturnType<typeof vi.fn> };
  let hubStub: {
    joinRoom: ReturnType<typeof vi.fn>;
    leaveRoom: ReturnType<typeof vi.fn>;
    onSlotChanged: ReturnType<typeof vi.fn>;
    onConnected: ReturnType<typeof vi.fn>;
  };
  let slotChangedCallback: ((event: SlotChangedEvent) => void) | undefined;
  let snackBarStub: { open: ReturnType<typeof vi.fn> };

  async function createComponent(): Promise<RoomSchedule> {
    TestBed.configureTestingModule({
      imports: [RoomSchedule],
      providers: [
        provideNativeDateAdapter(),
        { provide: RoomsService, useValue: roomsServiceStub },
        { provide: BookingsService, useValue: bookingsServiceStub },
        { provide: BookingHubService, useValue: hubStub },
        { provide: MatSnackBar, useValue: snackBarStub },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: String(ROOM_ID) }) } } },
      ],
    });
    const fixture = TestBed.createComponent(RoomSchedule);
    fixture.detectChanges();
    // ngOnInit's joinAndFetch() is async (awaits the joinRoom/timeout
    // race before fetching) - flush a real tick so the initial schedule
    // fetch actually lands before a test acts on it.
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    slotChangedCallback = undefined;
    roomsServiceStub = {
      getRoom: vi.fn().mockReturnValue(of({ id: ROOM_ID, name: 'Room', description: null, capacity: 4, timeSlots: [] })),
      getSchedule: vi.fn().mockReturnValue(of([scheduleSlot()])),
    };
    bookingsServiceStub = { createBooking: vi.fn(), cancelBooking: vi.fn() };
    hubStub = {
      joinRoom: vi.fn().mockResolvedValue(undefined),
      leaveRoom: vi.fn().mockResolvedValue(undefined),
      onSlotChanged: vi.fn((callback: (event: SlotChangedEvent) => void) => {
        slotChangedCallback = callback;
        return () => {
          slotChangedCallback = undefined;
        };
      }),
      onConnected: vi.fn(() => () => undefined),
    };
    snackBarStub = { open: vi.fn() };
  });

  it('fetches the schedule even when joinRoom never resolves, bounded by the join timeout', async () => {
    vi.useFakeTimers();
    hubStub.joinRoom.mockReturnValue(new Promise<void>(() => undefined));

    createComponent();
    expect(roomsServiceStub.getSchedule).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(2000);

    expect(roomsServiceStub.getSchedule).toHaveBeenCalledWith(ROOM_ID, expect.any(String));
    vi.useRealTimers();
  });

  it('book() success sets the slot to Mine with the returned bookingId', async () => {
    const booking = { id: 99 } as Booking;
    bookingsServiceStub.createBooking.mockReturnValue(of(booking));
    const component = await createComponent();

    component['book'](scheduleSlot());

    const updated = component['schedule']()[0];
    expect(updated.status).toBe('Mine');
    expect(updated.bookingId).toBe(99);
    expect(component['pendingSlotId']()).toBeNull();
  });

  it('book() 409 shows the snackbar and re-fetches instead of assuming Booked', async () => {
    bookingsServiceStub.createBooking.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    const component = await createComponent();
    roomsServiceStub.getSchedule.mockClear();
    roomsServiceStub.getSchedule.mockReturnValue(of([scheduleSlot({ status: 'Booked' })]));

    component['book'](scheduleSlot());

    expect(snackBarStub.open).toHaveBeenCalledWith(
      'This slot was just booked by someone else',
      'Dismiss',
      expect.anything(),
    );
    expect(roomsServiceStub.getSchedule).toHaveBeenCalledOnce();
    expect(component['schedule']()[0].status).toBe('Booked');
  });

  it('cancel() success sets Free only if the slot is still Mine locally', async () => {
    const cancelSubject = new Subject<void>();
    bookingsServiceStub.cancelBooking.mockReturnValue(cancelSubject.asObservable());
    const component = await createComponent();
    component['schedule'].set([scheduleSlot({ status: 'Mine', bookingId: 42 })]);

    component['cancel'](scheduleSlot({ status: 'Mine', bookingId: 42 }));

    // Something else moved the slot on before the cancel response lands.
    component['schedule'].set([scheduleSlot({ status: 'Booked', bookingId: null })]);

    cancelSubject.next();
    cancelSubject.complete();

    expect(component['schedule']()[0].status).toBe('Booked');
  });

  it('cancel() success clears to Free when the slot is still Mine when the response lands', async () => {
    bookingsServiceStub.cancelBooking.mockReturnValue(of(undefined));
    const component = await createComponent();
    component['schedule'].set([scheduleSlot({ status: 'Mine', bookingId: 42 })]);

    component['cancel'](scheduleSlot({ status: 'Mine', bookingId: 42 }));

    expect(component['schedule']()[0]).toMatchObject({ status: 'Free', bookingId: null });
  });

  it('ignores a SlotChanged event for a different room', async () => {
    const component = await createComponent();
    const before = component['schedule']();

    slotChangedCallback?.({ roomId: ROOM_ID + 1, date: '2099-01-01', slotId: 1, isBooked: true });

    expect(component['schedule']()).toBe(before);
  });
});
