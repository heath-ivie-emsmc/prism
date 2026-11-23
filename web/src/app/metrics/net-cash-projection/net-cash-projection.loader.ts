import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase } from '../shared/metric-http';
import { NetCashProjectionDto } from './net-cash-projection.dto';

/** GET .../projection. The period is the month after which to project. */
export function loadNetCashProjection(http: HttpClient, q: MetricQuery): Observable<NetCashProjectionDto> {
  const params = new HttpParams().set('endPeriod', q.period).set('serviceLine', q.serviceLine);
  return http.get<NetCashProjectionDto>(`${metricsBase(q.providerId)}/projection`, { params });
}
