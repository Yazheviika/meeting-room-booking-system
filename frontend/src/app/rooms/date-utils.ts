/**
 * Formats a `Date` as `"yyyy-MM-dd"` using its *local* year/month/day —
 * not `date.toISOString()`, which converts to UTC first and can shift the
 * date by a day depending on the browser's timezone offset (the classic
 * Angular Material datepicker pitfall).
 */
export function toIsoDate(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}
