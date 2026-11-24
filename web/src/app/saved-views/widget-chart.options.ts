import type { EChartsCoreOption } from 'echarts/core';
import type { ChartColors } from '../charts/chart.component';
import { escapeHtml } from '../shared/escape-html';
import { payerColor } from '../metrics/payer-mix/payer-mix';
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
 * The colour of one payer group's bar or slice, the same on every chart (the stacked bars and donuts on Payer mix use it too), so Commercial is
 * always the same green. Only when the category is a payer; months and other categories keep the palette. The outline keeps a pale fill visible.
 */
function payerFill(data: WidgetData, index: number): { itemStyle?: { color: string } } {
  const key = data.categoryKeys?.[index];
  return key === undefined ? {} : { itemStyle: { color: payerColor(key).fill } };
}

interface TooltipParam {
  marker?: string;
  seriesName?: string;
  name?: string;
  axisValueLabel?: string;
  value?: unknown;
  percent?: number;
}

/** The tooltip of a line or column chart. Category and series names are warehouse text, so each goes through escapeHtml before it joins the markup. */
function axisTooltip(unit: WidgetData['unit']): (params: TooltipParam | TooltipParam[]) => string {
  return (params) => {
    const rows = Array.isArray(params) ? params : [params];
    const head = escapeHtml(String(rows[0]?.axisValueLabel ?? rows[0]?.name ?? ''));
    const lines = rows.map((p) => `${p.marker ?? ''} ${escapeHtml(String(p.seriesName ?? ''))}: ${escapeHtml(formatValue(Number(Array.isArray(p.value) ? p.value[1] : p.value), unit, false))}`);
    return [head, ...lines].join('<br/>');
  };
}

/** The tooltip of a donut slice. The slice name is warehouse text and is escaped. */
function itemTooltip(unit: WidgetData['unit']): (p: TooltipParam) => string {
  return (p) => `${p.marker ?? ''} ${escapeHtml(String(p.name ?? ''))}: ${escapeHtml(formatValue(Number(p.value), unit, false))} (${(p.percent ?? 0).toFixed(1)}%)`;
}

/**
 * The same figures as the table, drawn. Pure: the same data, kind and colors always give the same option, so it is tested without a browser.
 * Series are told apart by shape and dash as well as colour, and ECharts' own generated description is off so the widget's own text stands.
 */
export function buildWidgetOption(data: WidgetData, kind: ChartKind, colors: ChartColors): EChartsCoreOption {
  const palette = colors.categorical?.length ? [...colors.categorical] : [colors.actual, colors.highlight, colors.projected, colors.mutedText, colors.text];
  const base = {
    animation: false,
    aria: { enabled: true, label: { enabled: false }, decal: { show: Math.max(data.series.length, data.trend?.series.length ?? 0) >= 4 } },
    color: palette,
    textStyle: { color: colors.text },
  };

  if (kind === 'donut') {
    // A donut shows one set of figures: the newest series, which is the month asked for.
    const latest = data.series[data.series.length - 1];
    return {
      ...base,
      tooltip: { trigger: 'item', formatter: itemTooltip(data.unit) },
      legend: { bottom: 0, textStyle: { color: colors.text } },
      series: [
        {
          type: 'pie',
          // A long legend (a phone, many slices) takes the bottom rows, so the ring sits higher and smaller there.
          radius: colors.narrow ? ['32%', '52%'] : ['45%', '68%'],
          center: ['50%', colors.narrow ? '36%' : '44%'],
          emphasis: { disabled: true },
          itemStyle: { borderColor: colors.text, borderWidth: 1 },
          label: { color: colors.text, formatter: (p: { name: string; percent: number }) => (colors.narrow || p.percent < 5 ? '' : `${p.name} ${p.percent.toFixed(0)}%`) },
          data: data.categories.map((name, i) => ({ name, value: latest?.values[i] ?? 0, ...payerFill(data, i) })),
        },
      ],
    } as EChartsCoreOption;
  }

  // The line and stacked presentations draw the Metric over time when it has a trend; the column and donut show the one period.
  const over = (kind === 'line' || kind === 'stacked') && data.trend ? data.trend : { categories: data.categories, series: data.series };
  const bar = kind === 'column' || kind === 'stacked';
  return {
    ...base,
    // The legend wraps onto more rows with more series or less width, and the plot starts below the last one.
    grid: { left: 8, right: 16, top: over.series.length > 1 ? 16 + 24 * Math.ceil(over.series.length / (colors.narrow ? 2 : 4)) : 16, bottom: 8, containLabel: true },
    legend: over.series.length > 1 ? { top: 0, textStyle: { color: colors.text } } : undefined,
    tooltip: { trigger: 'axis', formatter: axisTooltip(data.unit) },
    xAxis: { type: 'category', data: [...over.categories], axisLabel: { color: colors.mutedText, interval: 'auto' }, axisLine: { lineStyle: { color: colors.grid } } },
    yAxis: {
      type: 'value',
      axisLabel: { color: colors.mutedText, formatter: (value: number) => formatValue(value, data.unit, true) },
      splitLine: { lineStyle: { color: colors.grid } },
    },
    series: over.series.map((series, i) => ({
      name: series.name,
      type: bar ? 'bar' : 'line',
      emphasis: { disabled: true },
      data: bar && over === data.trend ? [...series.values] : bar && data.series.length === 1 && data.categoryKeys ? series.values.map((value, index) => ({ value, ...payerFill(data, index) })) : [...series.values],
      ...(kind === 'stacked' ? { stack: 'total' } : {}),
      ...(bar ? { itemStyle: { borderColor: colors.text, borderWidth: kind === 'stacked' || (data.series.length === 1 && data.categoryKeys) ? 1 : 0 } } : { symbol: SYMBOLS[i % SYMBOLS.length], symbolSize: 8, lineStyle: { type: DASHES[i % DASHES.length], width: 2 } }),
    })),
  } as EChartsCoreOption;
}
