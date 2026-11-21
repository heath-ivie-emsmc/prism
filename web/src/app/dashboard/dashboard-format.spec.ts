import { formatDate, formatInstant, periodOptions } from './dashboard-format';

describe('dashboard formatting', () => {
  it('shows a date with no time as month/day/year without moving it across a time zone', () => {
    expect(formatDate('2026-10-31')).toBe('10/31/2026');
    expect(formatDate('not a date')).toBe('not a date');
  });

  it('shows an instant in the Provider time zone', () => {
    expect(formatInstant('2026-11-05T13:42:00Z', 'America/Chicago')).toBe('11/05/2026, 7:42 AM');
    expect(formatInstant('2026-11-05T13:42:00Z', 'America/Los_Angeles')).toBe('11/05/2026, 5:42 AM');
  });

  it('shows nothing for an unreadable instant', () => {
    expect(formatInstant('garbage', 'America/Chicago')).toBe('');
  });

  it('lists the month of Data Through first, then earlier months, across a year boundary', () => {
    const options = periodOptions('2026-02-28', 4);

    expect(options.map((o) => o.label)).toEqual(['February 2026', 'January 2026', 'December 2025', 'November 2025']);
    expect(options.map((o) => o.value)).toEqual(['2026-02', '2026-01', '2025-12', '2025-11']);
  });

  it('lists nothing when Data Through is not a date', () => {
    expect(periodOptions('soon')).toEqual([]);
  });
});
