import { describe, expect, it } from 'vitest';
import { ChartColors } from '../charts/chart.component';
import { payerColor } from '../metrics/payer-mix/payer-mix';
import { buildWidgetOption } from './widget-chart.options';
import { WidgetData } from './widget-data';

const COLORS: ChartColors = { text: '#111', mutedText: '#555', grid: '#ddd', actual: '#606060', highlight: '#a01f1f', projected: '#00704a', categorical: ['#38469b', '#00724c'], narrow: false };

function data(over: Partial<WidgetData>): WidgetData {
  return {
    summary: 's', table: { caption: 'c', columns: [], rows: [] }, categories: ['Commercial', 'Medicare', 'Mystery'], series: [{ name: 'Cash', values: [3, 2, 1] }],
    headline: null, dataThrough: null, caveat: '', empty: false, unit: 'usd', ...over,
  };
}

interface Bars { series: { data: { value: number; itemStyle?: { color: string } }[]; itemStyle: { borderWidth: number } }[] }
interface Pie { series: { data: { name: string; itemStyle?: { color: string } }[] }[] }

describe('buildWidgetOption payer colours', () => {
  it('colours each bar of a single-series payer chart with that payer\'s own colour, and outlines it so a pale fill shows', () => {
    const option = buildWidgetOption(data({ categoryKeys: ['commercial', 'medicare', 'unknown-key'] }), 'column', COLORS) as unknown as Bars;

    expect(option.series[0]!.data.map((d) => d.itemStyle?.color)).toEqual([payerColor('commercial').fill, payerColor('medicare').fill, payerColor('not-recorded').fill]);
    expect(option.series[0]!.data.map((d) => d.value)).toEqual([3, 2, 1]);
    expect(option.series[0]!.itemStyle.borderWidth).toBe(1);
  });

  it('keeps the palette when the categories are not payers', () => {
    const option = buildWidgetOption(data({}), 'column', COLORS) as unknown as { series: { data: unknown[]; itemStyle: { borderWidth: number } }[]; color: string[] };

    expect(option.series[0]!.data).toEqual([3, 2, 1]);
    expect(option.series[0]!.itemStyle.borderWidth).toBe(0);
    expect(option.color).toEqual(['#38469b', '#00724c']);
  });

  it('keeps one colour per series when there are several, since the series are what differs', () => {
    const option = buildWidgetOption(data({ categoryKeys: ['commercial', 'medicare', 'va'], series: [{ name: 'Prior', values: [1, 2, 3] }, { name: 'Now', values: [3, 2, 1] }] }), 'column', COLORS) as unknown as { series: { data: unknown[] }[] };

    expect(option.series[0]!.data).toEqual([1, 2, 3]);
  });

  it('does not colour a line by payer', () => {
    const option = buildWidgetOption(data({ categoryKeys: ['commercial', 'medicare', 'va'] }), 'line', COLORS) as unknown as { series: { data: unknown[] }[] };

    expect(option.series[0]!.data).toEqual([3, 2, 1]);
  });

  it('colours the slices of a payer donut the same way', () => {
    const option = buildWidgetOption(data({ categoryKeys: ['commercial', 'medicare', 'va'] }), 'donut', COLORS) as unknown as Pie;

    expect(option.series[0]!.data.map((d) => d.itemStyle?.color)).toEqual([payerColor('commercial').fill, payerColor('medicare').fill, payerColor('va').fill]);
  });
});

