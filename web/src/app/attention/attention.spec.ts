import { describe, expect, it } from 'vitest';
import { AttentionClaimDto } from './attention.service';
import {
  attentionCard, daysText, downloadName, groupWords, headerState, reviewQuery, safeCheck, safeGroup, selectedMessage, sendButtonName, statusWords, tabOptions, toClaimRow, totalBalance, urgencyOf,
} from './attention';

const CLAIM: AttentionClaimDto = {
  id: '1', claimRef: 'FW-2610418', serviceDate: '2026-09-28', payer: 'Meridian Health Plan', payerCategory: 'Insurance', vehicle: 'N418CV', reportingDivision: 'Sky Harbor base',
  status: 'Submitted', denialCode: 'CO-16', denialReason: 'Missing documentation', balanceDue: 24600, filingDueDate: '2026-11-26', daysLeft: 21,
};

describe('days left', () => {
  it.each([
    [21, '21 days left'],
    [1, '1 day left'],
    [0, 'Due today'],
    [-1, 'Overdue by 1 day'],
    [-3, 'Overdue by 3 days'],
  ])('%i reads %s', (days, text) => expect(daysText(days)).toBe(text));

  it.each([
    [-1, 'overdue'],
    [0, 'soon'],
    [30, 'soon'],
    [31, 'ok'],
  ] as const)('%i is %s', (days, urgency) => expect(urgencyOf(days)).toBe(urgency));
});

describe('toClaimRow', () => {
  it('shows a denied claim with its reason, money and deadline as words', () => {
    expect(toClaimRow(CLAIM)).toMatchObject({
      id: '1', claimRef: 'FW-2610418', serviceDate: '09/28/2026', payer: 'Meridian Health Plan', vehicle: 'N418CV', division: 'Sky Harbor base',
      status: 'Denied', statusState: 'error', reason: 'Missing documentation', balance: '$24,600.00', due: '11/26/2026', daysText: '21 days left', urgency: 'soon',
    });
  });

  it('shows a claim on hold as a warning with no reason, and fills in what the warehouse left blank', () => {
    const row = toClaimRow({ ...CLAIM, denialCode: null, denialReason: null, status: 'On_Hold', payer: null, vehicle: null, daysLeft: -2 });

    expect(row).toMatchObject({ status: 'On hold', statusState: 'warn', reason: '', payer: 'No payer recorded', vehicle: 'Not recorded', urgency: 'overdue', daysText: 'Overdue by 2 days' });
  });

  it('shows any other status as neutral words', () => {
    expect(toClaimRow({ ...CLAIM, denialCode: null, status: 'Submitted' })).toMatchObject({ status: 'Submitted', statusState: 'neutral' });
  });
});

describe('statusWords', () => {
  it.each([['On_Hold', 'On hold'], ['SUBMITTED', 'Submitted'], ['', 'Unknown']])('%s reads %s', (raw, words) => expect(statusWords(raw)).toBe(words));
});

describe('selection', () => {
  const ids = ['a', 'b', 'c'];

  it('says none, some or all of the rows on the page are chosen', () => {
    expect(headerState(new Set(), ids)).toBe('none');
    expect(headerState(new Set(['a']), ids)).toBe('some');
    expect(headerState(new Set(['a', 'b', 'c', 'z']), ids)).toBe('all');
    expect(headerState(new Set(['a']), [])).toBe('none');
  });

  it('words the count for the live region and the Send button, verbatim', () => {
    expect([0, 1, 2].map(selectedMessage)).toEqual(['No claims selected', '1 claim selected', '2 claims selected']);
    expect([0, 1, 2].map(sendButtonName)).toEqual(['Send to CSE', 'Send to CSE, 1 claim selected', 'Send to CSE, 2 claims selected']);
  });

  it('adds up the balance of the chosen rows', () => {
    expect(totalBalance([toClaimRow(CLAIM), toClaimRow({ ...CLAIM, id: '2', balanceDue: 23600 })])).toBe(48200);
    expect(totalBalance([])).toBe(0);
  });
});

describe('tabs and address', () => {
  it('puts each count in its tab', () => {
    expect(tabOptions([{ key: 'denied', name: 'Denied', count: 2 }, { key: 'all', name: 'All', count: 8 }])).toEqual([
      { value: 'denied', label: 'Denied (2)' },
      { value: 'all', label: 'All (8)' },
    ]);
  });

  it('names the download for the tab it will export', () => {
    expect(downloadName('Needs attention', 4)).toBe('Download CSV for Needs attention (4)');
  });

  it.each([['needs_attention', 'needs_attention'], ['DENIED', 'denied'], ['a;b', 'denied'], [null, 'denied'], ['x'.repeat(60), 'denied']])('check %s becomes %s', (raw, safe) => expect(safeCheck(raw)).toBe(safe));

  it.each([['fixed-wing', 'fixed-wing'], ['Fixed Wing', null], ['../x', null], [null, null]])('group %s becomes %s', (raw, safe) => expect(safeGroup(raw)).toBe(safe));

  it('writes group words and the review address', () => {
    expect(groupWords('fixed-wing')).toBe('Fixed-wing');
    expect(groupWords('als')).toBe('ALS');
    expect(reviewQuery('denied', '202610', 'air', 'fixed-wing')).toEqual({ check: 'denied', period: '202610', line: 'air', group: 'fixed-wing' });
    expect(reviewQuery('denied', '202610', 'ground', null)).toEqual({ check: 'denied', period: '202610', line: 'ground' });
  });
});

describe('attentionCard', () => {
  it('says how many denied claims need attention and what is owed, verbatim', () => {
    expect(attentionCard(2, 48200)).toEqual({
      title: '2 denied claims need attention',
      body: '$48,200.00 is still owed on them. Timely filing deadlines apply, so the sooner they are reviewed the better.',
      button: 'Review 2 denied claims',
      amount: '$48,200.00',
      amountNote: 'still owed',
      amountSpoken: '$48,200.00 still owed',
    });
  });

  it('uses the singular for one claim', () => {
    expect(attentionCard(1, 100)).toMatchObject({ title: '1 denied claim needs attention', button: 'Review 1 denied claim' });
    expect(attentionCard(1, 100)?.body).toContain('still owed on it.');
  });

  it('shows no card when nothing is denied', () => {
    expect(attentionCard(0, 0)).toBeNull();
  });
});
