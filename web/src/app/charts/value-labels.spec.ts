import { describe, expect, it } from 'vitest';
import { MAX_LABELS_NARROW, MAX_LABELS_WIDE, planValueLabels, stackTotals, valueLabelStyle } from './value-labels';

describe('planValueLabels', () => {
  it('labels a column per bar when there is room', () => {
    expect(planValueLabels({ kind: 'column', categories: 6, series: 1, narrow: false })).toEqual({ values: true, totals: false });
    expect(planValueLabels({ kind: 'column', categories: MAX_LABELS_NARROW, series: 1, narrow: true }).values).toBe(true);
  });

  it('draws none on a phone with 12 months, and none past the wide cap', () => {
    expect(planValueLabels({ kind: 'column', categories: 12, series: 1, narrow: true })).toEqual({ values: false, totals: false });
    expect(planValueLabels({ kind: 'line', categories: 12, series: 1, narrow: true }).values).toBe(false);
    expect(planValueLabels({ kind: 'column', categories: MAX_LABELS_WIDE + 1, series: 1, narrow: false }).values).toBe(false);
  });

  it('counts every bar of a grouped column', () => {
    expect(planValueLabels({ kind: 'column', categories: 8, series: 2, narrow: false }).values).toBe(true);
    expect(planValueLabels({ kind: 'column', categories: 13, series: 2, narrow: false }).values).toBe(false);
  });

  it('labels a line only when it is the one series', () => {
    expect(planValueLabels({ kind: 'line', categories: 6, series: 1, narrow: false }).values).toBe(true);
    expect(planValueLabels({ kind: 'line', categories: 6, series: 2, narrow: false }).values).toBe(false);
  });

  it('shows the total, not a figure per segment, on a stack', () => {
    expect(planValueLabels({ kind: 'stacked', categories: 6, series: 3, narrow: false })).toEqual({ values: false, totals: true });
    expect(planValueLabels({ kind: 'stacked', categories: 12, series: 3, narrow: true }).totals).toBe(false);
  });

  it('draws nothing for an empty chart', () => {
    expect(planValueLabels({ kind: 'column', categories: 0, series: 1, narrow: false }).values).toBe(false);
    expect(planValueLabels({ kind: 'column', categories: 3, series: 0, narrow: false }).values).toBe(false);
  });
});

describe('valueLabelStyle', () => {
  const style = valueLabelStyle('#111', (v) => `USD ${v}`);

  it('uses the theme text color and formats the value', () => {
    expect(style.color).toBe('#111');
    expect(style.formatter({ value: 5 })).toBe('USD 5');
  });

  it('reads the last element of a pair and blanks a gap', () => {
    expect(style.formatter({ value: ['Oct', 7] })).toBe('USD 7');
    expect(style.formatter({ value: '-' })).toBe('');
  });
});

describe('stackTotals', () => {
  it('adds the series per category and counts a gap as zero', () => {
    expect(stackTotals([{ values: [1, 2, 3] }, { values: [4, null, 6] }], 3)).toEqual([5, 2, 9]);
  });
});
