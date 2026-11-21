import type { CustomSeriesRenderItem } from 'echarts';
import type { EChartsCoreOption } from 'echarts/core';
import type { ChartColors } from './chart.component';
import { NetCashMonth, boundedMonths, changeFromPrior, formatCompact, formatExact, formatLabel } from './net-cash';

export type NetCashChartKind = 'line' | 'bar';

/** A round step (1, 2 or 5 times a power of ten) that gives about `ticks` gridlines, so axis labels are neat and never crowd each other. */
export function niceStep(range: number, ticks = 5): number {
  const rough = range / ticks;
  const power = 10 ** Math.floor(Math.log10(rough));
  const unit = rough / power;
  return (unit <= 1 ? 1 : unit <= 2 ? 2 : unit <= 5 ? 5 : 10) * power;
}

function axisLabel(month: NetCashMonth, index: number): string {
  return index === 0 || month.label === 'Jan' ? `${month.label}\n${month.year}` : month.label;
}

/** The likely-range whisker above and below a projected month (a vertical line with a cap at each end), with the month's figure above it. */
function whiskers(colors: ChartColors): CustomSeriesRenderItem {
  return (_params, api) => {
    const low = api.coord([api.value(0), api.value(1)]);
    const high = api.coord([api.value(0), api.value(2)]);
    const x = low[0]!;
    const style = { stroke: colors.mutedText, lineWidth: 1.5 };
    return {
      type: 'group',
      children: [
        ...(colors.narrow ? [] : [{ type: 'text' as const, style: { text: formatLabel(Number(api.value(3))), x, y: high[1]! - 8, fill: colors.text, textAlign: 'center' as const, textVerticalAlign: 'bottom' as const, fontSize: 12 } }]),
        { type: 'line', shape: { x1: x, y1: low[1]!, x2: x, y2: high[1]! }, style },
        { type: 'line', shape: { x1: x - 5, y1: high[1]!, x2: x + 5, y2: high[1]! }, style },
        { type: 'line', shape: { x1: x - 5, y1: low[1]!, x2: x + 5, y2: low[1]! }, style },
      ],
    };
  };
}

