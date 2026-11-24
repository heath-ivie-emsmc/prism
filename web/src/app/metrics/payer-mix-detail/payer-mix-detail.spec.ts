import { flushDivisions } from '../../shared/division.testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../axe';
import { AuthService } from '../../auth/auth.service';
import { PayerMixDetailComponent } from './payer-mix-detail.component';
import { PayerMixDetailDto } from './payer-mix-detail.dto';
import { toPayerDetailMix } from './payer-mix-detail';

const BASE = '/api/v1/providers/526/metrics/net-collections';
const DETAIL: PayerMixDetailDto = {
  metric: { key: 'payer_mix_detail', name: 'Payers in a payer group', description: 'd', unit: 'transports' },
  providerId: '526',
  group: 'rotor-wing',
  groupName: 'Rotor wing',
  payerGroup: { key: 'commercial', name: 'Commercial' },
  period: '202610',
  priorPeriod: '202609',
  dataThrough: '2026-11-22',
  payers: [
    { name: 'Meridian Health Plan', current: 12, prior: 10 },
    { name: 'Northstar Health', current: 8, prior: 10 },
    { name: 'Other', current: 2, prior: 2 },
  ],
};

describe('toPayerDetailMix', () => {
  it('turns payers into shares with their own colors and no drill', () => {
    const figures = toPayerDetailMix(DETAIL);
    expect(figures.total).toBe(22);
    expect(figures.shares.map((s) => s.key)).toEqual(['payer-0', 'payer-1', 'payer-2']);
    expect(new Set(figures.shares.map((s) => s.fill)).size).toBe(3);
    expect(figures.shares.every((s) => !s.drill)).toBe(true);
  });

  it('is empty without a response', () => {
    expect(toPayerDetailMix(null).shares).toEqual([]);
  });
});

function choose(root: HTMLElement, name: string): void {
  [...root.querySelectorAll<HTMLElement>('app-segmented-control [role="radio"]')].find((r) => r.getAttribute('aria-label') === name || r.textContent?.includes(name))!.click();
}

describe('PayerMixDetailComponent', () => {
  async function open(url = 'rotor-wing/commercial?period=202610&line=air', detail: object = DETAIL): Promise<{ root: HTMLElement; detect: () => void; http: HttpTestingController; params: URLSearchParams }> {
    const [path, query = ''] = url.split('?');
    const [group, payerKey] = path!.split('/');
    TestBed.configureTestingModule({
      providers: [
        provideEmsUi(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ group, payerKey })), queryParamMap: of(convertToParamMap(Object.fromEntries(new URLSearchParams(query)))) } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush({ userName: 'j', displayName: 'J', providers: [{ id: '526', name: 'Seminole County Fire Department', facilityCount: 0 }], permissions: [] });
    const fixture = TestBed.createComponent(PayerMixDetailComponent);
    const detect = (): void => {
      TestBed.tick();
      fixture.detectChanges();
    };
    detect();
    // The page and the shared filter context (behind the Reporting Division control) each ask for the periods.
    for (const periods of http.match((r) => r.url === `${BASE}/periods`)) {
      periods.flush({ providerId: '526', periods: [{ period: '202610', label: 'Oct 2026' }, { period: '202609', label: 'Sep 2026' }], dataThrough: '2026-11-22' });
    }
    flushDivisions(http);
    await Promise.resolve();
    detect();
    // The page also reads the Metric Catalog for the Division badge.
    for (const catalog of http.match((r) => r.url.endsWith('/metrics/catalog'))) catalog.flush({ categories: [], metrics: [] });
    const request = http.expectOne((r) => r.url === `${BASE}/payer-mix/${payerKey}`);
    request.flush(detail);
    await fixture.whenStable();
    detect();
    return { root: fixture.nativeElement as HTMLElement, detect, http, params: new URLSearchParams(request.request.params.toString()) };
  }

  it('shows the payer group heading, breadcrumb, stamp, badge, help and a way back', async () => {
    const { root } = await open();

    expect(root.querySelector('h1')!.textContent).toContain('Commercial payers, rotor wing');
    const crumbs = root.querySelector('ems-breadcrumbs')!.textContent!;
    expect(crumbs).toContain('net cash');
    expect(crumbs).toContain('Rotor wing payer mix');
    expect(crumbs).toContain('Commercial');
    expect(root.querySelector('app-freshness-stamp')!.textContent).toContain('Data through');
    expect(root.querySelector('app-context-badge')!.textContent).toContain('Air');
    expect(root.querySelector('app-metric-help')!.textContent).toContain('ten largest');
    const back = root.querySelector<HTMLAnchorElement>('.page__back a')!;
    expect(back.textContent).toContain('Back to Rotor wing payer mix');
    expect(back.getAttribute('href')).toContain('/dashboard/net-cash/rotor-wing');
    expect(root.querySelector('app-chart [role="img"]')!.getAttribute('aria-label')).toContain('Meridian Health Plan');
  });

  it('moves focus to the heading when the page opens', async () => {
    const { root } = await open();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(document.activeElement).toBe(root.querySelector('h1'));
  });

  it('offers the same numbers as a table', async () => {
    const { root, detect } = await open();
    choose(root, 'Table');
    TestBed.tick();
    detect();

    const rows = [...root.querySelectorAll('tbody tr')].map((r) => r.textContent!.replace(/\s+/g, ' ').trim());
    expect(rows[0]).toContain('Meridian Health Plan');
    expect(rows[0]).toContain('55%');
    expect(rows).toHaveLength(3);
  });

  // jsdom cannot draw a canvas, so axe runs on the views that are plain markup. The bar chart is covered by the Playwright accessibility spec.
  it.each(['Table', 'Donut charts'])('passes axe in the %s view', async (view) => {
    const { root, detect } = await open();
    choose(root, view);
    TestBed.tick();
    detect();
    expect(await axeViolations(root)).toEqual([]);
  });

  it('says so when there are no transports', async () => {
    const { root } = await open(undefined, { ...DETAIL, payers: [] });
    expect(root.textContent).toContain('There are no commercial rotor wing transports for October 2026.');
  });

  it('sends the Division from the address, and drops a malformed one', async () => {
    const good = await open('rotor-wing/commercial?period=202610&line=air&division=north_base');
    expect(good.params.get('division')).toBe('north_base');
  });

  it('drops a malformed Division instead of sending it', async () => {
    const bad = await open('rotor-wing/commercial?period=202610&line=air&division=a%20b%26c');
    expect(bad.params.has('division')).toBe(false);
  });
});
