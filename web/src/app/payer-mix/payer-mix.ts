import type { EChartsCoreOption } from 'echarts/core';
import type { ChartColors } from '../charts/chart.component';
import { PayerMixDto } from '../metrics/net-collections.service';

/** At least this many percentage points of movement is worth naming. */
export const NOTABLE_POINTS = 3;

export interface PayerShare {
  readonly key: string;
  readonly name: string;
  readonly count: number;
  readonly priorCount: number;
  /** Share of the month's transports, 0 to 100. */
  readonly percent: number;
  readonly priorPercent: number;
  /** Change in share from the month before, in percentage points. */
  readonly points: number;
}

export interface PayerMixFigures {
  readonly shares: readonly PayerShare[];
  readonly total: number;
  readonly priorTotal: number;
}

const percentOf = (count: number, total: number): number => (total === 0 ? 0 : (count / total) * 100);

/** Turns the counts from the API into shares of each month's transports. */
export function toPayerMix(dto: PayerMixDto | null): PayerMixFigures {
  const rows = dto?.shares ?? [];
  const total = rows.reduce((sum, s) => sum + s.current, 0);
  const priorTotal = rows.reduce((sum, s) => sum + s.prior, 0);
  return {
    total,
    priorTotal,
    shares: rows.map((s) => {
      const percent = percentOf(s.current, total);
      const priorPercent = percentOf(s.prior, priorTotal);
      return { key: s.key, name: s.name, count: s.current, priorCount: s.prior, percent, priorPercent, points: percent - priorPercent };
    }),
  };
}

/** The group whose share rose the most, if it rose by a notable amount and there is a prior month to compare. */
export function biggestRise(figures: PayerMixFigures): PayerShare | null {
  if (figures.priorTotal === 0 || figures.total === 0) return null;
  const best = figures.shares.reduce<PayerShare | null>((top, s) => (top === null || s.points > top.points ? s : top), null);
  return best !== null && Math.round(best.points) >= NOTABLE_POINTS ? best : null;
}

/** A change in points, written with a real minus sign: "+5 pts", "−3 pts", "0 pts". */
export function formatPoints(points: number): string {
  const n = Math.round(points);
  return `${n > 0 ? '+' : n < 0 ? '−' : ''}${Math.abs(n)} ${Math.abs(n) === 1 ? 'pt' : 'pts'}`;
}

/** The fill and the readable text color on it for each payer group, in the stacked bars, donuts and their key. Fixed, so a group has one color on every view. */
const PAYER_COLORS: Record<string, { fill: string; text: string }> = {
  commercial: { fill: '#00704a', text: '#ffffff' },
  medicare: { fill: '#ffd400', text: '#1a1a1a' },
  medicaid: { fill: '#a01f1f', text: '#ffffff' },
  'other-gov': { fill: '#5f5f66', text: '#ffffff' },
  va: { fill: '#8c8c92', text: '#1a1a1a' },
  facility: { fill: '#3b6fb6', text: '#ffffff' },
  patient: { fill: '#e2e2e6', text: '#1a1a1a' },
  'not-recorded': { fill: '#c2c2c8', text: '#1a1a1a' },
};

export function payerColor(key: string): { fill: string; text: string } {
  return PAYER_COLORS[key] ?? PAYER_COLORS['not-recorded']!;
}

/** The payer a donut's centre calls out: the one whose share rose most, else the largest. */
export function highlightedPayer(figures: PayerMixFigures): PayerShare | null {
  return biggestRise(figures) ?? figures.shares.reduce<PayerShare | null>((top, s) => (top === null || s.count > top.count ? s : top), null);
}

export const formatShare = (percent: number): string => `${Math.round(percent)}%`;

/** One plain sentence: the largest payer, and the groups that moved by a notable amount. */
export function summarizePayerMix(figures: PayerMixFigures, groupName: string, currentName: string, priorName: string): string {
  if (figures.total === 0) return '';
  const largest = figures.shares.reduce((top, s) => (s.count > top.count ? s : top));
  const lead = `${largest.name} was ${formatShare(largest.percent)} of ${groupName.toLowerCase()} transports in ${currentName}.`;
  if (figures.priorTotal === 0) return lead;
  const moved = figures.shares
    .filter((s) => Math.abs(Math.round(s.points)) >= NOTABLE_POINTS)
    .sort((a, b) => Math.abs(b.points) - Math.abs(a.points))
    .map((s) => `${s.name} ${s.points > 0 ? 'rose' : 'fell'} ${Math.abs(Math.round(s.points))} points, from ${formatShare(s.priorPercent)} in ${priorName} to ${formatShare(s.percent)}.`);
  return [lead, ...moved].join(' ');
}

