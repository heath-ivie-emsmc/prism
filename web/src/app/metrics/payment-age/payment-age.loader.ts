import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { PaymentAgeDto } from './payment-age.dto';

/** GET .../payment-age. */
export function loadPaymentAge(http: HttpClient, q: MetricQuery): Observable<PaymentAgeDto> {
  return http.get<PaymentAgeDto>(`${metricsBase(q.providerId)}/payment-age`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
