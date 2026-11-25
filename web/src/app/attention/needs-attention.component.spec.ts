import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { BASE, PROVIDER, bootstrap, settle } from '../saved-views/saved-views.testing';
import { AttentionClaimDto, CLAIMS_PERMISSION } from './attention.service';
import { NeedsAttentionComponent } from './needs-attention.component';

/** Warehouse text can hold markup. It must reach the page as text. */
const HOSTILE = '<img src=x onerror=alert(1)>';
const COUNTS = {
  providerId: '526', period: '202610', dataThrough: '2026-11-05', deniedBalanceDue: 48200,
  counts: [{ key: 'denied', name: 'Denied', count: 2 }, { key: 'needs_attention', name: 'Needs attention', count: 4 }, { key: 'all', name: 'All', count: 8 }],
  deniedRate: 0.25,
  reasons: [
    { reason: HOSTILE, isOther: false, count: 2, balanceDue: 40000 },
    { reason: 'Other reason', isOther: false, count: 1, balanceDue: 5000 },
    { reason: 'Other', isOther: true, count: 3, balanceDue: 3200 },
  ],
  categories: [{ key: 'denied', name: 'Denied', count: 2, balanceDue: 48200 }, { key: 'on_hold', name: 'On hold', count: 1, balanceDue: 1200 }, { key: 'in_collections', name: 'In collections', count: 0, balanceDue: 0 }],
  filingBands: [
    { key: 'past_due', name: 'Past due', claims: 1, balanceDue: 1200 }, { key: 'due_within_10_days', name: 'Due within 10 days', claims: 0, balanceDue: 0 },
    { key: 'due_within_30_days', name: 'Due within 30 days', claims: 2, balanceDue: 48200 }, { key: 'due_later', name: 'Due later', claims: 1, balanceDue: 800 },
  ],
};
const claim = (id: string, over: Partial<AttentionClaimDto> = {}): AttentionClaimDto => ({
  id, claimRef: `FW-${id}`, serviceDate: '2026-09-28', payer: 'Meridian Health Plan', payerCategory: 'Insurance', vehicle: 'N418CV', reportingDivision: 'Sky Harbor base',
  status: 'Submitted', denialCode: 'CO-16', denialReason: 'Missing documentation', balanceDue: 24600, filingDueDate: '2026-11-26', daysLeft: 21, ...over,
});
const CLAIMS = { providerId: '526', check: 'denied', period: '202610', dataThrough: '2026-11-05', total: 2, page: 1, pageSize: 25, claims: [claim('1'), claim('2', { balanceDue: 23600 })] };
const ATTENTION = `${BASE}/claims/attention`;
const PERMISSIONS = ['prism.app', 'prism.dashboard.view', CLAIMS_PERMISSION];

