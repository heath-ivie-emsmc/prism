import { apPeriodLabel } from './dashboard-format';
import { monthName, priorPeriod } from '../breakdown/net-cash-breakdown';
import { CleanClaimRateDto } from '../metrics/net-collections.service';
import { CardDelta } from './transport-volume';

/**
 * The clean claim rate goal drawn on the tile, in percent. PLACEHOLDER from the mockup: it is not in the warehouse, and becomes a Provider setting
 * when Provider settings exist.
 */
export const CLEAN_CLAIM_GOAL = 95;

export interface CleanClaimCard {
  readonly value: string;
  readonly delta: CardDelta | null;
  /** The month the figure is for, such as "March 2026", written out only when it is not the month on screen. */
  readonly latestNote: string;
  /** Rate in percent for the bar, or null when there is no figure. */
  readonly percent: number | null;
  readonly description: string;
}

/** "96.9%", and "100%" rather than "100.0%". */
export function formatRate(percent: number): string {
  const rounded = Math.round(percent * 10) / 10;
  return `${Number.isInteger(rounded) ? rounded.toFixed(0) : rounded.toFixed(1)}%`;
}

const NONE: CleanClaimCard = { value: '—', delta: null, latestNote: '', percent: null, description: '' };

/**
 * The tile for the newest month the warehouse has a figure for, up to the month on screen. If that is an earlier month, `latestNote` says which,
 * so a figure is never passed off as another month's. The change is against the month before it, only when that month is in the data.
 */
export function toCleanClaimCard(dto: CleanClaimRateDto | null, shownPeriod: string): CleanClaimCard {
  const points = (dto?.points ?? []).filter((p) => p.rate !== null);
  const latest = points[points.length - 1];
  if (!latest) return dto ? { ...NONE, latestNote: `No clean claim data for ${apPeriodLabel(shownPeriod)} yet.` } : NONE;
  const percent = latest.rate! * 100;
  const before = points.find((p) => p.period === priorPeriod(latest.period));
  const points1 = before ? Math.round((percent - before.rate! * 100) * 10) / 10 : 0;
  const delta: CardDelta | null =
    before && points1 !== 0 ? { direction: points1 > 0 ? 'up' : 'down', text: `${Math.abs(points1).toFixed(1)} pts ${points1 > 0 ? 'higher' : 'lower'} than ${monthName(before.period)}` } : null;
  return {
    value: formatRate(percent),
    delta,
    latestNote: latest.period === shownPeriod ? '' : `Latest in the data: ${apPeriodLabel(latest.period)}.`,
    percent,
    description: `${formatRate(percent)} for ${apPeriodLabel(latest.period)}, goal ${CLEAN_CLAIM_GOAL} percent`,
  };
}
