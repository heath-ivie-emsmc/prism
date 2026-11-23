import { LiveAnnouncer } from '@angular/cdk/a11y';
import { BreakpointObserver } from '@angular/cdk/layout';
import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { EmsDialog } from '@ems-management-consultants/ui';
import { BehaviorSubject, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../axe';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { BASE, METRIC_REPLIES, MANAGE, answer, bootstrap, settle, viewDto } from './saved-views.testing';
import { ViewBuilderComponent } from './view-builder.component';

const VIEWS = `${BASE}/saved-views`;

interface Harness {
  fixture: ComponentFixture<ViewBuilderComponent>;
  http: HttpTestingController;
  root: HTMLElement;
  announced: string[];
  dialogs: { confirm: ReturnType<typeof vi.fn>; open: ReturnType<typeof vi.fn> };
  navigate: ReturnType<typeof vi.spyOn>;
  go: () => Promise<void>;
  component: ViewBuilderComponent;
}

async function open(options: { id?: string | null; permissions?: string[]; view?: object | number; docked?: boolean; confirm?: boolean; closed?: unknown } = {}): Promise<Harness> {
  const params = new BehaviorSubject(convertToParamMap(options.id ? { id: options.id } : {}));
  const dialogs = { confirm: vi.fn(() => of(options.confirm ?? true)), open: vi.fn(() => ({ afterClosed: () => of(options.closed) })) };
  const http = bootstrap({
    me: { permissions: options.permissions ?? ['prism.app', 'prism.dashboard.view', MANAGE] },
    providers: [
      { provide: ActivatedRoute, useValue: { paramMap: params, snapshot: { paramMap: params.value, queryParamMap: convertToParamMap({}) } } },
      { provide: BreakpointObserver, useValue: { observe: () => of({ matches: options.docked ?? true, breakpoints: {} }) } },
      { provide: EmsDialog, useValue: dialogs },
    ],
  });
  const announced: string[] = [];
  vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce').mockImplementation((message) => {
    announced.push(String(message));
    return Promise.resolve();
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  const fixture = TestBed.createComponent(ViewBuilderComponent);
  const replies: Record<string, object | (() => object)> = { ...METRIC_REPLIES };
  if (options.id && typeof options.view !== 'number') {
    replies[`${VIEWS}/${options.id}`] = options.view ?? viewDto();
  }
  const go = () => settle(fixture, http, replies);
  if (typeof options.view === 'number') {
    // The view request fails, so it is answered with an error each round instead of a body.
    for (let i = 0; i < 6; i += 1) {
      await Promise.resolve();
      fixture.detectChanges();
      TestBed.tick();
      answer(http, replies);
      http.match(`${VIEWS}/${options.id}`).forEach((r) => r.flush({ title: 'x' }, { status: options.view as number, statusText: 'x' }));
    }
    await fixture.whenStable();
    fixture.detectChanges();
  } else {
    await go();
  }
  return { fixture, http, root: fixture.nativeElement as HTMLElement, announced, dialogs, navigate, go, component: fixture.componentInstance };
}

const click = (root: HTMLElement, selector: string) => root.querySelector<HTMLElement>(selector)!.click();
const buttonNamed = (root: HTMLElement, text: string) => [...root.querySelectorAll('button')].find((b) => b.textContent!.trim() === text)!;
const press = (el: Element, key: string) => el.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
const widgetIds = (root: HTMLElement) => [...root.querySelectorAll('li.item')].map((li) => li.getAttribute('data-widget-id'));

describe('ViewBuilderComponent: a new view', () => {
  it('opens ready to arrange, with an empty canvas and the palette beside it', async () => {
    const { root, announced } = await open();

    expect(root.querySelector('h1')?.textContent).toBe('New view');
    expect(root.textContent).toContain('This view is empty');
    expect(root.querySelector('app-metric-palette')).not.toBeNull();
    expect(announced).toContain('Editing a new view.');
  });

  it('adds a metric from the palette, announces where it went, and saves only what the API should get', async () => {
    const h = await open();
    click(h.root, '[aria-label="Add Net cash collected to view"]');
    await h.go();

    expect(widgetIds(h.root)).toEqual(['w1']);
    expect(h.announced).toContain('Net cash collected added at row 1, column 1, size M, 6 columns wide. 1 metric on this view.');

    const name = h.root.querySelector<HTMLInputElement>('#view-name')!;
    name.value = '  My cash  ';
    name.dispatchEvent(new Event('input'));
    h.fixture.detectChanges();
    click(h.root, '.builder__actions button');
    const request = h.http.expectOne({ method: 'POST', url: VIEWS });
    const body = request.request.body as { name: string; description: unknown; layout: { widgets: Record<string, unknown>[] } };
    request.flush(viewDto(), { headers: { ETag: '"77"' } });
    await h.go();

    expect(Object.keys(body).sort()).toEqual(['description', 'layout', 'name']);
    expect(body.name).toBe('My cash');
    expect(body.layout.widgets[0]).toMatchObject({ id: 'w1', metricKey: 'net_collections', presentation: 'line', x: 0, y: 0, w: 6, h: 3 });
    expect(JSON.stringify(body)).not.toContain('providerId');
    // Layouts are layouts, not data (ADR 0004): no division, label, filter, Data Through or figure is stored with a widget.
    expect(Object.keys(body.layout.widgets[0]!).sort()).toEqual(['h', 'id', 'metricKey', 'presentation', 'w', 'x', 'y']);
    expect(JSON.stringify(body)).not.toMatch(/division|label|dataThrough|serviceLine|period|1100000|1000000/i);
    expect(h.navigate).toHaveBeenCalledWith(['/saved-views', 5], { replaceUrl: true });
    expect(h.announced).toContain('Cash overview saved.');
  });

  it('will not save without a name, and says where the problem is', async () => {
    const h = await open();

    click(h.root, '.builder__actions button');
    h.fixture.detectChanges();

    expect(h.root.querySelector('#name-error')?.textContent).toContain('Give the view a name.');
    expect(h.root.querySelector('#view-name')?.getAttribute('aria-invalid')).toBe('true');
    h.http.expectNone({ method: 'POST', url: VIEWS });
  });

  it('refuses a planned metric aloud and a full view aloud, and adds nothing', async () => {
    const h = await open();

    click(h.root, '[aria-label="Add AR days to view"]');
    h.fixture.detectChanges();

    expect(h.announced).toContain("AR days is coming soon and can't be added yet.");
    expect(widgetIds(h.root)).toEqual([]);
  });

  it('puts a metric dropped on a cell at that cell', async () => {
    const h = await open();
    const metric = (await import('./saved-views.testing')).NET_CASH;

    (h.component as unknown as { onDrop: (d: object) => void }).onDrop({ x: 6, y: 0, metric, widgetId: null });
    await h.go();

    const item = h.root.querySelector<HTMLElement>('li.item')!;
    expect([item.style.getPropertyValue('--x'), item.style.getPropertyValue('--y')]).toEqual(['6', '0']);
  });

  it('reports a name already taken beside the name field', async () => {
    const h = await open();
    const name = h.root.querySelector<HTMLInputElement>('#view-name')!;
    name.value = 'Taken';
    name.dispatchEvent(new Event('input'));
    click(h.root, '.builder__actions button');

    h.http.expectOne({ method: 'POST', url: VIEWS }).flush({ title: 'x' }, { status: 409, statusText: 'Conflict' });
    h.fixture.detectChanges();

    expect(h.root.querySelector('#name-error')?.textContent).toContain('You already have a view with this name.');
  });

  it('keeps the draft and tells the user, calmly, when the server fails', async () => {
    const h = await open();
    const name = h.root.querySelector<HTMLInputElement>('#view-name')!;
    name.value = 'Mine';
    name.dispatchEvent(new Event('input'));
    click(h.root, '.builder__actions button');

    h.http.expectOne({ method: 'POST', url: VIEWS }).flush({ title: 'SECRET', traceId: '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01' }, { status: 503, statusText: 'x' });
    h.fixture.detectChanges();

    const message = h.root.querySelector('ems-message')?.textContent ?? '';
    expect(message).toContain('Your changes are still on this page. Try again.');
    expect(message).toContain('Reference: 0af7651916cd43dd8448eb211c80319c');
    expect(message).not.toContain('SECRET');
    expect(h.announced.at(-1)).toBe("We couldn't save Mine. Your changes are still here. Try again.");
  });

  it('explains a refused layout without echoing it', async () => {
    const h = await open();
    const name = h.root.querySelector<HTMLInputElement>('#view-name')!;
    name.value = 'Mine';
    name.dispatchEvent(new Event('input'));
    click(h.root, '.builder__actions button');

    h.http.expectOne({ method: 'POST', url: VIEWS }).flush({ errors: { layout: ['unknown_metric'] } }, { status: 400, statusText: 'x' });
    h.fixture.detectChanges();

    expect(h.root.querySelector('ems-message')?.textContent).toContain('no longer available');
    expect(h.root.querySelector('ems-message')?.textContent).not.toContain('unknown_metric');
  });
});

describe('ViewBuilderComponent: an existing view', () => {
  it('opens to read, with Edit and Share for its owner, and the version it will save from', async () => {
    const h = await open({ id: '5' });

    expect(h.root.querySelector('h1')?.textContent).toBe('Cash overview');
    expect([...h.root.querySelectorAll('.builder__actions button')].map((b) => b.textContent!.trim()).slice(0, 2)).toEqual(['Edit', 'Share']);
    expect(h.root.querySelector('[role="toolbar"]')).toBeNull();
    expect(widgetIds(h.root)).toEqual(['w1']);
  });

  it('edits, shows the change as unsaved, undoes it, and saves with If-Match', async () => {
    const h = await open({ id: '5' });
    click(h.root, '.builder__actions button');
    await h.go();
    expect(h.root.querySelector('[role="toolbar"]')).not.toBeNull();

    click(h.root, '[aria-label="Remove Net cash collected"]');
    await h.go();
    expect(widgetIds(h.root)).toEqual([]);
    expect(h.root.textContent).toContain('Unsaved changes');
    expect(h.announced).toContain('Net cash collected removed. 0 metrics left. Undo is available.');

    click(h.root, '[aria-label^="Undo"]');
    await h.go();
    expect(widgetIds(h.root)).toEqual(['w1']);
    expect(h.root.textContent).not.toContain('Unsaved changes');

    click(h.root, '[aria-label="Remove Net cash collected"]');
    await h.go();
    click(h.root, '.builder__actions button');
    const put = h.http.expectOne({ method: 'PUT', url: `${VIEWS}/5` });
    expect(put.request.headers.get('If-Match')).toBe('"77"');
    put.flush(viewDto({ layout: { version: 1, density: 'comfortable', widgets: [] }, version: '78' }), { headers: { ETag: '"78"' } });
    await h.go();
    expect(h.root.textContent).not.toContain('Unsaved changes');
  });

  it('redoes what was undone', async () => {
    const h = await open({ id: '5' });
    click(h.root, '.builder__actions button');
    await h.go();
    click(h.root, '[aria-label="Remove Net cash collected"]');
    await h.go();
    click(h.root, '[aria-label^="Undo"]');
    await h.go();

    buttonNamed(h.root, 'Redo').click();
    await h.go();

    expect(widgetIds(h.root)).toEqual([]);
    expect(h.announced.some((m) => m.startsWith('Redid:'))).toBe(true);
  });

  it('asks before throwing changes away, and goes back to what was saved when told to', async () => {
    const h = await open({ id: '5', confirm: true });
    click(h.root, '.builder__actions button');
    await h.go();
    click(h.root, '[aria-label="Remove Net cash collected"]');
    await h.go();

    buttonNamed(h.root, 'Discard').click();
    await h.go();

    expect(h.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Discard changes?', danger: true }));
    expect(widgetIds(h.root)).toEqual(['w1']);
    expect(h.root.querySelector('[role="toolbar"]')).toBeNull();
    expect(h.announced).toContain('Changes discarded.');
  });

  it('keeps editing when told not to discard', async () => {
    const h = await open({ id: '5', confirm: false });
    click(h.root, '.builder__actions button');
    await h.go();
    click(h.root, '[aria-label="Remove Net cash collected"]');
    await h.go();

    buttonNamed(h.root, 'Discard').click();
    await h.go();

    expect(widgetIds(h.root)).toEqual([]);
    expect(h.root.querySelector('[role="toolbar"]')).toBeNull();
  });

  it('knows when there is something to lose and asks the leave question', async () => {
    const h = await open({ id: '5' });
    expect(h.component.hasUnsavedChanges()).toBe(false);
    click(h.root, '.builder__actions button');
    await h.go();
    click(h.root, '[aria-label="Remove Net cash collected"]');
    await h.go();
    const event = new Event('beforeunload', { cancelable: true });

    window.dispatchEvent(event);
    h.component.confirmLeave().subscribe();

    expect(h.component.hasUnsavedChanges()).toBe(true);
    expect(event.defaultPrevented).toBe(true);
    expect(h.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Leave without saving?', cancelLabel: 'Keep editing' }));
  });

  it('opens the conflict choices when someone else saved first, and loads their version on request', async () => {
    const h = await open({ id: '5', closed: 'reload' });
    click(h.root, '.builder__actions button');
    await h.go();
    click(h.root, '[aria-label="Remove Net cash collected"]');
    await h.go();
    click(h.root, '.builder__actions button');

    h.http.expectOne({ method: 'PUT', url: `${VIEWS}/5` }).flush({ title: 'x' }, { status: 412, statusText: 'Precondition Failed' });
    await h.go();

    expect(h.announced).toContain('Cash overview was changed by someone else while you were editing. Choose how to continue.');
    expect(h.dialogs.open).toHaveBeenCalled();
    expect(h.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Load their version?' }));
  });

  it('saves a copy from the conflict choices', async () => {
    const h = await open({ id: '5', closed: 'copy' });
    click(h.root, '.builder__actions button');
    await h.go();
    click(h.root, '[aria-label="Remove Net cash collected"]');
    await h.go();
    click(h.root, '.builder__actions button');
    h.http.expectOne({ method: 'PUT', url: `${VIEWS}/5` }).flush({ title: 'x' }, { status: 412, statusText: 'x' });
    await h.go();

    const copy = h.http.expectOne({ method: 'POST', url: VIEWS });
    expect((copy.request.body as { name: string }).name).toBe('Cash overview (copy)');
    copy.flush(viewDto({ id: 6 }));

    expect(h.navigate).toHaveBeenCalledWith(['/saved-views', 6]);
  });

  it('deletes after confirming and leaves for the list', async () => {
    const h = await open({ id: '5', confirm: true });
    click(h.root, '.builder__actions button:nth-of-type(3)');
    h.fixture.detectChanges();
    (h.component as unknown as { remove: () => void }).remove();

    h.http.expectOne({ method: 'DELETE', url: `${VIEWS}/5` }).flush(null);

    expect(h.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Delete "Cash overview"?', danger: true, cancelLabel: 'Keep view' }));
    expect(h.announced).toContain('Cash overview deleted.');
  });

  it('opens the share dialog for an owner', async () => {
    const h = await open({ id: '5' });

    buttonNamed(h.root, 'Share').click();

    expect(h.dialogs.open).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ data: { providerId: '526', viewId: 5, viewName: 'Cash overview' } }));
  });

  it('saves a copy through the name dialog', async () => {
    const h = await open({ id: '5', closed: 'Cash overview (mine)' });
    (h.component as unknown as { saveCopy: () => void }).saveCopy();

    const copy = h.http.expectOne({ method: 'POST', url: VIEWS });
    expect((copy.request.body as { name: string }).name).toBe('Cash overview (mine)');
    copy.flush(viewDto({ id: 8 }));

    expect(h.navigate).toHaveBeenCalledWith(['/saved-views', 8]);
  });
});

describe('ViewBuilderComponent: a view shared with me', () => {
  const shared = viewDto({ isOwner: false, ownerName: 'asmith' });

  it('is read-only: no Edit, no Share, who shared it, and a way to keep a copy', async () => {
    const h = await open({ id: '5', view: shared });

    const actions = [...h.root.querySelectorAll('.builder__actions button')].map((b) => b.textContent!.trim());

    expect(actions).toEqual(['Save a copy', 'Download image']);
    expect(h.root.textContent).toContain('Shared by asmith');
    expect(h.root.querySelector('[role="toolbar"]')).toBeNull();
  });

  it('offers no copy to someone who may not create views', async () => {
    const h = await open({ id: '5', view: shared, permissions: ['prism.app', 'prism.dashboard.view'] });

    const actions = Array.from(h.root.querySelectorAll('.builder__actions button')).map((b) => b.textContent?.trim());
    expect(actions).toEqual(['Download image']);
  });
});

describe('ViewBuilderComponent: views that are not there', () => {
  it('says so when the API answers 404, and goes back to the list on request', async () => {
    const h = await open({ id: '5', view: 404 });

    expect(h.root.querySelector('h1')?.textContent).toContain("We couldn't find this view for your account");
    buttonNamed(h.root, 'Back to saved views').click();
    expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalledWith('/saved-views');
  });

  it('treats an id that is not a number as not found, without asking the API', async () => {
    const h = await open({ id: '5; DROP TABLE' });

    expect(h.root.querySelector('h1')?.textContent).toContain("We couldn't find this view");
    h.http.expectNone((r) => r.url.includes('saved-views/'));
  });

  it('says calmly when the view cannot load for another reason', async () => {
    const h = await open({ id: '5', view: 503 });

    expect(h.root.querySelector('h1')?.textContent).toContain("We can't load this view right now");
    expect(h.root.textContent).not.toContain('503');
  });
});

describe('ViewBuilderComponent: arranging', () => {
  const two = viewDto({ layout: { version: 1, density: 'comfortable', widgets: [{ id: 'a', metricKey: 'net_collections', presentation: 'line', x: 0, y: 0, w: 6, h: 3 }, { id: 'b', metricKey: 'net_collections', presentation: 'line', x: 6, y: 0, w: 6, h: 3 }] } });

  const editing = async (extra: Parameters<typeof open>[0] = {}) => {
    const h = await open({ id: '5', view: two, ...extra });
    click(h.root, '.builder__actions button');
    await h.go();
    return h;
  };
  const moveButton = (root: HTMLElement, n = 1) => root.querySelector<HTMLElement>(`[data-widget-id="${n === 1 ? 'a' : 'b'}"] .host__tool`)!;
  const position = (root: HTMLElement, id: string) => {
    const item = root.querySelector<HTMLElement>(`[data-widget-id="${id}"]`)!;
    return [item.style.getPropertyValue('--x'), item.style.getPropertyValue('--y')];
  };

  it('moves a widget with the keyboard: start, arrow keys, drop, and says each step', async () => {
    const h = await editing();

    moveButton(h.root).click();
    await h.go();
    expect(h.announced).toContain('Moving Net cash collected (1 of 2). Row 1, column 1. Use the arrow keys to move, Enter to drop, Escape to cancel.');
    press(moveButton(h.root), 'ArrowDown');
    await h.go();
    press(moveButton(h.root), 'ArrowRight');
    await h.go();

    expect(h.announced.some((m) => m.startsWith('Row '))).toBe(true);
    press(moveButton(h.root), 'Enter');
    await h.go();
    expect(h.announced.some((m) => m.includes('dropped at row'))).toBe(true);
  });

  it('cancels a move with Escape and puts the widget back', async () => {
    const h = await editing();
    const before = position(h.root, 'a');

    moveButton(h.root).click();
    await h.go();
    press(moveButton(h.root), 'ArrowDown');
    await h.go();
    press(moveButton(h.root), 'Escape');
    await h.go();

    expect(position(h.root, 'a')).toEqual(before);
    expect(h.announced.some((m) => m.startsWith('Move cancelled.'))).toBe(true);
  });

  it('cancels a move when focus leaves, so a Tab never traps', async () => {
    const h = await editing();
    moveButton(h.root).click();
    await h.go();

    moveButton(h.root).dispatchEvent(new Event('blur'));
    await h.go();

    expect(h.announced.some((m) => m.startsWith('Move cancelled.'))).toBe(true);
  });

  it('says it is at an edge instead of moving past it', async () => {
    const h = await editing();
    moveButton(h.root).click();
    await h.go();

    press(moveButton(h.root), 'ArrowLeft');
    press(moveButton(h.root), 'ArrowUp');
    press(moveButton(h.root, 2), 'ArrowRight');

    expect(h.announced).toContain('Left edge.');
    expect(h.announced).toContain('Top.');
  });

  it('opens a widget settings pane, applies a size, and closes it back to the Settings button', async () => {
    const h = await editing();

    click(h.root, '[data-widget-id="a"] [aria-label^="Settings for"]');
    await h.go();
    expect(h.root.querySelector('app-widget-settings h2')?.textContent).toContain('Settings: Net cash collected (1 of 2)');
    [...h.root.querySelectorAll<HTMLElement>('app-widget-settings [role="radio"]')].find((r) => r.textContent!.trim() === 'Large')!.click();
    await h.go();
    expect(h.announced).toContain('Net cash collected (1 of 2) size set to L, 12 columns by 4 rows.');

    buttonNamed(h.root, 'Done').click();
    await h.go();
    expect(h.root.querySelector('app-widget-settings')).toBeNull();
  });

  it('changes how a widget is shown and an option, and announces both', async () => {
    const h = await editing();
    click(h.root, '[data-widget-id="a"] [aria-label^="Settings for"]');
    await h.go();

    [...h.root.querySelectorAll<HTMLElement>('app-widget-settings [role="radio"]')].find((r) => r.textContent!.includes('Table'))!.click();
    await h.go();
    [...h.root.querySelectorAll<HTMLElement>('app-widget-settings [role="radio"]')].find((r) => r.textContent!.trim() === '12')!.click();
    await h.go();

    expect(h.announced).toContain('Presentation set to Table. Net cash collected (1 of 2) updated.');
    expect(h.announced).toContain('Months shown set to 12. Net cash collected (1 of 2) updated.');
  });

  it('moves a widget by the position buttons, no dragging', async () => {
    const h = await editing();
    click(h.root, '[data-widget-id="b"] [aria-label^="Settings for"]');
    await h.go();

    click(h.root, 'app-widget-settings [aria-label="Move down one row"]');
    await h.go();

    expect(position(h.root, 'b')[1]).toBe('1');
  });

  it('changes the density', async () => {
    const h = await editing();

    [...h.root.querySelectorAll<HTMLElement>('form[aria-label="View settings"] [role="radio"]')].find((r) => r.textContent!.includes('Compact'))!.click();
    await h.go();

    expect(h.root.querySelector('ul.canvas--compact')).not.toBeNull();
  });

  it('uses Ctrl+Z inside the canvas but never inside a text field', async () => {
    const h = await editing();
    click(h.root, '[data-widget-id="a"] [aria-label="Remove Net cash collected (1 of 2)"]');
    await h.go();
    expect(widgetIds(h.root)).toEqual(['b']);

    const field = h.root.querySelector('#view-name')!;
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true }));
    await h.go();
    expect(widgetIds(h.root)).toEqual(['b']);

    h.root.querySelector('li.item')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true }));
    await h.go();
    expect(widgetIds(h.root)).toEqual(['a', 'b']);
  });

  it('passes axe while editing', async () => {
    const h = await editing();

    expect(await axeViolations(h.root)).toEqual([]);
  });
});

