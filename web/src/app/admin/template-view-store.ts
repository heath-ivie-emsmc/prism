import { Observable, map } from 'rxjs';
import { CatalogDto, SavedViewDto, SavedViewRequest } from '../saved-views/layout.model';
import { LoadedView, SavedViewsService } from '../saved-views/saved-views.service';
import { ViewStore } from '../saved-views/view-store';
import { TIER_AUTHOR } from './admin.model';
import { AdminTemplatesService, LoadedTemplate } from './admin-templates.service';

/** A template as the builder reads a view: no owner, and the person editing it always counts as its owner. */
function asView({ template, etag }: LoadedTemplate): LoadedView {
  const view: SavedViewDto = {
    id: template.id,
    name: template.name,
    description: template.description,
    ownerName: null,
    isOwner: true,
    layout: template.layout,
    version: template.version,
    updatedAt: template.updatedAt,
  };
  return { view, etag };
}

/**
 * Lets the Saved View builder lay out a View Template. Figures are drawn from the preview Provider (the `providerId` the builder passes, which the server
 * checks and audits), but a template is saved without it: the Provider never goes into a template.
 */
export function templateViewStore(
  templates: AdminTemplatesService,
  views: SavedViewsService,
): ViewStore {
  return {
    managePermission: TIER_AUTHOR,
    basePath: '/admin/templates',
    sectionLabel: 'View Templates',
    noun: 'template',
    canShare: false,
    catalog: (providerId: string): Observable<CatalogDto> => views.catalog(providerId),
    get: (_providerId: string, id: number): Observable<LoadedView> =>
      templates.get(id).pipe(map(asView)),
    create: (_providerId: string, body: SavedViewRequest): Observable<LoadedView> =>
      templates.create(body).pipe(map(asView)),
    update: (
      _providerId: string,
      id: number,
      body: SavedViewRequest,
      etag: string,
    ): Observable<LoadedView> => templates.update(id, body, etag).pipe(map(asView)),
    delete: (_providerId: string, id: number): Observable<void> => templates.delete(id),
  };
}
