import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase } from '../shared/metric-http';
import { NetCashProjectionDto } from './net-cash-projection.dto';

/** The longest the forecast table reaches. */
export const MAX_HORIZON = 3;

/** GET .../projection. The period is the month after which to project. `horizon` is 1 to 3 months and defaults to 3. */
export function loadNetCashProjection(http: HttpClient, q: MetricQuery, horizon = MAX_HORIZON): Observable<NetCashProjectionDto> {
  const params = new HttpParams().set('endPeriod', q.period).set('serviceLine', q.serviceLine).set('horizon', Math.min(MAX_HORIZON, Math.max(1, Math.trunc(horizon))));
  return http.get<NetCashProjectionDto>(`${metricsBase(q.providerId)}/projection`, { params });
}
