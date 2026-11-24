import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { CashContributionByAircraftDto } from './cash-contribution-by-aircraft.dto';

/** GET .../cash-contribution/aircraft. Air only, so a widget never calls it under Ground only. */
export function loadCashContributionByAircraft(http: HttpClient, q: MetricQuery): Observable<CashContributionByAircraftDto> {
  return http.get<CashContributionByAircraftDto>(`${metricsBase(q.providerId)}/cash-contribution/aircraft`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
