import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { PayerMixDetailDto } from './payer-mix-detail.dto';

/** GET .../payer-mix/{payerGroupKey}. `group` is the aircraft type or level of service the payer mix was read for. */
export function loadPayerMixDetail(http: HttpClient, q: MetricQuery, group: string, payerGroupKey: string): Observable<PayerMixDetailDto> {
  return http.get<PayerMixDetailDto>(`${metricsBase(q.providerId)}/payer-mix/${encodeURIComponent(payerGroupKey)}`, {
    params: periodParams(q.period, q.serviceLine, group, q.division),
  });
}