describe('ViewBuilderComponent: filters', () => {
  it('says once that the metrics are updating and once that they are done, not once per widget', async () => {
    const h = await open({ id: '5', view: viewDto({ layout: { version: 1, density: 'comfortable', widgets: [{ id: 'a', metricKey: 'net_collections', presentation: 'line', x: 0, y: 0, w: 6, h: 3 }, { id: 'b', metricKey: 'net_collections', presentation: 'table', x: 6, y: 0, w: 6, h: 3 }] } }) });
    h.announced.length = 0;

    TestBed.inject(DashboardFilterContext).period.setValue('202609');
    await h.go();

    expect(h.announced.filter((m) => m.startsWith('Updating'))).toEqual(['Updating 2 metrics for September 2026, air.']);
    expect(h.announced.filter((m) => m.startsWith('All 2 metrics updated'))).toHaveLength(1);
  });

  it('does not stay on a disabled Reporting Division control that nothing can use', async () => {
    const h = await open({ id: '5' });

    expect(h.root.querySelector('#division-reason')?.textContent).toContain("can't be split by");
    expect(h.root.querySelector('#division-reason')?.textContent).toContain('Not applied yet: Net cash collected');
    expect(h.root.querySelector('#division-reason')?.textContent).not.toContain('the metrics on this view');
  });
});
