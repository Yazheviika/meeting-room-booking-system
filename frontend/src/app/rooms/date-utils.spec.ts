import { toIsoDate } from './date-utils';

describe('toIsoDate', () => {
  it('formats using local date parts, zero-padded', () => {
    expect(toIsoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
    expect(toIsoDate(new Date(2026, 9, 31))).toBe('2026-10-31');
  });

  it('does not shift the date near midnight the way toISOString() would in a UTC-behind timezone', () => {
    // Local midnight, regardless of the machine's timezone offset from
    // UTC — date.toISOString() would render the *previous* calendar day
    // here for any timezone west of UTC, which is exactly the bug this
    // helper exists to avoid.
    const localMidnight = new Date(2026, 2, 15, 0, 0, 0);
    expect(toIsoDate(localMidnight)).toBe('2026-03-15');
  });
});
