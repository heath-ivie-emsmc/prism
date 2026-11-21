import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { MeDto } from '../auth/me.dto';
import { DashboardComponent } from './dashboard.component';

describe('DashboardComponent header', () => {
  async function create(me: Partial<MeDto> = {}): Promise<{ fixture: ComponentFixture<DashboardComponent>; root: HTMLElement }> {
    TestBed.configureTestingModule({ providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.inject(AuthService).refreshMe().subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({ userName: 'jdoe', displayName: 'Jane Doe', providers: [], permissions: [], ...me });
    const fixture = TestBed.createComponent(DashboardComponent);
    // With a Provider the page asks the API for its periods, which the test answers itself, so it cannot wait for the page to settle first.
    if (me.providers?.length) {
      fixture.detectChanges();
    } else {
      await fixture.whenStable();
      fixture.detectChanges();
    }
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  const radios = (root: HTMLElement) => [...root.querySelectorAll<HTMLButtonElement>('app-service-line-toggle [role="radio"]')];

  const BASE = '/api/v1/providers/526/metrics/net-collections';
  const PERIODS = { providerId: '526', periods: [{ period: '202610', label: 'Oct 2026' }, { period: '202609', label: 'Sep 2026' }] };
  const SERIES = {
    metric: { key: 'net_collections', name: 'Net cash collected', description: 'd', unit: 'USD' },
    providerId: '526',
    endPeriod: '202610',
    months: 6,
    snapshotDate: '2026-11-05',
    points: [
      { period: '202509', label: 'Sep', year: 2025, value: 1_250_460 },
      { period: '202510', label: 'Oct', year: 2025, value: 1_100_240 },
    ],
  };
  const PROVIDER = { providers: [{ id: '526', name: 'Seminole County Fire Department', facilityCount: 0 }] };

  /** Runs the effects and change detection until the page has reacted to what the test just flushed (the default period is set by an effect). */
  async function settle(fixture: ComponentFixture<DashboardComponent>): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await Promise.resolve();
      fixture.detectChanges();
      TestBed.tick();
    }
  }

  /** The Provider's periods arrive first, then the series for the newest one. */
  async function createWithData(series: object = SERIES) {
    const result = await create(PROVIDER);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/periods`).flush(PERIODS);
    await settle(result.fixture);
    http.expectOne((r) => r.url === BASE && r.params.get('endPeriod') === '202610').flush(series);
    await result.fixture.whenStable();
    result.fixture.detectChanges();
    return { ...result, http };
  }

  it('shows the Provider name and Data Through from the newest accounting period', async () => {
    const { root } = await createWithData();

    expect(root.querySelector('h1')?.textContent).toBe('Seminole County Fire Department');
    expect(root.querySelector('.dashboard__subtitle')?.textContent).toContain('Data through 10/31/2026');
  });

  it('fills the Period drop-down from the warehouse periods and starts on the newest', async () => {
    const { fixture } = await createWithData();

    const control = (fixture.componentInstance as unknown as { period: { value: string } }).period;
    expect(control.value).toBe('202610');
    const shown = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input.ems-select__input')].map((i) => i.value);
    expect(shown).toContain('October 2026');
  });

  it('asks for six months by default and twelve when Last 12 months is chosen', async () => {
    const { fixture, root, http } = await createWithData();
    const six = http.match((r) => r.url === BASE);
    expect(six).toHaveLength(0);

    [...root.querySelectorAll<HTMLButtonElement>('app-net-cash-panel [role="radio"]')].find((r) => r.textContent?.includes('Last 12'))!.click();
    fixture.detectChanges();

    http.expectOne((r) => r.url === BASE && r.params.get('months') === '12' && r.params.get('endPeriod') === '202610').flush(SERIES);
  });

  it('does not ask for anything when the user has no Provider', async () => {
    const { root } = await create();

    expect(root.querySelector('h1')?.textContent).toBe('Acme Air Care');
    TestBed.inject(HttpTestingController).expectNone((r) => r.url.includes('/providers/'));
  });

  it('shows the Provider name exactly as the session carries it', async () => {
    const { root } = await create({ providers: [{ id: '100', name: '100', facilityCount: 0 }] });
    TestBed.inject(HttpTestingController).match(() => true).forEach((r) => r.flush({ providerId: '100', periods: [] }));

    expect(root.querySelector('h1')?.textContent).toBe('100');
  });

  it('shows the latest month, how it moved and both amounts', async () => {
    const { root } = await createWithData();
    const panel = root.querySelector('app-net-cash-panel')!.textContent!.replace(/\s+/g, ' ');

    expect(panel).toContain('$1.10M');
    expect(panel).toContain('-12% vs Sep');
    expect(panel).toContain('$1,100,240.00 in October · $1,250,460.00 in September');
  });

  it('says so when a Provider has no net cash data', async () => {
    const { root } = await createWithData({ ...SERIES, points: [] });

    expect(root.querySelector('app-net-cash-panel')?.textContent).toContain('No net cash data for this period');
  });

  it('shows a calm error with a way to retry when net cash cannot be loaded', async () => {
    const result = await create(PROVIDER);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/periods`).flush(PERIODS);
    await settle(result.fixture);
    http.expectOne((r) => r.url === BASE).flush({ title: 'The data warehouse is not available. Try again shortly.' }, { status: 503, statusText: 'Service Unavailable' });
    await result.fixture.whenStable();
    result.fixture.detectChanges();

    const text = result.root.querySelector('app-net-cash-panel app-load-problem')!.textContent!;
    expect(text).toContain("We can't load net cash right now");
    expect(text).not.toContain('warehouse');
    [...result.root.querySelectorAll<HTMLButtonElement>('app-net-cash-panel button')].find((b) => b.textContent?.includes('Try again'))!.click();
    await settle(result.fixture);
    http.expectOne((r) => r.url === BASE).flush(SERIES);
  });

  it('says net cash cannot load, not that there is none, when the list of periods fails, and retries the periods', async () => {
    const result = await create(PROVIDER);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/periods`).flush(
      { title: 'The data warehouse is not available. Try again shortly.', traceId: '00-77114806214001843f6761e5254e96e3-cfc570a3d3f659d3-03' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await result.fixture.whenStable();
    result.fixture.detectChanges();

    const text = result.root.querySelector('app-net-cash-panel app-load-problem')!.textContent!;
    expect(text).toContain("We can't load net cash right now");
    expect(text).toContain('77114806214001843f6761e5254e96e3');
    expect(text).not.toContain('warehouse');
    expect(result.root.querySelector('app-net-cash-panel')!.textContent).not.toContain('No net cash data');
    [...result.root.querySelectorAll<HTMLButtonElement>('app-net-cash-panel button')].find((b) => b.textContent?.includes('Try again'))!.click();
    await settle(result.fixture);
    http.expectOne(`${BASE}/periods`).flush(PERIODS);
  });

  it('lays out the net cash panel, four metric tiles and the projection panel', async () => {
    const { root } = await createWithData();

    expect(root.querySelector('app-net-cash-panel h2')?.textContent).toContain('Net cash collected');
    const tiles = [...root.querySelectorAll('app-kpi-card')].map((tile) => tile.textContent?.replace(/\s+/g, ' ').trim() ?? '');
    expect(tiles).toHaveLength(4);
    expect(tiles[0]).toContain('Transports');
    expect(tiles[1]).toContain('Loaded miles');
    expect(tiles[2]).toContain('Clean claim rate');
    expect(tiles[3]).toContain('Open negotiation');
    expect(root.querySelector('app-projection-panel h2')?.textContent).toContain('projection');
  });

  it('switches the net cash panel between a chart and a table with the chart type control', async () => {
    const { fixture, root } = await createWithData();
    const radio = (name: string) => root.querySelector<HTMLButtonElement>(`app-net-cash-panel [role="radio"][aria-label="${name}"]`)!;
    expect(root.querySelector('app-net-cash-panel app-chart')).not.toBeNull();
    expect(root.querySelector('app-net-cash-panel table')).toBeNull();

    radio('Table').click();
    fixture.detectChanges();

    expect(root.querySelector('app-net-cash-panel app-chart')).toBeNull();
    const rows = [...root.querySelectorAll('app-net-cash-panel tbody tr')].map((r) => r.textContent?.replace(/\s+/g, ' ').trim());
    expect(rows).toHaveLength(2);
    expect(rows[1]).toContain('Oct 2025');
    expect(rows[1]).toContain('$1,100,240.00');
    expect(rows[1]).toContain('12.0%');

    radio('Bar chart').click();
    fixture.detectChanges();
    expect(root.querySelector('app-net-cash-panel table')).toBeNull();
  });

  it('shows the call type and pickup filters as unavailable because the data has neither', async () => {
    const { root } = await createWithData();

    const callType = [...root.querySelectorAll<HTMLButtonElement>('app-net-cash-panel [aria-label="Call type"] [role="radio"]')];
    expect(callType.length).toBeGreaterThan(0);
    expect(callType.every((b) => b.disabled)).toBe(true);
  });

  it('starts on Air only with the air label for the Reporting Division filter', async () => {
    const { root } = await create();

    expect(radios(root).map((r) => [r.textContent?.trim(), r.getAttribute('aria-checked')])).toEqual([
      ['flight Air only', 'true'],
      ['Ground only', 'false'],
      ['Air + ground', 'false'],
    ]);
    expect(root.textContent).toContain('Base location');
    expect(root.textContent).toContain('Period');
  });

  it('renames the Reporting Division filter when the Service Line changes', async () => {
    const { fixture, root } = await create();

    radios(root)[1].click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(radios(root)[1].getAttribute('aria-checked')).toBe('true');
    expect(root.textContent).toContain('Reporting division');
    expect(root.textContent).not.toContain('Base location');
  });

  it('moves the Service Line with the arrow keys and keeps only the chosen option in the tab order', async () => {
    const { fixture, root } = await create();
    const press = (key: string) => radios(root).find((r) => r.tabIndex === 0)!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));

    press('ArrowRight');
    fixture.detectChanges();

    expect(radios(root).map((r) => r.getAttribute('aria-checked'))).toEqual(['false', 'true', 'false']);
    expect(radios(root).map((r) => r.tabIndex)).toEqual([-1, 0, -1]);

    press('ArrowLeft');
    fixture.detectChanges();
    press('ArrowLeft');
    fixture.detectChanges();
    expect(radios(root).map((r) => r.getAttribute('aria-checked'))).toEqual(['false', 'false', 'true']);
  });
});
