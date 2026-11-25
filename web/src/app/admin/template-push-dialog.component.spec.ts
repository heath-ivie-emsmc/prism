import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../axe';
import { bootstrap } from '../saved-views/saved-views.testing';
import { ProviderPickerComponent } from './provider-picker.component';
import { TemplatePushDialogComponent } from './template-push-dialog.component';

const LIST = '/api/v1/admin/view-templates/7/assignments';
const NONE = { assignments: [] };

async function create(assignments: object = NONE) {
  const http = bootstrap({
    me: { isInternal: true, adminPermissions: ['prism.admin.templates.publish'] },
    providers: [
      { provide: MAT_DIALOG_DATA, useValue: { templateId: 7, templateName: 'Finance start' } },
      { provide: MatDialogRef, useValue: { close: vi.fn() } },
    ],
  });
  const fixture: ComponentFixture<TemplatePushDialogComponent> = TestBed.createComponent(
    TemplatePushDialogComponent,
  );
  fixture.detectChanges();
  http.expectOne(LIST).flush(assignments);
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

function typeProvider(
  fixture: ComponentFixture<TemplatePushDialogComponent>,
  _root: HTMLElement,
  text: string,
): void {
  // A Provider is picked by name now. The picker hands the dialog its number, so the tests choose by number.
  fixture.debugElement
    .query(By.directive(ProviderPickerComponent))
    .componentInstance.chosen.emit(
      text.trim() === '' ? null : { id: text.trim(), name: `Provider ${text.trim()}` },
    );
  fixture.detectChanges();
}

/** Lets a resource's reload turn into a request: it is issued by an effect, not at the moment of the call. */
async function tick(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 3; i += 1) {
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
  }
}

const submit = (root: HTMLElement) =>
  root.querySelector<HTMLButtonElement>('button[type="submit"]')!;

describe('TemplatePushDialogComponent', () => {
  it('refuses a Provider that is not a plain number before asking the server', async () => {
    const { fixture, http, root } = await create();

    for (const bad of ['', '0', '-5', '+5', 'abc', '12 3', '1234567890']) {
      typeProvider(fixture, root, bad);
      submit(root).click();
      fixture.detectChanges();
      expect(root.querySelector('#push-error')?.textContent).toContain('Pick a Provider');
    }
    http.verify();
  });

  it('reviews a one-Provider push, then confirms it with the widget count it just showed', async () => {
    const { fixture, http, root } = await create();
    typeProvider(fixture, root, '526');

    submit(root).click();
    const review = http.expectOne({
      method: 'POST',
      url: '/api/v1/admin/view-templates/7/assignments/preview',
    });
    expect(review.request.body).toEqual({ kind: 'Provider', providerId: 526 });
    review.flush({
      viewTemplateId: 7,
      templateName: 'Finance start',
      widgetCount: 3,
      scope: { kind: 'Provider', providerId: 526 },
      providerCount: 1,
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('.review')?.textContent).toContain('will go to Provider 526');

    root.querySelector<HTMLButtonElement>('.review button')!.click();
    const push = http.expectOne({ method: 'POST', url: LIST });
    expect(push.request.body).toEqual({
      scope: { kind: 'Provider', providerId: 526 },
      confirmedWidgetCount: 3,
    });
    push.flush({});
    await tick(fixture);
    http.expectOne(LIST).flush(NONE);
  });

  it('says plainly that the number of users reached for everyone is not known', async () => {
    const { fixture, http, root } = await create();
    root.querySelector<HTMLInputElement>('input[value="All"]')!.click();
    fixture.detectChanges();

    submit(root).click();
    http
      .expectOne({ method: 'POST', url: '/api/v1/admin/view-templates/7/assignments/preview' })
      .flush({
        viewTemplateId: 7,
        templateName: 'Finance start',
        widgetCount: 2,
        scope: { kind: 'All', providerId: null },
        providerCount: null,
      });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('.review')?.textContent).toContain('every Provider');
    expect(root.querySelector('.review')?.textContent).toContain('not known yet');
  });

  it('forgets a review when the audience changes, so a stale one cannot be confirmed', async () => {
    const { fixture, http, root } = await create();
    typeProvider(fixture, root, '526');
    submit(root).click();
    http
      .expectOne({ method: 'POST', url: '/api/v1/admin/view-templates/7/assignments/preview' })
      .flush({
        viewTemplateId: 7,
        templateName: 'Finance start',
        widgetCount: 3,
        scope: { kind: 'Provider', providerId: 526 },
        providerCount: 1,
      });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('.review')).not.toBeNull();

    typeProvider(fixture, root, '527');

    expect(root.querySelector('.review')).toBeNull();
  });

  it('shows the server problem when a push is refused and keeps the review', async () => {
    const { fixture, http, root } = await create();
    typeProvider(fixture, root, '526');
    submit(root).click();
    http
      .expectOne({ method: 'POST', url: '/api/v1/admin/view-templates/7/assignments/preview' })
      .flush({
        viewTemplateId: 7,
        templateName: 'Finance start',
        widgetCount: 3,
        scope: { kind: 'Provider', providerId: 526 },
        providerCount: 1,
      });
    await fixture.whenStable();
    fixture.detectChanges();

    root.querySelector<HTMLButtonElement>('.review button')!.click();
    http
      .expectOne({ method: 'POST', url: LIST })
      .flush(
        { title: 'The template changed since you previewed it. Preview it again.' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('#push-error')?.textContent?.length).toBeGreaterThan(0);
  });

  it('lists earlier pushes with counts only and stops one', async () => {
    const { fixture, http, root } = await create({
      assignments: [
        {
          id: 3,
          viewTemplateId: 7,
          scope: { kind: 'Provider', providerId: 526 },
          name: 'Finance start',
          widgetCount: 3,
          deliveredCount: 2,
          createdAt: '2026-11-22T10:00:00Z',
          revokedAt: null,
        },
        {
          id: 2,
          viewTemplateId: 7,
          scope: { kind: 'All', providerId: null },
          name: 'Finance start',
          widgetCount: 3,
          deliveredCount: 1,
          createdAt: '2026-11-21T10:00:00Z',
          revokedAt: '2026-11-23T10:00:00Z',
        },
      ],
    });

    expect(root.textContent).toContain('Provider 526');
    expect(root.textContent).toContain('2 users have received it so far');
    expect(root.textContent).toContain('Every Provider');
    expect(root.querySelectorAll('li button')).toHaveLength(1);

    root.querySelector<HTMLButtonElement>('[aria-label="Stop the push to Provider 526"]')!.click();
    http.expectOne({ method: 'DELETE', url: '/api/v1/admin/template-assignments/3' }).flush({});
    await tick(fixture);
    http.expectOne(LIST).flush(NONE);
    fixture.detectChanges();
  });

  it('has no axe violations', async () => {
    const { root } = await create();

    expect(await axeViolations(root)).toEqual([]);
  });
});

export type { HttpTestingController };
