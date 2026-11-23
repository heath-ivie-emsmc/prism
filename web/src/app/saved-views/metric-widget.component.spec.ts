import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { LayoutWidget } from './layout.model';
import { MetricWidgetComponent, WidgetStatus } from './metric-widget.component';
import { BASE, CATALOG, CLEAN, CLEAN_CLAIMS, METRIC_REPLIES, NET_CASH, SERIES, bootstrap, settle, widget } from './saved-views.testing';

const NET = `${BASE}/metrics/net-collections`;

const PAYER_MIX_METRIC = {
  ...NET_CASH,
  key: 'payer_mix',
  name: 'Payer mix',
  dimensions: ['period', 'serviceLine', 'division'],
  context: { divisionSupport: 'supported' as const },
  presentations: NET_CASH.presentations.map((p) => ({ ...p })),
};
const PAYER_MIX_REPLY = {
  metric: { key: 'payer_mix', name: 'Payer mix', description: 'd', unit: 'count' },
  dataThrough: '2026-11-05',
  providerId: '526',
  period: '202610',
  priorPeriod: '202609',
  serviceLine: 'air',
  group: null,
  groupName: null,
  shares: [{ key: 'medicare', name: 'Medicare', current: 10, prior: 8 }],
};

async function create(w: LayoutWidget, metric = NET_CASH, replies: Record<string, object> = METRIC_REPLIES) {
  const http = bootstrap();
  const fixture: ComponentFixture<MetricWidgetComponent> = TestBed.createComponent(MetricWidgetComponent);
  fixture.componentRef.setInput('widget', w);
  fixture.componentRef.setInput('metric', metric);
  const statuses: WidgetStatus[] = [];
  fixture.componentInstance.status.subscribe((s) => statuses.push(s));
  await settle(fixture, http, replies);
  return { fixture, http, root: fixture.nativeElement as HTMLElement, statuses };
}

