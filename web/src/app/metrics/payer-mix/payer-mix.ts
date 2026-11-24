import type { EChartsCoreOption } from 'echarts/core';
import { planValueLabels, valueLabelStyle } from '../../charts/value-labels';
import type { ChartColors } from '../../charts/chart.component';
import { escapeHtml } from '../../shared/escape-html';
import { drillOf } from '../drill-link';
import { LinkDto, PayerMixDto } from './payer-mix.dto';

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
  /** Where choosing this payer group leads, or null when the API gave no link for it. */
  readonly drill?: DrillTarget | null;
  /** Colors for payers that have no fixed group color (the payers inside a group). */
  readonly fill?: string;
  readonly text?: string;
}

/** The payer group a drill link points at, read from the API's link. Never the API path itself: the SPA has its own route. */
export interface DrillTarget {
  readonly group: string;
  readonly payerKey: string;
}

/** The drill target in a share's links, or null. Parsed by the shared drill link parser, which ignores anything but a known target under the API's route. */
export function drillTarget(links: readonly LinkDto[] | undefined, fallbackGroup: string): DrillTarget | null {
  const drill = drillOf(links, fallbackGroup);
  return drill?.target === 'payer-mix-detail' ? { group: drill.group, payerKey: drill.payerKey! } : null;
}

/**
 * "Fixed Wing" and "fixed wing" both read as "Fixed wing"; level-of-service codes such as ALS stay as they are.
 * The warehouse cleans names in SQL for the Metric, but names that reach the page through the address and fallbacks are not cleaned, so the page evens them out. Cosmetic only: never used as a key.
 */
export function displayName(name: string): string {
  const trimmed = name.trim();
  if (trimmed === '' || trimmed === trimmed.toUpperCase()) return trimmed.toUpperCase();
  return trimmed.charAt(0).toUpperCase() + trimmed.slice(1).toLowerCase();
}

/** The accessible name of a link to a payer group's breakdown. */
export const drillName = (name: string): string => `View ${name} breakdown`;

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
      return { key: s.key, name: s.name, count: s.current, priorCount: s.prior, percent, priorPercent, points: percent - priorPercent, drill: drillTarget(s.links, dto?.group ?? '') };
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

/** The fill and the readable text color on it for each payer group, in the stacked bars, donuts and their key. Fixed, so a group has one color on every view. None is a grey: grey reads as "nothing to see" or "no data", and every payer is something to see. */
const PAYER_COLORS: Record<string, { fill: string; text: string }> = {
  commercial: { fill: '#00704a', text: '#ffffff' },
  medicare: { fill: '#ffd400', text: '#1a1a1a' },
  medicaid: { fill: '#a01f1f', text: '#ffffff' },
  'other-gov': { fill: '#752e58', text: '#ffffff' },
  va: { fill: '#c4560a', text: '#ffffff' },
  facility: { fill: '#3b6fb6', text: '#ffffff' },
  patient: { fill: '#6b4fc4', text: '#ffffff' },
  'not-recorded': { fill: '#7a4a21', text: '#ffffff' },
};

export function payerColor(key: string): { fill: string; text: string } {
  return PAYER_COLORS[key] ?? PAYER_COLORS['not-recorded']!;
}

/** The colors of a share: its own when the page gave it one (payers inside a group), else the fixed color of its payer group. */
export function shareColor(share: Pick<PayerShare, 'key' | 'fill' | 'text'>): { fill: string; text: string } {
  const fixed = payerColor(share.key);
  return { fill: share.fill ?? fixed.fill, text: share.text ?? fixed.text };
}

/** Colors for the payers inside one group, in order, each told apart from its neighbours. Plain hex, as ECharts cannot read theme tokens. */
const PAYER_PALETTE: readonly { fill: string; text: string }[] = [
  { fill: '#00704a', text: '#ffffff' },
  { fill: '#3b6fb6', text: '#ffffff' },
  { fill: '#c4560a', text: '#ffffff' },
  { fill: '#752e58', text: '#ffffff' },
  { fill: '#ffd400', text: '#1a1a1a' },
  { fill: '#6b4fc4', text: '#ffffff' },
  { fill: '#a01f1f', text: '#ffffff' },
  { fill: '#0b7285', text: '#ffffff' },
  { fill: '#5c7c00', text: '#ffffff' },
  { fill: '#b0306a', text: '#ffffff' },
  { fill: '#7a4a21', text: '#ffffff' },
];

