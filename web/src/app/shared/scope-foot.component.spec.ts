import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { ScopeFootComponent } from './scope-foot.component';

function create(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(ScopeFootComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('ScopeFootComponent', () => {
  it('shows the Data Through stamp first and the Context Badge second, and passes axe', async () => {
    const root = create({ dataThrough: '2026-11-22', serviceLine: 'air' });
    const children = [...(root.querySelector('.foot')?.children ?? [])].map((c) => c.tagName);
    expect(children).toEqual(['APP-FRESHNESS-STAMP', 'APP-CONTEXT-BADGE']);
    expect(root.textContent).toContain('Data through Nov 22');
    expect(root.textContent).toContain('Air · All bases');
    expect(await axeViolations(root)).toEqual([]);
  });

  it('says that a selected Division was applied, using the Label', () => {
    const root = create({ dataThrough: '2026-11-22', serviceLine: 'air', divisionApplied: true, labels: { reportingDivision: 'Station', allReportingDivisions: 'All stations' } });
    expect(root.textContent).toContain('Selected station');
  });

  it('renders nothing without a Data Through date', () => {
    expect(create({ serviceLine: 'air' }).querySelector('.foot')).toBeNull();
  });
});
