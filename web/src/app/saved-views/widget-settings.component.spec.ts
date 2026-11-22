import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { LayoutWidget } from './layout.model';
import { NET_CASH, widget } from './saved-views.testing';
import { SettingsChange, WidgetSettingsComponent } from './widget-settings.component';

function create(w: LayoutWidget = widget('w1', 2, 3), metric = NET_CASH) {
  TestBed.configureTestingModule({ providers: [provideEmsUi()] });
  const fixture: ComponentFixture<WidgetSettingsComponent> = TestBed.createComponent(WidgetSettingsComponent);
  fixture.componentRef.setInput('widget', w);
  fixture.componentRef.setInput('metric', metric);
  fixture.componentRef.setInput('title', 'Net cash collected');
  const changes: SettingsChange[] = [];
  fixture.componentInstance.settingsChange.subscribe((c) => changes.push(c));
  let done = 0;
  fixture.componentInstance.done.subscribe(() => (done += 1));
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  return { fixture, root, changes, done: () => done };
}

const byLabel = (root: HTMLElement, label: string) => root.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;

describe('WidgetSettingsComponent', () => {
  it('names the widget in its heading and shows its position from one', () => {
    const { root } = create();

    expect(root.querySelector('h2')?.textContent).toBe('Settings: Net cash collected');
    expect((root.querySelector('#x') as HTMLInputElement).value).toBe('3');
    expect((root.querySelector('#y') as HTMLInputElement).value).toBe('4');
    expect((root.querySelector('#w') as HTMLInputElement).value).toBe('6');
  });

  it('moves with plain buttons, no dragging: nudge and steppers', () => {
    const { root, changes } = create();

    byLabel(root, 'Move right one column').click();
    byLabel(root, 'Move up one row').click();
    byLabel(root, 'Increase column').click();
    byLabel(root, 'Decrease row').click();

    expect(changes).toEqual([
      { kind: 'position', x: 3, y: 3 },
      { kind: 'position', x: 2, y: 2 },
      { kind: 'position', x: 3, y: 3 },
      { kind: 'position', x: 2, y: 2 },
    ]);
  });

  it('resizes with steppers and typed numbers, and ignores what is not a number', () => {
    const { root, changes } = create();

    byLabel(root, 'Increase width').click();
    byLabel(root, 'Decrease height').click();
    const width = root.querySelector<HTMLInputElement>('#w')!;
    width.value = '9';
    width.dispatchEvent(new Event('change'));
    width.value = '';
    width.dispatchEvent(new Event('change'));

    expect(changes).toEqual([{ kind: 'size', w: 7, h: 3 }, { kind: 'size', w: 6, h: 2 }, { kind: 'size', w: 9, h: 3 }]);
  });

  it('offers only the presentations and sizes the catalog allows, and reports a choice', () => {
    const { root, changes } = create();

    const presentations = [...root.querySelectorAll<HTMLElement>('app-segmented-control [role="radio"]')].map((r) => r.textContent!.replace(/show_chart/u, '').trim());

    expect(presentations).toContain('Line');
    expect(presentations).toContain('Table');
    expect(presentations).not.toContain('Small');
    expect(presentations).toContain('Medium');
    [...root.querySelectorAll<HTMLElement>('[role="radio"]')].find((r) => r.textContent!.includes('Column'))!.click();
    expect(changes.at(-1)).toEqual({ kind: 'presentation', key: 'column' });
  });

  it('draws the catalog options for the presentation, and reports a change', () => {
    const { root, changes } = create();

    expect(root.textContent).toContain('Months shown');
    [...root.querySelectorAll<HTMLElement>('[role="radio"]')].find((r) => r.textContent!.trim() === '12')!.click();

    expect(changes.at(-1)).toEqual({ kind: 'option', key: 'months', value: '12' });
  });

  it('shows no option that does not apply to the presentation', () => {
    const metric = { ...NET_CASH, options: [{ key: 'months', name: 'Months shown', type: 'choice' as const, choices: ['6', '12'], default: '6', appliesTo: ['column'] }] };

    const { root } = create(widget('w1', 0, 0), metric);

    expect(root.textContent).not.toContain('Months shown');
  });

  it('closes with Done', () => {
    const { root, done } = create();

    [...root.querySelectorAll('button')].find((b) => b.textContent!.trim() === 'Done')!.click();

    expect(done()).toBe(1);
  });

  it('passes axe', async () => {
    const { root } = create();

    expect(await axeViolations(root)).toEqual([]);
  });
});