describe('buildWidgetOption trend and tooltips', () => {
  const trend = { categories: ['Sep 2026', 'Oct 2026'], series: [{ name: 'Scene', values: [5, 10] }, { name: 'Interfacility', values: [5, 4] }] };

  it('draws the trend, not the one period, for the line and stacked presentations, and the one period for columns', () => {
    const withTrend = data({ unit: 'count', trend });
    const stacked = buildWidgetOption(withTrend, 'stacked', COLORS) as unknown as { xAxis: { data: string[] }; series: { name: string; stack?: string; data: number[] }[] };
    const line = buildWidgetOption(withTrend, 'line', COLORS) as unknown as { xAxis: { data: string[] }; series: { name: string }[] };
    const column = buildWidgetOption(withTrend, 'column', COLORS) as unknown as { xAxis: { data: string[] } };

    expect(stacked.xAxis.data).toEqual(trend.categories);
    // The last series is the invisible one that writes each stack's total.
    expect(stacked.series.slice(0, 2).map((s) => [s.name, s.stack, s.data])).toEqual([['Scene', 'total', [5, 10]], ['Interfacility', 'total', [5, 4]]]);
    expect(stacked.series[2]!.data).toEqual([10, 14]);
    expect(line.series.map((s) => s.name)).toEqual(['Scene', 'Interfacility']);
    expect(column.xAxis.data).toEqual(['Commercial', 'Medicare', 'Mystery']);
  });

  it('escapes warehouse text in the axis and donut tooltips', () => {
    const hostile = '<img src=x onerror=alert(1)>';
    const axis = (buildWidgetOption(data({ unit: 'count' }), 'column', COLORS) as unknown as { tooltip: { formatter: (p: unknown) => string } }).tooltip.formatter([{ axisValueLabel: hostile, seriesName: hostile, value: 3, marker: '' }]);
    const pie = (buildWidgetOption(data({ unit: 'count' }), 'donut', COLORS) as unknown as { tooltip: { formatter: (p: unknown) => string } }).tooltip.formatter({ name: hostile, value: 3, percent: 50, marker: '' });

    for (const html of [axis, pie]) {
      expect(html).not.toContain('<img');
      expect(html).toContain('&lt;img src=x onerror=alert(1)&gt;');
    }
  });
});

describe('buildWidgetOption value labels', () => {
  interface Labelled { grid: { top: number }; series: { type: string; name: string; label?: { show: boolean; formatter: (p: { value: unknown }) => string }; data: number[] }[]; tooltip: { formatter: (p: unknown) => string } }
  const months = ['Jan', 'Feb', 'Mar'];
  const two = { categories: months, series: [{ name: 'Cash', values: [2_100_000, 900, 5] }] };

  it('writes each column\'s figure on the bar, abbreviated like the axis', () => {
    const option = buildWidgetOption(data({ categories: months, series: two.series }), 'column', COLORS) as unknown as Labelled;
    expect(option.series[0]!.label?.show).toBe(true);
    expect(option.series[0]!.label?.formatter({ value: 2_100_000 })).toBe('$2.1M');
    expect(option.grid.top).toBeGreaterThan(16);
  });

  it('formats counts and percents the way their unit is measured', () => {
    const counts = buildWidgetOption(data({ unit: 'count', categories: months, series: two.series }), 'column', COLORS) as unknown as Labelled;
    expect(counts.series[0]!.label?.formatter({ value: 1500 })).toBe('1.5K');
    const percent = buildWidgetOption(data({ unit: 'percent', categories: months, series: two.series }), 'column', COLORS) as unknown as Labelled;
    expect(percent.series[0]!.label?.formatter({ value: 12.34 })).toBe('12.3%');
  });

  it('draws no labels for 12 months on a narrow chart, and keeps the grid headroom small', () => {
    const many = Array.from({ length: 12 }, (_, i) => `M${i}`);
    const option = buildWidgetOption(data({ categories: many, series: [{ name: 'Cash', values: many.map(() => 1) }] }), 'column', { ...COLORS, narrow: true }) as unknown as Labelled;
    expect(option.series[0]!.label).toBeUndefined();
    expect(option.grid.top).toBe(16);
  });

  it('labels a single-series line but not a line with several series', () => {
    const one = buildWidgetOption(data({ categories: months, series: two.series }), 'line', COLORS) as unknown as Labelled;
    expect(one.series[0]!.label?.show).toBe(true);
    const several = buildWidgetOption(data({ categories: months, series: [two.series[0]!, { name: 'Other', values: [1, 2, 3] }] }), 'line', COLORS) as unknown as Labelled;
    expect(several.series.every((s) => s.label === undefined)).toBe(true);
  });

  it('shows the stack total above a stacked column through an invisible series that the tooltip skips', () => {
    const stacked = buildWidgetOption(data({ unit: 'count', categories: months, series: [{ name: 'A', values: [1, 2, 3] }, { name: 'B', values: [4, 5, 6] }] }), 'stacked', COLORS) as unknown as Labelled;
    const total = stacked.series.at(-1)!;
    expect(total.type).toBe('line');
    expect(total.data).toEqual([5, 7, 9]);
    expect(stacked.series.slice(0, -1).every((s) => s.label === undefined)).toBe(true);
    const tip = stacked.tooltip.formatter([{ seriesName: 'A', value: 1, axisValueLabel: 'Jan' }, { seriesName: total.name, value: 5, axisValueLabel: 'Jan' }]);
    expect(tip).toContain('A: 1');
    expect(tip).not.toContain('5');
  });
});
