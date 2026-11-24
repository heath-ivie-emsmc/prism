import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { CashContributionByPayerDto } from './cash-contribution-by-payer.dto';

/** GET .../cash-contribution/payer. */
export function loadCashContributionByPayer(http: HttpClient, q: MetricQuery): Observable<CashContributionByPayerDto> {
  return http.get<CashContributionByPayerDto>(`${metricsBase(q.providerId)}/cash-contribution/payer`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
