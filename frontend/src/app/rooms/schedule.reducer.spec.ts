import { ScheduleSlot } from './rooms.service';
import { SlotChangedEvent } from '../signalr/booking-hub.service';
import { applySlotChanged } from './schedule.reducer';

const DATE = '2026-10-01';

function slot(overrides: Partial<ScheduleSlot> = {}): ScheduleSlot {
  return { timeSlotId: 1, startTime: '09:00:00', endTime: '10:00:00', status: 'Free', bookingId: null, ...overrides };
}

function event(overrides: Partial<SlotChangedEvent> = {}): SlotChangedEvent {
  return { roomId: 1, date: DATE, slotId: 1, isBooked: true, ...overrides };
}

describe('applySlotChanged', () => {
  it('ignores an event for a different date, returning the same array reference', () => {
    const schedule = [slot({ status: 'Free' })];
    const result = applySlotChanged(schedule, event({ date: '2026-10-02' }), DATE);
    expect(result).toBe(schedule);
  });

  it('ignores an event for a slot not in the schedule, returning the same array reference', () => {
    const schedule = [slot({ timeSlotId: 1 })];
    const result = applySlotChanged(schedule, event({ slotId: 999 }), DATE);
    expect(result).toBe(schedule);
  });

  it('never changes a slot that is already Past', () => {
    const schedule = [slot({ status: 'Past', bookingId: null })];
    expect(applySlotChanged(schedule, event({ isBooked: true }), DATE)).toBe(schedule);
    expect(applySlotChanged(schedule, event({ isBooked: false }), DATE)).toBe(schedule);
  });

  it('Free + isBooked:true -> Booked, no bookingId', () => {
    const schedule = [slot({ status: 'Free' })];
    const result = applySlotChanged(schedule, event({ isBooked: true }), DATE);
    expect(result).not.toBe(schedule);
    expect(result[0]).toMatchObject({ status: 'Booked', bookingId: null });
  });

  it('Booked + isBooked:true is a no-op (same reference)', () => {
    const schedule = [slot({ status: 'Booked', bookingId: null })];
    const result = applySlotChanged(schedule, event({ isBooked: true }), DATE);
    expect(result).toBe(schedule);
  });

  it('Mine + isBooked:true never downgrades to Booked (same reference, bookingId untouched)', () => {
    const schedule = [slot({ status: 'Mine', bookingId: 42 })];
    const result = applySlotChanged(schedule, event({ isBooked: true }), DATE);
    expect(result).toBe(schedule);
    expect(result[0]).toMatchObject({ status: 'Mine', bookingId: 42 });
  });

  it('Booked + isBooked:false -> Free, bookingId cleared', () => {
    const schedule = [slot({ status: 'Booked', bookingId: null })];
    const result = applySlotChanged(schedule, event({ isBooked: false }), DATE);
    expect(result).not.toBe(schedule);
    expect(result[0]).toMatchObject({ status: 'Free', bookingId: null });
  });

  it('Mine + isBooked:false -> Free, bookingId cleared', () => {
    const schedule = [slot({ status: 'Mine', bookingId: 42 })];
    const result = applySlotChanged(schedule, event({ isBooked: false }), DATE);
    expect(result).not.toBe(schedule);
    expect(result[0]).toMatchObject({ status: 'Free', bookingId: null });
  });

  it('Free + isBooked:false is a no-op (same reference)', () => {
    const schedule = [slot({ status: 'Free' })];
    const result = applySlotChanged(schedule, event({ isBooked: false }), DATE);
    expect(result).toBe(schedule);
  });

  it('only updates the matching slot, leaving others (and their references) untouched', () => {
    const other = slot({ timeSlotId: 2, status: 'Free' });
    const schedule = [slot({ timeSlotId: 1, status: 'Free' }), other];
    const result = applySlotChanged(schedule, event({ slotId: 1, isBooked: true }), DATE);
    expect(result[1]).toBe(other);
  });

  describe('both arrival orders converge to the same end state — book', () => {
    it('my own response first (Mine), then the broadcast event for the same booking: stays Mine', () => {
      // Simulates the component setting status directly off its own
      // successful response, *then* the SlotChanged broadcast for that
      // same booking arriving afterward.
      const afterMyResponse = [slot({ status: 'Mine', bookingId: 42 })];

      const afterEvent = applySlotChanged(afterMyResponse, event({ isBooked: true }), DATE);

      expect(afterEvent).toBe(afterMyResponse);
      expect(afterEvent[0]).toMatchObject({ status: 'Mine', bookingId: 42 });
    });

    it('the broadcast event first (Booked), then my own response arrives: ends up Mine', () => {
      const initial = [slot({ status: 'Free' })];

      const afterEvent = applySlotChanged(initial, event({ isBooked: true }), DATE);
      expect(afterEvent[0]).toMatchObject({ status: 'Booked', bookingId: null });

      // The component's own successful response is applied directly, not
      // through the reducer (see room-schedule.ts) — modeled here as the
      // same kind of immutable slot replacement it performs.
      const afterMyResponse = afterEvent.map((s) =>
        s.timeSlotId === 1 ? { ...s, status: 'Mine' as const, bookingId: 42 } : s,
      );

      expect(afterMyResponse[0]).toMatchObject({ status: 'Mine', bookingId: 42 });
    });
  });

  describe('both arrival orders converge to the same end state — cancel', () => {
    it('my own response first (Free), then the broadcast event arrives: stays Free', () => {
      const afterMyResponse = [slot({ status: 'Free', bookingId: null })];

      const afterEvent = applySlotChanged(afterMyResponse, event({ isBooked: false }), DATE);

      expect(afterEvent).toBe(afterMyResponse);
      expect(afterEvent[0]).toMatchObject({ status: 'Free', bookingId: null });
    });

    it('the broadcast event first (Free), then my own response arrives: still Free', () => {
      const initial = [slot({ status: 'Mine', bookingId: 42 })];

      const afterEvent = applySlotChanged(initial, event({ isBooked: false }), DATE);
      expect(afterEvent[0]).toMatchObject({ status: 'Free', bookingId: null });

      // The page only sets Free "if still Mine" on its own cancel success
      // (see room-schedule.ts) — by the time this response lands the
      // event already moved it to Free, so the page leaves it alone.
      const stillFree = afterEvent[0].status === 'Mine' ? afterEvent.map((s) => ({ ...s, status: 'Free' as const, bookingId: null })) : afterEvent;

      expect(stillFree[0]).toMatchObject({ status: 'Free', bookingId: null });
    });
  });
});