async function create(claims: object = CLAIMS, permissions: string[] = PERMISSIONS) {
  const http = bootstrap({ me: { permissions } });
  const fixture: ComponentFixture<NeedsAttentionComponent> = TestBed.createComponent(NeedsAttentionComponent);
  await settle(fixture, http, { [`${ATTENTION}/counts`]: COUNTS, [`${ATTENTION}/denied`]: claims });
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

const boxes = (root: HTMLElement) => [...root.querySelectorAll<HTMLInputElement>('tbody input[type="checkbox"]')];
const send = (root: HTMLElement) => root.querySelector<HTMLButtonElement>('button.send')!;

describe('NeedsAttentionComponent', () => {
  it('lists the claims with words for status, money and deadline, and a tab for each check with its count', async () => {
    const { root } = await create();

    expect(root.querySelector('#claims-title')?.textContent).toContain('Air claims, October 2026');
    expect([...root.querySelectorAll('[role="radio"]')].map((r) => r.textContent?.trim())).toEqual(['Denied (2)', 'Needs attention (4)', 'All (8)']);
    expect(root.querySelector('ems-chip')?.textContent).toContain('2 of 8');
    const cells = [...root.querySelectorAll('.claims tbody tr')[0]!.querySelectorAll('th, td')].map((c) => c.textContent?.replace(/\s+/g, ' ').trim());
    expect(cells.join('|')).toContain('FW-1Service date 09/28/2026');
    expect(cells.join('|')).toContain('DeniedMissing documentation');
    expect(cells.join('|')).toContain('$24,600.00');
    expect(cells.join('|')).toContain('21 days leftDue 11/26/2026');
    expect(root.textContent).toContain('Sorted by estimated filing deadline, soonest first');
  });

  it('has Send to CSE off, with no badge, until a claim is ticked', async () => {
    const { root } = await create();

    expect(send(root).disabled).toBe(true);
    expect(root.querySelector('.send__badge')).toBeNull();
    expect(send(root).getAttribute('aria-label')).toBe('Send to CSE');
  });

  it('shows the count badge over Send to CSE and puts the count in the button name when claims are ticked', async () => {
    const { root, fixture } = await create();

    boxes(root)[0]!.click();
    fixture.detectChanges();
    expect(send(root).disabled).toBe(false);
    expect(root.querySelector('.send__badge ems-badge')?.textContent?.trim()).toBe('1');
    expect(send(root).getAttribute('aria-label')).toBe('Send to CSE, 1 claim selected');

    boxes(root)[1]!.click();
    fixture.detectChanges();
    expect(root.querySelector('.send__badge ems-badge')?.textContent?.trim()).toBe('2');
    expect(send(root).getAttribute('aria-label')).toBe('Send to CSE, 2 claims selected');
    expect(root.querySelector('[role="status"]')?.textContent).toContain('2 claims selected');
  });

  it('ticks and clears every row on the page from the header checkbox, which is half-ticked for a partial choice', async () => {
    const { root, fixture } = await create();
    const header = root.querySelector<HTMLInputElement>('thead input[type="checkbox"]')!;

    boxes(root)[0]!.click();
    fixture.detectChanges();
    expect(header.indeterminate).toBe(true);
    header.click();
    fixture.detectChanges();
    expect(boxes(root).every((b) => b.checked)).toBe(true);
    header.click();
    fixture.detectChanges();
    expect(boxes(root).some((b) => b.checked)).toBe(false);
    expect(send(root).disabled).toBe(true);
  });

  it('opens the drawer with the ticked claims and their total', async () => {
    const { root, fixture } = await create();
    boxes(root)[0]!.click();
    boxes(root)[1]!.click();
    fixture.detectChanges();

    send(root).click();
    fixture.detectChanges();
    await fixture.whenStable();

    const drawer = document.querySelector('app-send-to-cse-drawer')!;
    expect(drawer.querySelector('h3')?.textContent).toContain('Claims attached (2) · $48,200.00');
    expect([...drawer.querySelectorAll('li .drawer__ref')].map((e) => e.textContent)).toEqual(['FW-1', 'FW-2']);
    expect(drawer.querySelector('[aria-disabled="true"]')).not.toBeNull();
    TestBed.inject(MatDialog).closeAll();
  });

  it('says nothing is denied when the list is empty, and shows no table', async () => {
    const { root } = await create({ ...CLAIMS, total: 0, claims: [] });

    expect(root.textContent).toContain('No denied claims for October 2026.');
    expect(root.querySelector('.claims table')).toBeNull();
  });

  it('explains, and asks for nothing, when the user lacks the claims permission', async () => {
    const http = bootstrap({ me: { providers: [{ ...PROVIDER, permissions: ['prism.dashboard.view'] }] } });
    const fixture = TestBed.createComponent(NeedsAttentionComponent);
    await settle(fixture, http, {}, false);

    expect(fixture.nativeElement.textContent).toContain("You don't have access to claim details");
    http.expectNone((r) => r.url.includes('/claims/attention'));
  });

  it('shows the summary above the list: denial rate, top reasons, overlapping categories and the four estimated filing bands', async () => {
    const { root } = await create();
    const summary = root.querySelector('app-attention-summary')!;

    expect(summary.querySelector('h2')?.textContent).toContain('Summary, October 2026');
    expect(summary.textContent).toContain('25.0%');
    expect(summary.textContent).toContain('All other reasons');
    expect(summary.textContent).toContain('Other reason');
    expect(summary.textContent).toContain('overlap and are not added together');
    expect(summary.textContent).toContain('These are estimates. The filing limits are a placeholder');
    expect(summary.querySelectorAll('table')).toHaveLength(3);
    expect(summary.querySelectorAll('table')[2]!.querySelectorAll('tbody tr')).toHaveLength(4);
    expect(root.querySelector('app-attention-summary')!.compareDocumentPosition(root.querySelector('#claims-title')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('shows a hostile denial reason as literal text and builds no element from it', async () => {
    const { root } = await create();
    const summary = root.querySelector('app-attention-summary')!;

    expect(summary.textContent).toContain(HOSTILE);
    expect(root.querySelector('img')).toBeNull();
    expect(summary.innerHTML).not.toContain('<img');
  });

  it('says there is no rate, not zero, when the period has no claims', async () => {
    const http = bootstrap({ me: { permissions: PERMISSIONS } });
    const fixture: ComponentFixture<NeedsAttentionComponent> = TestBed.createComponent(NeedsAttentionComponent);
    await settle(fixture, http, { [`${ATTENTION}/counts`]: { ...COUNTS, deniedRate: null, reasons: [] }, [`${ATTENTION}/denied`]: CLAIMS });

    const text = (fixture.nativeElement as HTMLElement).querySelector('app-attention-summary')!.textContent;
    expect(text).toContain('No claims');
    expect(text).toContain('No denied claims in this period.');
  });

  it('names the vehicle column Tail number', async () => {
    const { root } = await create();

    expect([...root.querySelectorAll('thead th')].map((h) => h.textContent?.trim())).toContain('Tail number');
  });

  it('passes axe', async () => {
    const { root } = await create();

    expect(await axeViolations(root)).toEqual([]);
  });
});
