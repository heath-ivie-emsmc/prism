import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase } from '../shared/metric-http';
import { CleanClaimRateDto } from './clean-claim-rate.dto';

/** GET .../clean-claims. Not split by Service Line. */
export function loadCleanClaims(http: HttpClient, q: MetricQuery): Observable<CleanClaimRateDto> {
  const params = new HttpParams().set('endPeriod', q.period).set('months', q.months);
  return http.get<CleanClaimRateDto>(`${metricsBase(q.providerId)}/clean-claims`, { params });
}
