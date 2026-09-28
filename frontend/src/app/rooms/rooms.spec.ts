import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { Rooms } from './rooms';
import { RoomsService, Room } from './rooms.service';

describe('Rooms', () => {
  let roomsServiceStub: { getRooms: ReturnType<typeof vi.fn> };

  const rooms: Room[] = [
    { id: 1, name: 'Alpha', description: 'A quiet room', capacity: 4, timeSlots: [] },
    { id: 2, name: 'Beta', description: null, capacity: 8, timeSlots: [] },
  ];

  beforeEach(() => {
    roomsServiceStub = { getRooms: vi.fn() };

    TestBed.configureTestingModule({
      imports: [Rooms],
      providers: [provideRouter([]), { provide: RoomsService, useValue: roomsServiceStub }],
    });
  });

  it('shows a loading state before the rooms arrive', () => {
    roomsServiceStub.getRooms.mockReturnValue(of(rooms));
    const fixture = TestBed.createComponent(Rooms);

    expect(fixture.componentInstance['loading']()).toBe(true);
  });

  it('renders each active room once loaded', () => {
    roomsServiceStub.getRooms.mockReturnValue(of(rooms));
    const fixture = TestBed.createComponent(Rooms);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Alpha');
    expect(compiled.textContent).toContain('Beta');
    expect(compiled.querySelectorAll('a.room-link').length).toBe(2);
  });

  it('shows an error message when the rooms fail to load', () => {
    roomsServiceStub.getRooms.mockReturnValue(throwError(() => new Error('network error')));
    const fixture = TestBed.createComponent(Rooms);
    fixture.detectChanges();

    expect(fixture.componentInstance['error']()).toBe('Could not load rooms.');
  });
});
