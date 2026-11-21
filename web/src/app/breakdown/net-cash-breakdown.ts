import { formatExact } from '../charts/net-cash';
import { BreakdownGroup } from './net-cash-breakdown.model';

const SHORT_MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** The short name of the month before an accounting period (yyyyMM): "Sep" for 202610. Empty for a bad period. */
export function priorMonthName(period: string): string {
  const month = /^\d{4}(0[1-9]|1[0-2])$/.exec(period)?.[1];
  return month ? SHORT_MONTHS[(Number(month) + 10) % 12]! : '';
}

/** The short name of the month itself. */
export function monthName(period: string): string {
  const month = /^\d{4}(0[1-9]|1[0-2])$/.exec(period)?.[1];
  return month ? SHORT_MONTHS[Number(month) - 1]! : '';
}

/** Percent change from the prior month, or null when there was none. */
export function percentChange(current: number, prior: number): number | null {
  return prior === 0 ? null : ((current - prior) / prior) * 100;
}

/** One plain sentence on what moved the total: the group that explains most of a drop (or a rise), and that the others held steady. */
export function summarizeBreakdown(groups: readonly BreakdownGroup[]): string {
  if (groups.length === 0) return '';
  const delta = (g: BreakdownGroup): number => g.cash - g.priorCash;
  const total = groups.reduce((sum, g) => sum + delta(g), 0);
  if (total === 0) return 'Net cash did not change from the month before.';
  const direction = total < 0 ? 'drop' : 'rise';
  const biggest = groups.reduce((best, g) => (Math.abs(delta(g)) > Math.abs(delta(best)) ? g : best));
  const share = Math.round((delta(biggest) / total) * 100);
  const others = groups.filter((g) => g !== biggest);
  // A group only "held steady" if it really did: a smaller group can still have moved a lot.
  const isSteady = (g: BreakdownGroup): boolean => {
    const change = percentChange(g.cash, g.priorCash);
    return change === null || Math.abs(change) < 5;
  };
  const steadyGroups = others.filter(isSteady);
  const movedGroups = others.filter((g) => !isSteady(g));
  const moved = movedGroups.map((g) => `${g.name} ${delta(g) < 0 ? 'fell' : 'rose'} ${formatExact(Math.abs(delta(g)))}.`).join(' ');
  const steady = steadyGroups.length === 0 ? '' : steadyGroups.length === 1 ? `${steadyGroups[0]!.name} held steady.` : 'The others held steady.';
  const rest = [moved, steady].filter(Boolean).join(' ');
  return `${biggest.name} accounts for ${formatExact(Math.abs(delta(biggest)))} of the ${formatExact(Math.abs(total))} ${direction} (${share}%).${rest ? ' ' + rest : ''}`;
}
