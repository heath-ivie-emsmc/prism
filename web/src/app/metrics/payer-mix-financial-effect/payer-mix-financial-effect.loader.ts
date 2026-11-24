import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { PayerMixFinancialEffectDto } from './payer-mix-financial-effect.dto';

/** GET .../payer-mix-financial-effect. */
export function loadPayerMixFinancialEffect(http: HttpClient, q: MetricQuery): Observable<PayerMixFinancialEffectDto> {
  return http.get<PayerMixFinancialEffectDto>(`${metricsBase(q.providerId)}/payer-mix-financial-effect`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
