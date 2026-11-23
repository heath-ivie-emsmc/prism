import { Injectable, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { CatalogDto, CatalogMetricDto } from '../saved-views/layout.model';
import { SavedViewsService } from '../saved-views/saved-views.service';

/** How a Metric treats the Reporting Division filter: honors it, or always shows every division. */
export type DivisionSupport = 'supported' | 'none';

/** A Metric's Division support from its catalog entry. Entries from an older API carry only the `division` dimension. */
export function metricDivisionSupport(metric: CatalogMetricDto): DivisionSupport {
  return metric.context?.divisionSupport ?? (metric.dimensions.includes('division') ? 'supported' : 'none');
}

/** The Metric Catalog for the active Provider, loaded once and shared by every page that needs to know what a Metric supports. */
@Injectable({ providedIn: 'root' })
export class MetricCatalogService {
  private readonly auth = inject(AuthService);
  private readonly views = inject(SavedViewsService);

  private readonly resource = rxResource<CatalogDto | null, string | null>({
    params: () => this.auth.activeProviderId(),
    stream: ({ params }) => (params ? this.views.catalog(params) : of(null)),
  });

  /** Metrics by key. Empty until the catalog has loaded. */
  readonly metrics = computed(() => new Map<string, CatalogMetricDto>(((this.resource.hasValue() ? this.resource.value() : null)?.metrics ?? []).map((m) => [m.key, m])));
  readonly loaded = computed(() => this.resource.hasValue() && this.resource.value() !== null);

  /** The Metric's Division support; undefined while the catalog has not loaded or does not list the Metric. */
  divisionSupport(key: string): DivisionSupport | undefined {
    const metric = this.metrics().get(key);
    if (!metric) return undefined;
    return metricDivisionSupport(metric);
  }

  /** True when the Metric is split by the selected Division: the catalog says it honors it. Reads signals, so call it in a computed. */
  honorsDivision(key: string): boolean {
    return this.divisionSupport(key) === 'supported';
  }
}
