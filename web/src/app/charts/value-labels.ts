/**
 * Value labels drawn on bars and points, for every chart built from the shared options. One place decides whether there is room for them
 * and how they look, so no Metric adds its own. Pure, so it is unit tested without a browser.
 */
export type LabelledKind = 'column' | 'stacked' | 'line';

/** The most labels drawn on one chart, by width. Past this they would run into each other, so none are drawn and the table carries the figures. */
export const MAX_LABELS_WIDE = 24;
export const MAX_LABELS_NARROW = 6;

export interface LabelPlanInput {
  readonly kind: LabelledKind;
  /** Bars or points along the axis (months, payers, aircraft). */
  readonly categories: number;
  readonly series: number;
  /** The chart is too narrow for a figure on every bar or point. */
  readonly narrow: boolean;
}

export interface LabelPlan {
  /** Draw the value on each bar or point (grouped columns, single-series columns and lines). */
  readonly values: boolean;
  /** Draw the stack total above each stacked column instead of a figure in every segment. */
  readonly totals: boolean;
}

const NONE: LabelPlan = { values: false, totals: false };

/**
 * Decides which labels fit. A line is labelled only with one series (several would overlap), a stack shows its total, a grouped column
 * shows every bar. The count of labels is capped by width: 12 months of one series on a phone get none.
 */
export function planValueLabels(input: LabelPlanInput): LabelPlan {
  const cap = input.narrow ? MAX_LABELS_NARROW : MAX_LABELS_WIDE;
  if (input.categories === 0 || input.series === 0) return NONE;
  if (input.kind === 'line') return input.series === 1 && input.categories <= cap ? { values: true, totals: false } : NONE;
  if (input.kind === 'stacked') return input.categories <= cap ? { values: false, totals: true } : NONE;
  return input.categories * input.series <= cap ? { values: true, totals: false } : NONE;
}

/** The label style shared by every chart: the text color of the theme (so dark mode and high contrast stay readable), small, and never overlapping another label. */
export function valueLabelStyle(color: string, format: (value: number) => string, position: 'top' | 'right' = 'top'): {
  show: true;
  position: 'top' | 'right';
  color: string;
  fontSize: number;
  formatter: (p: { value: unknown }) => string;
} {
  return {
    show: true,
    position,
    color,
    fontSize: 12,
    formatter: (p) => {
      const raw = Array.isArray(p.value) ? p.value[p.value.length - 1] : p.value;
      const value = Number(raw);
      return Number.isFinite(value) && raw !== null ? format(value) : '';
    },
  };
}

/** Sums the series per category, for the total above a stacked column. A gap counts as zero. */
export function stackTotals(series: readonly { readonly values: readonly (number | null)[] }[], categories: number): number[] {
  return Array.from({ length: categories }, (_, i) => series.reduce((sum, s) => sum + (s.values[i] ?? 0), 0));
}

/** Extra headroom above the plot for labels, in pixels. */
export const LABEL_HEADROOM_PX = 14;
