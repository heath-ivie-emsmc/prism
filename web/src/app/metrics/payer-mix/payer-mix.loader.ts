import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { PayerMixDto } from './payer-mix.dto';

/** GET .../payer-mix. `group` narrows to one payer group's members. */
export function loadPayerMix(http: HttpClient, q: MetricQuery, group?: string): Observable<PayerMixDto> {
  return http.get<PayerMixDto>(`${metricsBase(q.providerId)}/payer-mix`, { params: periodParams(q.period, q.serviceLine, group, q.division) });
}
