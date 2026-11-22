import type { EChartsCoreOption } from 'echarts/core';
import type { ChartColors } from '../charts/chart.component';
import { WidgetData, money, whole } from './widget-data';

export type ChartKind = 'line' | 'column' | 'stacked' | 'donut';

const SYMBOLS = ['circle', 'rect', 'triangle', 'diamond', 'pin'] as const;
const DASHES = ['solid', 'dashed', 'dotted', 'dashed', 'dotted'] as const;

const compactMoney = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', notation: 'compact', maximumFractionDigits: 1 });
const compactCount = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 });

/** Formats a value the way its series is measured, for axes and value labels. */
export function formatValue(value: number, unit: WidgetData['unit'], compact: boolean): string {
  switch (unit) {
    case 'usd':
      return compact ? compactMoney.format(value) : money(value);
    case 'percent':
      return `${value.toFixed(1)}%`;
    default:
      return compact ? compactCount.format(value) : whole(value);
  }
}

/**
 * The same figures as the table, drawn. Pure: the same data, kind and colors always give the same option, so it is tested without a browser.
 * Series are told apart by shape and dash as well as colour, and ECharts' own generated description is off so the widget's own text stands.
 */
export function buildWidgetOption(data: WidgetData, kind: ChartKind, colors: ChartColors): EChartsCoreOption {
  const palette = [colors.actual, colors.highlight, colors.projected, colors.mutedText, colors.text];
  const base = {
    animation: false,
    aria: { enabled: true, label: { enabled: false }, decal: { show: data.series.length >= 4 } },
    color: palette,
    textStyle: { color: colors.text },
  };

  if (kind === 'donut') {
    // A donut shows one set of figures: the newest series, which is the month asked for.
    const latest = data.series[data.series.length - 1];
    return {
      ...base,
      tooltip: { trigger: 'item', valueFormatter: (value: unknown) => formatValue(Number(value), data.unit, false) },
      legend: { bottom: 0, textStyle: { color: colors.text } },
      series: [
        {
          type: 'pie',
          radius: ['45%', '70%'],
          itemStyle: { borderColor: colors.text, borderWidth: 1 },
          label: { color: colors.text, formatter: (p: { name: string; percent: number }) => (colors.narrow ? '' : `${p.name} ${p.percent.toFixed(0)}%`) },
          data: data.categories.map((name, i) => ({ name, value: latest?.values[i] ?? 0 })),
        },
      ],
    } as EChartsCoreOption;
  }

  const bar = kind === 'column' || kind === 'stacked';
  return {
    ...base,
    grid: { left: 8, right: 16, top: data.series.length > 1 ? 36 : 16, bottom: 8, containLabel: true },
    legend: data.series.length > 1 ? { top: 0, textStyle: { color: colors.text } } : undefined,
    tooltip: { trigger: 'axis', valueFormatter: (value: unknown) => formatValue(Number(value), data.unit, false) },
    xAxis: { type: 'category', data: [...data.categories], axisLabel: { color: colors.mutedText, interval: 'auto' }, axisLine: { lineStyle: { color: colors.grid } } },
    yAxis: {
      type: 'value',
      axisLabel: { color: colors.mutedText, formatter: (value: number) => formatValue(value, data.unit, true) },
      splitLine: { lineStyle: { color: colors.grid } },
    },
    series: data.series.map((series, i) => ({
      name: series.name,
      type: bar ? 'bar' : 'line',
      data: [...series.values],
      ...(kind === 'stacked' ? { stack: 'total' } : {}),
      ...(bar ? { itemStyle: { borderColor: colors.text, borderWidth: kind === 'stacked' ? 1 : 0 } } : { symbol: SYMBOLS[i % SYMBOLS.length], symbolSize: 8, lineStyle: { type: DASHES[i % DASHES.length], width: 2 } }),
    })),
  } as EChartsCoreOption;
}
