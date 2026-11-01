function pad(value: number, length = 2): string {
  return String(value).padStart(length, '0');
}

/** Format an instant in the browser's local timezone for datetime-local. */
export function toLocalDateTimeInput(iso: string): string {
  const date = new Date(iso);
  if (!Number.isFinite(date.getTime()) || date.getFullYear() < 1 || date.getFullYear() > 9999) {
    return '';
  }
  return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
    + `T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** Convert a real browser-local calendar minute to UTC; reject normalized dates and DST gaps. */
export function toUtcIso(localValue: string): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(localValue);
  if (!match) return null;
  const [, yearText, monthText, dayText, hourText, minuteText] = match;
  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);
  const hour = Number(hourText);
  const minute = Number(minuteText);
  if (year < 1 || month < 1 || month > 12 || day < 1 || day > 31
    || hour > 23 || minute > 59) return null;

  // ISO local parsing also preserves years 0001–0099; the numeric Date constructor adds 1900.
  const date = new Date(localValue);
  if (!Number.isFinite(date.getTime()) || date.getFullYear() !== year
    || date.getMonth() !== month - 1 || date.getDate() !== day
    || date.getHours() !== hour || date.getMinutes() !== minute) return null;

  // Keep UTC values inside the server's supported four-digit, positive year range.
  if (date.getUTCFullYear() < 1 || date.getUTCFullYear() > 9999) return null;
  return date.toISOString();
}
