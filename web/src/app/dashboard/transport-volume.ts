import { monthName, priorMonthName, priorPeriod } from '../breakdown/net-cash-breakdown';
import { TransportVolumeDto } from '../metrics/net-collections.service';

export type MileUnit = 'statute' | 'nautical';

/** One statute mile is this many nautical miles. */
export const NAUTICAL_PER_STATUTE = 0.868976;

/** How many months the small charts show. */
export const CARD_MONTHS = 6;

const count = new Intl.NumberFormat('en-US');

export interface CardDelta {
  readonly direction: 'up' | 'down';
  readonly text: string;
}

export interface TransportsCard {
  readonly value: string;
  readonly delta: CardDelta | null;
  readonly footnote: string;
  /** Bar heights as percent of the tallest month, oldest first. A month with no transports is a stub, not nothing, so the row still reads as six months. */
  readonly bars: readonly number[];
  /** The same six months in words, for a screen reader and for hovering. */
  readonly description: string;
  readonly titles: readonly string[];
  /** The count of each month, to print over its bar. */
  readonly labels: readonly string[];
}

export interface MilesCard {
  readonly value: string;
  readonly unit: string;
  readonly delta: CardDelta | null;
  /** Points of a line across a 100 by 30 box, oldest first. */
  readonly line: string;
  /** Where each month sits on the line, as percent across and units down the 30 high box, with its figure to print over it. */
  readonly marks: readonly MileMark[];
  readonly description: string;
}

export interface MileMark {
  readonly x: number;
  readonly y: number;
  readonly label: string;
}

/** Room left and right of the line so the first and last figures are not cut off. */
const LINE_LEFT = 8;
const LINE_WIDTH = 84;

/** The months the cards cover, oldest first, ending at `end` (yyyyMM). */
export function cardMonths(end: string, months = CARD_MONTHS): string[] {
  const list = [end];
  while (list.length < months && priorPeriod(list[0]!) !== '') list.unshift(priorPeriod(list[0]!));
  return list;
}

/** "6 fewer than Sep", "12 more than Sep", or null when nothing changed or there was no month before to compare with. */
function countDelta(current: number, prior: number, priorName: string): CardDelta | null {
  if (current === prior) return null;
  const n = Math.abs(current - prior);
  return { direction: current > prior ? 'up' : 'down', text: `${count.format(n)} ${current > prior ? 'more' : 'fewer'} than ${priorName}` };
}

function percentDelta(current: number, prior: number, priorName: string): CardDelta | null {
  if (prior === 0) return null;
  const pct = Math.round((Math.abs(current - prior) / prior) * 100);
  if (pct === 0) return null;
  return { direction: current > prior ? 'up' : 'down', text: `${pct}% ${current > prior ? 'more' : 'fewer'} than ${priorName}` };
}

const MAX_GROUPS_SHOWN = 3;

/** "Rotor wing 92 · Fixed wing 48", the largest first, with the rest folded into "+2 more". */
export function groupsText(groups: TransportVolumeDto['groups']): string {
  const shown = groups.slice(0, MAX_GROUPS_SHOWN).map((g) => `${g.name} ${count.format(g.transports)}`);
  const more = groups.length - MAX_GROUPS_SHOWN;
  return [...shown, ...(more > 0 ? [`+${more} more`] : [])].join(' · ');
}

function valuesByMonth(volume: TransportVolumeDto, months: string[], pick: (p: TransportVolumeDto['points'][number]) => number): number[] {
  return months.map((m) => {
    const point = volume.points.find((p) => p.period === m);
    return point ? pick(point) : 0;
  });
}

export function toTransportsCard(volume: TransportVolumeDto | null): TransportsCard {
  const end = volume?.endPeriod ?? '';
  if (!volume || !end) return { value: '—', delta: null, footnote: '', bars: [], description: '', titles: [], labels: [] };
  const months = cardMonths(end);
  const values = valuesByMonth(volume, months, (p) => p.transports);
  const last = values[values.length - 1]!;
  const prior = values.length > 1 ? values[values.length - 2]! : 0;
  const tallest = Math.max(1, ...values);
  const titles = months.map((m, i) => `${monthName(m)} ${count.format(values[i]!)}`);
  return {
    value: count.format(last),
    delta: values.length > 1 ? countDelta(last, prior, priorMonthName(end)) : null,
    footnote: groupsText(volume.groups),
    bars: values.map((v) => Math.max(6, (v / tallest) * 100)),
    description: `Transports by month: ${titles.join(', ')}.`,
    titles,
    labels: values.map((v) => count.format(v)),
  };
}

/** "17.9k" for thousands so six figures fit across a narrow tile; the headline above has the exact number. */
export function shortCount(n: number): string {
  return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : count.format(n);
}

/** The figure in the chosen unit, rounded to a whole number. */
export function milesIn(statute: number, unit: MileUnit): number {
  return Math.round(unit === 'nautical' ? statute * NAUTICAL_PER_STATUTE : statute);
}

export function toMilesCard(volume: TransportVolumeDto | null, unit: MileUnit): MilesCard {
  const end = volume?.endPeriod ?? '';
  const unitName = unit === 'nautical' ? 'nautical mi' : 'statute mi';
  if (!volume || !end) return { value: '—', unit: unitName, delta: null, line: '', marks: [], description: '' };
  const months = cardMonths(end);
  const statute = valuesByMonth(volume, months, (p) => p.loadedMiles);
  const last = statute[statute.length - 1]!;
  const prior = statute.length > 1 ? statute[statute.length - 2]! : 0;
  const low = Math.min(...statute);
  const high = Math.max(...statute);
  const span = high - low;
  const step = months.length > 1 ? LINE_WIDTH / (months.length - 1) : 0;
  // Inverted: a larger value is higher on the screen. A flat run is drawn across the middle.
  const marks = statute.map((v, i) => ({
    x: Number((LINE_LEFT + i * step).toFixed(1)),
    y: Number((span === 0 ? 15 : 28 - ((v - low) / span) * 26).toFixed(1)),
    label: shortCount(milesIn(v, unit)),
  }));
  const line = marks.map((m) => `${m.x.toFixed(1)},${m.y.toFixed(1)}`).join(' ');
  const words = months.map((m, i) => `${monthName(m)} ${count.format(milesIn(statute[i]!, unit))}`);
  return {
    value: count.format(milesIn(last, unit)),
    unit: unitName,
    delta: months.length > 1 ? percentDelta(last, prior, priorMonthName(end)) : null,
    line,
    marks,
    description: `Loaded miles by month, in ${unitName.replace(' mi', ' miles')}: ${words.join(', ')}.`,
  };
}
