import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { MeDto } from '../auth/me.dto';
import { DIVISIONS, flushDivisions } from '../shared/division.testing';
import { DashboardFilterContext } from './dashboard-filter-context';
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
  const entry = (key: string, name: string, divisionSupport: 'supported' | 'none') => ({ key, name, description: 'd', unit: 'x', category: 'cash', status: 'available', placeable: true, claimLevel: false, serviceLines: ['air', 'ground', 'both'], dimensions: [], presentations: [], options: [], context: { divisionSupport } });
  const CATALOG_METRICS = [
    entry('net_collections', 'Net cash collected', 'none'),
    entry('net_cash_projection', 'Projected net cash', 'none'),
    entry('transport_volume', 'Transports and loaded miles', 'supported'),
    entry('clean_claim_rate', 'Clean claim rate', 'none'),
  ];
  const PROVIDER = { providers: [{ id: '526', name: 'Seminole County Fire Department', facilityCount: 0 }] };

  const VOLUME = {
    metric: { key: 'transport_volume', name: 'Transports and loaded miles', description: 'd', unit: 'transports' },
    providerId: '526',
    endPeriod: '202610',
    months: 6,
    serviceLine: 'air',
    periodOpen: false,
    points: [
      { period: '202609', label: 'Sep', year: 2026, transports: 146, loadedMiles: 19_000 },
      { period: '202610', label: 'Oct', year: 2026, transports: 140, loadedMiles: 18_420 },
    ],
    groups: [
      { key: 'rotor-wing', name: 'Rotor wing', serviceLine: 'air', transports: 92, priorTransports: 95, changeTransports: -3, changePercent: -3.2 },
      { key: 'fixed-wing', name: 'Fixed wing', serviceLine: 'air', transports: 48, priorTransports: 0, changeTransports: 48, changePercent: null },
    ],
  };

  const EMPTY_PROJECTION = {
    metric: { key: 'net_cash_projection', name: 'Projected net cash', description: 'd', unit: 'USD' },
    providerId: '526', afterPeriod: '202610', serviceLine: 'air', snapshotDate: null, total: 0, low: 0, high: 0, months: [],
    horizon: { value: 0, low: 0, high: 0, months: 0, fromExistingTrips: null, fromFutureTrips: null, existingTripsShare: null, futureTripsShare: null }, latestActual: null,
  };

  const CLEAN = {
    metric: { key: 'clean_claim_rate', name: 'Clean claim rate', description: 'd', unit: 'percent' },
    providerId: '526',
    endPeriod: '202610',
    months: 6,
    snapshotDate: '2026-04-07',
    points: [
      { period: '202609', label: 'Sep', year: 2026, cleanClaims: 962, rejectedClaims: 38, totalClaims: 1000, rate: 0.962 },
      { period: '202610', label: 'Oct', year: 2026, cleanClaims: 945, rejectedClaims: 55, totalClaims: 1000, rate: 0.945 },
    ],
  };

  /** What the transports route answers in a test: the sample, a body the test chose, or a 503. Reset for every test. */
  let volumeAnswer: { body?: object; fails?: boolean; hold?: boolean; clean?: object; cleanFails?: boolean } = {};
  beforeEach(() => (volumeAnswer = {}));

  /** Answers any transports request the page has made. The two tiles share one request, and most tests are about something else. */
  function answerVolume(): void {
    // The page reads the Metric Catalog for what each card does with the Reporting Division.
    for (const request of TestBed.inject(HttpTestingController).match((r) => r.url === '/api/v1/providers/526/metrics/catalog')) {
      request.flush({ categories: [], metrics: CATALOG_METRICS });
    }
    // The projection panel asks for the forecast as soon as the period is known. Most tests are about something else, so it gets an empty forecast.
    for (const request of TestBed.inject(HttpTestingController).match((r) => r.url === `${BASE}/projection`)) {
      request.flush({ ...EMPTY_PROJECTION });
    }
    if (volumeAnswer.hold) return;
    for (const request of TestBed.inject(HttpTestingController).match((r) => r.url === `${BASE}/transports`)) {
      if (volumeAnswer.fails) request.flush({ title: 'x' }, { status: 503, statusText: 'Service Unavailable' });
      else request.flush(volumeAnswer.body ?? VOLUME);
    }
    for (const request of TestBed.inject(HttpTestingController).match((r) => r.url === `${BASE}/clean-claims`)) {
      if (volumeAnswer.cleanFails) request.flush({ title: 'x' }, { status: 503, statusText: 'Service Unavailable' });
      else request.flush(volumeAnswer.clean ?? CLEAN);
    }
  }

  /** Runs the effects and change detection until the page has reacted to what the test just flushed (the default period is set by an effect). */
  async function settle(fixture: ComponentFixture<DashboardComponent>): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await Promise.resolve();
      fixture.detectChanges();
      TestBed.tick();
      answerVolume();
    }
    await Promise.resolve();
    fixture.detectChanges();
  }

  /** The Provider's periods arrive first, then the series for the newest one. */
  async function createWithData(series: object = SERIES) {
    const result = await create(PROVIDER);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/periods`).flush(PERIODS);
    flushDivisions(http);
    await settle(result.fixture);
    http.expectOne((r) => r.url === BASE && r.params.get('endPeriod') === '202610').flush(series);
    await result.fixture.whenStable();
    await settle(result.fixture);
    return { ...result, http };
  }

  it('shows the Provider name and Data Through from the newest accounting period', async () => {
    const { root } = await createWithData();

    expect(root.querySelector('h1')?.textContent).toBe('Seminole County Fire Department');
    expect(root.querySelector('app-freshness-stamp')?.textContent).toContain('Data through');
    expect(root.querySelector('app-freshness-stamp')?.textContent).toContain('Updates once a day');
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

  it('asks for net cash again for the new Service Line when the toggle changes, so the headline and chart change with it', async () => {
    const { fixture, root, http } = await createWithData();
    http.match(() => true);

    radios(root).find((r) => r.textContent?.includes('Ground only'))!.click();
    fixture.detectChanges();

    const request = http.expectOne((r) => r.url === BASE && r.params.get('serviceLine') === 'ground');
    expect(request.request.params.get('endPeriod')).toBe('202610');
    request.flush(SERIES);
  });

  it('asks for net cash for the chosen division and shows the note about cash that belongs to none, as text', async () => {
    const { fixture, root, http } = await createWithData();
    http.match(() => true);
    TestBed.inject(DashboardFilterContext).division.setValue('summit');
    await settle(fixture);

    const request = http.expectOne((r) => r.url === BASE && r.params.get('division') === 'summit');
    request.flush({ ...SERIES, division: 'summit', divisionName: 'Summit Base', divisionNote: 'Cash with no division belongs to no division. <b>x</b>' });
    await settle(fixture);

    const note = root.querySelector('#net-cash-division-note')!;
    expect(note.textContent).toContain('belongs to no division. <b>x</b>');
    expect(note.querySelector('b')).toBeNull();
  });

  it('does not ask again, and sends no division, for the cards whose Metric has no division (projection, clean claim rate), and says all bases', async () => {
    const { fixture, root, http } = await createWithData();
    http.match(() => true);
    TestBed.inject(DashboardFilterContext).division.setValue('summit');
    await settle(fixture);

    const asked = http.match(() => true).filter((r) => !r.cancelled);
    expect(asked.some((r) => r.request.url.endsWith('/projection') || r.request.url.endsWith('/clean-claims'))).toBe(false);
    expect(asked.filter((r) => r.request.url.endsWith('/projection') || r.request.url.endsWith('/clean-claims')).map((r) => r.request.params.get('division'))).toEqual([]);
    for (const r of asked) r.flush(r.request.url.endsWith('/divisions') ? DIVISIONS : { ...SERIES, division: 'summit' });
    await settle(fixture);
    expect(root.querySelector('app-projection-panel app-context-badge')?.textContent).toContain('All bases');
  });

  it('shows no division note when all divisions are chosen', async () => {
    const { root } = await createWithData({ ...SERIES, divisionNote: 'should not show' });
    expect(root.querySelector('#net-cash-division-note')).toBeNull();
  });

  it('asks for net cash for the Service Line the page starts on', async () => {
    const result = await create(PROVIDER);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/periods`).flush(PERIODS);
    flushDivisions(http);
    await settle(result.fixture);

    http.expectOne((r) => r.url === BASE).flush(SERIES);
    http.match(() => true);
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

    // On Air only for a Provider that also has ground, the line is named as what narrowed the answer.
    expect(root.querySelector('app-net-cash-panel [data-testid="panel-empty"]')?.textContent).toBe('No net cash collected under Air only in October 2026. Try Air + ground.');
  });

  it('names the chosen division and the line when they are why net cash is empty, and the plain words when nothing narrows it', async () => {
    const { root, fixture, http } = await createWithData({ ...SERIES, points: [] });
    const filters = TestBed.inject(DashboardFilterContext);
    filters.division.setValue('summit');
    filters.serviceLine.set('both');
    await settle(fixture);
    http.match(() => true).forEach((r) => !r.cancelled && r.flush(r.request.url.endsWith('/divisions') ? DIVISIONS : { ...SERIES, points: [] }));
    await settle(fixture);

    expect(root.querySelector('app-net-cash-panel [data-testid="panel-empty"]')?.textContent).toBe('No net cash collected for Summit Base in October 2026. Try All reporting divisions.');
  });

  it('shows a calm error with a way to retry when net cash cannot be loaded', async () => {
    const result = await create(PROVIDER);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/periods`).flush(PERIODS);
    flushDivisions(http);
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
    flushDivisions(http);
    answerVolume();
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
    flushDivisions(http);
  });

  it('shows grey placeholders, not "no data" or blanks, while the months are still loading', async () => {
    const { root } = await create(PROVIDER);

    expect(root.querySelectorAll('app-kpi-card app-skeleton').length).toBeGreaterThan(8);
    expect([...root.querySelectorAll('app-kpi-card')].slice(0, 3).every((c) => c.querySelector('[role="status"]')?.textContent?.startsWith('Loading'))).toBe(true);
    expect(root.querySelector('app-net-cash-panel app-skeleton')).not.toBeNull();
    expect(root.querySelector('app-net-cash-panel')!.textContent).not.toContain('No net cash data');
    TestBed.inject(HttpTestingController).match(() => true);
  });

  it('shows real transports and loaded miles: the latest month, the change from the month before and the split of the last month', async () => {
    const { root } = await createWithData();

    const [transports, miles] = [...root.querySelectorAll('app-kpi-card')].slice(0, 2).map((c) => c.textContent!.replace(/\s+/g, ' '));
    expect(transports).toContain('140');
    expect(transports).toContain('6 fewer than Sep');
    expect(transports).toContain('Rotor wing 92 · Fixed wing 48');
    expect(miles).toContain('18,420');
    expect(miles).toContain('statute mi');
    expect(miles).toContain('3% fewer than Sep');
    expect(root.querySelectorAll('.bars__bar')).toHaveLength(6);
    expect(root.querySelector('.bars')!.getAttribute('aria-label')).toContain('Oct 140');
  });

  it('asks for six months of transports ending at the chosen period, for the Service Line on screen', async () => {
    volumeAnswer = { hold: true };
    const result = await create(PROVIDER);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${BASE}/periods`).flush(PERIODS);
    flushDivisions(http);
    await settle(result.fixture);

    const request = http.expectOne((r) => r.url === `${BASE}/transports`);
    expect(request.request.params.get('endPeriod')).toBe('202610');
    expect(request.request.params.get('months')).toBe('6');
    expect(request.request.params.get('serviceLine')).toBe('air');
    request.flush(VOLUME);
    http.match(() => true).forEach((r) => r.flush(SERIES));
  });

  it('shows loaded miles in nautical miles when asked', async () => {
    const { root, fixture } = await createWithData();

    [...root.querySelectorAll<HTMLButtonElement>('app-kpi-card [role="radio"]')].find((r) => r.textContent?.includes('Nautical'))!.click();
    await settle(fixture);

    const miles = root.querySelectorAll('app-kpi-card')[1]!.textContent!.replace(/\s+/g, ' ');
    expect(miles).toContain('16,007');
    expect(miles).toContain('nautical mi');
  });

  it('warns on both tiles that an open month is not final', async () => {
    volumeAnswer = { body: { ...VOLUME, periodOpen: true } };
    const { root } = await createWithData();

    expect([...root.querySelectorAll('app-kpi-card')].slice(0, 2).every((c) => c.textContent!.includes('still open, so counts may rise'))).toBe(true);
  });

  it('says the tiles cannot load, with a retry that asks again, when transports fail', async () => {
    volumeAnswer = { fails: true };
    const { root, fixture, http } = await createWithData();

    const text = root.querySelector('app-kpi-card')!.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain("We can't load this right now");
    expect(text).toContain('—');
    volumeAnswer = {};
    [...root.querySelectorAll<HTMLButtonElement>('app-kpi-card button')].find((b) => b.textContent?.includes('Try again'))!.click();
    await settle(fixture);
    expect(http.match(() => false)).toEqual([]);
    expect(root.querySelector('app-kpi-card')!.textContent).toContain('140');
  });

  it('shows the real clean claim rate, the change in points from the month before and the goal', async () => {
    const { root } = await createWithData();

    const text = root.querySelectorAll('app-kpi-card')[2]!.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('94.5%');
    expect(text).toContain('1.7 pts lower than Sep');
    expect(text).toContain('Goal 95%');
    expect(root.querySelector('.goal')!.getAttribute('aria-label')).toBe('94.5% for October 2026, goal 95 percent');
    expect(root.querySelector<HTMLElement>('.goal__fill')!.style.width).toBe('94.5%');
  });

  it('says which month the clean claim rate is for when the data is behind the month on screen', async () => {
    volumeAnswer = { clean: { ...CLEAN, points: [{ period: '202603', label: 'Mar', year: 2026, cleanClaims: 969, rejectedClaims: 31, totalClaims: 1000, rate: 0.969 }] } };
    const { root } = await createWithData();

    const text = root.querySelectorAll('app-kpi-card')[2]!.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('96.9%');
    expect(text).toContain('Latest in the data: March 2026.');
  });

  it('says there is no clean claim data, with a dash, when the warehouse has none', async () => {
    volumeAnswer = { clean: { ...CLEAN, points: [] } };
    const { root } = await createWithData();

    const text = root.querySelectorAll('app-kpi-card')[2]!.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('—');
    expect(text).toContain('No clean claim data for October 2026 yet.');
    expect(root.querySelector('.goal')).toBeNull();
  });

  it('notes that clean claims are not split by Service Line when one is chosen, and retries when the load fails', async () => {
    volumeAnswer = { cleanFails: true };
    const { root, fixture } = await createWithData();

    const text = () => root.querySelectorAll('app-kpi-card')[2]!.textContent!.replace(/\s+/g, ' ');
    expect(text()).toContain("We can't load this right now");
    expect(text()).toContain('Covers all service lines');
    volumeAnswer = {};
    [...root.querySelectorAll<HTMLButtonElement>('app-kpi-card button')].filter((b) => b.textContent?.includes('Try again')).at(-1)!.click();
    await settle(fixture);
    expect(text()).toContain('94.5%');
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

  it('shows no call type or pickup controls, since no Metric on this panel serves them', async () => {
    const { root } = await createWithData();

    expect(root.querySelector('app-net-cash-panel [aria-label="Call type"]')).toBeNull();
    expect(root.querySelector('app-net-cash-panel ems-select')).toBeNull();
    expect(root.textContent).not.toContain('Not available for this data yet');
  });

  it('explains the net cash panel and each backed tile with a help disclosure, and the stamp and badge sit in the same footer', async () => {
    const { root } = await createWithData();

    const toggles = [...root.querySelectorAll<HTMLButtonElement>('app-metric-help .help__toggle')].map((b) => b.textContent?.replace(/\s+/g, ' ').trim());
    expect(toggles).toEqual(
      expect.arrayContaining(['What these numbers mean for Net cash collected', 'What these numbers mean for Transports', 'What these numbers mean for Loaded miles', 'What these numbers mean for Clean claim rate']),
    );
    const feet = [...root.querySelectorAll('app-data-frame app-scope-foot .foot')];
    expect(feet.length).toBeGreaterThanOrEqual(4);
    for (const foot of feet) expect(foot.firstElementChild?.tagName).toBe('APP-FRESHNESS-STAMP');
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
