import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { MetricHelpComponent } from './metric-help.component';

function create(metric: string, inputs: Record<string, unknown> = {}) {
  const fixture = TestBed.createComponent(MetricHelpComponent);
  fixture.componentRef.setInput('metric', metric);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

describe('MetricHelpComponent', () => {
  it('shows the summary and opens the glossary with a real button, and passes axe', async () => {
    const { fixture, root } = create('payment_age');
    expect(root.textContent).toContain('How long payers take to pay');
    const button = root.querySelector('button')!;
    expect(button.getAttribute('aria-expanded')).toBe('false');
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect([...root.querySelectorAll('dt')].map((dt) => dt.textContent)).toContain('Payment age');
    expect(await axeViolations(root)).toEqual([]);
  });

  it('closes on Escape and keeps focus on the toggle; Escape on a closed panel does nothing', () => {
    const { fixture, root } = create('payment_age');
    document.body.appendChild(root);
    const button = root.querySelector('button')!;
    button.focus();
    button.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(button.getAttribute('aria-expanded')).toBe('false');
    button.click();
    fixture.detectChanges();
    expect(root.querySelector('.help__panel')).not.toBeNull();
    button.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(root.querySelector('.help__panel')).toBeNull();
    expect(document.activeElement).toBe(button);
    root.remove();
  });

  it('uses the Service Line default Label for the Reporting Division, never a hard-coded word', () => {
    expect(create('net_cash_breakdown', { serviceLine: 'air' }).root.textContent).toContain('then by base location and vehicle');
    expect(create('net_cash_breakdown').root.textContent).toContain('then by reporting division and vehicle');
  });

  it('uses the Provider’s own Label when the page has one', () => {
    const { root } = create('net_cash_breakdown', { labels: { reportingDivision: 'Station', allReportingDivisions: 'All stations' } });
    expect(root.textContent).toContain('then by station and vehicle');
    expect(root.textContent).not.toMatch(/\{[A-Za-z]+\}/);
  });

  it('in compact mode hides the summary until the disclosure opens, names the figure for screen readers, and passes axe', async () => {
    const { fixture, root } = create('payment_age', { compact: true, heading: 'Payment age' });
    expect(root.querySelector('.help__summary')).toBeNull();
    const button = root.querySelector('button')!;
    expect(button.textContent?.replace(/\s+/g, ' ').trim()).toBe('What these numbers mean for Payment age');
    button.click();
    fixture.detectChanges();
    expect(root.querySelector('.help__panel .help__summary')?.textContent).toContain('How long payers take to pay');
    expect(root.querySelectorAll('dt').length).toBeGreaterThan(2);
    expect(await axeViolations(root)).toEqual([]);
  });

  it('shows nothing for a Metric with no help', () => {
    expect(create('nope').root.querySelector('button')).toBeNull();
  });
});
