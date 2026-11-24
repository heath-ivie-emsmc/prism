/** One month of net cash: what was collected, or what is projected. */
export interface NetCashMonth {
  /** Short month name, "May". */
  readonly label: string;
  /** Four-digit year, shown under the month on the first month and in January. */
  readonly year: number;
  readonly value: number;
  readonly kind: 'actual' | 'projected';
  /** Forecast range, for projected months. */
  readonly low?: number;
  readonly high?: number;
}

/** Most months a chart or table draws. Callers aggregate first, so a bad response cannot freeze the page. */
export const MAX_NET_CASH_MONTHS = 36;

const currency = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2 });
const compact = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', notation: 'compact', minimumFractionDigits: 0, maximumFractionDigits: 2 });

/** Chart labels: millions always with two decimals ($1.20M) so neighbouring labels line up. */
export function formatLabel(value: number): string {
  return Math.abs(value) >= 1_000_000 ? `$${(value / 1_000_000).toFixed(2)}M` : formatCompact(value);
}

export const formatExact = (value: number): string => currency.format(value);
export const formatCompact = (value: number): string => compact.format(value);

export function boundedMonths(months: readonly NetCashMonth[]): readonly NetCashMonth[] {
  return months.slice(0, MAX_NET_CASH_MONTHS);
}

/** Percent change from the month before, or null for the first month. */
export function changeFromPrior(months: readonly NetCashMonth[], index: number): number | null {
  const prior = months[index - 1];
  if (!prior || prior.value === 0) return null;
  return ((months[index]!.value - prior.value) / prior.value) * 100;
}

/** One sentence for screen readers: the range, the latest actual month and the projection. */
export function summarizeNetCash(months: readonly NetCashMonth[]): string {
  const all = boundedMonths(months);
  if (all.length === 0) return 'Net cash collected: no data.';
  const actual = all.filter((m) => m.kind === 'actual');
  const projected = all.filter((m) => m.kind === 'projected');
  const first = all[0]!;
  const last = all[all.length - 1]!;
  const latest = actual[actual.length - 1];
  const parts = [`Net cash collected, ${first.label} ${first.year} to ${last.label} ${last.year}.`];
  if (latest) {
    const change = changeFromPrior(all, all.indexOf(latest));
    parts.push(`Latest month ${latest.label} ${formatCompact(latest.value)}${change === null ? '' : `, ${change >= 0 ? 'up' : 'down'} ${Math.abs(Math.round(change))} percent`}.`);
  }
  if (projected.length > 0) {
    parts.push(`Projected ${projected.map((m) => `${m.label} ${formatCompact(m.value)}`).join(', ')}.`);
  }
  return parts.join(' ');
}
