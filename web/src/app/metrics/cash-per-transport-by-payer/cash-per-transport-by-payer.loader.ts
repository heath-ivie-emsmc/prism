import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { CashPerTransportDto } from './cash-per-transport-by-payer.dto';

/** GET .../cash-per-transport. */
export function loadCashPerTransport(http: HttpClient, q: MetricQuery): Observable<CashPerTransportDto> {
  return http.get<CashPerTransportDto>(`${metricsBase(q.providerId)}/cash-per-transport`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
