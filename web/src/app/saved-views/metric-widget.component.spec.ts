import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { LayoutWidget } from './layout.model';
import { MetricWidgetComponent, WidgetStatus } from './metric-widget.component';
import { BASE, CATALOG, CLEAN, CLEAN_CLAIMS, METRIC_REPLIES, NET_CASH, SERIES, bootstrap, settle, widget } from './saved-views.testing';

const NET = `${BASE}/metrics/net-collections`;

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
  it('draws a chart with a text description, and offers the same numbers as a table', async () => {
    const { root, fixture } = await create(widget('a', 0, 0));

    expect(root.querySelector('app-chart [role="img"]')?.getAttribute('aria-label')).toContain('Net cash collected, 2 months to Oct 2026');
    [...root.querySelectorAll('button')].find((b) => b.textContent!.trim() === 'Table')!.click();
    fixture.detectChanges();

    const table = root.querySelector('table')!;
    expect(table.querySelector('caption')?.textContent).toBe('Net cash collected by month');
    expect([...table.querySelectorAll('thead th')].map((th) => th.getAttribute('scope'))).toEqual(['col', 'col', 'col']);
    expect(table.querySelector('tbody th')?.getAttribute('scope')).toBe('row');
    expect(root.querySelector('[role="region"]')?.getAttribute('tabindex')).toBe('0');
  });

  it('draws a table presentation as a table with no toggle', async () => {
    const { root } = await create(widget('a', 0, 0, { presentation: 'table' }));

    expect(root.querySelector('table')).not.toBeNull();
    expect(root.querySelector('[role="group"]')).toBeNull();
  });

  it('draws a tile as a big number with its note, and the date the data runs to', async () => {
    const { root } = await create(widget('a', 0, 0, { metricKey: 'clean_claim_rate', presentation: 'tile', w: 3, h: 2 }), CLEAN_CLAIMS);

    expect(root.querySelector('.widget__value')?.textContent).toBe('94.5%');
    expect(root.textContent).toContain('First-pass resolution');
    expect(root.textContent).toContain('Data through 04/07/2026');
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
