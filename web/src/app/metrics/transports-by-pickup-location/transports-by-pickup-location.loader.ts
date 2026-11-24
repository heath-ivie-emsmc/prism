import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, periodParams } from '../shared/metric-http';
import { TransportsByPickupLocationDto } from './transports-by-pickup-location.dto';

/** GET .../transports-by-pickup-location. */
export function loadTransportsByPickupLocation(http: HttpClient, q: MetricQuery): Observable<TransportsByPickupLocationDto> {
  return http.get<TransportsByPickupLocationDto>(`${metricsBase(q.providerId)}/transports-by-pickup-location`, { params: periodParams(q.period, q.serviceLine, undefined, q.division) });
}
