

import type { DrillRoute } from '../metrics/drill-link';

/** What a widget needs to draw itself, whatever the Metric: a sentence for the chart's accessible name, the same numbers as a table, and series for the chart. */

export interface WidgetTable {
  readonly caption: string;
  readonly columns: readonly string[];
  /** Cells are already formatted text. The first cell of a row is its header. */
  readonly rows: readonly (readonly string[])[];
}

export interface WidgetSeries {
  readonly name: string;
  readonly values: readonly (number | null)[];
}

export interface WidgetData {
  /** One or two sentences, at most 250 characters: the chart's accessible name. */
  readonly summary: string;
  readonly table: WidgetTable;
  /** More tables of the same Metric, shown under the first in the table presentation (a payer timing table, a latest-month split). */
  readonly extraTables?: readonly WidgetTable[];
  readonly categories: readonly string[];
  /** When each category is a payer group, its key, in the order of `categories`, so a payer keeps one colour on every chart. */
  readonly categoryKeys?: readonly string[];
  /** Where each category leads, in the order of `categories`, read from the API's drill links. Null for a category with no link. Absent when the Metric has none. Navigation only: never saved with a view. */
  readonly drills?: readonly (DrillRoute | null)[];
  readonly series: readonly WidgetSeries[];
  /** The same Metric over time, for the line and stacked presentations, when its column and donut show a single period. Categories are the months. */
  readonly trend?: { readonly categories: readonly string[]; readonly series: readonly WidgetSeries[] };
  /** The one figure a tile shows, with a short note under it. */
  readonly headline: { readonly value: string; readonly note: string } | null;
  /** The warehouse load the figures come from, yyyy-MM-dd, when the API says. */
  readonly dataThrough: string | null;
  readonly caveat: string;
  readonly empty: boolean;
  /** Formats a number in the unit of the series, for axis and value labels. */
  readonly unit: 'usd' | 'count' | 'percent';
}

export const SUMMARY_MAX = 250;

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
const usdCents = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
const count = new Intl.NumberFormat('en-US');

export const money = (value: number): string => usd.format(value);
export const moneyExact = (value: number): string => usdCents.format(value);
export const whole = (value: number): string => count.format(value);
export const percent = (ratio: number): string => `${(ratio * 100).toFixed(1)}%`;

/** A sentence kept under the accessible-name limit. */
export function limit(text: string): string {
  return text.length <= SUMMARY_MAX ? text : `${text.slice(0, SUMMARY_MAX - 1).trimEnd()}…`;
}

/** "+$1,200.00" or "-$300.00": the sign is in the text, so a rise or a fall is never only a colour. */
export function signedMoney(value: number): string {
  return `${value > 0 ? '+' : ''}${usdCents.format(value)}`;
}

/** A change in percent units: 12.5 gives "+12.5%". Null (no base to compare with) gives "n/a". */
export function signedPercentUnits(value: number | null): string {
  return value === null ? 'n/a' : `${value > 0 ? '+' : ''}${value.toFixed(1)}%`;
}

/** A share as a fraction 0..1 gives "12.3%". Null (nothing to divide by) gives "n/a". */
export const sharePercent = (share: number | null): string => (share === null ? 'n/a' : percent(share));

/** A change in percentage points: 1.25 gives "+1.3 pts". Null gives "n/a". The sign is in the text. */
export function signedPoints(points: number | null): string {
  return points === null ? 'n/a' : `${points > 0 ? '+' : ''}${points.toFixed(1)} pts`;
}

/** Miles to the nearest whole mile, with thousands separators. */
export const miles = (value: number): string => whole(Math.round(value));

/** A figure that may be missing: formatted, or "n/a". */
export function orNa(value: number | null, format: (v: number) => string): string {
  return value === null ? 'n/a' : format(value);
}

/** A whole-number change: 3 gives "+3". */
export function signedWhole(value: number): string {
  return `${value > 0 ? '+' : ''}${count.format(value)}`;
}

export function monthLabel(label: string, year: number): string {
  return `${label} ${year}`;
}

export function change(current: number, prior: number): string {
  if (prior === 0) {
    return 'n/a';
  }
  const ratio = (current - prior) / Math.abs(prior);
  return `${ratio >= 0 ? '+' : ''}${(ratio * 100).toFixed(1)}%`;
}

export const NONE = (caption: string, unit: WidgetData['unit'] = 'usd'): WidgetData => ({
  summary: 'There is no data for this period.',
  table: { caption, columns: [], rows: [] },
  categories: [],
  series: [],
  headline: null,
  dataThrough: null,
  caveat: '',
  empty: true,
  unit,
});
