import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase } from '../shared/metric-http';
import { NetCollectionsDto, NetCollectionsPeriodsDto } from './net-collections.dto';

/** GET .../metrics/net-collections. */
export function loadNetCollections(http: HttpClient, q: MetricQuery): Observable<NetCollectionsDto> {
  const params = new HttpParams().set('endPeriod', q.period).set('months', q.months).set('serviceLine', q.serviceLine);
  return http.get<NetCollectionsDto>(metricsBase(q.providerId), { params });
}

/** GET .../metrics/net-collections/periods. */
export function loadPeriods(http: HttpClient, providerId: string): Observable<NetCollectionsPeriodsDto> {
  return http.get<NetCollectionsPeriodsDto>(`${metricsBase(providerId)}/periods`);
}
