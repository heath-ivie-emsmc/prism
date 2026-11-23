import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../axe';
import { ContextBadgeComponent } from './context-badge.component';
import { FreshnessStampComponent } from './freshness-stamp.component';
import { MetricHelpComponent } from './metric-help.component';

@Component({
  imports: [ContextBadgeComponent, FreshnessStampComponent, MetricHelpComponent],
  template: `
    <app-freshness-stamp dataThrough="2026-11-22" />
    <app-context-badge serviceLine="air" />
    <app-metric-help metric="payer_mix" />
  `,
})
class HostComponent {}

describe('freshness stamp, context badge and metric help', () => {
  it('shows visible text and passes axe, with the help disclosure working', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Data through');
    expect(root.textContent).toContain('Updates once a day');
    expect(root.textContent).toContain('Air · All bases');

    const button = root.querySelector('button')!;
    expect(button.getAttribute('aria-expanded')).toBe('false');
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(root.querySelector(`#${button.getAttribute('aria-controls')}`)?.textContent).toContain('Primary payer');
    expect(await axeViolations(root)).toEqual([]);
  });

  it('renders no stamp without a date', () => {
    const fixture = TestBed.createComponent(FreshnessStampComponent);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('');
  });
});
