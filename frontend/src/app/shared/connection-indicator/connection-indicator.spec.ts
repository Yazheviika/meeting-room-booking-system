import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ConnectionIndicator } from './connection-indicator';
import { BookingHubService, ConnectionState } from '../../signalr/booking-hub.service';

describe('ConnectionIndicator', () => {
  let stateSignal: ReturnType<typeof signal<ConnectionState>>;

  function createFixture() {
    stateSignal = signal<ConnectionState>('disconnected');
    TestBed.configureTestingModule({
      imports: [ConnectionIndicator],
      providers: [{ provide: BookingHubService, useValue: { connectionState: stateSignal } }],
    });
    const fixture = TestBed.createComponent(ConnectionIndicator);
    fixture.detectChanges();
    return fixture;
  }

  it('shows "Live" when connected', () => {
    const fixture = createFixture();
    stateSignal.set('connected');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.status--live')?.textContent).toContain('Live');
  });

  it('shows "Reconnecting" when reconnecting', () => {
    const fixture = createFixture();
    stateSignal.set('reconnecting');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.status--reconnecting')?.textContent).toContain(
      'Reconnecting',
    );
  });

  it('shows "Offline" when disconnected or still connecting', () => {
    const fixture = createFixture();
    stateSignal.set('disconnected');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.status--offline')).toBeTruthy();

    stateSignal.set('connecting');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.status--offline')).toBeTruthy();
  });
});
