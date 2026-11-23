import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { NetCashByGroupDto } from './net-cash-by-group.dto';

/** GET .../by-group. */
export function loadNetCashByGroup(http: HttpClient, q: MetricQuery): Observable<NetCashByGroupDto> {
  return http.get<NetCashByGroupDto>(`${metricsBase(q.providerId)}/by-group`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