/** The text alternative to the chart (WCAG 1.1.1): every share, in order. */
export function describePayerMix(figures: PayerMixFigures, groupName: string, currentName: string, priorName: string): string {
  const parts = figures.shares.map((s) => `${s.name} ${formatShare(s.percent)} in ${currentName}, ${formatShare(s.priorPercent)} in ${priorName}`);
  return `Share of ${groupName.toLowerCase()} transports by payer. ${parts.join('. ')}.`;
}

/** Pure: grouped bars of each payer's share, the earlier month in grey and the later one in green. Horizontal when the chart is narrow. */
export function buildPayerMixOption(figures: PayerMixFigures, currentName: string, priorName: string, colors: ChartColors): EChartsCoreOption {
  const shares = figures.shares;
  const rise = biggestRise(figures);
  const highest = Math.max(10, ...shares.flatMap((s) => [s.percent, s.priorPercent]));
  const max = Math.min(100, Math.ceil((highest + 5) / 10) * 10);
  const horizontal = colors.narrow;
  const category = {
    type: 'category' as const,
    data: shares.map((s) => s.name),
    inverse: horizontal,
    axisLine: { lineStyle: { color: colors.grid } },
    axisTick: { show: false },
    axisLabel: { color: colors.text, interval: 0, width: horizontal ? 110 : 84, overflow: 'break' as const, lineHeight: 16 },
  };
  const value = {
    type: 'value' as const,
    min: 0,
    max,
    interval: max > 50 ? 20 : 10,
    axisLabel: { color: colors.mutedText, formatter: '{value}%' },
    splitLine: { lineStyle: { color: colors.grid } },
  };
  const round = (n: number): number => Math.round(n * 10) / 10;
  return {
    animation: true,
    grid: { left: 8, right: horizontal ? 40 : 16, top: 28, bottom: 8, containLabel: true },
    tooltip: {
      trigger: 'axis',
      // The highlight for the hovered payer sits behind the bars (z below a bar series' 2) and is faint, so a bar shows through it.
      axisPointer: { type: 'shadow', z: 1, shadowStyle: { color: colors.grid, opacity: 0.15 } },
      confine: true,
      formatter: (params: unknown) => {
        const first = (Array.isArray(params) ? params : [params])[0] as { dataIndex: number } | undefined;
        const s = first ? shares[first.dataIndex] : undefined;
        return s
          ? `${s.name}<br/>${priorName}: <b>${formatShare(s.priorPercent)}</b> (${s.priorCount} transports)<br/>${currentName}: <b>${formatShare(s.percent)}</b> (${s.count} transports)<br/>${formatPoints(s.points)}`
          : '';
      },
    },
    xAxis: horizontal ? value : category,
    yAxis: horizontal ? category : value,
    series: [
      // The bars have no hover state of their own: ECharts would dim the one under the pointer, and they are already told apart by color.
      { name: priorName, type: 'bar', barMaxWidth: 28, emphasis: { disabled: true }, itemStyle: { color: colors.actual, borderRadius: 3 }, data: shares.map((s) => round(s.priorPercent)) },
      {
        name: currentName,
        type: 'bar',
        emphasis: { disabled: true },
        barMaxWidth: 28,
        itemStyle: { color: colors.projected, borderRadius: 3 },
        data: shares.map((s) => ({
          value: round(s.percent),
          itemStyle: rise?.key === s.key ? { color: colors.highlight, borderRadius: 3 } : undefined,
          label: rise?.key === s.key ? { show: true, position: horizontal ? 'right' : 'top', color: colors.highlight, fontWeight: 'bold', formatter: formatPoints(s.points) } : undefined,
        })),
      },
    ],
    textStyle: { color: colors.text, fontFamily: 'inherit' },
  };
}
