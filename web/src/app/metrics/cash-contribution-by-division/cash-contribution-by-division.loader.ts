import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { CashContributionByDivisionDto } from './cash-contribution-by-division.dto';

/** GET .../cash-contribution/division. */
export function loadCashContributionByDivision(http: HttpClient, q: MetricQuery): Observable<CashContributionByDivisionDto> {
  return http.get<CashContributionByDivisionDto>(`${metricsBase(q.providerId)}/cash-contribution/division`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
