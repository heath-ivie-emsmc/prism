import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { NetCashBreakdownDto } from './net-cash-breakdown.dto';

/** GET .../breakdown. */
export function loadNetCashBreakdown(http: HttpClient, q: MetricQuery): Observable<NetCashBreakdownDto> {
  const params = periodParams(q.period, q.serviceLine, undefined, q.division);
  return http.get<NetCashBreakdownDto>(`${metricsBase(q.providerId)}/breakdown`, { params });
}
