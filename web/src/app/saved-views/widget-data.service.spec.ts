import { HttpTestingController } from '@angular/common/http/testing';
import { describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { LOADABLE_METRICS, WidgetDataService, WidgetQuery } from './widget-data.service';
import { BASE, bootstrap } from './saved-views.testing';

const query = (metricKey: string, over: Partial<WidgetQuery> = {}): WidgetQuery => ({ metricKey, providerId: '526', period: '202610', serviceLine: 'air', months: 12, ...over });
const NET = `${BASE}/metrics/net-collections`;

describe('WidgetDataService', () => {
  const routes: [string, string, (r: { params: { get: (k: string) => string | null } }) => void][] = [
    ['net_collections', NET, (r) => expect([r.params.get('endPeriod'), r.params.get('months'), r.params.get('serviceLine')]).toEqual(['202610', '12', 'air'])],
    ['net_cash_projection', `${NET}/projection`, (r) => expect(r.params.get('endPeriod')).toBe('202610')],
    ['net_cash_breakdown', `${NET}/breakdown`, (r) => expect([r.params.get('period'), r.params.get('serviceLine')]).toEqual(['202610', 'air'])],
    ['net_cash_by_group', `${NET}/by-group`, (r) => expect(r.params.get('period')).toBe('202610')],
    ['cash_per_transport_by_payer', `${NET}/cash-per-transport`, (r) => expect(r.params.get('period')).toBe('202610')],
    ['payment_age', `${NET}/payment-age`, (r) => expect(r.params.get('period')).toBe('202610')],
    ['payer_mix', `${NET}/payer-mix`, (r) => expect(r.params.get('group')).toBeNull()],
    ['transport_volume', `${NET}/transports`, (r) => expect(r.params.get('months')).toBe('12')],
    ['clean_claim_rate', `${NET}/clean-claims`, (r) => expect(r.params.get('serviceLine')).toBeNull()],
  ];

  it.each(routes)('draws %s through its own route with the page filters', (key, url, check) => {
    const http: HttpTestingController = bootstrap();
    const service = TestBed.inject(WidgetDataService);

    service.load(query(key)).subscribe();
    const request = http.expectOne((r) => r.url === url);
    check(request.request);
    request.flush({ points: [], months: [], shares: [], groups: [], rows: [], cells: [], payers: [], metric: { key, name: 'n', description: 'd', unit: 'u' } });

    http.verify();
  });

  it('covers every metric the service can load, and nothing it cannot', () => {
    expect(routes.map(([key]) => key).sort()).toEqual([...LOADABLE_METRICS].sort());
  });

  it('refuses a metric it does not know instead of guessing a route', () => {
    bootstrap();
    const service = TestBed.inject(WidgetDataService);

    expect(() => service.load(query('ar_days'))).toThrow('This metric cannot be drawn.');
  });
});
