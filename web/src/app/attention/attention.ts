import { HttpErrorResponse } from '@angular/common/http';
import { formatDate } from '../dashboard/dashboard-format';
import { SegmentOption } from '../dashboard/segmented-control.component';
import { AttentionClaimDto, AttentionCountDto } from './attention.service';

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

export type ChipState = 'neutral' | 'warn' | 'error';
export type Urgency = 'overdue' | 'soon' | 'ok';

/** What one row looks like. Everything is worked out here, so the template only prints it. */
export interface ClaimRowView {
  readonly id: string;
  readonly claimRef: string;
  readonly serviceDate: string;
  readonly payer: string;
  readonly payerCategory: string;
  readonly vehicle: string;
  readonly division: string;
  /** The status as words: "Denied", "On hold". Never only a color. */
  readonly status: string;
  readonly statusState: ChipState;
  readonly reason: string;
  readonly balance: string;
  readonly balanceValue: number;
  readonly due: string;
  /** "21 days left" or "Overdue by 3 days". */
  readonly daysText: string;
  readonly urgency: Urgency;
  readonly urgencyIcon: string;
}

export const SOON_DAYS = 30;

export function formatMoney(amount: number): string {
  return money.format(amount);
}

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

/** "21 days left", "1 day left", "Due today", or "Overdue by 3 days". */
export function daysText(daysLeft: number): string {
  if (daysLeft < 0) return `Overdue by ${plural(-daysLeft, 'day', 'days')}`;
  if (daysLeft === 0) return 'Due today';
  return daysLeft === 1 ? '1 day left' : `${daysLeft} days left`;
}

export function urgencyOf(daysLeft: number): Urgency {
  if (daysLeft < 0) return 'overdue';
  return daysLeft <= SOON_DAYS ? 'soon' : 'ok';
}

const URGENCY_ICONS: Record<Urgency, string> = { overdue: 'error', soon: 'hourglass_top', ok: 'schedule' };

/** "On_Hold" becomes "On hold". */
export function statusWords(status: string): string {
  const spaced = status.replace(/_/g, ' ').trim();
  return spaced ? spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase() : 'Unknown';
}

export function toClaimRow(claim: AttentionClaimDto): ClaimRowView {
  const denied = !!claim.denialCode;
  const status = denied ? 'Denied' : statusWords(claim.status);
  const urgency = urgencyOf(claim.daysLeft);
  return {
    id: claim.id,
    claimRef: claim.claimRef,
    serviceDate: formatDate(claim.serviceDate),
    payer: claim.payer ?? 'No payer recorded',
    payerCategory: claim.payerCategory ?? '',
    vehicle: claim.vehicle ?? 'Not recorded',
    division: claim.reportingDivision,
    status,
    statusState: denied ? 'error' : /hold/i.test(claim.status) ? 'warn' : 'neutral',
    reason: denied ? (claim.denialReason ?? '') : '',
    balance: formatMoney(claim.balanceDue),
    balanceValue: claim.balanceDue,
    due: formatDate(claim.filingDueDate),
    daysText: daysText(claim.daysLeft),
    urgency,
    urgencyIcon: URGENCY_ICONS[urgency],
  };
}

export function totalBalance(rows: readonly ClaimRowView[]): number {
  return rows.reduce((sum, row) => sum + row.balanceValue, 0);
}

/** The tabs: each check's name with its count, in the order the API gave them. */
export function tabOptions(counts: readonly AttentionCountDto[]): readonly SegmentOption<string>[] {
  return counts.map((c) => ({ value: c.key, label: `${c.name} (${c.count})` }));
}

/** Shown before the counts arrive, so the tabs do not jump. */
export const FALLBACK_TABS: readonly AttentionCountDto[] = [
  { key: 'denied', name: 'Denied', count: 0 },
  { key: 'needs_attention', name: 'Needs attention', count: 0 },
  { key: 'all', name: 'All', count: 0 },
];

export type HeaderState = 'none' | 'some' | 'all';

/** The state of the header checkbox for the rows on the page. */
export function headerState(selected: ReadonlySet<string>, pageIds: readonly string[]): HeaderState {
  if (pageIds.length === 0) return 'none';
  const chosen = pageIds.filter((id) => selected.has(id)).length;
  return chosen === 0 ? 'none' : chosen === pageIds.length ? 'all' : 'some';
}

/** The words for the live region and the Send button's name. */
export function selectedMessage(count: number): string {
  return count === 0 ? 'No claims selected' : `${plural(count, 'claim', 'claims')} selected`;
}

export function sendButtonName(count: number): string {
  return count === 0 ? 'Send to CSE' : `Send to CSE, ${selectedMessage(count)}`;
}

export function downloadName(tabName: string, count: number): string {
  return `Download CSV for ${tabName} (${count})`;
}

const GROUP_PATTERN = /^[a-z0-9-]{1,40}$/;
const CHECK_PATTERN = /^[a-z_]{1,48}$/;

/** A key from the address that is safe to use, or the fallback. */
export function safeCheck(value: string | null): string {
  return value && CHECK_PATTERN.test(value) ? value : 'denied';
}

export function safeGroup(value: string | null): string | null {
  return value && GROUP_PATTERN.test(value) ? value : null;
}

/** "fixed-wing" becomes "Fixed-wing"; short codes such as als become "ALS". */
export function groupWords(group: string): string {
  return group.length <= 3 ? group.toUpperCase() : group.charAt(0).toUpperCase() + group.slice(1);
}

/** The address query for the review page. Only values the page accepts back are put in it. */
export function reviewQuery(check: string, period: string, line: 'air' | 'ground', group: string | null): Record<string, string> {
  const query: Record<string, string> = { check, period, line };
  if (group) query['group'] = group;
  return query;
}

/** The card on the Insights panel that points at the denied claims. Aggregates only: a count and what is still owed. */
export interface AttentionCardView {
  readonly title: string;
  readonly body: string;
  readonly button: string;
  readonly amount: string;
  readonly amountNote: string;
  readonly amountSpoken: string;
}

/** Null when no claim is denied, so the card never says "0 denied claims need attention". */
export function attentionCard(deniedCount: number, deniedBalance: number): AttentionCardView | null {
  if (deniedCount <= 0) return null;
  const claims = plural(deniedCount, 'denied claim', 'denied claims');
  const owed = formatMoney(deniedBalance);
  return {
    title: `${claims} ${deniedCount === 1 ? 'needs' : 'need'} attention`,
    body: `${owed} is still owed on ${deniedCount === 1 ? 'it' : 'them'}. Timely filing deadlines apply, so the sooner they are reviewed the better.`,
    button: `Review ${claims}`,
    amount: owed,
    amountNote: 'still owed',
    amountSpoken: `${owed} still owed`,
  };
}

/** What to announce when the CSV could not be downloaded. Plain words by status; nothing from the server body is shown. */
export function downloadFailureMessage(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : undefined;
  if (status === 429) return 'Too many downloads. Wait a minute, then try again.';
  if (status === 422) return 'More than 10,000 claims match, so they cannot be downloaded. Choose a single month, Service Line or aircraft type.';
  return "The claims couldn't be downloaded. Try again.";
}
