import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../axe';
import { ShareDialogComponent, looksLikeEmail, parseAddresses } from './share-dialog.component';
import { BASE, bootstrap } from './saved-views.testing';

const SHARES = `${BASE}/saved-views/5/shares`;
const EMPTY = { recipients: [], pending: [] };

async function create(sharing: object = EMPTY) {
  const http = bootstrap({ providers: [{ provide: MAT_DIALOG_DATA, useValue: { providerId: '526', viewId: 5, viewName: 'Cash overview' } }, { provide: MatDialogRef, useValue: { close: vi.fn() } }] });
  const fixture: ComponentFixture<ShareDialogComponent> = TestBed.createComponent(ShareDialogComponent);
  fixture.detectChanges();
  http.expectOne(SHARES).flush(sharing);
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

/** Lets the resource's reload turn into a request: it is issued by an effect, not at the moment of the call. */
async function tick(fixture: ComponentFixture<ShareDialogComponent>): Promise<void> {
  for (let i = 0; i < 3; i += 1) {
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
  }
}

function type(root: HTMLElement, fixture: ComponentFixture<ShareDialogComponent>, text: string): void {
  const box = root.querySelector<HTMLTextAreaElement>('#share-emails')!;
  box.value = text;
  box.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

const sendButton = (root: HTMLElement) => root.querySelector<HTMLButtonElement>('button[type="submit"]')!;

describe('parseAddresses', () => {
  it('splits on commas, semicolons and whitespace, lower-cases, and drops blanks and repeats', () => {
    expect(parseAddresses(' A@x.org, b@x.org;\n a@X.org   c@x.org ,, ')).toEqual({ emails: ['a@x.org', 'b@x.org', 'c@x.org'], tooMany: false });
  });

  it('caps the count and says so', () => {
    const many = Array.from({ length: 12 }, (_, i) => `p${i}@x.org`).join(' ');

    const result = parseAddresses(many);

    expect(result.emails).toHaveLength(10);
    expect(result.tooMany).toBe(true);
  });

  it('gives nothing for empty text', () => {
    expect(parseAddresses('   ')).toEqual({ emails: [], tooMany: false });
  });
});

describe('looksLikeEmail', () => {
  it('accepts an ordinary address', () => {
    expect(looksLikeEmail('someone+tag@sub.example.org')).toBe(true);
  });

  it.each(['', 'nope', '@x.org', 'a@b', 'a b@x.org', 'a@x.org>', '"q"@x.org', 'a@@x.org', `${'a'.repeat(400)}@x.org`])('refuses %s', (bad) => {
    expect(looksLikeEmail(bad)).toBe(false);
  });
});

describe('ShareDialogComponent', () => {
  it('lists who has access and what is waiting, each with a button named for the person', async () => {
    const { root } = await create({
      recipients: [{ shareId: 1, email: 'guest@x.org', sharedAt: '2026-11-22T00:00:00Z' }, { shareId: 2, email: null, sharedAt: '2026-11-22T00:00:00Z' }],
      pending: [{ invitationId: 3, email: 'wait@x.org', expiresAt: '2026-11-29T00:00:00Z', isExpired: false }, { invitationId: 4, email: 'old@x.org', expiresAt: '2026-11-01T00:00:00Z', isExpired: true }],
    });

    expect(root.textContent).toContain('People with access (2)');
    expect(root.textContent).toContain('A colleague');
    expect(root.textContent).toContain('Waiting to be opened (2)');
    expect(root.textContent).toContain('expired');
    expect(root.querySelector('[aria-label="Remove access for guest@x.org"]')).not.toBeNull();
    expect(root.querySelector('[aria-label="Cancel the invitation to wait@x.org"]')).not.toBeNull();
  });

  it('says nobody has access yet when that is so', async () => {
    const { root } = await create();

    expect(root.textContent).toContain('Nobody yet.');
    expect(root.textContent).toContain('No invitations waiting.');
  });

  it('sends the addresses it was given, and no more than the API needs', async () => {
    const { root, fixture, http } = await create();
    type(root, fixture, 'New@X.org, other@x.org');

    sendButton(root).click();
    const request = http.expectOne(`${BASE}/saved-views/5/invitations`);
    request.flush({ sent: [{ invitationId: 9, email: 'new@x.org', expiresAt: '2026-11-29T00:00:00Z', isExpired: false }], failed: [] });
    await tick(fixture);
    http.expectOne(SHARES).flush(EMPTY);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(request.request.body).toEqual({ emails: ['new@x.org', 'other@x.org'] });
    expect(root.querySelector<HTMLTextAreaElement>('#share-emails')!.value).toBe('');
  });

  it('leaves the addresses that could not be sent in the box and says how many', async () => {
    const { root, fixture, http } = await create();
    type(root, fixture, 'a@x.org b@x.org');

    sendButton(root).click();
    http.expectOne(`${BASE}/saved-views/5/invitations`).flush({ sent: [], failed: ['b@x.org'] });
    await tick(fixture);
    http.expectOne(SHARES).flush(EMPTY);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector<HTMLTextAreaElement>('#share-emails')!.value).toBe('b@x.org');
    expect(root.querySelector('#share-error')?.textContent).toContain("We couldn't send to one address");
    expect(root.querySelector('#share-error')?.getAttribute('role')).toBe('alert');
  });

  it('refuses bad input before asking the server', async () => {
    const { root, fixture, http } = await create();

    sendButton(root).click();
    fixture.detectChanges();
    expect(root.querySelector('#share-error')?.textContent).toContain('valid email addresses');
    type(root, fixture, 'not-an-address');
    sendButton(root).click();
    fixture.detectChanges();
    expect(root.querySelector('#share-error')?.textContent).toContain('valid email addresses');
    type(root, fixture, Array.from({ length: 11 }, (_, i) => `p${i}@x.org`).join(' '));
    sendButton(root).click();
    fixture.detectChanges();
    expect(root.querySelector('#share-error')?.textContent).toContain('up to 10');

    http.expectNone(`${BASE}/saved-views/5/invitations`);
  });

  it('shows a calm message, never server text, when sending fails', async () => {
    const { root, fixture, http } = await create();
    type(root, fixture, 'a@x.org');

    sendButton(root).click();
    http.expectOne(`${BASE}/saved-views/5/invitations`).flush({ title: 'secret detail' }, { status: 503, statusText: 'Service Unavailable' });
    fixture.detectChanges();

    expect(root.querySelector('#share-error')?.textContent).not.toContain('secret detail');
    expect(root.querySelector('#share-error')?.textContent?.length).toBeGreaterThan(0);
  });

  it('removes a recipient and cancels an invitation, then refreshes the lists', async () => {
    const { root, fixture, http } = await create({
      recipients: [{ shareId: 1, email: 'guest@x.org', sharedAt: '2026-11-22T00:00:00Z' }],
      pending: [{ invitationId: 3, email: 'wait@x.org', expiresAt: '2026-11-29T00:00:00Z', isExpired: false }],
    });

    root.querySelector<HTMLButtonElement>('[aria-label="Remove access for guest@x.org"]')!.click();
    http.expectOne(`${SHARES}/1`).flush(null);
    await tick(fixture);
    http.expectOne(SHARES).flush({ recipients: [], pending: [{ invitationId: 3, email: 'wait@x.org', expiresAt: '2026-11-29T00:00:00Z', isExpired: false }] });
    await tick(fixture);
    root.querySelector<HTMLButtonElement>('[aria-label="Cancel the invitation to wait@x.org"]')!.click();
    http.expectOne(`${BASE}/saved-views/5/invitations/3`).flush(null);
    await tick(fixture);
    http.expectOne(SHARES).flush(EMPTY);
    await tick(fixture);

    expect(root.textContent).toContain('No invitations waiting.');
    http.verify();
  });

  it('passes axe', async () => {
    const { root } = await create();

    expect(await axeViolations(root)).toEqual([]);
  });
});

export type { HttpTestingController };
