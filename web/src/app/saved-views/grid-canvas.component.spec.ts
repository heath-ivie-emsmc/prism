import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { GridCanvasComponent } from './grid-canvas.component';
import { LayoutWidget } from './layout.model';
import { WidgetAction } from './widget-host.component';
import { CATALOG, METRIC_REPLIES, bootstrap, settle, widget } from './saved-views.testing';

async function create(widgets: readonly LayoutWidget[], editing = true) {
  const http = bootstrap();
  const fixture: ComponentFixture<GridCanvasComponent> = TestBed.createComponent(GridCanvasComponent);
  fixture.componentRef.setInput('widgets', widgets);
  fixture.componentRef.setInput('metrics', new Map(CATALOG.metrics.map((m) => [m.key, m])));
  fixture.componentRef.setInput('editing', editing);
  const actions: { id: string; action: WidgetAction }[] = [];
  fixture.componentInstance.action.subscribe((a) => actions.push(a));
  let adds = 0;
  fixture.componentInstance.addRequested.subscribe(() => (adds += 1));
  await settle(fixture, http, METRIC_REPLIES);
  return { fixture, http, root: fixture.nativeElement as HTMLElement, actions, adds: () => adds };
}

describe('GridCanvasComponent', () => {
  it('invites the owner to add a metric when the view is empty', async () => {
    const { root, adds, fixture } = await create([]);

    expect(root.textContent).toContain('This view is empty');
    expect(root.textContent).toContain('Add a metric from the palette to get started.');
    root.querySelector<HTMLButtonElement>('#empty-add')!.click();
    fixture.detectChanges();
    expect(adds()).toBe(1);
  });

  it('tells a reader an empty view is empty, with no add button', async () => {
    const { root } = await create([], false);

    expect(root.textContent).toContain('There are no metrics on this view yet.');
    expect(root.querySelector('#empty-add')).toBeNull();
  });

  it('lists widgets in reading order, whatever order they were stored in', async () => {
    const { root } = await create([widget('b', 6, 0, { metricKey: 'clean_claim_rate', presentation: 'tile', w: 3, h: 2 }), widget('a', 0, 0)], false);

    const names = [...root.querySelectorAll('li.item ems-card h2, li.item app-widget-host h2')].map((h) => h.textContent!.trim());

    expect(names.length).toBeGreaterThanOrEqual(2);
    expect(names[0]).toContain('Net cash collected');
    expect(names[1]).toContain('Clean claim rate');
  });

  it('tells two widgets of one metric apart in their names', async () => {
    const { root } = await create([widget('a', 0, 0), widget('b', 6, 0)]);

    const toolbars = [...root.querySelectorAll('[role="toolbar"]')].map((t) => t.getAttribute('aria-label'));

    expect(toolbars).toEqual(['Net cash collected (1 of 2) actions', 'Net cash collected (2 of 2) actions']);
  });

  it('shows a placeholder, and a way to remove it, for a metric the catalog no longer has', async () => {
    const { root, actions, fixture } = await create([widget('gone', 0, 0, { metricKey: 'retired_metric' })]);

    expect(root.textContent).toContain('Metric no longer available');
    root.querySelector<HTMLButtonElement>('.gone button')!.click();
    fixture.detectChanges();
    expect(actions).toEqual([{ id: 'gone', action: { kind: 'remove' } }]);
  });

  it('places each widget on its column and row through the layout, not through its order', async () => {
    const { root } = await create([widget('a', 6, 3)]);
    const item = root.querySelector<HTMLElement>('li.item')!;

    expect(item.style.getPropertyValue('--x')).toBe('6');
    expect(item.style.getPropertyValue('--y')).toBe('3');
    expect(item.style.getPropertyValue('--w')).toBe('6');
    expect(item.getAttribute('data-widget-id')).toBe('a');
  });

  it('passes a toolbar action up with the widget it came from', async () => {
    const { root, actions } = await create([widget('a', 0, 0)]);

    root.querySelector<HTMLButtonElement>('[aria-label^="Remove "]')!.click();

    expect(actions).toEqual([{ id: 'a', action: { kind: 'remove' } }]);
  });

  it('passes axe with widgets on it', async () => {
    const { root, http } = await create([widget('a', 0, 0), widget('b', 6, 0, { presentation: 'table' })]);

    expect(await axeViolations(root)).toEqual([]);
    http.verify();
  });
});

export type { HttpTestingController };
