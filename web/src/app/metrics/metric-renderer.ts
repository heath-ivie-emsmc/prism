import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { WidgetData } from '../saved-views/widget-data';
import type { MetricQuery } from './shared/metric-http';

/** Everything a Saved View widget needs to draw one Metric: fetch it, turn the response into widget data, and the presentations it supports. */
export interface MetricRenderer<TDto> {
  readonly loader: (http: HttpClient, query: MetricQuery) => Observable<TDto>;
  readonly toWidgetData: (dto: TDto) => WidgetData;
  /** Presentation keys, as the Metric Catalog lists them for this Metric. */
  readonly presentations: readonly string[];
}
