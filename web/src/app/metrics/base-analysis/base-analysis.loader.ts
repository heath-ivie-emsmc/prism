import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { BaseAnalysisDto } from './base-analysis.dto';

/** GET .../base-analysis. */
export function loadBaseAnalysis(http: HttpClient, q: MetricQuery): Observable<BaseAnalysisDto> {
  return http.get<BaseAnalysisDto>(`${metricsBase(q.providerId)}/base-analysis`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
