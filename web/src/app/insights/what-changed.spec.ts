import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { WhatChangedDto } from './insights.service';
import { basisLine, formatDataThrough, plainMoney, signedMoney, toCardView, toCardViews } from './what-changed';
import { WhatChangedComponent } from './what-changed.component';

const URL = '/api/v1/providers/526/insights/what-changed';

const DTO: WhatChangedDto = {
  providerId: '526',
  period: '202610',
  priorPeriod: '202609',
  serviceLine: 'air',
  group: 'fixed-wing',
  groupName: 'Fixed wing',
  dataThrough: '2026-10-31',
  headline: 'Fixed wing cash fell $137,770.00 (22%) from September. Most of it is timing, not a lasting change.',
  cashNow: 487_230,
  cashPrior: 625_000,
  delta: -137_770,
  deltaRatio: -0.22,
  normalizedDeltaRatio: -0.0977,
  drivers: [
    {
      kind: 'aged-payments',
      label: 'temporary',
      title: 'September had unusually high payments on older transports',
      body: 'Payments on older transports in September ran $85,000.00 above the usual level, across 14 transports.',
      impact: -85_000,
    },
  ],
  expected: [
    {
      kind: 'payer-mix',
      label: 'watch',
      title: 'More fixed wing transports were Medicaid',
      body: "Medicaid's share of fixed wing transports rose from 18% to 23%.",
      impact: -38_400,
    },
  ],
  remainder: { amount: -14_370, judgment: 'withinNormalRange', text: 'The remaining $14,370.00 is normal month-to-month variation.' },
};

describe('what changed display', () => {
  it('writes signed dollars with a real minus sign, and zero with none', () => {
    expect(signedMoney(-85_000)).toBe('−$85,000.00');
    expect(signedMoney(1234.5)).toBe('+$1,234.50');
    expect(signedMoney(0)).toBe('$0.00');
    expect(signedMoney(0.001)).toBe('$0.00');
    expect(plainMoney(-38_400)).toBe('$38,400.00');
  });

  it('puts every label in words, so its meaning never rests on color', () => {
    const labels = (['temporary', 'structural', 'watch', 'info'] as const).map((label) => toCardView({ ...DTO.drivers[0]!, label }, false, 0).labelText);
    expect(labels).toEqual(['Temporary', 'Lasting', 'Watch', 'For information']);
  });

  it('says whether an amount is for this month or expected, in text for a screen reader too', () => {
    const driver = toCardView(DTO.drivers[0]!, false, 0);
    const expected = toCardView(DTO.expected[0]!, true, 0);
    expect(driver.amountSpoken).toBe('−$85,000.00 this month');
    expect(expected.amountSpoken).toBe('−$38,400.00 expected per month');
  });

  it('lists the drivers first and then the expected effects, with keys that cannot clash', () => {
    const views = toCardViews(DTO);
    expect(views.map((v) => v.kind)).toEqual(['aged-payments', 'payer-mix']);
    expect(new Set(views.map((v) => v.key)).size).toBe(2);
    expect(toCardViews(null)).toEqual([]);
  });

  it('formats Data Through as a date, and says nothing for anything else', () => {
    expect(formatDataThrough('2026-10-31')).toBe('10/31/2026');
    expect(formatDataThrough(null)).toBe('');
    expect(formatDataThrough('<b>2026</b>')).toBe('');
    expect(basisLine(DTO)).toBe('Based on payments, payer mix and claim data through 10/31/2026.');
    expect(basisLine({ ...DTO, dataThrough: null })).toBe('Based on payments, payer mix and claim data.');
  });
});

describe('WhatChangedComponent', () => {
  function create(providerId: string | null = '526'): { fixture: ComponentFixture<WhatChangedComponent>; root: HTMLElement; http: HttpTestingController } {
    TestBed.configureTestingModule({ providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(WhatChangedComponent);
    fixture.componentRef.setInput('providerId', providerId);
    fixture.componentRef.setInput('period', '202610');
    fixture.componentRef.setInput('line', 'air');
    fixture.componentRef.setInput('group', 'fixed-wing');
    fixture.componentRef.setInput('groupName', 'Fixed wing');
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, http: TestBed.inject(HttpTestingController) };
  }

  const text = (root: HTMLElement) => root.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  it('asks for the chosen month, Service Line and group, and shows a loading status meanwhile', () => {
    const { root, http } = create();

    const request = http.expectOne((r) => r.url === URL);
    expect(request.request.params.get('period')).toBe('202610');
    expect(request.request.params.get('serviceLine')).toBe('air');
    expect(request.request.params.get('group')).toBe('fixed-wing');
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Loading what changed');
    expect(text(root)).toContain('What changed in fixed wing');
  });

  it('shows the headline, a card for each finding with its label in words, the remainder and what the figures are based on', async () => {
    const { fixture, root, http } = create();
    http.expectOne((r) => r.url === URL).flush(DTO);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(root)).toContain('Fixed wing cash fell $137,770.00 (22%) from September.');
    const cards = [...root.querySelectorAll('.card')];
    expect(cards).toHaveLength(2);
    expect(cards[0]?.textContent).toContain('September had unusually high payments on older transports');
    expect(cards[0]?.textContent).toContain('Temporary');
    expect(cards[0]?.textContent).toContain('−$85,000.00');
    expect(cards[1]?.textContent).toContain('Watch');
    expect(cards[1]?.textContent).toContain('expected per month');
    expect(text(root)).toContain('The remaining $14,370.00 is normal month-to-month variation.');
    expect(text(root)).toContain('through 10/31/2026');
  });

  it('says there is nothing to explain when the API has nothing', async () => {
    const { fixture, root, http } = create();
    http.expectOne((r) => r.url === URL).flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(root)).toContain('There is no net cash for fixed wing in this month or the month before it to explain.');
    expect(root.querySelector('.card')).toBeNull();
  });

  it('shows a calm problem with a retry when it cannot load, and never the server text', async () => {
    const { fixture, root, http } = create();
    http.expectOne((r) => r.url === URL).flush({ title: 'Host=secret.internal Password=hunter2' }, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('app-load-problem')).not.toBeNull();
    expect(text(root)).toContain('Try again');
    expect(text(root)).not.toContain('hunter2');
  });

  it('asks for nothing until it knows the Provider', () => {
    const { http } = create(null);

    http.expectNone((r) => r.url.includes('/insights/'));
  });

  it('has no axe violations with its findings showing', async () => {
    const { fixture, root, http } = create();
    http.expectOne((r) => r.url === URL).flush(DTO);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(await axeViolations(root)).toEqual([]);
  });
});
