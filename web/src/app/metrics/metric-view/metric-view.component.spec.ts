import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../axe';
import { BASE, CATALOG, METRIC_REPLIES, NET_CASH, bootstrap, answer } from '../../saved-views/saved-views.testing';
import { DashboardFilterContext } from '../../dashboard/dashboard-filter-context';
import { HOSTILE, NEW_REPLIES } from '../new-metrics.testing';
import { MetricViewComponent } from './metric-view.component';

const ROUTES = [
  { path: 'dashboard/metrics/:key', component: MetricViewComponent },
  { path: 'dashboard/net-cash', component: MetricViewComponent },
  { path: 'dashboard/net-cash/:group', component: MetricViewComponent },
  { path: 'dashboard/metrics/payer-mix/:group/:payerKey', component: MetricViewComponent },
];

const NEW_METRICS = Object.values(NEW_REPLIES).map((r) => ({
  ...NET_CASH,
  key: r.key,
  name: r.key.replaceAll('_', ' '),
  serviceLines: r.airOnly ? ['air', 'both'] : ['air', 'ground', 'both'],
  context: { divisionSupport: 'supported' as const, appliesTo: r.airOnly ? ('air' as const) : ('both' as const) },
  presentations: [{ key: 'table', name: 'Table', icon: 'x', sizes: ['M', 'L'], minWidth: 3, minHeight: 2, maxWidth: 12, maxHeight: 6, isDefault: true }],
}));
const WITH_NEW = {
  ...METRIC_REPLIES,
  [`${BASE}/metrics/catalog`]: { ...CATALOG, metrics: [...CATALOG.metrics, ...NEW_METRICS] },
  ...Object.fromEntries(Object.values(NEW_REPLIES).map((r) => [`${BASE}/metrics/net-collections/${r.route}`, r.reply])),
};

async function open(url: string, replies: Record<string, object> = METRIC_REPLIES): Promise<{ harness: RouterTestingHarness; http: HttpTestingController; root: HTMLElement; router: Router }> {
  const http = bootstrap({ providers: [provideRouter(ROUTES)] });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    harness.detectChanges();
    TestBed.tick();
    answer(http, replies);
  }
  await harness.fixture.whenStable();
  harness.detectChanges();
  return { harness, http, root: harness.routeNativeElement as HTMLElement, router: TestBed.inject(Router) };
}

describe('MetricViewComponent', () => {
  it('shows a calm not-found state for an unknown key, and for keys that are not keys', async () => {
    for (const key of ['nope', 'constructor', 'Bad%20Key', '__proto__']) {
      const { root } = await open(`/dashboard/metrics/${key}`);

      expect(root.querySelector('h1')?.textContent).toBe('View not found');
      expect(root.textContent).toContain('Back to the dashboard');
      TestBed.resetTestingModule();
    }
  });

  it('is not found for a drill target whose address lacks the group or payer, and never requests data', async () => {
    const { root, http } = await open('/dashboard/metrics/payer_mix_detail?group=rotor-wing');

    expect(root.querySelector('h1')?.textContent).toBe('View not found');
    http.expectNone((r) => r.url.includes('payer-mix'));
  });

  it('sends a Metric that has its own page there, replacing this address and keeping the month, Service Line and Division', async () => {
    const { router } = await open('/dashboard/metrics/payer_mix_detail?group=rotor-wing&payerKey=commercial&period=202610&line=air&division=north');

    expect(router.url).toBe('/dashboard/metrics/payer-mix/rotor-wing/commercial?period=202610&line=air&division=north');
  });

  it.each([
    ['payer_mix?group=fixed-wing&period=202610&line=ground', '/dashboard/net-cash/fixed-wing?period=202610&line=ground'],
    ['net_cash_breakdown?period=202610&line=air', '/dashboard/net-cash?period=202610&line=air'],
    ['net_cash_breakdown?period=oops&line=sky&division=a%20b', '/dashboard/net-cash'],
  ])('maps %s to its page, dropping values that are not valid', async (path, expected) => {
    const { router } = await open(`/dashboard/metrics/${path}`);

    expect(router.url).toBe(expected);
  });

  it('draws any other Metric the catalog lists with a heading, its data stamp, context badge and help, and passes axe', async () => {
    const { root } = await open('/dashboard/metrics/net_collections');

    expect(root.querySelector('h1')?.textContent).toBe('Net cash collected');
    expect(root.querySelector('app-metric-widget')).not.toBeNull();
    expect(root.querySelector('app-freshness-stamp')).not.toBeNull();
    expect(root.querySelector('app-context-badge')).not.toBeNull();
    expect(root.querySelector('app-metric-help')).not.toBeNull();
    expect(root.querySelector('ems-breadcrumbs')?.textContent).toContain('Dashboard');
    expect(await axeViolations(root)).toEqual([]);
    expect(BASE).toBeTruthy();
  });

  it('moves focus to the heading when it opens', async () => {
    const { root } = await open('/dashboard/metrics/net_collections');

    expect(document.activeElement).toBe(root.querySelector('h1'));
  });

  describe('the six new Metrics', () => {
    it.each(Object.keys(NEW_REPLIES))('%s renders through the widget path with its table, help, stamp and badge, and passes axe', async (key) => {
      const { root } = await open(`/dashboard/metrics/${key}?period=202610&line=air`, WITH_NEW);

      expect(root.querySelector('h1')?.textContent).toBe(key.replaceAll('_', ' '));
      expect(root.querySelector('app-metric-widget table caption')).not.toBeNull();
      expect(root.querySelector('app-freshness-stamp')?.textContent).toContain('Data through');
      expect(root.querySelector('app-context-badge')).not.toBeNull();
      expect(root.querySelector('app-metric-help')?.textContent?.length).toBeGreaterThan(40);
      expect(await axeViolations(root)).toEqual([]);
    });

    it('defaults to air and shows the Air only text for an air-only Metric', async () => {
      const { root } = await open('/dashboard/metrics/aircraft_performance', WITH_NEW);

      expect(root.querySelector('app-context-badge')?.textContent).toBe('Air · All bases · Air only');
      expect(TestBed.inject(DashboardFilterContext).serviceLine()).toBe('air');
    });

    it('shows the not-available state for an air-only Metric with line=ground, and calls nothing', async () => {
      const { root, http } = await open('/dashboard/metrics/loaded_miles_by_aircraft?line=ground', WITH_NEW);

      expect(root.textContent).toContain("isn't available for ground");
      expect(root.querySelector('app-metric-widget table')).toBeNull();
      http.expectNone((r) => r.url.includes('loaded-miles-by-aircraft'));
    });

    it('shows a Metric for both lines under line=ground', async () => {
      const { root } = await open('/dashboard/metrics/base_analysis?line=ground', WITH_NEW);

      expect(root.querySelector('app-metric-widget table')).not.toBeNull();
      expect(root.querySelector('app-context-badge')?.textContent).toBe('Ground · All reporting divisions');
    });

    it('shows warehouse text literally', async () => {
      const { root } = await open('/dashboard/metrics/aircraft_performance?line=air', WITH_NEW);

      expect(root.textContent).toContain(HOSTILE);
      expect(root.querySelector('app-metric-widget img')).toBeNull();
    });
  });
});
