const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

export interface PeriodOption {
  /** yyyy-MM */
  readonly value: string;
  readonly label: string;
}

/** 2026-10-31 becomes 10/31/2026. A date with no time has no zone, so nothing is converted. */
export function formatDate(isoDate: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoDate);
  return match ? `${match[2]}/${match[3]}/${match[1]}` : isoDate;
}

/** An instant shown in the Provider's own time zone, for example 11/05/2026, 7:42 AM. */
export function formatInstant(iso: string, timeZone: string): string {
  const instant = new Date(iso);
  if (Number.isNaN(instant.getTime())) return '';
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: 'numeric',
    minute: '2-digit',
    timeZone,
  }).format(instant);
}

/** The month Data Through falls in, then the months before it, newest first. */
export function periodOptions(dataThrough: string, count = 12): readonly PeriodOption[] {
  const match = /^(\d{4})-(\d{2})-\d{2}$/.exec(dataThrough);
  if (!match) return [];
  let year = Number(match[1]);
  let month = Number(match[2]) - 1;
  const options: PeriodOption[] = [];
  for (let i = 0; i < count; i++) {
    options.push({ value: `${year}-${String(month + 1).padStart(2, '0')}`, label: `${MONTH_NAMES[month]} ${year}` });
    month -= 1;
    if (month < 0) {
      month = 11;
      year -= 1;
    }
  }
  return options;
}

/** The warehouse's accounting period, 202610, written out as "October 2026". Anything else comes back unchanged. */
export function apPeriodLabel(period: string): string {
  const match = /^(\d{4})(\d{2})$/.exec(period);
  const name = match ? MONTH_NAMES[Number(match[2]) - 1] : undefined;
  return match && name ? `${name} ${match[1]}` : period;
}

/** The last day of an accounting period as yyyy-MM-dd: 202610 gives 2026-10-31. Anything else comes back empty. */
export function apPeriodEnd(period: string): string {
  const match = /^(\d{4})(\d{2})$/.exec(period);
  if (!match) return '';
  const day = new Date(Date.UTC(Number(match[1]), Number(match[2]), 0)).getUTCDate();
  return `${match[1]}-${match[2]}-${String(day).padStart(2, '0')}`;
}