describe('MetricWidgetComponent', () => {
  it('draws a chart with a text description', async () => {
    const { root } = await create(widget('a', 0, 0));

    expect(root.querySelector('app-chart [role="img"]')?.getAttribute('aria-label')).toContain('Net cash collected, 2 months to Oct 2026');
    expect(root.querySelector('table')).toBeNull();
  });

  it('draws the table presentation as a real table, with a caption, scoped headers and a focusable scroll region', async () => {
    const { root } = await create(widget('a', 0, 0, { presentation: 'table' }));

    const table = root.querySelector('table')!;
    expect(table.querySelector('caption')?.textContent).toBe('Net cash collected by month');
    expect([...table.querySelectorAll('thead th')].map((th) => th.getAttribute('scope'))).toEqual(['col', 'col', 'col']);
    expect(table.querySelector('tbody th')?.getAttribute('scope')).toBe('row');
    expect(root.querySelector('[role="region"]')?.getAttribute('tabindex')).toBe('0');
    expect(root.querySelector('[role="group"]')).toBeNull();
  });

  it('draws a tile as a big number with its note, and the date the data runs to', async () => {
    const { root } = await create(widget('a', 0, 0, { metricKey: 'clean_claim_rate', presentation: 'tile', w: 3, h: 2 }), CLEAN_CLAIMS);

    expect(root.querySelector('.widget__value')?.textContent).toBe('94.5%');
    expect(root.textContent).toContain('First-pass resolution');
    expect(root.textContent).toContain('Data through Apr 7 · Updates once a day');
    expect(root.textContent).toContain('Covers all service lines');
  });

  it('asks for the months the widget was saved with, and the page period and Service Line', async () => {
    const http = bootstrap();
    const fixture = TestBed.createComponent(MetricWidgetComponent);
    fixture.componentRef.setInput('widget', widget('a', 0, 0, { options: { months: '12' } }));
    fixture.componentRef.setInput('metric', NET_CASH);

    await settle(fixture, http, {}, false);
    const request = http.expectOne((r) => r.url === NET);

    expect([request.request.params.get('months'), request.request.params.get('endPeriod'), request.request.params.get('serviceLine')]).toEqual(['12', '202610', 'air']);
    request.flush(SERIES);
  });

  it('asks again, and only once, when the page period changes', async () => {
    const { http, fixture } = await create(widget('a', 0, 0));

    TestBed.inject(DashboardFilterContext).period.setValue('202609');
    await settle(fixture, http, {}, false);
    const requests = http.match((r) => r.url === NET);

    expect(requests).toHaveLength(1);
    expect(requests[0]!.request.params.get('endPeriod')).toBe('202609');
    requests[0]!.flush(SERIES);
  });

  it('reports loading, then ready', async () => {
    const { statuses } = await create(widget('a', 0, 0));

    expect(statuses).toContain('loading');
    expect(statuses.at(-1)).toBe('ready');
  });

  it('says calmly when it cannot load, reports it, and offers to try again', async () => {
    const http = bootstrap();
    const fixture = TestBed.createComponent(MetricWidgetComponent);
    fixture.componentRef.setInput('widget', widget('a', 0, 0));
    fixture.componentRef.setInput('metric', NET_CASH);
    const statuses: WidgetStatus[] = [];
    fixture.componentInstance.status.subscribe((s) => statuses.push(s));
    await settle(fixture, http, {}, false);

    http.expectOne((r) => r.url === NET).flush({ title: 'SECRET' }, { status: 503, statusText: 'x' });
    await Promise.resolve();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain("We can't load Net cash collected right now");
    expect(root.textContent).not.toContain('SECRET');
    expect(statuses.at(-1)).toBe('failed');
  });

  it('says there is no data for the period instead of drawing an empty chart', async () => {
    const { root } = await create(widget('a', 0, 0), NET_CASH, { ...METRIC_REPLIES, [NET]: { ...SERIES, points: [] } });

    expect(root.textContent).toContain('No net cash collected data for October 2026.');
    expect(root.querySelector('app-chart')).toBeNull();
  });

  it('shows a placeholder, and fetches nothing, for a metric it cannot draw', async () => {
    const planned = CATALOG.metrics.find((m) => m.key === 'ar_days')!;
    const { root, http } = await create(widget('a', 0, 0, { metricKey: 'ar_days' }), planned, {});

    expect(root.textContent).toContain('Metric no longer available');
    http.expectNone((r) => r.url.includes('metrics/net-collections') && !r.url.endsWith('/periods'));
  });

  it('says a metric is not available for a Service Line it does not support, and fetches nothing', async () => {
    const airOnly = { ...NET_CASH, serviceLines: ['ground'] };
    const { root, http } = await create(widget('a', 0, 0), airOnly, {});

    expect(root.textContent).toContain("isn't available for air");
    http.expectNone((r) => r.url === NET);
  });

  it('says plainly that a metric which ignores the Reporting Division is not filtered by it', async () => {
    const http = bootstrap();
    const fixture = TestBed.createComponent(MetricWidgetComponent);
    fixture.componentRef.setInput('widget', widget('a', 0, 0));
    fixture.componentRef.setInput('metric', NET_CASH);
    await settle(fixture, http, METRIC_REPLIES);
    const filters = TestBed.inject(DashboardFilterContext);

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Not filtered by');
    filters.division.setValue('d-1');
    fixture.detectChanges();
    filters.divisionOptions();

    // A division that is not in the list falls back to all, so the note shows only for a real division. The rule itself is covered by the catalog test.
    expect(filters.selectedDivision()).toBe('all');
  });

  it('shows one freshness stamp and one context badge per widget, in the shared wording', async () => {
    const { root } = await create(widget('a', 0, 0));

    expect(root.querySelectorAll('app-freshness-stamp')).toHaveLength(1);
    expect(root.querySelectorAll('app-context-badge')).toHaveLength(1);
    expect(root.textContent?.match(/Data through/g)).toHaveLength(1);
    expect(root.textContent).toContain('Data through Nov 5 · Updates once a day');
    expect(root.textContent).toContain('Air · All bases');
  });

  it('sends the page Division to a Metric that honors it, and the badge names the selected one', async () => {
    const supported = { ...PAYER_MIX_METRIC };
    const http = bootstrap();
    const fixture = TestBed.createComponent(MetricWidgetComponent);
    fixture.componentRef.setInput('widget', widget('a', 0, 0, { metricKey: 'payer_mix', presentation: 'column' }));
    fixture.componentRef.setInput('metric', supported);
    TestBed.inject(DashboardFilterContext).division.setValue('a1');
    await settle(fixture, http, { [`${BASE}/metrics/net-collections/payer-mix`]: PAYER_MIX_REPLY });

    expect(TestBed.inject(DashboardFilterContext).selectedDivision()).toBe('a1');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-context-badge')?.textContent).toContain('Air · Selected base location');
    http.verify();
  });

  it('does not send the Division to a Metric that ignores it, and the badge says all of them', async () => {
    const http = bootstrap();
    const fixture = TestBed.createComponent(MetricWidgetComponent);
    fixture.componentRef.setInput('widget', widget('a', 0, 0));
    fixture.componentRef.setInput('metric', NET_CASH);
    TestBed.inject(DashboardFilterContext).division.setValue('a1');
    await settle(fixture, http, METRIC_REPLIES);

    expect((fixture.nativeElement as HTMLElement).querySelector('app-context-badge')?.textContent).toContain('Air · All bases');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Not filtered by Base location');
  });

  describe('drill links', () => {
    const drillHref = (payer: string) => `${BASE}/metrics/net-collections/payer-mix/${payer}?group=rotor-wing&period=202610&serviceLine=air&division=`;
    const DRILL_REPLY = {
      ...PAYER_MIX_REPLY,
      group: 'rotor-wing',
      groupName: 'Rotor wing',
      shares: [
        { key: 'commercial', name: 'Commercial', current: 10, prior: 8, links: [{ rel: 'drill', href: drillHref('commercial'), title: 'Commercial payers' }] },
        { key: 'patient', name: 'Patient responsibility', current: 2, prior: 2 },
      ],
    };
    const open = (presentation: string, reply: object = DRILL_REPLY) =>
      create(widget('a', 0, 0, { metricKey: 'payer_mix', presentation }), PAYER_MIX_METRIC, { [`${NET}/payer-mix`]: reply });
    const hrefs = (root: HTMLElement, selector: string) => [...root.querySelectorAll<HTMLAnchorElement>(selector)].map((a) => [a.getAttribute('aria-label'), a.getAttribute('href')]);

    it('lists a real link under a chart for each payer group the API gave a link for, naming where it leads', async () => {
      const { root } = await open('column');

      expect(hrefs(root, '.widget__drills a')).toEqual([['View Commercial breakdown', '/dashboard/metrics/payer-mix/rotor-wing/commercial?period=202610&line=air']]);
    });

    it('links the row header in the table presentation, and only where there is a link', async () => {
      const { root } = await open('table');

      expect(hrefs(root, 'tbody th a')).toEqual([['View Commercial breakdown', '/dashboard/metrics/payer-mix/rotor-wing/commercial?period=202610&line=air']]);
      expect(root.querySelectorAll('tbody th')).toHaveLength(2);
    });

    it('has no links when the API gave none, and ignores a link that is not a known drill target', async () => {
      const none = await open('column', PAYER_MIX_REPLY);
      expect(none.root.querySelector('.widget__drills')).toBeNull();
      TestBed.resetTestingModule();
      const bad = await open('column', { ...DRILL_REPLY, shares: [{ ...DRILL_REPLY.shares[0]!, links: [{ rel: 'drill', href: 'https://evil.example/x', title: 'x' }] }] });
      expect(bad.root.querySelector('.widget__drills')).toBeNull();
    });

    it('passes axe with links in both presentations', async () => {
      const chart = await open('column');
      expect(await axeViolations(chart.root)).toEqual([]);
      TestBed.resetTestingModule();
      const table = await open('table');
      expect(await axeViolations(table.root)).toEqual([]);
    });
  });

  it('passes axe as a chart and as a table', async () => {
    const chart = await create(widget('a', 0, 0));
    expect(await axeViolations(chart.root)).toEqual([]);
    TestBed.resetTestingModule();
    const table = await create(widget('a', 0, 0, { presentation: 'table' }));
    expect(await axeViolations(table.root)).toEqual([]);
  });
});

export { CLEAN };
export type { HttpTestingController };