export const paletteColor = (index: number): { fill: string; text: string } => PAYER_PALETTE[index % PAYER_PALETTE.length]!;

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
export function buildPayerMixOption(figures: PayerMixFigures, currentName: string, priorName: string, colors: ChartColors, drillable: ReadonlySet<string> = new Set()): EChartsCoreOption {
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
    // On a narrow chart the value axis runs along the bottom, so it gets fewer ticks: at 10% steps the labels collide on a phone.
    interval: horizontal ? (max > 60 ? 50 : max > 20 ? 20 : 10) : max > 50 ? 20 : 10,
    axisLabel: { color: colors.mutedText, formatter: '{value}%', hideOverlap: true },
    splitLine: { lineStyle: { color: colors.grid } },
  };
  const round = (n: number): number => Math.round(n * 10) / 10;
  // Each bar carries its share when there is room (the shared plan), thinned if two would touch.
  const labels = planValueLabels({ kind: 'column', categories: shares.length, series: 2, narrow: colors.narrow });
  const barLabel = { ...valueLabelStyle(colors.text, (v) => `${Math.round(v)}%`, horizontal ? 'right' : 'top'), show: labels.values };
  return {
    animation: true,
    grid: { left: 8, right: horizontal ? 56 : 16, top: 28, bottom: 8, containLabel: true },
    tooltip: {
      trigger: 'axis',
      // The highlight for the hovered payer sits behind the bars (z below a bar series' 2) and is faint, so a bar shows through it.
      axisPointer: { type: 'shadow', z: 1, shadowStyle: { color: colors.grid, opacity: 0.15 } },
      confine: true,
      formatter: (params: unknown) => {
        const first = (Array.isArray(params) ? params : [params])[0] as { dataIndex: number } | undefined;
        const s = first ? shares[first.dataIndex] : undefined;
        return s
          ? `${escapeHtml(s.name)}<br/>${escapeHtml(priorName)}: <b>${formatShare(s.priorPercent)}</b> (${s.priorCount} transports)<br/>${escapeHtml(currentName)}: <b>${formatShare(s.percent)}</b> (${s.count} transports)<br/>${formatPoints(s.points)}${drillable.has(s.key) ? '<br/>Click to see the breakdown' : ''}`
          : '';
      },
    },
    xAxis: horizontal ? value : category,
    yAxis: horizontal ? category : value,
    series: [
      // The bars have no hover state of their own: ECharts would dim the one under the pointer, and they are already told apart by color.
      { name: priorName, type: 'bar', barMaxWidth: 28, emphasis: { disabled: true }, label: barLabel, labelLayout: { hideOverlap: true }, itemStyle: { color: colors.actual, borderRadius: 3 }, data: shares.map((s) => (drillable.has(s.key) ? { value: round(s.priorPercent), cursor: 'pointer' } : round(s.priorPercent))) },
      {
        name: currentName,
        type: 'bar',
        emphasis: { disabled: true },
        label: barLabel,
        labelLayout: { hideOverlap: true },
        barMaxWidth: 28,
        itemStyle: { color: colors.projected, borderRadius: 3 },
        data: shares.map((s) => ({
          value: round(s.percent),
          cursor: drillable.has(s.key) ? 'pointer' : undefined,
          itemStyle: rise?.key === s.key ? { color: colors.highlight, borderRadius: 3 } : undefined,
          label: rise?.key === s.key ? { show: true, position: horizontal ? 'right' : 'top', color: colors.highlight, fontWeight: 'bold', formatter: `${formatShare(s.percent)} ${formatPoints(s.points)}` } : undefined,
        })),
      },
    ],
    textStyle: { color: colors.text, fontFamily: 'inherit' },
  };
}
