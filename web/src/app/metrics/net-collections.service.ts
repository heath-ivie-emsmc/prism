import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { loadCashPerTransport } from './cash-per-transport-by-payer/cash-per-transport-by-payer.loader';
import { CashPerTransportDto } from './cash-per-transport-by-payer/cash-per-transport-by-payer.dto';
import { loadCleanClaims } from './clean-claim-rate/clean-claim-rate.loader';
import { CleanClaimRateDto } from './clean-claim-rate/clean-claim-rate.dto';
import { loadNetCashBreakdown } from './net-cash-breakdown/net-cash-breakdown.loader';
import { NetCashBreakdownDto } from './net-cash-breakdown/net-cash-breakdown.dto';
import { loadNetCashByGroup } from './net-cash-by-group/net-cash-by-group.loader';
import { NetCashByGroupDto } from './net-cash-by-group/net-cash-by-group.dto';
import { loadNetCashProjection } from './net-cash-projection/net-cash-projection.loader';
import { NetCashProjectionDto } from './net-cash-projection/net-cash-projection.dto';
import { loadNetCollections, loadPeriods } from './net-collections/net-collections.loader';
import { NetCollectionsDto, NetCollectionsPeriodsDto } from './net-collections/net-collections.dto';
import { loadPayerMix } from './payer-mix/payer-mix.loader';
import { PayerMixDto } from './payer-mix/payer-mix.dto';
import { loadPayerMixDetail } from './payer-mix-detail/payer-mix-detail.loader';
import { PayerMixDetailDto } from './payer-mix-detail/payer-mix-detail.dto';
import { loadPaymentAge } from './payment-age/payment-age.loader';
import { PaymentAgeDto } from './payment-age/payment-age.dto';
import { loadTransportVolume } from './transport-volume/transport-volume.loader';
import { TransportVolumeDto } from './transport-volume/transport-volume.dto';

type ServiceLine = 'all' | 'air' | 'ground';

/**
 * Reads Metrics for a Provider, for the dashboard and review pages. A thin facade over the per-metric loaders in each metric's folder,
 * kept so page components and their tests keep one injectable. The Provider is in the path, and the API checks it against the user's assignments on every call.
 */
@Injectable({ providedIn: 'root' })
export class NetCollectionsService {
  private readonly http = inject(HttpClient);

  periods(providerId: string): Observable<NetCollectionsPeriodsDto> {
    return loadPeriods(this.http, providerId);
  }

  series(providerId: string, endPeriod: string, months: number, serviceLine: ServiceLine = 'all'): Observable<NetCollectionsDto> {
    return loadNetCollections(this.http, { providerId, period: endPeriod, months, serviceLine });
  }

  /** The forecast for the months after `afterPeriod`. `horizon` is 1 to 3 months and defaults to 3. */
  projection(providerId: string, afterPeriod: string, serviceLine: ServiceLine, horizon = 3): Observable<NetCashProjectionDto> {
    return loadNetCashProjection(this.http, { providerId, period: afterPeriod, months: 0, serviceLine }, horizon);
  }

  breakdown(providerId: string, period: string, serviceLine: ServiceLine, division?: string): Observable<NetCashBreakdownDto> {
    return loadNetCashBreakdown(this.http, { providerId, period, months: 0, serviceLine, division });
  }

  payerMix(providerId: string, period: string, serviceLine: ServiceLine, group?: string, division?: string): Observable<PayerMixDto> {
    return loadPayerMix(this.http, { providerId, period, months: 0, serviceLine, division }, group);
  }

  /** The payers inside one payer group of a group's payer mix: the drill target of the payer mix. */
  payerMixDetail(providerId: string, period: string, serviceLine: ServiceLine, group: string, payerKey: string, division?: string): Observable<PayerMixDetailDto> {
    return loadPayerMixDetail(this.http, { providerId, period, months: 0, serviceLine, division }, group, payerKey);
  }

  paymentAge(providerId: string, period: string, serviceLine: ServiceLine, division?: string): Observable<PaymentAgeDto> {
    return loadPaymentAge(this.http, { providerId, period, months: 0, serviceLine, division });
  }

  cashPerTransport(providerId: string, period: string, serviceLine: ServiceLine, division?: string): Observable<CashPerTransportDto> {
    return loadCashPerTransport(this.http, { providerId, period, months: 0, serviceLine, division });
  }

  byGroup(providerId: string, period: string, serviceLine: ServiceLine, division?: string): Observable<NetCashByGroupDto> {
    return loadNetCashByGroup(this.http, { providerId, period, months: 0, serviceLine, division });
  }

  transports(providerId: string, endPeriod: string, months: number, serviceLine: ServiceLine, division?: string): Observable<TransportVolumeDto> {
    return loadTransportVolume(this.http, { providerId, period: endPeriod, months, serviceLine, division });
  }

  cleanClaims(providerId: string, endPeriod: string, months: number): Observable<CleanClaimRateDto> {
    return loadCleanClaims(this.http, { providerId, period: endPeriod, months, serviceLine: 'all' });
  }
}
