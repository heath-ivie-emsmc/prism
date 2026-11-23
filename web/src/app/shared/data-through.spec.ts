import { describe, expect, it } from 'vitest';
import { isIsoDate, shortDate } from './data-through';

describe('data through dates', () => {
  it('accepts only real yyyy-MM-dd days', () => {
    expect(isIsoDate('2026-11-23')).toBe(true);
    for (const bad of ['2026-02-30', '2026-13-01', '20261123', '2026-11-23T00:00:00Z', '', null, undefined, 20261123]) {
      expect(isIsoDate(bad)).toBe(false);
    }
  });

  it('writes a day without shifting it by the time zone', () => {
    expect(shortDate('2026-11-23')).toMatch(/Nov(ember)? 23|23 Nov/);
    expect(shortDate('2026-01-01')).toMatch(/Jan(uary)? 1|1 Jan/);
  });

  it('writes nothing for something that is not a date', () => {
    expect(shortDate('soon')).toBe('');
    expect(shortDate(null)).toBe('');
  });
});
