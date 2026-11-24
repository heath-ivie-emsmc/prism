import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { AircraftPerformanceDto } from './aircraft-performance.dto';

/** GET .../aircraft-performance. Air only, so a widget never calls it under Ground only. */
export function loadAircraftPerformance(http: HttpClient, q: MetricQuery): Observable<AircraftPerformanceDto> {
  return http.get<AircraftPerformanceDto>(`${metricsBase(q.providerId)}/aircraft-performance`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
