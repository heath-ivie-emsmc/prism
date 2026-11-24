import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { ContextBadgeComponent } from './context-badge.component';

function render(inputs: Record<string, unknown>): HTMLElement {
  const fixture = TestBed.createComponent(ContextBadgeComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('ContextBadgeComponent', () => {
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

  it('passes axe', async () => {
    expect(await axeViolations(render({ serviceLine: 'air', appliesTo: 'air' }))).toEqual([]);
  });
});
