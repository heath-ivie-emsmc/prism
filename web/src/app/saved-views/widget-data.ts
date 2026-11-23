

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
  readonly categories: readonly string[];
  /** When each category is a payer group, its key, in the order of `categories`, so a payer keeps one colour on every chart. */
  readonly categoryKeys?: readonly string[];
  /** Where each category leads, in the order of `categories`, read from the API's drill links. Null for a category with no link. Absent when the Metric has none. Navigation only: never saved with a view. */
  readonly drills?: readonly (DrillRoute | null)[];
  readonly series: readonly WidgetSeries[];
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
