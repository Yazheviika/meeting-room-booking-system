import { ScheduleSlot } from './rooms.service';
import { SlotChangedEvent } from '../signalr/booking-hub.service';

/**
 * Pure `(schedule, event) -> schedule`, per CLAUDE.md's SignalR protocol.
 * Only ever applies to *externally*-arriving SlotChanged events — a
 * component's own book()/cancel() response sets "Mine"/"Free" directly,
 * never through this function (see room-schedule.ts for why, and the
 * both-arrival-orders tests below for what that guarantees either way).
 *
 * Returns the exact same array reference whenever nothing actually
 * changes, so an unrelated update doesn't cause every slot to re-render.
 */
export function applySlotChanged(
  schedule: ScheduleSlot[],
  event: SlotChangedEvent,
  currentDate: string,
): ScheduleSlot[] {
  if (event.date !== currentDate) {
    return schedule;
  }

  const index = schedule.findIndex((slot) => slot.timeSlotId === event.slotId);
  if (index === -1) {
    return schedule;
  }

  const slot = schedule[index];
  if (slot.status === 'Past') {
    return schedule;
  }

  // The event carries no identity, so "someone booked it" can never tell
  // the difference between "someone else" and "actually still me" — the
  // only way those don't fight is leaving an already-"Mine" slot alone.
  const nextStatus = event.isBooked ? (slot.status === 'Mine' ? 'Mine' : 'Booked') : 'Free';
  const nextBookingId = nextStatus === 'Mine' ? slot.bookingId : null;

  if (nextStatus === slot.status && nextBookingId === slot.bookingId) {
    return schedule;
  }

  const next = [...schedule];
  next[index] = { ...slot, status: nextStatus, bookingId: nextBookingId };
  return next;
}
