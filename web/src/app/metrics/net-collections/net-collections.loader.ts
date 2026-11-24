import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase } from '../shared/metric-http';
import { NetCollectionsDto, NetCollectionsPeriodsDto, ReportingDivisionsDto } from './net-collections.dto';

/** GET .../metrics/net-collections. */
export function loadNetCollections(http: HttpClient, q: MetricQuery): Observable<NetCollectionsDto> {
  let params = new HttpParams().set('endPeriod', q.period).set('months', q.months).set('serviceLine', q.serviceLine);
  if (q.division) params = params.set('division', q.division);
  return http.get<NetCollectionsDto>(metricsBase(q.providerId), { params });
}

/** GET .../metrics/net-collections/periods. */
export function loadPeriods(http: HttpClient, providerId: string): Observable<NetCollectionsPeriodsDto> {
  return http.get<NetCollectionsPeriodsDto>(`${metricsBase(providerId)}/periods`);
}

/**
 * GET .../metrics/net-collections/divisions: the Provider's Reporting Divisions (key and name). With `serviceLine` air or ground, only the divisions that have a Transport
 * under it (keys are the same under every line). Left out for all, which the API reads as all.
 */
export function loadDivisions(http: HttpClient, providerId: string, serviceLine: 'all' | 'air' | 'ground' = 'all'): Observable<ReportingDivisionsDto> {
  return http.get<ReportingDivisionsDto>(`${metricsBase(providerId)}/divisions`, serviceLine === 'all' ? {} : { params: { serviceLine } });
}
