const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** True for a yyyy-MM-dd date that is a real calendar day. */
export function isIsoDate(value: unknown): value is string {
  if (typeof value !== 'string') return false;
  const match = ISO_DATE.exec(value);
  if (!match) return false;
  const date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
  return date.getUTCMonth() === Number(match[2]) - 1 && date.getUTCDate() === Number(match[3]);
}

/** "Nov 23" for 2026-11-23, in the user's locale. The date has no time of day, so it is read and written in UTC to avoid shifting a day. Empty when not a date. */
export function shortDate(iso: string | null | undefined): string {
  if (!isIsoDate(iso)) return '';
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, day)));
}
