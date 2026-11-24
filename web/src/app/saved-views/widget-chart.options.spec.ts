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
    expect(stacked.series.map((s) => [s.name, s.stack, s.data])).toEqual([['Scene', 'total', [5, 10]], ['Interfacility', 'total', [5, 4]]]);
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
