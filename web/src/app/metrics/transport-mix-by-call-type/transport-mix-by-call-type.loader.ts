import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase, trendParams } from '../shared/metric-http';
import { TransportMixByCallTypeDto } from './transport-mix-by-call-type.dto';

/** GET .../transport-mix-by-call-type. */
export function loadTransportMixByCallType(http: HttpClient, q: MetricQuery): Observable<TransportMixByCallTypeDto> {
  return http.get<TransportMixByCallTypeDto>(`${metricsBase(q.providerId)}/transport-mix-by-call-type`, { params: trendParams(q) });
}