/** Pure: the same months, kind and colors always give the same option, so it is tested without a browser. */
export function buildNetCashOption(allMonths: readonly NetCashMonth[], kind: NetCashChartKind, colors: ChartColors): EChartsCoreOption {
  const months = boundedMonths(allMonths);
  const firstProjected = months.findIndex((m) => m.kind === 'projected');
  const lastActual = firstProjected === -1 ? months.length - 1 : firstProjected - 1;
  const ranges = months.flatMap((m, i) => (m.kind === 'projected' && m.low !== undefined && m.high !== undefined ? [[i, m.low, m.high, m.value]] : []));
  const dense = months.length > 24;
  // The whiskers and their figures are not part of the axis range, so work it out here: room for the whisker and the label above it.
  const values = months.flatMap((m) => [m.value, m.low ?? m.value, m.high ?? m.value]);
  const lowest = Math.min(...values);
  const highest = Math.max(...values);
  const pad = (highest - lowest || highest || 1) * 0.12;
  const rawMin = kind === 'bar' ? 0 : Math.max(0, lowest - pad);
  const rawMax = highest + pad * (kind === 'bar' ? 1 : 1.5);
  const step = niceStep(rawMax - rawMin);
  const yMin = Math.floor(rawMin / step) * step;
  const yMax = Math.ceil(rawMax / step) * step;

  const base = {
    animation: !dense,
    grid: { left: 8, right: 16, top: 36, bottom: 8, containLabel: true },
    tooltip: {
      trigger: 'axis',
      confine: true,
      valueFormatter: (value: unknown) => formatExact(Number(value)),
    },
    xAxis: {
      type: 'category',
      data: months.map(axisLabel),
      boundaryGap: kind === 'bar',
      axisLine: { lineStyle: { color: colors.grid } },
      axisLabel: { color: colors.mutedText, interval: 0, lineHeight: 16, margin: 12 },
      axisTick: { show: false },
    },
    yAxis: {
      type: 'value',
      min: yMin,
      max: yMax,
      interval: step,
      axisLabel: { color: colors.mutedText, formatter: (value: number) => formatCompact(value) },
      splitLine: { lineStyle: { color: colors.grid } },
    },
    textStyle: { color: colors.text, fontFamily: 'inherit' },
  };

  const dividerSeries =
    firstProjected > 0
      ? {
          name: 'Actual and projected divider',
          type: 'custom',
          data: [[firstProjected - 0.5]],
          silent: true,
          tooltip: { show: false },
          z: 1,
          renderItem: ((params, api) => {
            // A category axis snaps a half index to a whole one, so average the two neighbours to land between them.
            const between = Number(api.value(0));
            const x = (api.coord([Math.floor(between), 0])[0]! + api.coord([Math.ceil(between), 0])[0]!) / 2;
            const area = params.coordSys as unknown as { y: number; height: number };
            const text = { fontSize: 12, textVerticalAlign: 'bottom' as const };
            return {
              type: 'group',
              children: [
                { type: 'line', shape: { x1: x, y1: area.y, x2: x, y2: area.y + area.height }, style: { stroke: colors.mutedText, lineWidth: 1, lineDash: [4, 4] } },
                { type: 'text', style: { ...text, text: 'Actual', x: x - 8, y: area.y - 8, fill: colors.mutedText, textAlign: 'right' } },
                { type: 'text', style: { ...text, text: 'Projected', x: x + 8, y: area.y - 8, fill: colors.text, fontWeight: 'bold', textAlign: 'left' } },
              ],
            };
          }) as CustomSeriesRenderItem,
        }
      : undefined;

  const range = {
    name: 'Likely range',
    type: 'custom',
    renderItem: whiskers(colors),
    data: ranges,
    z: 5,
    silent: true,
    tooltip: { show: false },
  };

  if (kind === 'bar') {
    return {
      ...base,
      series: [
        {
          name: 'Net cash collected',
          type: 'bar',
          barMaxWidth: 64,
          data: months.map((m, i) => ({
            value: m.value,
            itemStyle:
              m.kind === 'projected'
                ? { color: 'transparent', borderColor: colors.projected, borderWidth: 2, borderType: i === firstProjected ? 'solid' : 'dashed', borderRadius: 4 }
                : { color: i === lastActual ? colors.highlight : colors.actual, borderRadius: 4 },
            label: m.kind === 'projected' ? { show: false } : i === lastActual ? { fontWeight: 'bold' } : undefined,
          })),
          label: { show: !colors.narrow && months.length <= 12, position: 'top', color: colors.text, formatter: (p: { value: number }) => formatLabel(p.value) },
        },
        range,
        ...(dividerSeries ? [dividerSeries] : []),
      ],
    };
  }

  const latest = months[lastActual];
  const change = lastActual >= 0 ? changeFromPrior(months, lastActual) : null;
  const callout = latest ? `${latest.label} ${formatCompact(latest.value)}${change === null ? '' : `, ${Math.round(change)}%`}` : '';
  // The latest actual month is a larger filled point with its figure written under it, as in the mockup.
  const actualData = months.map((m, i) =>
    i === lastActual
      ? { value: m.value, symbolSize: 14, itemStyle: { color: colors.projected }, label: { show: true, position: 'bottom', distance: 10, color: colors.text, fontWeight: 'bold', formatter: callout } }
      : i < lastActual
        ? { value: m.value }
        : null,
  );
  // The projected line starts at the latest actual month so the two lines join.
  const projectedData = months.map((m, i) => (i >= lastActual && firstProjected !== -1 ? m.value : null));
  return {
    ...base,
    series: [
      {
        name: 'Net cash collected',
        type: 'line',
        data: actualData,
        showSymbol: true,
        symbol: 'circle',
        symbolSize: 8,
        lineStyle: { width: 2, color: colors.projected },
        itemStyle: { color: 'transparent', borderColor: colors.projected, borderWidth: 2 },
      },
      {
        name: 'Projected',
        type: 'line',
        data: projectedData,
        showSymbol: true,
        symbol: 'circle',
        symbolSize: 8,
        lineStyle: { width: 2, color: colors.projected, type: 'dashed' },
        itemStyle: { color: 'transparent', borderColor: colors.projected, borderWidth: 2 },
      },
      range,
      ...(dividerSeries ? [dividerSeries] : []),
    ],
  };
}
