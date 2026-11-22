import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { BASE, bootstrap, settle } from '../saved-views/saved-views.testing';
import { AttentionClaimDto, CLAIMS_PERMISSION } from './attention.service';
import { NeedsAttentionComponent } from './needs-attention.component';

const COUNTS = {
  providerId: '526', period: '202610', dataThrough: '2026-11-05', deniedBalanceDue: 48200,
  counts: [{ key: 'denied', name: 'Denied', count: 2 }, { key: 'needs_attention', name: 'Needs attention', count: 4 }, { key: 'all', name: 'All', count: 8 }],
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

    expect(root.querySelector('h2')?.textContent).toContain('Air claims, October 2026');
    expect([...root.querySelectorAll('[role="radio"]')].map((r) => r.textContent?.trim())).toEqual(['Denied (2)', 'Needs attention (4)', 'All (8)']);
    expect(root.querySelector('ems-chip')?.textContent).toContain('2 of 8');
    const cells = [...root.querySelectorAll('tbody tr')[0]!.querySelectorAll('th, td')].map((c) => c.textContent?.replace(/\s+/g, ' ').trim());
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
    expect(root.querySelector('table')).toBeNull();
  });

  it('explains, and asks for nothing, when the user lacks the claims permission', async () => {
    const http = bootstrap({ me: { permissions: ['prism.app', 'prism.dashboard.view'] } });
    const fixture = TestBed.createComponent(NeedsAttentionComponent);
    await settle(fixture, http, {}, false);

    expect(fixture.nativeElement.textContent).toContain("You don't have access to claim details");
    http.expectNone((r) => r.url.includes('/claims/attention'));
  });

  it('passes axe', async () => {
    const { root } = await create();

    expect(await axeViolations(root)).toEqual([]);
  });
});
