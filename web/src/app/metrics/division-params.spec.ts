import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { loadCashPerTransport } from './cash-per-transport-by-payer/cash-per-transport-by-payer.loader';
import { loadCleanClaims } from './clean-claim-rate/clean-claim-rate.loader';
import { loadNetCashBreakdown } from './net-cash-breakdown/net-cash-breakdown.loader';
import { loadNetCashByGroup } from './net-cash-by-group/net-cash-by-group.loader';
import { loadNetCashProjection } from './net-cash-projection/net-cash-projection.loader';
import { loadNetCollections } from './net-collections/net-collections.loader';
import { loadPayerMix } from './payer-mix/payer-mix.loader';
import { loadPaymentAge } from './payment-age/payment-age.loader';
import { loadTransportVolume } from './transport-volume/transport-volume.loader';
import { MetricQuery } from './shared/metric-http';

const query: MetricQuery = { providerId: '526', period: '202610', serviceLine: 'air', months: 6, division: 'north_base' };

describe('the Reporting Division query parameter', () => {
  function sent(load: (http: HttpClient, q: MetricQuery) => unknown, q: MetricQuery = query): string | null {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    (load(TestBed.inject(HttpClient), q) as { subscribe: () => void }).subscribe();
    const request = http.expectOne(() => true);
    const value = request.request.params.get('division');
    request.flush({});
    return value;
  }

  it.each([
    ['payer_mix', loadPayerMix],
    ['transport_volume', loadTransportVolume],
    ['payment_age', loadPaymentAge],
    ['cash_per_transport_by_payer', loadCashPerTransport],
    ['net_cash_breakdown', loadNetCashBreakdown],
    ['net_cash_by_group', loadNetCashByGroup],
    ['net_collections', loadNetCollections],
  ] as const)('is sent by %s when a Division is selected', (_key, load) => {
    expect(sent((http, q) => load(http, q))).toBe('north_base');
  });

  it.each([
    ['payer_mix', loadPayerMix],
    ['transport_volume', loadTransportVolume],
    ['net_cash_breakdown', loadNetCashBreakdown],
  ] as const)('is left out by %s for all divisions', (_key, load) => {
    expect(sent((http, q) => load(http, q), { ...query, division: undefined })).toBeNull();
  });

  it.each([
    ['net_cash_projection', loadNetCashProjection],
    ['clean_claim_rate', loadCleanClaims],
  ] as const)('is never sent by %s, which ignores it', (_key, load) => {
    expect(sent((http, q) => load(http, q))).toBeNull();
  });
});
