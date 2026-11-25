import { InjectionToken, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CatalogDto, SavedViewRequest } from './layout.model';
import { LoadedView, SavedViewsService } from './saved-views.service';

/**
 * Where the builder loads and saves what it lays out. Saved Views are the default. View Templates provide their own, so one builder serves both.
 * `providerId` is the Provider whose figures are drawn: the user's own for a Saved View, the preview Provider for a template.
 */
export interface ViewStore {
  /** The permission that allows editing. For a template it is an admin tier, which the server checks again. */
  readonly managePermission: string;
  /** Where the list lives, and where a saved item opens: `${basePath}/${id}`. */
  readonly basePath: string;
  /** The plural name shown in the breadcrumb, the empty state and the page title. */
  readonly sectionLabel: string;
  /** The singular name shown in headings and messages. */
  readonly noun: string;
  /** Whether sharing by invitation applies. */
  readonly canShare: boolean;
  catalog(providerId: string): Observable<CatalogDto>;
  get(providerId: string, id: number): Observable<LoadedView>;
  create(providerId: string, body: SavedViewRequest): Observable<LoadedView>;
  update(providerId: string, id: number, body: SavedViewRequest, etag: string): Observable<LoadedView>;
  delete(providerId: string, id: number): Observable<void>;
}

/** Saved Views for the signed-in user, which is what every page that does not provide another store gets. */
export const VIEW_STORE = new InjectionToken<ViewStore>('VIEW_STORE', {
  providedIn: 'root',
  factory: () => {
    const api = inject(SavedViewsService);
    return {
      managePermission: 'prism.views.manage',
      basePath: '/saved-views',
      sectionLabel: 'Saved views',
      noun: 'view',
      canShare: true,
      catalog: (providerId) => api.catalog(providerId),
      get: (providerId, id) => api.get(providerId, id),
      create: (providerId, body) => api.create(providerId, body),
      update: (providerId, id, body, etag) => api.update(providerId, id, body, etag),
      delete: (providerId, id) => api.delete(providerId, id),
    } satisfies ViewStore;
  },
});
