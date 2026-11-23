import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../axe';
import { BASE, METRIC_REPLIES, bootstrap, answer } from '../../saved-views/saved-views.testing';
import { MetricViewComponent } from './metric-view.component';

const ROUTES = [
  { path: 'dashboard/metrics/:key', component: MetricViewComponent },
  { path: 'dashboard/net-cash', component: MetricViewComponent },
  { path: 'dashboard/net-cash/:group', component: MetricViewComponent },
  { path: 'dashboard/metrics/payer-mix/:group/:payerKey', component: MetricViewComponent },
];

async function open(url: string): Promise<{ harness: RouterTestingHarness; http: HttpTestingController; root: HTMLElement; router: Router }> {
  const http = bootstrap({ providers: [provideRouter(ROUTES)] });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    harness.detectChanges();
    TestBed.tick();
    answer(http, METRIC_REPLIES);
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
});
