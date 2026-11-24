import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { NetCashProjectionDto } from '../metrics/net-cash-projection/net-cash-projection.dto';
import { BASE, bootstrap } from '../saved-views/saved-views.testing';
import { ProjectionPanelComponent } from './projection-panel.component';

const URL = `${BASE}/metrics/net-collections/projection`;
const METRIC = { key: 'net_cash_projection', name: 'Projected net cash', description: 'd', unit: 'USD' };

/** The parts (130 and 190) add up to more than the total (150), as they can in the warehouse. */
function projection(months: number): NetCashProjectionDto {
  const all = [
    { period: '202611', label: 'Nov', year: 2026, value: 150, low: 130, high: 190, changeFromPrevious: { amount: 50, percent: 50 }, cumulative: { value: 150, low: 130, high: 190 }, fromExistingTrips: 130, fromFutureTrips: 190, existingTripsShare: 0.8667, futureTripsShare: 1.2667 },
    { period: '202612', label: 'Dec', year: 2026, value: 100, low: 90, high: 120, changeFromPrevious: { amount: -50, percent: -33.3 }, cumulative: { value: 250, low: 220, high: 310 }, fromExistingTrips: 60, fromFutureTrips: 90, existingTripsShare: 0.6, futureTripsShare: 0.9 },
    { period: '202701', label: 'Jan', year: 2027, value: 100, low: 90, high: 120, changeFromPrevious: null, cumulative: { value: 350, low: 310, high: 430 }, fromExistingTrips: null, fromFutureTrips: null, existingTripsShare: null, futureTripsShare: null },
  ].slice(0, months);
  return {
    metric: METRIC, providerId: '526', afterPeriod: '202610', serviceLine: 'air', snapshotDate: '2026-11-05', dataThrough: '2026-11-22',
    total: all.at(-1)!.cumulative.value, low: all.at(-1)!.cumulative.low, high: all.at(-1)!.cumulative.high, months: all,
    horizon: { value: all.at(-1)!.cumulative.value, low: all.at(-1)!.cumulative.low, high: all.at(-1)!.cumulative.high, months, fromExistingTrips: 130, fromFutureTrips: 190, existingTripsShare: 0.8667, futureTripsShare: 1.2667 },
    latestActual: { period: '202610', value: 100 },
  };
}

async function create() {
  const http = bootstrap();
  const fixture: ComponentFixture<ProjectionPanelComponent> = TestBed.createComponent(ProjectionPanelComponent);
  fixture.componentRef.setInput('providerId', '526');
  fixture.componentRef.setInput('endPeriod', '202610');
  fixture.componentRef.setInput('serviceLine', 'air');
  fixture.componentRef.setInput('dataThrough', '2026-11-22');
  const answer = async (months: number) => {
    for (let i = 0; i < 4; i += 1) {
      await Promise.resolve();
      fixture.detectChanges();
      TestBed.tick();
      for (const request of http.match((r) => r.url === URL)) request.flush(projection(months));
    }
    await fixture.whenStable();
    fixture.detectChanges();
  };
  await answer(3);
  return { fixture, http, root: fixture.nativeElement as HTMLElement, answer };
}

const radios = (root: HTMLElement) => [...root.querySelectorAll<HTMLButtonElement>('[aria-label="Months to project"] [role="radio"]')];

describe('ProjectionPanelComponent', () => {
  it('asks for three months by default and offers a 1, 2 or 3 month choice', async () => {
    const http = bootstrap();
    const fixture = TestBed.createComponent(ProjectionPanelComponent);
    fixture.componentRef.setInput('providerId', '526');
    fixture.componentRef.setInput('endPeriod', '202610');
    fixture.detectChanges();
    TestBed.tick();
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();

    const request = http.expectOne((r) => r.url === URL);
    expect(request.request.params.get('horizon')).toBe('3');
    expect(request.request.params.get('endPeriod')).toBe('202610');
    request.flush(projection(3));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(radios(fixture.nativeElement).map((r) => [r.textContent?.trim(), r.getAttribute('aria-checked')])).toEqual([['1 month', 'false'], ['2 months', 'false'], ['3 months', 'true']]);
  });

  it('asks again for the chosen horizon', async () => {
    const { root, fixture, http, answer } = await create();

    radios(root)[0]!.click();
    fixture.detectChanges();
    TestBed.tick();
    const request = http.expectOne((r) => r.url === URL);
    expect(request.request.params.get('horizon')).toBe('1');
    request.flush(projection(1));
    await answer(1);

    expect(root.querySelectorAll('thead th')).toHaveLength(2);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Showing the next month.');
  });

  it('labels the two parts with the database team wording and shows each share as its own figure, including one above 100%', async () => {
    const { root } = await create();
    const text = root.textContent!.replace(/\s+/g, ' ');

    expect(text).toContain('Expected collections on trips that already existed');
    expect(text).toContain('Assumed run rate of trips that do not exist yet');
    expect(text).not.toMatch(/already billed/i);
    expect(text).toContain('86.7% of the expected net cash');
    expect(text).toContain('126.7% of the expected net cash');
    expect(root.querySelector('[role="img"]')).toBeNull();
  });

  it('shows the footnote that the parts may not add up, and that the range is not a statistical interval', async () => {
    const { root } = await create();
    const text = root.textContent!.replace(/\s+/g, ' ');

    expect(text).toContain('The two parts are shown as reported by the warehouse and may not add up to the expected total.');
    expect(text).toContain('The range is across the forecast components, not a statistical interval.');
  });

  it('shows each month change with its base, the first against the latest actual month, n/a for a missing change and component', async () => {
    const { root } = await create();
    const rows = [...root.querySelectorAll('tbody tr')].map((r) => r.textContent!.replace(/\s+/g, ' ').trim());

    expect(rows.find((r) => r.startsWith('Change from the month before'))).toContain('+$50.00 (+50.0%)');
    expect(rows.find((r) => r.startsWith('Change from the month before'))).toContain('-$50.00 (-33.3%)');
    expect(rows.find((r) => r.startsWith('Change from the month before'))).toContain('n/a');
    expect(rows.find((r) => r.startsWith('Cumulative expected'))).toContain('$350.00');
    expect(root.textContent).toContain('actual');
  });

  it('says there is no forecast when none reaches the months after the period', async () => {
    const http = bootstrap();
    const fixture = TestBed.createComponent(ProjectionPanelComponent);
    fixture.componentRef.setInput('providerId', '526');
    fixture.componentRef.setInput('endPeriod', '202610');
    fixture.detectChanges();
    TestBed.tick();
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne((r) => r.url === URL).flush({ ...projection(1), months: [] });
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('There is no forecast for the months after this period.');
  });

  it('passes axe', async () => {
    const { root } = await create();

    expect(await axeViolations(root)).toEqual([]);
  });
});

