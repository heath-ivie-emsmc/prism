import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { MetricPaletteComponent } from './metric-palette.component';
import { CATALOG } from './saved-views.testing';

function create(inputs: { canAdd?: boolean; dragEnabled?: boolean } = {}): { fixture: ComponentFixture<MetricPaletteComponent>; root: HTMLElement; added: string[]; blocked: string[] } {
  TestBed.configureTestingModule({ providers: [provideEmsUi()] });
  const fixture = TestBed.createComponent(MetricPaletteComponent);
  fixture.componentRef.setInput('catalog', CATALOG);
  fixture.componentRef.setInput('canAdd', inputs.canAdd ?? true);
  fixture.componentRef.setInput('dragEnabled', inputs.dragEnabled ?? false);
  const added: string[] = [];
  const blocked: string[] = [];
  fixture.componentInstance.add.subscribe((m) => added.push(m.key));
  fixture.componentInstance.blocked.subscribe((e) => blocked.push(`${e.metric.key}:${e.reason}`));
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement, added, blocked };
}

/** A category heading's words, without the icon ligature that sits inside its button. */
const headingText = (h: Element): string => h.textContent!.replace(/expand_(?:less|more)/u, '').replace(/\s+/gu, ' ').trim();

const button = (root: HTMLElement, label: string) => root.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;

describe('MetricPaletteComponent', () => {
  it('groups the metrics by category and leaves out the dashboard-only insight', () => {
    const { root } = create();

    const headings = [...root.querySelectorAll('h3')].map(headingText);

    expect(headings).toEqual(['Cash (1)', 'Claims and AR (2)']);
    expect(root.textContent).not.toContain('What changed');
  });

  it('adds an available metric with its Add button', () => {
    const { root, fixture, added } = create();

    button(root, 'Add Net cash collected to view').click();
    fixture.detectChanges();

    expect(added).toEqual(['net_collections']);
  });

  it('lists a planned metric as coming soon: focusable, aria-disabled, and it adds nothing', () => {
    const { root, fixture, added, blocked } = create();
    const planned = button(root, 'Add AR days to view');

    planned.click();
    fixture.detectChanges();

    expect(planned.getAttribute('aria-disabled')).toBe('true');
    expect(planned.getAttribute('aria-describedby')).toBe('planned-reason');
    expect(planned.disabled).toBe(false);
    expect(root.textContent).toContain('Coming soon');
    expect(root.querySelector('#planned-reason')?.textContent).toContain("Can't be added yet");
    expect(added).toEqual([]);
    expect(blocked).toEqual(['ar_days:planned']);
  });

  it('says so, instead of doing nothing, when the view is full', () => {
    const { root, fixture, added, blocked } = create({ canAdd: false });

    button(root, 'Add Net cash collected to view').click();
    fixture.detectChanges();

    expect(button(root, 'Add Net cash collected to view').getAttribute('aria-disabled')).toBe('true');
    expect(added).toEqual([]);
    expect(blocked).toEqual(['net_collections:full']);
  });

  it('expands and collapses a category with aria-expanded', () => {
    const { root, fixture } = create();
    const toggle = root.querySelector<HTMLButtonElement>('h3 button')!;

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    toggle.click();
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(root.querySelector(`#${toggle.getAttribute('aria-controls')}`)).toBeNull();
  });

  it('filters by what is typed, opens every match, reports the count and clears with Escape', () => {
    const { root, fixture } = create();
    const searches: string[] = [];
    fixture.componentInstance.searched.subscribe((s) => searches.push(`${s.count}:${s.query}`));
    const input = root.querySelector<HTMLInputElement>('input[type="search"]')!;

    input.value = 'clean';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect([...root.querySelectorAll('h3')].map(headingText)).toEqual(['Claims and AR (1)']);
    expect(searches).toEqual(['1:clean']);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(root.querySelectorAll('h3').length).toBe(2);
  });

  it('says when nothing matches, and caps the query length', () => {
    const { root, fixture } = create();
    const input = root.querySelector<HTMLInputElement>('input[type="search"]')!;

    input.value = 'zzz'.repeat(30);
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(root.textContent).toContain('No metrics match');
    expect(input.maxLength).toBe(40);
    root.querySelector<HTMLButtonElement>('button[aria-label="Clear search"]')!.click();
    fixture.detectChanges();
    expect(root.querySelectorAll('h3').length).toBe(2);
  });

  it('offers a drag grip only where dragging is on and the metric can be placed', () => {
    const off = create({ dragEnabled: false });
    expect(off.root.querySelectorAll('.palette__grip').length).toBe(0);
    TestBed.resetTestingModule();

    const on = create({ dragEnabled: true });
    expect(on.root.querySelectorAll('.palette__grip').length).toBe(2);
  });

  it('passes axe', async () => {
    const { root } = create({ dragEnabled: true });

    expect(await axeViolations(root)).toEqual([]);
  });
});
