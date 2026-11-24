import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { axeViolations } from '../axe';
import { ContextBadgeComponent } from './context-badge.component';

const DIVISIONS = [
  { key: 'summit', name: 'Summit Base' },
  { key: 'x-img', name: '<img src=x onerror=alert(1)> Base' },
];

function withContext(chosen: string | undefined): void {
  TestBed.configureTestingModule({ providers: [{ provide: DashboardFilterContext, useValue: { divisionKey: () => chosen, divisions: () => DIVISIONS, divisionName: (key: string) => DIVISIONS.find((d) => d.key === key)?.name } }] });
}

function render(inputs: Record<string, unknown>): HTMLElement {
  const fixture = TestBed.createComponent(ContextBadgeComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('ContextBadgeComponent', () => {
  beforeEach(() => withContext(undefined));

  it('reads as before when the Metric covers both lines', () => {
    expect(render({ serviceLine: 'air' }).textContent).toBe('Air · All bases');
    expect(render({ serviceLine: 'all', appliesTo: 'both' }).textContent).toBe('All service lines · All reporting divisions');
  });

  it('shows Air only as visible text beside the badge, and names the line in effect, whatever the page filter says', () => {
    const root = render({ serviceLine: 'all', appliesTo: 'air' });

    expect(root.textContent).toBe('Air · All bases · Air only');
    expect(root.querySelector('.badge__scope')?.textContent?.trim()).toBe('· Air only');
  });

  it('shows Ground only for a ground-only Metric', () => {
    expect(render({ serviceLine: 'ground', appliesTo: 'ground' }).textContent).toBe('Ground · All reporting divisions · Ground only');
  });

  it('can name Air and ground, for the palette', () => {
    expect(render({ serviceLine: 'all', appliesTo: 'both', showBoth: true }).textContent).toContain('Air and ground');
  });

  it('names the chosen division from the loaded list instead of the generic wording', () => {
    withContext('summit');
    expect(render({ serviceLine: 'air', divisionApplied: true }).textContent).toBe('Air · Summit Base');
  });

  it('names the division from the address key when the page passes one', () => {
    withContext(undefined);
    expect(render({ serviceLine: 'air', divisionApplied: true, divisionKey: 'summit' }).textContent).toBe('Air · Summit Base');
  });

  it('keeps the Label wording for all divisions, and when the key is not in the list', () => {
    withContext('summit');
    expect(render({ serviceLine: 'air' }).textContent).toBe('Air · All bases');
    expect(render({ serviceLine: 'air', divisionApplied: true, divisionKey: 'gone' }).textContent).toBe('Air · Selected base location');
  });

  it('shows a hostile division name as text, never as markup', () => {
    withContext('x-img');
    const root = render({ serviceLine: 'air', divisionApplied: true });
    expect(root.textContent).toBe('Air · <img src=x onerror=alert(1)> Base');
    expect(root.querySelector('img')).toBeNull();
  });

  it('passes axe', async () => {
    expect(await axeViolations(render({ serviceLine: 'air', appliesTo: 'air' }))).toEqual([]);
  });
});
