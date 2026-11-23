import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { METRIC_RENDERERS, rendererFor } from '../metrics/metric-renderers';
import { MetricQuery } from '../metrics/shared/metric-http';
import { WidgetData } from './widget-data';

/** What a widget asks the Metric Catalog for. All of it comes from the page filters and the saved options, never from a stored value. */
export interface WidgetQuery extends MetricQuery {
  readonly metricKey: string;
  /** Changes when the user applies a Data Through update, so the widget loads again. Not sent to the API. */
  readonly revision?: number;
}

/** The Metric keys a widget can draw today. A key outside this list is shown as no longer available, never fetched. */
export const LOADABLE_METRICS: readonly string[] = Object.keys(METRIC_RENDERERS);

/** Loads one Metric through the renderer registry, the same loaders the dashboard uses. */
@Injectable({ providedIn: 'root' })
export class WidgetDataService {
  private readonly http = inject(HttpClient);

  load(query: WidgetQuery): Observable<WidgetData> {
    const renderer = rendererFor(query.metricKey);
    if (!renderer) {
      throw new Error('This metric cannot be drawn.');
    }
    return renderer.loader(this.http, query).pipe(map(renderer.toWidgetData));
  }
}
