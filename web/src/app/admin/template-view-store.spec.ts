import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { EMPTY_LAYOUT } from '../saved-views/layout.model';
import { SavedViewsService } from '../saved-views/saved-views.service';
import { TIER_AUTHOR, ViewTemplateDto } from './admin.model';
import { AdminTemplatesService } from './admin-templates.service';
import { templateViewStore } from './template-view-store';

const TEMPLATE: ViewTemplateDto = {
  id: 4,
  name: 'Finance start',
  description: 'For the billing team',
  isPublished: false,
  layout: EMPTY_LAYOUT,
  version: '77',
  updatedAt: '2026-11-24T10:00:00Z',
};

function setup() {
  const templates = {
    get: vi.fn(() => of({ template: TEMPLATE, etag: '"77"' })),
    create: vi.fn(() => of({ template: TEMPLATE, etag: '"77"' })),
    update: vi.fn(() => of({ template: TEMPLATE, etag: '"78"' })),
    delete: vi.fn(() => of(undefined)),
  } as unknown as AdminTemplatesService;
  const views = {
    catalog: vi.fn(() => of({ metrics: [], categories: [] })),
  } as unknown as SavedViewsService;
  return { templates, views, store: templateViewStore(templates, views) };
}

describe('templateViewStore', () => {
  it('needs the author tier, lives under the admin templates path and does not share', () => {
    const { store } = setup();

    expect(store.managePermission).toBe(TIER_AUTHOR);
    expect(store.basePath).toBe('/admin/templates');
    expect(store.canShare).toBe(false);
    expect(store.noun).toBe('template');
  });

  it('reads a template as a view the editor owns, with no owner name, and keeps the version', () => {
    const { store } = setup();
    let loaded: unknown;

    store.get('526', 4).subscribe((value) => (loaded = value));

    expect(loaded).toEqual({
      view: {
        id: 4,
        name: 'Finance start',
        description: 'For the billing team',
        ownerName: null,
        isOwner: true,
        layout: EMPTY_LAYOUT,
        version: '77',
        updatedAt: '2026-11-24T10:00:00Z',
      },
      etag: '"77"',
    });
  });

  it('never sends the preview Provider to the template API', () => {
    const { store, templates } = setup();
    const body = { name: 'x', description: null, layout: EMPTY_LAYOUT };

    store.get('526', 4).subscribe();
    store.create('526', body).subscribe();
    store.update('526', 4, body, '"77"').subscribe();
    store.delete('526', 4).subscribe();

    expect(templates.get).toHaveBeenCalledWith(4);
    expect(templates.create).toHaveBeenCalledWith(body);
    expect(templates.update).toHaveBeenCalledWith(4, body, '"77"');
    expect(templates.delete).toHaveBeenCalledWith(4);
  });

  it('draws figures from the preview Provider through the catalog', () => {
    const { store, views } = setup();

    store.catalog('526').subscribe();

    expect(views.catalog).toHaveBeenCalledWith('526');
  });
});
