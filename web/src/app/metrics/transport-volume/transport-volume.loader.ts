import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MetricQuery, metricsBase } from '../shared/metric-http';
import { TransportVolumeDto } from './transport-volume.dto';

/** GET .../transports. */
export function loadTransportVolume(http: HttpClient, q: MetricQuery): Observable<TransportVolumeDto> {
  let params = new HttpParams().set('endPeriod', q.period).set('months', q.months).set('serviceLine', q.serviceLine);
  if (q.division) params = params.set('division', q.division);
  return http.get<TransportVolumeDto>(`${metricsBase(q.providerId)}/transports`, { params });
}
