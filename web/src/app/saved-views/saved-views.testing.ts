import { DIVISIONS } from '../shared/division.testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentProviders, Provider } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { MeDto } from '../auth/me.dto';
import { CatalogDto, CatalogMetricDto, LayoutWidget } from './layout.model';

/** Shared fixtures for the Saved Views specs: a small catalog, a signed-in user, and helpers that answer the page's requests. */

const line = { key: 'line', name: 'Line', icon: 'show_chart', sizes: ['M', 'L'], minWidth: 6, minHeight: 3, maxWidth: 12, maxHeight: 6, isDefault: true };
const column = { key: 'column', name: 'Column', icon: 'bar_chart', sizes: ['M', 'L'], minWidth: 6, minHeight: 3, maxWidth: 12, maxHeight: 6, isDefault: false };
const table = { key: 'table', name: 'Table', icon: 'table_rows', sizes: ['M', 'L'], minWidth: 6, minHeight: 3, maxWidth: 12, maxHeight: 6, isDefault: false };
const tile = { key: 'tile', name: 'Tile', icon: 'pin', sizes: ['S', 'M', 'L'], minWidth: 3, minHeight: 2, maxWidth: 6, maxHeight: 3, isDefault: true };

export const NET_CASH: CatalogMetricDto = {
  key: 'net_collections',
  name: 'Net cash collected',
  description: 'Cash collected in the accounting period.',
  unit: 'USD',
  category: 'cash',
  status: 'available',
  placeable: true,
  claimLevel: false,
  serviceLines: ['air', 'ground', 'both'],
  dimensions: ['period', 'months', 'serviceLine'],
  presentations: [line, column, table],
  options: [{ key: 'months', name: 'Months shown', type: 'choice', choices: ['6', '12'], default: '6', appliesTo: ['line', 'column', 'table'] }],
};

export const CLEAN_CLAIMS: CatalogMetricDto = {
  ...NET_CASH,
  key: 'clean_claim_rate',
  name: 'Clean claim rate',
  description: 'The share of claims accepted first time.',
  unit: 'percent',
  category: 'claims',
  presentations: [{ ...tile }, { ...line, isDefault: false }, table],
  options: [{ key: 'months', name: 'Months shown', type: 'choice', choices: ['6', '12'], default: '6', appliesTo: ['tile', 'line', 'table'] }],
};

export const AR_DAYS: CatalogMetricDto = {
  ...NET_CASH,
  key: 'ar_days',
  name: 'AR days',
  description: 'Days in accounts receivable.',
  unit: 'days',
  category: 'claims',
  status: 'planned',
  placeable: false,
  serviceLines: [],
  dimensions: [],
  presentations: [],
  options: [],
};

export const INSIGHT: CatalogMetricDto = { ...AR_DAYS, key: 'what_changed', name: 'What changed', category: 'insights', status: 'available' };

export const CATALOG: CatalogDto = {
  categories: [
    { key: 'cash', name: 'Cash' },
    { key: 'claims', name: 'Claims and AR' },
    { key: 'insights', name: 'Insights' },
  ],
  metrics: [NET_CASH, CLEAN_CLAIMS, AR_DAYS, INSIGHT],
};

export { DIVISIONS };
export const PROVIDER = { id: '526', name: 'Seminole County Fire Department', facilityCount: 0 };
export const PERIODS = { providerId: '526', periods: [{ period: '202610', label: 'Oct 2026' }, { period: '202609', label: 'Sep 2026' }] };
export const MANAGE = 'prism.views.manage';
export const BASE = '/api/v1/providers/526';

export const SERIES = {
  metric: { key: 'net_collections', name: 'Net cash collected', description: 'd', unit: 'USD' },
  providerId: '526',
  endPeriod: '202610',
  months: 6,
  snapshotDate: '2026-11-05',
  points: [
    { period: '202609', label: 'Sep', year: 2026, value: 1_000_000 },
    { period: '202610', label: 'Oct', year: 2026, value: 1_100_000 },
  ],
};

export const CLEAN = {
  metric: { key: 'clean_claim_rate', name: 'Clean claim rate', description: 'd', unit: 'percent' },
  providerId: '526',
  endPeriod: '202610',
  months: 6,
  snapshotDate: '2026-04-07',
  points: [{ period: '202610', label: 'Oct', year: 2026, cleanClaims: 945, rejectedClaims: 55, totalClaims: 1000, rate: 0.945 }],
};

/** Replies for every metric route the specs place on a view. */
export const METRIC_REPLIES: Record<string, object> = {
  [`${BASE}/metrics/net-collections`]: SERIES,
  [`${BASE}/metrics/net-collections/clean-claims`]: CLEAN,
};

export function widget(id: string, x: number, y: number, over: Partial<LayoutWidget> = {}): LayoutWidget {
  return { id, metricKey: 'net_collections', presentation: 'line', x, y, w: 6, h: 3, ...over };
}

export function viewDto(over: Record<string, unknown> = {}) {
  return {
    id: 5,
    name: 'Cash overview',
    description: null,
    ownerName: 'jdoe',
    isOwner: true,
    layout: { version: 1, density: 'comfortable', widgets: [widget('w1', 0, 0)] },
    version: '77',
    updatedAt: '2026-11-22T00:00:00Z',
    ...over,
  };
}

/** Configures TestBed, signs a user in, and returns the HTTP controller. Extra providers go last so a test can replace anything. */
export function bootstrap(options: { me?: Partial<MeDto>; providers?: (Provider | EnvironmentProviders)[] } = {}): HttpTestingController {
  TestBed.configureTestingModule({
    providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting(), provideRouter([]), ...(options.providers ?? [])],
  });
  const http = TestBed.inject(HttpTestingController);
  TestBed.inject(AuthService).refreshMe().subscribe();
  http.expectOne('/api/v1/me').flush({ userName: 'jdoe', displayName: 'Jane Doe', providers: [PROVIDER], permissions: ['prism.app', 'prism.dashboard.view', MANAGE], ...options.me });
  return http;
}

/** Answers what the page has asked for so far: the periods list, the catalog, and any view or metric requests given. */
export function answer(http: HttpTestingController, replies: Record<string, object | (() => object)> = {}): void {
  const all: Record<string, object | (() => object)> = {
    [`${BASE}/metrics/net-collections/periods`]: PERIODS,
    [`${BASE}/metrics/net-collections/divisions`]: DIVISIONS,
    [`${BASE}/metrics/catalog`]: CATALOG,
    ...replies,
  };
  for (const [url, body] of Object.entries(all)) {
    for (const request of http.match((r) => r.url === url)) {
      if (!request.cancelled) request.flush(typeof body === 'function' ? body() : body);
    }
  }
}

/** Runs effects, change detection and the requests they trigger until the page stops reacting. */
export async function settle<T>(fixture: ComponentFixture<T>, http: HttpTestingController, replies: Record<string, object | (() => object)> = {}, waitForRequests = true): Promise<void> {
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
    answer(http, replies);
  }
  if (waitForRequests) {
    await fixture.whenStable();
  }
  fixture.detectChanges();
}
