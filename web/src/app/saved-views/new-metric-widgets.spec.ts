import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import catalog from '../metrics/catalog.fixture.json';
import { HOSTILE, NEW_REPLIES } from '../metrics/new-metrics.testing';
import { CatalogMetricDto } from './layout.model';
import { MetricWidgetComponent } from './metric-widget.component';
import { BASE, NET_CASH, bootstrap, settle, widget } from './saved-views.testing';

interface FixtureMetric {
  readonly key: string;
  readonly presentations: readonly string[];
  readonly serviceLines: readonly string[];
  readonly appliesTo: 'air' | 'ground' | 'both';
}
const FIXTURE = catalog.metrics as readonly FixtureMetric[];

/** The catalog entry for a new Metric, with the presentations, service lines and scope the generated catalog lists. */
function metricFor(key: string): CatalogMetricDto {
  const entry = FIXTURE.find((m) => m.key === key)!;
  return {
    ...NET_CASH,
    key,
    name: key.replaceAll('_', ' '),
    serviceLines: entry.serviceLines,
    context: { divisionSupport: 'supported', appliesTo: entry.appliesTo },
    presentations: entry.presentations.map((p, i) => ({ key: p, name: p, icon: 'x', sizes: ['M', 'L'], minWidth: 3, minHeight: 2, maxWidth: 12, maxHeight: 6, isDefault: i === 0 })),
  };
}

async function open(key: string, presentation: string, line: 'air' | 'ground' | 'both' = 'air') {
  const http = bootstrap();
  TestBed.inject(DashboardFilterContext).serviceLine.set(line);
  const fixture: ComponentFixture<MetricWidgetComponent> = TestBed.createComponent(MetricWidgetComponent);
  fixture.componentRef.setInput('widget', widget('a', 0, 0, { metricKey: key, presentation, w: 12, h: 4 }));
  fixture.componentRef.setInput('metric', metricFor(key));
  const reply = NEW_REPLIES[key]!;
  await settle(fixture, http, { [`${BASE}/metrics/net-collections/${reply.route}`]: reply.reply });
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

const NEW_KEYS = Object.keys(NEW_REPLIES);

describe('the six new Metrics as widgets', () => {
  it('are in the generated catalog with the presentations the renderers draw', () => {
    expect(NEW_KEYS.every((k) => FIXTURE.some((m) => m.key === k))).toBe(true);
  });

  it.each(NEW_KEYS)('%s draws a real table with a caption, scoped headers and the stamp and badge', async (key) => {
    const { root } = await open(key, 'table');

    const table = root.querySelector('table')!;
    expect(table.querySelector('caption')?.textContent?.trim()).not.toBe('');
    expect(table.querySelector('thead th')?.getAttribute('scope')).toBe('col');
    expect(table.querySelector('tbody th')?.getAttribute('scope')).toBe('row');
    expect(root.querySelector('[role="region"]')?.getAttribute('tabindex')).toBe('0');
    expect(root.querySelector('app-freshness-stamp')?.textContent).toContain('Data through');
    expect(root.querySelector('app-context-badge')).not.toBeNull();
    expect(await axeViolations(root)).toEqual([]);
  });

  it.each(NEW_KEYS)('%s draws its default presentation with a text description', async (key) => {
    const entry = FIXTURE.find((m) => m.key === key)!;
    const first = entry.presentations[0]!;
    const { root } = await open(key, first);

    if (first === 'tile') {
      expect(root.querySelector('.widget__value')?.textContent).not.toBe('');
    } else if (first === 'table') {
      expect(root.querySelector('table')).not.toBeNull();
    } else {
      expect(root.querySelector('app-chart [role="img"]')?.getAttribute('aria-label')).toBeTruthy();
    }
    expect(await axeViolations(root)).toEqual([]);
  });

  it('shows the effect as a tile, with the word expected and the recurring note', async () => {
    const { root } = await open('payer_mix_financial_effect', 'tile');

    expect(root.querySelector('.widget__value')?.textContent).toBe('-$3,600.00');
    expect(root.textContent).toContain('not posted cash');
    expect(root.textContent).toContain('the same amount repeats each month');
  });

  it('puts Air only beside the badge for the air-only Metrics, and nothing extra for the others', async () => {
    const air = await open('aircraft_performance', 'table');
    expect(air.root.querySelector('app-context-badge')?.textContent).toBe('Air · All bases · Air only');
    TestBed.resetTestingModule();
    const both = await open('base_analysis', 'table');
    expect(both.root.querySelector('app-context-badge')?.textContent).toBe('Air · All bases');
  });

  it.each(NEW_KEYS.filter((k) => NEW_REPLIES[k]!.airOnly))('%s under Ground only shows the not-available state and never calls the API', async (key) => {
    const { root, http } = await open(key, 'table', 'ground');

    expect(root.textContent).toContain("isn't available for ground");
    expect(root.querySelector('table')).toBeNull();
    http.expectNone((r) => r.url.includes(NEW_REPLIES[key]!.route));
  });

  it.each(NEW_KEYS.filter((k) => NEW_REPLIES[k]!.airOnly))('%s under Air + ground asks for all, which the API reads as air', async (key) => {
    const http = bootstrap();
    TestBed.inject(DashboardFilterContext).serviceLine.set('both');
    const fixture = TestBed.createComponent(MetricWidgetComponent);
    fixture.componentRef.setInput('widget', widget('a', 0, 0, { metricKey: key, presentation: 'table' }));
    fixture.componentRef.setInput('metric', metricFor(key));
    await settle(fixture, http, {}, false);

    const request = http.expectOne((r) => r.url.includes(NEW_REPLIES[key]!.route));
    expect(request.request.params.get('serviceLine')).toBe('all');
    request.flush(NEW_REPLIES[key]!.reply);
  });

  it('says a ground-only Metric is not available under Air only, and never calls the API', async () => {
    const http = bootstrap();
    const fixture = TestBed.createComponent(MetricWidgetComponent);
    fixture.componentRef.setInput('widget', widget('a', 0, 0, { metricKey: 'payer_mix_financial_effect', presentation: 'table' }));
    fixture.componentRef.setInput('metric', { ...metricFor('payer_mix_financial_effect'), serviceLines: ['ground', 'both'], context: { appliesTo: 'ground' } });
    await settle(fixture, http, {});

    expect((fixture.nativeElement as HTMLElement).textContent).toContain("isn't available for air");
    http.expectNone((r) => r.url.includes('payer-mix-financial-effect'));
  });

  it.each(['aircraft_performance', 'base_analysis', 'loaded_miles_by_aircraft', 'transports_by_pickup_location'])('%s shows warehouse text literally and never as markup', async (key) => {
    const { root } = await open(key, 'table');

    expect(root.textContent).toContain(HOSTILE);
    expect(root.querySelector('img')).toBeNull();
  });
});
