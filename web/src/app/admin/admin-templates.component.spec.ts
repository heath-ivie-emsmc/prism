import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { bootstrap } from '../saved-views/saved-views.testing';
import { AdminTemplatesComponent } from './admin-templates.component';

const LIST = {
  templates: [
    {
      id: 1,
      name: 'Finance start',
      description: 'For billing',
      isPublished: true,
      widgetCount: 3,
      updatedAt: '2026-11-20T10:00:00Z',
    },
    {
      id: 2,
      name: 'Chief start',
      description: null,
      isPublished: false,
      widgetCount: 1,
      updatedAt: '2026-11-21T10:00:00Z',
    },
  ],
};

async function create(adminPermissions: string[], list: object = LIST) {
  const http = bootstrap({ me: { isInternal: true, adminPermissions } });
  const fixture = TestBed.createComponent(AdminTemplatesComponent);
  fixture.detectChanges();
  http.expectOne('/api/v1/admin/view-templates').flush(list);
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

/** Lets a resource's reload turn into a request: it is issued by an effect, not at the moment of the call. */
async function tick(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 3; i += 1) {
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
  }
}

const names = (root: HTMLElement) =>
  [...root.querySelectorAll('.templates__list li > a')].map((a) => a.textContent);
const labels = (root: HTMLElement) =>
  [...root.querySelectorAll('.templates__actions button')].map((b) => b.getAttribute('aria-label'));

describe('AdminTemplatesComponent', () => {
  it('lists the templates with their state and size, built once as rows', async () => {
    const { root } = await create(['prism.admin.templates.author']);

    expect(names(root)).toEqual(['Finance start', 'Chief start']);
    expect(root.textContent).toContain('Published');
    expect(root.textContent).toContain('Draft');
    expect(root.textContent).toContain('3 widgets');
    expect(root.textContent).toContain('1 widget ');
  });

  it('shows Create to an author and no publish or push buttons to someone who is only an author', async () => {
    const { root } = await create(['prism.admin.templates.author']);

    expect(root.textContent).toContain('Create a template');
    expect(labels(root)).toEqual([]);
  });

  it('shows Push and Unpublish for a published template and Publish for a draft, to a publisher only', async () => {
    const { root } = await create(['prism.admin.templates.publish']);

    expect(root.textContent).not.toContain('Create a template');
    expect(labels(root)).toEqual([
      'Push Finance start',
      'Unpublish Finance start',
      'Publish Chief start',
    ]);
  });

  it('publishes a draft and reloads the list', async () => {
    const { fixture, http, root } = await create(['prism.admin.templates.publish']);

    root.querySelector<HTMLButtonElement>('[aria-label="Publish Chief start"]')!.click();
    http.expectOne({ method: 'POST', url: '/api/v1/admin/view-templates/2/publish' }).flush({});
    await tick(fixture);

    http.expectOne('/api/v1/admin/view-templates').flush(LIST);
  });

  it('says there are no templates yet', async () => {
    const { root } = await create(['prism.admin.templates.author'], { templates: [] });

    expect(root.querySelector('h2')?.textContent).toBe('No templates yet');
  });

  it('has no axe violations', async () => {
    const { root } = await create([
      'prism.admin.templates.author',
      'prism.admin.templates.publish',
    ]);

    expect(await axeViolations(root)).toEqual([]);
  });

  it('verifies nothing is left outstanding', async () => {
    const { http } = await create(['prism.admin.templates.author']);

    http.verify();
  });
});

export type { HttpTestingController };
