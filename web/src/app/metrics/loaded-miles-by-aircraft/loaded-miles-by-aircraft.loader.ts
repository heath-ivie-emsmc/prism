import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, trendParams } from '../shared/metric-http';
import { LoadedMilesByAircraftDto } from './loaded-miles-by-aircraft.dto';

/** GET .../loaded-miles-by-aircraft. Air only, so a widget never calls it under Ground only. */
export function loadLoadedMilesByAircraft(http: HttpClient, q: MetricQuery): Observable<LoadedMilesByAircraftDto> {
  return http.get<LoadedMilesByAircraftDto>(`${metricsBase(q.providerId)}/loaded-miles-by-aircraft`, { params: trendParams(q) });
}
