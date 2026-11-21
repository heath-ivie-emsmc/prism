import { NetCashMonth, boundedMonths, changeFromPrior, formatCompact, formatExact, formatLabel, MAX_NET_CASH_MONTHS, summarizeNetCash } from './net-cash';

const MONTHS: NetCashMonth[] = [
  { label: 'Sep', year: 2026, value: 1_250_460, kind: 'actual' },
  { label: 'Oct', year: 2026, value: 1_100_240, kind: 'actual' },
  { label: 'Nov', year: 2026, value: 1_214_774, kind: 'projected', low: 1_150_000, high: 1_280_000 },
];

describe('net cash helpers', () => {
  it('formats exact, compact and label figures', () => {
    expect(formatExact(1_100_240)).toBe('$1,100,240.00');
    expect(formatCompact(1_100_240)).toBe('$1.1M');
    expect(formatLabel(1_200_000)).toBe('$1.20M');
    expect(formatLabel(500_000)).toBe('$500K');
  });

  it('computes the change from the prior month, and none for the first', () => {
    expect(changeFromPrior(MONTHS, 0)).toBeNull();
    expect(Math.round(changeFromPrior(MONTHS, 1)!)).toBe(-12);
  });

  it('summarizes the latest month and the projection for screen readers', () => {
    const text = summarizeNetCash(MONTHS);

    expect(text).toContain('Sep 2026 to Nov 2026');
    expect(text).toContain('Latest month Oct $1.1M, down 12 percent');
    expect(text).toContain('Projected Nov $1.21M');
  });

  it('says so when there is no data', () => {
    expect(summarizeNetCash([])).toBe('Net cash collected: no data.');
  });

  it('caps the months it draws', () => {
    const many = Array.from({ length: MAX_NET_CASH_MONTHS + 10 }, (_, i) => ({ ...MONTHS[0]!, label: `M${i}` }));

    expect(boundedMonths(many)).toHaveLength(MAX_NET_CASH_MONTHS);
  });
});
