import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { NetCollectionsService } from '../metrics/net-collections.service';
import {
  WidgetData,
  fromBreakdown,
  fromByGroup,
  fromCashPerTransport,
  fromCleanClaims,
  fromNetCash,
  fromPayerMix,
  fromPaymentAge,
  fromProjection,
  fromTransports,
} from './widget-data';

/** What a widget asks the Metric Catalog for. All of it comes from the page filters and the saved options, never from a stored value. */
export interface WidgetQuery {
  readonly metricKey: string;
  readonly providerId: string;
  readonly period: string;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly months: number;
}

/** The Metric keys a widget can draw today. A key outside this list is shown as no longer available, never fetched. */
export const LOADABLE_METRICS: readonly string[] = [
  'net_collections',
  'net_cash_projection',
  'net_cash_breakdown',
  'net_cash_by_group',
  'cash_per_transport_by_payer',
  'payment_age',
  'payer_mix',
  'transport_volume',
  'clean_claim_rate',
];

/** Loads one Metric through the same typed service the dashboard uses. */
@Injectable({ providedIn: 'root' })
export class WidgetDataService {
  private readonly api = inject(NetCollectionsService);

  load(query: WidgetQuery): Observable<WidgetData> {
    const { providerId, period, serviceLine, months } = query;
    switch (query.metricKey) {
      case 'net_collections':
        return this.api.series(providerId, period, months, serviceLine).pipe(map(fromNetCash));
      case 'net_cash_projection':
        return this.api.projection(providerId, period, serviceLine).pipe(map(fromProjection));
      case 'net_cash_breakdown':
        return this.api.breakdown(providerId, period, serviceLine).pipe(map(fromBreakdown));
      case 'net_cash_by_group':
        return this.api.byGroup(providerId, period, serviceLine).pipe(map(fromByGroup));
      case 'cash_per_transport_by_payer':
        return this.api.cashPerTransport(providerId, period, serviceLine).pipe(map(fromCashPerTransport));
      case 'payment_age':
        return this.api.paymentAge(providerId, period, serviceLine).pipe(map(fromPaymentAge));
      case 'payer_mix':
        return this.api.payerMix(providerId, period, serviceLine).pipe(map(fromPayerMix));
      case 'transport_volume':
        return this.api.transports(providerId, period, months, serviceLine).pipe(map(fromTransports));
      case 'clean_claim_rate':
        return this.api.cleanClaims(providerId, period, months).pipe(map(fromCleanClaims));
      default:
        throw new Error('This metric cannot be drawn.');
    }
  }
}
