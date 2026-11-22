import { HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { InvitationTokenShape } from './invitation-token';
import { RedeemInvitationComponent } from './redeem-invitation.component';
import { unsavedChangesGuard } from './unsaved-changes.guard';
import { BASE, bootstrap } from './saved-views.testing';
import { ViewBuilderComponent } from './view-builder.component';
import { ConflictDialogComponent, NameDialogComponent } from './view-dialogs.component';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideEmsUi } from '@ems-management-consultants/ui';

const TOKEN = 'A'.repeat(43);

function redeemRoute(token: string | null) {
  return { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(token ? { token } : {}), paramMap: convertToParamMap({}) } } };
}

describe('RedeemInvitationComponent', () => {
  it('takes the token out of the address bar, redeems it once and opens the view', async () => {
    const http = bootstrap({ providers: [redeemRoute(TOKEN)] });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const replace = vi.spyOn(history, 'replaceState');

    const fixture = TestBed.createComponent(RedeemInvitationComponent);
    fixture.detectChanges();
    const request = http.expectOne('/api/v1/saved-view-invitations/redeem');
    request.flush({ providerId: '526', viewId: 9 });

    expect(replace).toHaveBeenCalled();
    expect(request.request.body).toEqual({ token: TOKEN });
    expect(navigate).toHaveBeenCalledWith(['/saved-views', 9], { replaceUrl: true });
    replace.mockRestore();
  });

  it('reads the same for every failure, and never calls the API for a token of the wrong shape', () => {
    const http = bootstrap({ providers: [redeemRoute('short')] });

    const fixture = TestBed.createComponent(RedeemInvitationComponent);
    fixture.detectChanges();

    http.expectNone('/api/v1/saved-view-invitations/redeem');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('This invitation is no longer valid');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Ask the sender for a new one.');
  });

  it('shows the same message when the server refuses', () => {
    const http = bootstrap({ providers: [redeemRoute(TOKEN)] });

    const fixture = TestBed.createComponent(RedeemInvitationComponent);
    fixture.detectChanges();
    http.expectOne('/api/v1/saved-view-invitations/redeem').flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('This invitation is no longer valid');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    root.querySelector('button')!.click();
    expect(navigate).toHaveBeenCalledWith('/saved-views');
  });

  it('works without a token at all', () => {
    const http = bootstrap({ providers: [redeemRoute(null)] });
    const fixture = TestBed.createComponent(RedeemInvitationComponent);

    fixture.detectChanges();

    http.expectNone('/api/v1/saved-view-invitations/redeem');
    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toContain('no longer valid');
  });
});

describe('InvitationTokenShape', () => {
  it('accepts exactly 43 URL-safe characters', () => {
    expect(InvitationTokenShape.isWellFormed(TOKEN)).toBe(true);
    expect(InvitationTokenShape.isWellFormed('a-b_' + 'c'.repeat(39))).toBe(true);
    for (const bad of [null, undefined, '', 'short', TOKEN + 'A', 'A'.repeat(42) + '+', 'A'.repeat(42) + ' ', 'A'.repeat(42) + '=']) {
      expect(InvitationTokenShape.isWellFormed(bad)).toBe(false);
    }
  });
});

describe('unsavedChangesGuard', () => {
  const guard = (component: Pick<ViewBuilderComponent, 'hasUnsavedChanges' | 'confirmLeave'>) =>
    TestBed.runInInjectionContext(() => unsavedChangesGuard(component as ViewBuilderComponent, null as never, null as never, null as never));

  it('lets a clean view go and asks about a dirty one', async () => {
    TestBed.configureTestingModule({});
    const asked = vi.fn(() => of(false));

    const clean = guard({ hasUnsavedChanges: () => false, confirmLeave: asked });
    const dirty = guard({ hasUnsavedChanges: () => true, confirmLeave: asked });

    await expect(new Promise((resolve) => (clean as ReturnType<typeof of>).subscribe(resolve))).resolves.toBe(true);
    await expect(new Promise((resolve) => (dirty as ReturnType<typeof of>).subscribe(resolve))).resolves.toBe(false);
    expect(asked).toHaveBeenCalledTimes(1);
  });
});

describe('dialogs', () => {
  function open<T>(component: new () => T, data: object) {
    const closed = new Subject<string | undefined>();
    const close = vi.fn((value?: string) => closed.next(value));
    TestBed.configureTestingModule({ providers: [provideEmsUi(), { provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }] });
    const fixture = TestBed.createComponent(component);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, close };
  }

  it('the name dialog trims the name and refuses an empty one', () => {
    const { root, fixture, close } = open(NameDialogComponent, { heading: 'Save a copy', confirmLabel: 'Save copy', name: '  Mine  ' });
    const input = root.querySelector<HTMLInputElement>('#view-name')!;
    const confirm = [...root.querySelectorAll('button')].find((b) => b.textContent!.trim() === 'Save copy')!;

    confirm.click();
    expect(close).toHaveBeenCalledWith('Mine');

    input.value = '   ';
    input.dispatchEvent(new Event('input'));
    confirm.click();
    fixture.detectChanges();
    expect(close).toHaveBeenCalledTimes(1);
    expect(root.textContent).toContain('Give the view a name.');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.maxLength).toBe(80);
  });

  it('the name dialog shows a server error it was given', () => {
    const { root } = open(NameDialogComponent, { heading: 'h', confirmLabel: 'ok', name: 'x', error: 'You already have a view with this name.' });

    expect(root.textContent).toContain('You already have a view with this name.');
  });

  it('the conflict dialog offers the safe choice first', () => {
    const { root } = open(ConflictDialogComponent, {});

    const buttons = [...root.querySelectorAll('[emsDialogActions] button')].map((b) => b.textContent!.trim());

    expect(buttons[0]).toBe('Save as a copy');
    expect(buttons).toEqual(['Save as a copy', 'Keep editing', 'Load their version']);
  });
});

export { BASE, type HttpTestingController };
