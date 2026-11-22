import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { BASE, NET_CASH, SERIES, bootstrap, settle, widget } from './saved-views.testing';
import { WidgetAction, WidgetHostComponent } from './widget-host.component';

async function create(inputs: { editing?: boolean; moving?: boolean; stacked?: boolean } = {}) {
  const http = bootstrap();
  const fixture: ComponentFixture<WidgetHostComponent> = TestBed.createComponent(WidgetHostComponent);
  fixture.componentRef.setInput('widget', widget('w1', 0, 0));
  fixture.componentRef.setInput('metric', NET_CASH);
  fixture.componentRef.setInput('title', 'Net cash collected');
  fixture.componentRef.setInput('editing', inputs.editing ?? true);
  fixture.componentRef.setInput('moving', inputs.moving ?? false);
  fixture.componentRef.setInput('stacked', inputs.stacked ?? false);
  fixture.componentRef.setInput('size', 'M');
  const actions: WidgetAction[] = [];
  fixture.componentInstance.action.subscribe((a) => actions.push(a));
  await settle(fixture, http, { [`${BASE}/metrics/net-collections`]: SERIES });
  return { fixture, root: fixture.nativeElement as HTMLElement, actions, http };
}

const tools = (root: HTMLElement) => [...root.querySelectorAll<HTMLButtonElement>('.host__tool')];
const press = (el: Element, key: string) => el.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));

describe('WidgetHostComponent', () => {
  it('shows the metric name as the card heading and no toolbar when only viewing', async () => {
    const { root } = await create({ editing: false });

    expect(root.querySelector('h2')?.textContent).toContain('Net cash collected');
    expect(root.querySelector('[role="toolbar"]')).toBeNull();
  });

  const switcher = (root: HTMLElement) => [...root.querySelectorAll<HTMLButtonElement>('app-segmented-control button')];

  it('offers one icon button per presentation the catalog lists, each named, with the current one pressed', async () => {
    const { root } = await create({ editing: false });

    expect(switcher(root).map((b) => b.getAttribute('aria-label'))).toEqual(NET_CASH.presentations.map((p) => p.name));
    expect(switcher(root).filter((b) => b.getAttribute('aria-checked') === 'true').map((b) => b.getAttribute('aria-label'))).toEqual(['Line']);
    expect(root.querySelector('[role="radiogroup"][aria-label="Show Net cash collected as"]')).not.toBeNull();
    expect(root.textContent).toContain('US dollars');
  });

  it('in view mode a different choice is the viewer\'s own: the chart changes and nothing is sent to the page', async () => {
    const { fixture, root, actions, http } = await create({ editing: false });

    switcher(root).find((b) => b.getAttribute('aria-label') === 'Table')!.click();
    await settle(fixture, http, { [`${BASE}/metrics/net-collections`]: SERIES });

    expect(root.querySelector('table')).not.toBeNull();
    expect(actions).toEqual([]);
  });

  it('in edit mode the choice goes to the page to be saved with the layout', async () => {
    const { root, actions } = await create({ editing: true });

    switcher(root).find((b) => b.getAttribute('aria-label') === 'Column')!.click();

    expect(actions).toEqual([{ kind: 'presentation', key: 'column' }]);
  });

  it('gives every tool a name that includes the widget, in one toolbar', async () => {
    const { root } = await create();

    expect(root.querySelector('[role="toolbar"]')?.getAttribute('aria-label')).toBe('Net cash collected actions');
    expect(tools(root).map((t) => t.getAttribute('aria-label'))).toEqual([
      'Move Net cash collected',
      'Size medium, Net cash collected',
      'Settings for Net cash collected',
      'Remove Net cash collected',
    ]);
  });

  it('puts only one tool in the Tab order and moves between them with the arrow keys', async () => {
    const { root } = await create();
    const all = tools(root);

    expect(all.map((t) => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1', '-1']);
    all[0]!.focus();
    press(all[0]!, 'ArrowRight');
    expect(document.activeElement).toBe(all[1]);
    press(all[1]!, 'End');
    expect(document.activeElement).toBe(all[3]);
    press(all[3]!, 'ArrowRight');
    expect(document.activeElement).toBe(all[0]);
    press(all[0]!, 'ArrowLeft');
    expect(document.activeElement).toBe(all[3]);
    press(all[3]!, 'Home');
    expect(document.activeElement).toBe(all[0]);
  });

  it('adds Move up and Move down in the one-column layout', async () => {
    const { root, actions } = await create({ stacked: true });

    root.querySelector<HTMLButtonElement>('[aria-label="Move Net cash collected down"]')!.click();
    root.querySelector<HTMLButtonElement>('[aria-label="Move Net cash collected up"]')!.click();

    expect(actions).toEqual([{ kind: 'reorder', direction: 1 }, { kind: 'reorder', direction: -1 }]);
  });

  it('starts a move on click, so a screen reader or voice command works, and reports settings and remove', async () => {
    const { root, actions } = await create();

    tools(root)[0]!.click();
    root.querySelector<HTMLButtonElement>('[aria-label="Settings for Net cash collected"]')!.click();
    root.querySelector<HTMLButtonElement>('[aria-label="Remove Net cash collected"]')!.click();

    expect(actions).toEqual([{ kind: 'move-start' }, { kind: 'settings' }, { kind: 'remove' }]);
  });

  it('hands every key and every blur on the Move button to the page, whether or not it thinks a move is on', async () => {
    const { root, actions, fixture } = await create({ moving: true });
    const move = tools(root)[0]!;

    press(move, 'ArrowDown');
    press(move, 'Escape');
    move.click();
    move.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(actions.map((a) => a.kind)).toEqual(['move-key', 'move-key', 'move-blur']);
    expect(move.getAttribute('aria-pressed')).toBe('true');
    expect(root.textContent).toContain('Moving');
  });

  it('keeps the arrow keys for the page while moving instead of roving the toolbar', async () => {
    const { root, fixture } = await create({ moving: true });
    fixture.componentInstance.action.subscribe((a) => a.kind === 'move-key' && a.event.preventDefault());
    const move = tools(root)[0]!;
    move.focus();

    press(move, 'ArrowRight');

    expect(document.activeElement).toBe(move);
  });

  it('does not rove the toolbar when the page used the key, even before this host knows a move began', async () => {
    const { root, fixture } = await create({ moving: false });
    fixture.componentInstance.action.subscribe((a) => a.kind === 'move-key' && a.event.preventDefault());
    const move = tools(root)[0]!;
    move.focus();

    press(move, 'ArrowRight');

    expect(document.activeElement).toBe(move);
  });

  it('offers only the sizes the presentation allows', async () => {
    const { root } = await create();

    root.querySelector<HTMLButtonElement>('[aria-label^="Size "]')!.click();
    await Promise.resolve();
    const items = [...document.querySelectorAll('[role="menuitemradio"]')].map((i) => i.textContent!.trim());

    expect(items).toEqual(['Medium', 'Large']);
  });

  it('passes axe in edit mode', async () => {
    const { root, http } = await create();

    expect(await axeViolations(root)).toEqual([]);
    http.verify();
  });
});

export type { HttpTestingController };
