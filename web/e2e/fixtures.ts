import { test as base, expect, type Page } from '@playwright/test';

/** What `GET /api/v1/me` returns for a signed-in user with one Provider. Used only when no real session is supplied. */
const MOCK_ME = {
  userName: 'jdoe',
  displayName: 'Jane Doe',
  providers: [{ id: '526', name: 'Seminole County Fire Department', facilityCount: 3 }],
  permissions: ['prism.app', 'prism.dashboard.view', 'prism.claims.view', 'prism.views.manage'],
};

/** The Data Through date every mocked Metric answers with, unless a test overrides the route. */
export const DATA_THROUGH = '2026-11-22';

const MONTH_NAMES = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** Two years of months ending October 2026. The last six match the mockup: $1.18M ... $1.10M. */
const MONTHS = Array.from({ length: 24 }, (_, i) => {
  const index = 2024 * 12 + 10 + i; // Nov 2024 .. Oct 2026
  const year = Math.floor(index / 12);
  const month = (index % 12) + 1;
  const fixed: Record<string, number> = { '202605': 1_180_000, '202606': 1_220_000, '202607': 1_200_000, '202608': 1_240_000, '202609': 1_250_460, '202610': 1_100_240 };
  const period = `${year}${String(month).padStart(2, '0')}`;
  return { period, label: MONTH_NAMES[month - 1]!, year, value: fixed[period] ?? 900_000 + ((i * 37_000) % 250_000) };
});

type Figures = { cash: number; transports: number; loadedMiles: number; primaryMix: number; secondaryMix: number };
const fig = (cash: number, transports: number, loadedMiles: number, primaryMix: number, secondaryMix: number): Figures => ({ cash, transports, loadedMiles, primaryMix, secondaryMix });

/** A little movement from one period to the next, so changing the Period visibly changes the page. October is the base. */
function breakdownFor(line: 'air' | 'ground', period: string) {
  const month = Number(period.slice(4, 6)) || 10;
  const factor = period === '202610' ? 1 : 1 + (((month * 7) % 10) - 5) / 100;
  const scaled = (f: Figures): Figures => ({ ...f, cash: Math.round(f.cash * factor) });
  const air = line === 'air';
  const groups = air
    ? [
        { key: 'rotor-wing', name: 'Rotor wing', current: fig(607_860, 92, 6_940, 72, 20), prior: fig(620_310, 95, 7_100, 74, 21) },
        { key: 'fixed-wing', name: 'Fixed wing', current: fig(492_380, 48, 11_480, 3, 45), prior: fig(630_150, 51, 12_000, 3, 48) },
      ]
    : [
        { key: 'als', name: 'Advanced life support', current: fig(410_200, 310, 5_120, 198, 112), prior: fig(402_900, 298, 5_000, 190, 108) },
        { key: 'bls', name: 'Basic life support', current: fig(288_400, 402, 7_830, 165, 237), prior: fig(301_100, 415, 8_100, 170, 245) },
        { key: 'sct', name: 'Specialty care', current: fig(96_750, 38, 3_960, 3, 35), prior: fig(118_300, 47, 4_200, 4, 43) },
      ];
  const rows = air
    ? [
        ['N611CV', 0, 'North', fig(224_700, 34, 2_580, 26, 8), fig(229_300, 35, 2_600, 27, 8)],
        ['N612CV', 0, 'East', fig(205_300, 31, 2_310, 24, 7), fig(209_500, 32, 2_400, 25, 7)],
        ['N418CV', 1, 'Sky Harbor', fig(271_580, 26, 6_200, 1, 25), fig(339_480, 27, 6_300, 1, 26)],
        ['N227CV', 1, 'Regional Airport', fig(220_800, 22, 5_280, 2, 20), fig(290_670, 24, 5_700, 2, 22)],
      ]
    : [
        ['M-12', 0, 'Station 12', fig(198_600, 148, 2_480, 95, 53), fig(190_100, 142, 2_400, 90, 52)],
        ['M-31', 0, 'Station 31', fig(211_600, 162, 2_640, 103, 59), fig(212_800, 156, 2_600, 100, 56)],
        ['A-07', 1, 'Station 12', fig(144_300, 201, 3_900, 82, 119), fig(151_900, 210, 4_000, 86, 124)],
        ['C-02', 2, 'Station 31', fig(96_750, 38, 3_960, 3, 35), fig(118_300, 47, 4_200, 4, 43)],
      ];
  return {
    // Each group card leads to that group's payer mix: the API sends the drill link, and the page draws no link without one.
    groups: groups.map((g) => ({
      ...g,
      serviceLine: line,
      current: scaled(g.current),
      links: [{ rel: 'drill', href: `/api/v1/providers/526/metrics/net-collections/payer-mix?group=${g.key}&period=${period}&serviceLine=${line}`, title: `${g.name} payer mix` }],
    })),
    rows: rows.map(([vehicle, group, base, current, prior]) => ({
      groupKey: groups[group as number]!.key,
      groupName: groups[group as number]!.name,
      serviceLine: line,
      base,
      vehicle,
      current: scaled(current as Figures),
      prior,
    })),
  };
}

/** Payer counts for a group: 100 transports in Sep (prior) and 100 in Oct (current), with Medicaid rising from 18% to 23%, as in the mockup. */
function payerMixFor(providerId: string, group: string | null, period: string, line: string): { key: string; name: string; prior: number; current: number; links?: { rel: string; href: string; title: string }[] }[] {
  const rows: [string, string, number, number][] = [
    ['commercial', 'Commercial', 30, 26],
    ['medicare', 'Medicare', 40, 38],
    ['medicaid', 'Medicaid', 18, 23],
    ['other-gov', 'Other government', 6, 6],
    ['va', 'VA', 3, 4],
    ['facility', 'Facility', 2, 1],
    ['patient', 'Patient responsibility', 1, 2],
  ];
  // Patient responsibility and Facility have no breakdown, so the API sends no link for them.
  const noDrill = new Set(['facility', 'patient']);
  return rows.map(([key, name, prior, current]) => ({
    key,
    name,
    prior,
    current,
    ...(noDrill.has(key) ? {} : { links: [{ rel: 'drill', href: `/api/v1/providers/${providerId}/metrics/net-collections/payer-mix/${key}?group=${group ?? ''}&period=${period}&serviceLine=${line}&division=`, title: `${name} payers` }] }),
  }));
}

/** Six months of transports and loaded miles ending at the asked-for period: the last two are 146 then 140, 19,000 then 18,420 miles, as in the mockup. */
function transportsFor(end: string, line: 'all' | 'air' | 'ground') {
  const endIndex = Number(end.slice(0, 4)) * 12 + Number(end.slice(4, 6)) - 1;
  const counts = [112, 131, 128, 150, 146, 140];
  const miles = [15_200, 17_900, 17_100, 19_800, 19_000, 18_420];
  const points = counts.map((transports, i) => {
    const index = endIndex - (counts.length - 1 - i);
    const year = Math.floor(index / 12);
    const month = (index % 12) + 1;
    return { period: `${year}${String(month).padStart(2, '0')}`, label: MONTH_NAMES[month - 1]!, year, transports, loadedMiles: miles[i]! };
  });
  // Fixed wing had none the month before, so its percent change is null.
  const air = [
    { key: 'rotor-wing', name: 'Rotor wing', serviceLine: 'air', transports: 92, priorTransports: 95, changeTransports: -3, changePercent: -3.2 },
    { key: 'fixed-wing', name: 'Fixed wing', serviceLine: 'air', transports: 48, priorTransports: 0, changeTransports: 48, changePercent: null },
  ];
  const ground = [{ key: 'als', name: 'Advanced life support', serviceLine: 'ground', transports: 140, priorTransports: 146, changeTransports: -6, changePercent: -4.1 }];
  return { points, groups: line === 'ground' ? ground : air };
}

/** The clean claim rate for the months up to the asked-for period: 96.2% then 94.5%, so the tile shows a drop of 1.7 points, as in the mockup. */
function cleanClaimsFor(end: string) {
  const index = Number(end.slice(0, 4)) * 12 + Number(end.slice(4, 6)) - 1;
  const rates = [0.981, 0.975, 0.97, 0.968, 0.962, 0.945];
  return rates.map((rate, i) => {
    const at = index - (rates.length - 1 - i);
    const year = Math.floor(at / 12);
    const month = (at % 12) + 1;
    const total = 1000;
    return { period: `${year}${String(month).padStart(2, '0')}`, label: MONTH_NAMES[month - 1]!, year, cleanClaims: Math.round(total * rate), rejectedClaims: total - Math.round(total * rate), totalClaims: total, rate };
  });
}


/**
 * The projection as the API answers it. The two components do NOT add up to the total: in November 1,000,000 + 600,000 against 1,210,000,
 * so the shares are 82.6% and 49.6% and in December they pass 100% together, as the real warehouse data can.
 */
export function projectionFor(horizon: number, noActual = false) {
  const base = [
    { period: '202611', label: 'Nov', year: 2026, value: 1_210_000, low: 1_150_000, high: 1_280_000, existing: 1_000_000, future: 600_000 },
    { period: '202612', label: 'Dec', year: 2026, value: 1_200_000, low: 1_120_000, high: 1_290_000, existing: 700_000, future: 650_000 },
    { period: '202701', label: 'Jan', year: 2027, value: 1_200_000, low: 1_080_000, high: 1_320_000, existing: null, future: 700_000 },
  ].slice(0, horizon);
  const actual = 1_100_240;
  let running = 0;
  let runningLow = 0;
  let runningHigh = 0;
  const share = (part: number | null, total: number) => (part === null ? null : Math.round((part / total) * 10_000) / 10_000);
  const months = base.map((m, i) => {
    running += m.value;
    runningLow += m.low;
    runningHigh += m.high;
    const previous = i === 0 ? (noActual ? null : actual) : base[i - 1]!.value;
    return {
      period: m.period, label: m.label, year: m.year, value: m.value, low: m.low, high: m.high,
      changeFromPrevious: previous === null ? null : { amount: m.value - previous, percent: Math.round(((m.value - previous) / previous) * 1000) / 10 },
      cumulative: { value: running, low: runningLow, high: runningHigh },
      fromExistingTrips: m.existing, fromFutureTrips: m.future, existingTripsShare: share(m.existing, m.value), futureTripsShare: share(m.future, m.value),
    };
  });
  const sum = (pick: (m: (typeof base)[number]) => number | null) => (base.every((m) => pick(m) === null) ? null : base.reduce((t, m) => t + (pick(m) ?? 0), 0));
  const existing = sum((m) => m.existing);
  const future = sum((m) => m.future);
  return {
    metric: { key: 'net_cash_projection', name: 'Projected net cash', description: 'Forecast cash.', unit: 'USD' },
    providerId: '526',
    afterPeriod: '202610',
    serviceLine: noActual ? 'ground' : 'air',
    snapshotDate: '2026-11-05',
    dataThrough: DATA_THROUGH,
    total: running,
    low: runningLow,
    high: runningHigh,
    months,
    horizon: { value: running, low: runningLow, high: runningHigh, months: months.length, fromExistingTrips: existing, fromFutureTrips: future, existingTripsShare: existing === null ? null : share(existing, running), futureTripsShare: future === null ? null : share(future, running) },
    latestActual: noActual ? null : { period: '202610', value: actual },
  };
}


/** Warehouse text that holds markup. It must be drawn as literal text on every page. */
export const HOSTILE_NAME = '<img src=x onerror=alert(1)>';

const MONTH_LABELS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
/** The `months` months ending at `end` (yyyyMM), oldest first. */
function monthsEnding(end: string, months: number): { period: string; label: string; year: number }[] {
  const endIndex = Number(end.slice(0, 4)) * 12 + Number(end.slice(4, 6)) - 1;
  return Array.from({ length: months }, (_, i) => {
    const index = endIndex - (months - 1 - i);
    const year = Math.floor(index / 12);
    const month = (index % 12) + 1;
    return { period: `${year}${String(month).padStart(2, '0')}`, label: MONTH_LABELS[month - 1]!, year };
  });
}

const envelopeFor = (key: string, name: string, url: URL, extra: Record<string, unknown> = {}) => {
  const period = url.searchParams.get('period') ?? '202610';
  const line = url.searchParams.get('serviceLine') === 'ground' ? 'ground' : url.searchParams.get('serviceLine') === 'all' ? 'all' : 'air';
  return {
    metric: { key, name, description: 'd', unit: 'transports' }, providerId: '526', period, priorPeriod: String(Number(period) - 1), serviceLine: line, group: null,
    division: url.searchParams.get('division') || null, divisionName: null, dataThrough: DATA_THROUGH, ...extra,
  };
};

/** Transports, miles and posted cash for one row, with the month before. */
function figureOf(transports: number, prior: number, miles: number, cash: number) {
  const pct = (now: number, before: number) => (before === 0 ? null : Math.round(((now - before) / before) * 1000) / 10);
  const priorMiles = Math.round(miles * 0.96);
  const priorCash = Math.round(cash * 1.05);
  const perTransport = (c: number, t: number) => (t === 0 ? null : Math.round((c / t) * 100) / 100);
  return {
    transports, priorTransports: prior, changeTransports: transports - prior, changeTransportsPercent: pct(transports, prior),
    loadedMiles: miles, priorLoadedMiles: priorMiles, changeLoadedMiles: miles - priorMiles, changeLoadedMilesPercent: pct(miles, priorMiles),
    netCash: cash, priorNetCash: priorCash, changeNetCash: cash - priorCash, changeNetCashPercent: pct(cash, priorCash),
    netCashPerTransport: perTransport(cash, transports), priorNetCashPerTransport: perTransport(priorCash, prior),
  };
}

const share = (n: number, total: number) => (total === 0 ? null : Math.round((n / total) * 10_000) / 10_000);
const payerShares = (transports: number, prior: number) => [
  { key: 'commercial', name: 'Commercial', transports: Math.round(transports * 0.4), priorTransports: Math.round(prior * 0.45) },
  { key: 'medicare', name: 'Medicare', transports: Math.round(transports * 0.4), priorTransports: Math.round(prior * 0.35) },
  { key: 'medicaid', name: 'Medicaid', transports: transports - Math.round(transports * 0.8), priorTransports: prior - Math.round(prior * 0.8) },
].map((p) => ({ ...p, share: share(p.transports, transports), priorShare: share(p.priorTransports, prior) }));

/** 18 tail numbers, one of them hostile, and an Other row: wide and long enough to stress the table and the chart. */
const TAILS = ['N611CV', 'N612CV', 'N418CV', 'N227CV', HOSTILE_NAME, 'N300LF', 'N301LF', 'N302LF', 'N410MB', 'N411MB', 'N512AA', 'N513AA', 'N620EM', 'N621EM', 'N730SH', 'N731SH', 'N840RW', 'N841RW'];
const tailRows = () => TAILS.map((tail, i) => ({ tail, isOther: false, transports: 40 - i * 2, prior: 42 - i * 2, miles: 5200 - i * 230, cash: 210_000 - i * 9_000 }));

function callTypesFor(url: URL) {
  const months = Number(url.searchParams.get('months') ?? '6');
  const base = [
    { key: 'scene', name: 'Scene', transports: 62, prior: 55, miles: 4120.5, cash: 310_400 },
    { key: 'interfacility', name: 'Interfacility', transports: 71, prior: 80, miles: 9420, cash: 402_900 },
    { key: 'emergency', name: 'Emergency', transports: 38, prior: 31, miles: 2210, cash: 150_250 },
    { key: 'non-emergency', name: 'Non-emergency', transports: 12, prior: 14, miles: 880, cash: 41_900 },
    { key: 'not-recorded', name: 'Not recorded', transports: 3, prior: 2, miles: 120, cash: 5_100 },
  ];
  const total = base.reduce((t, c) => t + c.transports, 0);
  const priorTotal = base.reduce((t, c) => t + c.prior, 0);
  const trendMonths = monthsEnding(envelopeFor('x', 'x', url).period, months);
  return {
    months, totalTransports: total, priorTotalTransports: priorTotal,
    callTypes: base.map((c) => ({
      key: c.key, name: c.name, transports: c.transports, priorTransports: c.prior, changeTransports: c.transports - c.prior,
      share: share(c.transports, total), priorShare: share(c.prior, priorTotal), changePoints: Math.round((share(c.transports, total)! - share(c.prior, priorTotal)!) * 1000) / 10,
      loadedMiles: c.miles, priorLoadedMiles: Math.round(c.miles * 0.9), netCash: c.cash, priorNetCash: Math.round(c.cash * 0.95), payerMix: payerShares(c.transports, c.prior),
    })),
    trend: trendMonths.map((m, i) => {
      const types = base.map((c) => ({ key: c.key, name: c.name, transports: Math.max(0, c.transports - (months - 1 - i) * 2 + (i % 3)), share: null as number | null }));
      const sum = types.reduce((t, c) => t + c.transports, 0);
      return { ...m, transports: sum, callTypes: types.map((c) => ({ ...c, share: share(c.transports, sum) })) };
    }),
  };
}

function pickupsFor() {
  const base = [
    { key: 'scene', name: 'Scene', facilityBased: false, transports: 62, prior: 55 },
    { key: 'residence', name: 'Residence', facilityBased: false, transports: 24, prior: 20 },
    { key: 'hospital', name: 'Hospital', facilityBased: true, transports: 70, prior: 75 },
    { key: 'skilled-nursing', name: 'Skilled nursing facility', facilityBased: true, transports: 18, prior: 22 },
    { key: 'dialysis', name: 'Dialysis facility', facilityBased: true, transports: 6, prior: 4 },
    { key: 'other', name: 'Other', facilityBased: false, transports: 4, prior: 5 },
  ];
  const total = base.reduce((t, c) => t + c.transports, 0);
  const priorTotal = base.reduce((t, c) => t + c.prior, 0);
  return {
    totalTransports: total, priorTotalTransports: priorTotal,
    sources: base.map((c) => ({
      key: c.key, name: c.name, facilityBased: c.facilityBased, transports: c.transports, priorTransports: c.prior, changeTransports: c.transports - c.prior,
      share: share(c.transports, total), priorShare: share(c.prior, priorTotal), changePoints: Math.round((share(c.transports, total)! - share(c.prior, priorTotal)!) * 1000) / 10,
      loadedMiles: c.transports * 70, priorLoadedMiles: c.prior * 68, netCash: c.transports * 4_100, priorNetCash: c.prior * 4_000, payerMix: payerShares(c.transports, c.prior),
    })),
    facilities: [
      { sourceKey: 'hospital', name: HOSTILE_NAME, isOther: false, transports: 31, priorTransports: 30, changeTransports: 1, share: share(31, total), loadedMiles: 2_100 },
      { sourceKey: 'hospital', name: 'Regional Medical Center', isOther: false, transports: 22, priorTransports: 25, changeTransports: -3, share: share(22, total), loadedMiles: 1_500 },
      { sourceKey: 'skilled-nursing', name: 'Pine Grove Skilled Nursing and Rehabilitation Center of Seminole County', isOther: false, transports: 12, priorTransports: 14, changeTransports: -2, share: share(12, total), loadedMiles: 800 },
      { sourceKey: null, name: 'Other', isOther: true, transports: 10, priorTransports: 12, changeTransports: -2, share: share(10, total), loadedMiles: 700 },
    ],
  };
}

function effectFor() {
  const groups = [
    { key: 'commercial', name: 'Commercial', transports: 26, priorTransports: 30, cashPerTransport: 900, effect: 14_400 },
    { key: 'medicare', name: 'Medicare', transports: 38, priorTransports: 40, cashPerTransport: 600, effect: 3_800 },
    { key: 'medicaid', name: 'Medicaid', transports: 23, priorTransports: 18, cashPerTransport: 466.67, effect: -19_200 },
    { key: 'va', name: 'VA', transports: 0, priorTransports: 0, cashPerTransport: null, effect: null },
  ];
  return {
    basis: 'expected', transports: 100, priorTransports: 100, cohorts: { firstServiceMonth: '202501', lastServiceMonth: '202512', cashThroughPeriod: '202611' },
    groups: groups.map((g) => ({ ...g, share: share(g.transports, 100), priorShare: share(g.priorTransports, 100), changePoints: g.transports === 0 ? null : g.transports - g.priorTransports })),
    estimatedEffect: -1_000, recurringMonthlyEffect: -1_000,
  };
}

/** Rows of net cash this month and the month before for a contribution Metric, with a change and a share of the total change that add up exactly. The total of the changes is not zero, so shares are real. */
function contributionFor(kind: 'aircraft' | 'division' | 'payer', url: URL) {
  const period = url.searchParams.get('period') ?? '202610';
  const ground = url.searchParams.get('serviceLine') === 'ground';
  const line = ground ? 'ground' : kind !== 'aircraft' && url.searchParams.get('serviceLine') === 'all' ? 'all' : 'air';
  type Source = { key: string | null; name: string; now: number; before: number; isOther?: boolean };
  const sources: Source[] =
    kind === 'aircraft'
      ? [...TAILS.slice(0, 8).map((tail, i) => ({ key: null, name: tail, now: 210_000 - i * 17_000, before: 205_000 - i * 9_000 + (i % 2) * 14_000 })), { key: null, name: 'Other', now: 61_000, before: 77_500, isOther: true }]
      : kind === 'division'
        ? ground
          ? [{ key: 'station-12', name: 'Station 12', now: 198_600, before: 190_100 }, { key: 'station-31', name: HOSTILE_NAME, now: 211_600, before: 223_900 }]
          : [
              { key: 'north', name: 'North base', now: 330_000, before: 297_000 },
              { key: 'sky-harbor', name: HOSTILE_NAME, now: 281_000, before: 318_000 },
              { key: null, name: 'Not recorded', now: 12_000, before: 12_000 },
              { key: null, name: 'Other', now: 20_500, before: 19_000, isOther: true },
            ]
        : [
            { key: 'commercial', name: 'Commercial', now: 412_000, before: 366_000 },
            { key: 'medicare', name: 'Medicare', now: 288_000, before: 301_500 },
            { key: 'medicaid', name: 'Medicaid', now: 96_400, before: 120_900 },
            { key: 'patient', name: 'Patient responsibility', now: 18_200, before: 21_000 },
            { key: null, name: 'Not recorded', now: 4_000, before: 4_000 },
          ];
  const net = (sum: (s: Source) => number) => sources.reduce((t, s) => t + sum(s), 0);
  const netCash = net((s) => s.now);
  const priorNetCash = net((s) => s.before);
  const change = netCash - priorNetCash;
  // The payer drill needs a group in effect, as the API issues it. This fixture has one: Fixed wing.
  const group = kind === 'payer' ? 'fixed-wing' : null;
  const noDrill = new Set(['patient']);
  return {
    serviceLine: line, group,
    total: { netCash, priorNetCash, change, changePercent: priorNetCash === 0 ? null : Math.round((change / priorNetCash) * 1000) / 10 },
    rows: sources.map((s) => ({
      key: s.key, name: s.name, isOther: s.isOther ?? false, netCash: s.now, priorNetCash: s.before, change: s.now - s.before,
      shareOfChange: change === 0 ? null : Math.round(((s.now - s.before) / change) * 10_000) / 10_000,
      links: kind === 'payer' && s.key && !noDrill.has(s.key)
        ? [{ rel: 'drill', href: `/api/v1/providers/526/metrics/net-collections/payer-mix/${s.key}?group=${group}&period=${period}&serviceLine=${line}`, title: `Payer mix detail: ${s.name}` }]
        : [],
    })),
  };
}

/** The what-changed answer for a request, as the mocked API gives it. */
export function whatChangedFor(url: URL) {
  const period = url.searchParams.get('period') ?? '202610';
  const group = url.searchParams.get('group');
  return {
    providerId: '526',
    period,
    priorPeriod: String(Number(period) - 1),
    serviceLine: url.searchParams.get('serviceLine') === 'ground' ? 'ground' : 'air',
    group,
    groupName: group === 'fixed-wing' ? 'Fixed wing' : group === 'als' ? 'ALS' : group,
    dataThrough: '2026-10-31',
    headline: 'Fixed wing cash fell $137,770.00 (22%) from September. Most of it is timing, not a lasting change.',
    cashNow: 487230,
    cashPrior: 625000,
    delta: -137770,
    deltaRatio: -0.2204,
    normalizedDeltaRatio: -0.0977,
    drivers: [
      {
        kind: 'aged-payments',
        label: 'temporary',
        title: 'September had unusually high payments on older transports',
        body: 'Payments on older transports in September ran $85,000.00 above the usual level, across 14 transports. Without that, fixed wing cash fell about 10%, not 22%.',
        impact: -85000,
      },
    ],
    expected: [
      {
        kind: 'payer-mix',
        label: 'watch',
        title: 'More fixed wing transports were Medicaid',
        body: "Medicaid's share of fixed wing transports rose from 18% to 23%. At what transports have earned once paid, the shift in payer mix is expected to make cash about $38,400.00 lower a month as those claims are paid. This is an expected effect, not part of what moved this month's cash.",
        impact: -38400,
      },
    ],
    remainder: { amount: -14370, judgment: 'withinNormalRange', text: 'The remaining $14,370.00 is normal month-to-month variation.' },
  };
}

/** Answers the six Metrics added with the catalog's appliesTo field. The air-only two answer 400 for Ground only, as the API does. */
async function mockMetricSix(page: import('@playwright/test').Page): Promise<void> {
  const route = (path: string, key: string, name: string, build: (url: URL) => Record<string, unknown>, airOnly = false) =>
    page.route(new RegExp(`/api/v1/providers/[^/]+/metrics/net-collections/${path}(\\?.*)?$`), (r) => {
      const url = new URL(r.request().url());
      if (airOnly && url.searchParams.get('serviceLine') === 'ground') {
        return r.fulfill({ status: 400, json: { title: 'This metric covers air only.', status: 400 } });
      }
      return r.fulfill({ json: { ...envelopeFor(key, name, url, airOnly ? { serviceLine: 'air' } : {}), ...build(url) } });
    });

  await route('transport-mix-by-call-type', 'transport_mix_by_call_type', 'Transport mix by call type', callTypesFor);
  await route('transports-by-pickup-location', 'transports_by_pickup_location', 'Transports by pickup location', pickupsFor);
  await route('payer-mix-financial-effect', 'payer_mix_financial_effect', 'Payer mix financial effect', effectFor);
  await route('aircraft-performance', 'aircraft_performance', 'Aircraft performance', () => {
    const rows = tailRows();
    const sum = (pick: (r: (typeof rows)[number]) => number) => rows.reduce((t, r) => t + pick(r), 0);
    return {
      totals: figureOf(sum((r) => r.transports) + 14, sum((r) => r.prior) + 16, sum((r) => r.miles) + 1_900, sum((r) => r.cash) + 60_000),
      aircraft: [...rows.map((r) => ({ tail: r.tail, isOther: false, figures: figureOf(r.transports, r.prior, r.miles, r.cash) })), { tail: 'Other', isOther: true, figures: figureOf(14, 16, 1_900, 60_000) }],
    };
  }, true);
  await route('base-analysis', 'base_analysis', 'Base analysis', (url) => {
    const ground = url.searchParams.get('serviceLine') === 'ground';
    const divisions = ground
      ? [{ key: 'station-12', name: 'Station 12', figures: figureOf(148, 142, 2_480, 198_600), aircraft: [] }, { key: 'station-31', name: 'Station 31', figures: figureOf(162, 156, 2_640, 211_600), aircraft: [] }]
      : [
          { key: 'north', name: 'North base', figures: figureOf(74, 70, 9_200, 330_000), aircraft: [{ tail: 'N611CV', isOther: false, transports: 40 }, { tail: 'N612CV', isOther: false, transports: 34 }] },
          { key: 'sky-harbor', name: 'Sky Harbor base', figures: figureOf(51, 55, 12_800, 281_000), aircraft: [{ tail: HOSTILE_NAME, isOther: false, transports: 30 }, { tail: 'Other', isOther: true, transports: 21 }] },
          { key: null, name: 'Not recorded', figures: figureOf(4, 3, 400, 12_000), aircraft: [] },
        ];
    const sum = (pick: (f: ReturnType<typeof figureOf>) => number) => divisions.reduce((t, d) => t + pick(d.figures), 0);
    return {
      totals: figureOf(sum((f) => f.transports), sum((f) => f.priorTransports), sum((f) => f.loadedMiles), sum((f) => f.netCash)),
      divisions: divisions.map((d) => ({ ...d, isOther: false })),
    };
  });
  // Net cash changes by contributor. Each route's rows add up exactly to its total, Other included, as the API guarantees.
  const contribution = (kind: 'aircraft' | 'division' | 'payer', key: string, name: string, airOnly = false) =>
    route(`cash-contribution/${kind}`, key, name, (url) => contributionFor(kind, url), airOnly);
  await contribution('aircraft', 'cash_contribution_by_aircraft', 'Cash contribution by aircraft', true);
  await contribution('division', 'cash_contribution_by_division', 'Cash contribution by reporting division');
  await contribution('payer', 'cash_contribution_by_payer', 'Cash contribution by payer');
  await route('loaded-miles-by-aircraft', 'loaded_miles_by_aircraft', 'Loaded miles by aircraft', (url) => {
    const months = Number(url.searchParams.get('months') ?? '6');
    const trendMonths = monthsEnding(url.searchParams.get('period') ?? '202610', months);
    const rows = tailRows().slice(0, 12);
    return {
      priorPeriod: undefined, months,
      aircraft: [...rows, { tail: 'Other', isOther: true, transports: 14, prior: 16, miles: 1_900, cash: 0 }].map((r) => ({
        tail: r.tail, isOther: r.isOther, transports: r.transports, loadedMiles: r.miles, averageLoadedMiles: r.transports === 0 ? null : Math.round((r.miles / r.transports) * 10) / 10,
        trend: trendMonths.map((m, i) => ({ ...m, transports: Math.max(1, Math.round(r.transports / months)), loadedMiles: Math.round(r.miles / months) + i * 20, averageLoadedMiles: 120.5 + i })),
      })),
    };
  }, true);
}

/** Answers the net cash routes the way the API does, for the provider the mocked user has. */
async function mockNetCollections(page: import('@playwright/test').Page): Promise<void> {
  await page.route('**/api/v1/providers/*/metrics/net-collections/periods', (route) =>
    route.fulfill({ json: { providerId: '526', dataThrough: DATA_THROUGH, periods: [...MONTHS].reverse().map((m) => ({ period: m.period, label: `${m.label} ${m.year}` })) } }),
  );
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/projection(\?.*)?$/, (route) => {
    const url = new URL(route.request().url());
    const horizon = Math.min(3, Math.max(1, Number(url.searchParams.get('horizon') ?? '3') || 3));
    // Ground has no actual month to compare with, so its first change is null. The other lines compare the first month with October.
    const ground = url.searchParams.get('serviceLine') === 'ground';
    return route.fulfill({ json: projectionFor(horizon, ground) });
  });
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/breakdown(\?.*)?$/, (route) => {
    const url = new URL(route.request().url());
    const line = url.searchParams.get('serviceLine') === 'ground' ? 'ground' : 'air';
    const period = url.searchParams.get('period') ?? '202610';
    return route.fulfill({
      json: {
        metric: { key: 'net_cash_breakdown', name: 'Net cash breakdown', description: 'Cash by aircraft type or level of service.', unit: 'USD' },
        providerId: '526',
        period,
        priorPeriod: String(Number(period) - 1),
        serviceLine: line,
        dataThrough: DATA_THROUGH,
        // September is still open in the mock, the earlier months are closed.
        periodOpen: period >= '202609',
        ...breakdownFor(line, period),
      },
    });
  });
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/transports(\?.*)?$/, (route) => {
    const url = new URL(route.request().url());
    const end = url.searchParams.get('endPeriod') ?? '202610';
    const requested = url.searchParams.get('serviceLine');
    const line = requested === 'ground' || requested === 'air' ? requested : 'all';
    return route.fulfill({
      json: {
        metric: { key: 'transport_volume', name: 'Transports and loaded miles', description: 'd', unit: 'transports' },
        providerId: '526',
        endPeriod: end,
        months: 6,
        serviceLine: line,
        dataThrough: DATA_THROUGH,
        periodOpen: end >= '202609',
        ...transportsFor(end, line),
      },
    });
  });
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/cash-per-transport(\?.*)?$/, (route) =>
    route.fulfill({
      json: {
        metric: { key: 'cash_per_transport_by_payer', name: 'Cash per transport by payer', description: 'd', unit: 'USD' },
        providerId: '526', period: '202611', cohortFrom: '202501', cohortThrough: '202512', serviceLine: 'air', group: null, dataThrough: DATA_THROUGH,
        overall: { transports: 700, cash: 490_000, cashPerTransport: 700 },
        cohorts: { firstServiceMonth: '202501', lastServiceMonth: '202512', cashThroughPeriod: '202611' },
        payers: [
          { key: 'commercial', name: 'Commercial', transports: 300, cash: 270_000, cashPerTransport: 900, differenceFromOverall: 200, percentFromOverall: 28.6 },
          { key: 'medicare', name: 'Medicare', transports: 250, cash: 150_000, cashPerTransport: 600, differenceFromOverall: -100, percentFromOverall: -14.3 },
          { key: 'medicaid', name: 'Medicaid', transports: 150, cash: 70_000, cashPerTransport: 466.67, differenceFromOverall: -233.33, percentFromOverall: -33.3 },
          { key: 'va', name: 'VA', transports: 0, cash: 0, cashPerTransport: null, differenceFromOverall: null, percentFromOverall: null },
        ],
      },
    }),
  );
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/payment-age(\?.*)?$/, (route) => {
    const bands = (shares: number[]) => ['0-1', '2-3', '4-6', '7-12', '13+'].map((key, i) => ({ key, fromMonths: [0, 2, 4, 7, 13][i], toMonths: [1, 3, 6, 12, null][i], share: shares[i] }));
    return route.fulfill({
      json: {
        metric: { key: 'payment_age', name: 'Payment age', description: 'd', unit: 'USD' },
        providerId: '526', period: '202611', serviceLine: 'air', group: null, dataThrough: DATA_THROUGH,
        cells: [
          { payerKey: 'commercial', payerName: 'Commercial', postingPeriod: '202611', ageMonths: 1, cash: 50_000, transports: 20 },
          { payerKey: 'commercial', payerName: 'Commercial', postingPeriod: '202611', ageMonths: 4, cash: 30_000, transports: 12 },
          { payerKey: 'medicare', payerName: 'Medicare', postingPeriod: '202611', ageMonths: 2, cash: 40_000, transports: 25 },
          { payerKey: 'va', payerName: 'VA', postingPeriod: '202611', ageMonths: 0, cash: 0, transports: 0 },
        ],
        // VA has no cash, so no typical age and an empty bands list.
        payers: [
          { payerKey: 'commercial', payerName: 'Commercial', typicalAgeMonths: 1, bands: bands([0.5, 0.2, 0.2, 0.08, 0.02]) },
          { payerKey: 'medicare', payerName: 'Medicare', typicalAgeMonths: 3, bands: bands([0.25, 0.4, 0.2, 0.1, 0.05]) },
          { payerKey: 'va', payerName: 'VA', typicalAgeMonths: null, bands: [] },
        ],
      },
    });
  });
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/clean-claims(\?.*)?$/, (route) => {
    const end = new URL(route.request().url()).searchParams.get('endPeriod') ?? '202610';
    return route.fulfill({
      json: {
        metric: { key: 'clean_claim_rate', name: 'Clean claim rate', description: 'd', unit: 'percent' },
        providerId: '526',
        endPeriod: end,
        months: 6,
        snapshotDate: '2026-04-07',
        dataThrough: DATA_THROUGH,
        points: cleanClaimsFor(end),
      },
    });
  });
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/payer-mix(\?.*)?$/, (route) => {
    const url = new URL(route.request().url());
    const period = url.searchParams.get('period') ?? '202610';
    const group = url.searchParams.get('group');
    return route.fulfill({
      json: {
        metric: { key: 'payer_mix', name: 'Payer mix', description: 'Transports by primary payer group.', unit: 'transports' },
        providerId: '526',
        period,
        priorPeriod: String(Number(period) - 1),
        serviceLine: url.searchParams.get('serviceLine') === 'ground' ? 'ground' : 'air',
        group,
        groupName: group === 'fixed-wing' ? 'Fixed Wing' : group === 'als' ? 'ALS' : group === 'rotor-wing' ? 'Rotor wing' : group,
        dataThrough: DATA_THROUGH,
        shares: payerMixFor('526', group, period, url.searchParams.get('serviceLine') === 'ground' ? 'ground' : 'air'),
      },
    });
  });
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/payer-mix\/[a-z-]+(\?.*)?$/, (route) => {
    const url = new URL(route.request().url());
    const period = url.searchParams.get('period') ?? '202610';
    const group = url.searchParams.get('group') ?? 'rotor-wing';
    const payerKey = /payer-mix\/([a-z-]+)/.exec(url.pathname)![1]!;
    const names: Record<string, string> = { commercial: 'Commercial', medicare: 'Medicare', medicaid: 'Medicaid', 'other-gov': 'Other government', va: 'VA' };
    return route.fulfill({
      json: {
        providerId: '526',
        metric: { key: 'payer_mix_detail', name: 'Payers in a payer group', description: 'The payers inside one payer group.', unit: 'transports' },
        group,
        groupName: group === 'rotor-wing' ? 'Rotor wing' : group === 'fixed-wing' ? 'Fixed wing' : group,
        payerGroup: { key: payerKey, name: names[payerKey] ?? payerKey },
        period,
        priorPeriod: String(Number(period) - 1),
        serviceLine: url.searchParams.get('serviceLine') ?? 'air',
        division: url.searchParams.get('division') || null,
        divisionName: null,
        dataThrough: DATA_THROUGH,
        payers: [
          { name: 'Meridian Health Plan', current: 9, prior: 8 },
          { name: 'Northstar Health', current: 6, prior: 8 },
          { name: 'Coastal Mutual', current: 4, prior: 5 },
          { name: 'Pioneer Assurance', current: 3, prior: 3 },
          { name: 'Other', current: 4, prior: 6 },
        ],
      },
    });
  });
  await page.route(/\/api\/v1\/providers\/[^/]+\/insights\/what-changed(\?.*)?$/, (route) => route.fulfill({ json: whatChangedFor(new URL(route.request().url())) }));
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections(\?.*)?$/, (route) => {
    const url = new URL(route.request().url());
    const end = url.searchParams.get('endPeriod') ?? '202610';
    const months = Number(url.searchParams.get('months') ?? '6');
    const endIndex = MONTHS.findIndex((m) => m.period === end);
    // Air is the page's starting line and keeps the mockup figures. Ground is a fifth of that and both together add up, so changing the Service Line visibly changes the figures.
    const share = { air: 1, ground: 0.2, all: 1.2 }[url.searchParams.get('serviceLine') ?? ''] ?? 1;
    const points = (endIndex < 0 ? [] : MONTHS.slice(Math.max(0, endIndex - months + 1), endIndex + 1)).map((m) => ({ ...m, value: Math.round(m.value * share) }));
    return route.fulfill({
      json: {
        metric: { key: 'net_collections', name: 'Net cash collected', description: 'Cash collected in the accounting period.', unit: 'USD' },
        providerId: '526',
        endPeriod: end,
        months,
        snapshotDate: '2026-11-05',
        dataThrough: DATA_THROUGH,
        points,
      },
    });
  });
}

type Pres = { key: string; name: string; icon: string; sizes: string[]; minWidth: number; minHeight: number; maxWidth: number; maxHeight: number; isDefault: boolean };
const pres = (key: string, name: string, icon: string, isDefault: boolean, sizes = ['M', 'L'], bounds: [number, number, number, number] = [6, 3, 12, 6]): Pres => ({
  key, name, icon, sizes, minWidth: bounds[0], minHeight: bounds[1], maxWidth: bounds[2], maxHeight: bounds[3], isDefault,
});
const LINE = (d: boolean) => pres('line', 'Line', 'show_chart', d);
const COLUMN = (d: boolean) => pres('column', 'Column', 'bar_chart', d);
const TABLE = (d: boolean) => pres('table', 'Table', 'table_rows', d);
const STACKED = (d: boolean) => pres('stacked', 'Stacked', 'stacked_bar_chart', d);
const DONUT = (d: boolean) => pres('donut', 'Donut', 'donut_large', d);
const TILE = (d: boolean) => pres('tile', 'Tile', 'pin', d, ['S', 'M', 'L'], [3, 2, 6, 3]);

const DIVISION_SUPPORTED = new Set([
  'payer_mix', 'transport_volume', 'payment_age', 'cash_per_transport_by_payer',
  'transport_mix_by_call_type', 'aircraft_performance', 'base_analysis', 'loaded_miles_by_aircraft', 'payer_mix_financial_effect', 'transports_by_pickup_location',
  'cash_contribution_by_aircraft', 'cash_contribution_by_division', 'cash_contribution_by_payer',
]);
/** Metrics about one Service Line only. The API answers 400 for the other line, and the catalog says so in context.appliesTo. */
const AIR_ONLY = new Set(['aircraft_performance', 'loaded_miles_by_aircraft', 'cash_contribution_by_aircraft']);

function available(key: string, name: string, description: string, unit: string, category: string, presentations: Pres[], months = false) {
  return {
    key, name, description, unit, category, status: 'available', placeable: true, claimLevel: false,
    serviceLines: AIR_ONLY.has(key) ? ['air', 'both'] : ['air', 'ground', 'both'], dimensions: months ? ['period', 'months', 'serviceLine'] : ['period', 'serviceLine'], presentations,
    context: { divisionSupport: DIVISION_SUPPORTED.has(key) ? 'supported' : 'none', appliesTo: AIR_ONLY.has(key) ? 'air' : 'both' },
    options: months ? [{ key: 'months', name: 'Months shown', type: 'choice', choices: ['6', '12'], default: '6', appliesTo: presentations.map((p) => p.key) }] : [],
  };
}

const planned = (key: string, name: string, description: string, unit: string, category: string, claimLevel = false) => ({
  key, name, description, unit, category, status: 'planned', placeable: false, claimLevel, serviceLines: [], dimensions: [], presentations: [], options: [],
});

/** The Metric Catalog as the API serves it, in the shape the palette and the settings pane read. */
export const CATALOG = {
  categories: [
    { key: 'cash', name: 'Cash' }, { key: 'transports', name: 'Transports' }, { key: 'payer', name: 'Payers' },
    { key: 'claims', name: 'Claims and AR' }, { key: 'fleet', name: 'Fleet and bases' }, { key: 'insights', name: 'Insights' },
  ],
  metrics: [
    available('net_collections', 'Net cash collected', 'Cash collected in the accounting period, after refunds.', 'USD', 'cash', [LINE(true), COLUMN(false), TABLE(false)], true),
    available('net_cash_projection', 'Projected net cash', 'Cash the data warehouse forecasts for the months after the chosen period.', 'USD', 'cash', [COLUMN(true), LINE(false), TABLE(false)]),
    available('transport_volume', 'Transports and loaded miles', 'Transports by month of service and the loaded miles billed.', 'transports', 'transports', [TILE(true), COLUMN(false), TABLE(false)], true),
    available('cash_per_transport_by_payer', 'Cash per transport by payer', 'Matured net cash per transport for each payer group.', 'USD', 'payer', [COLUMN(true), TABLE(false)]),
    available('payment_age', 'Payment age', 'How long each payer group takes to pay, in whole months.', 'USD', 'payer', [COLUMN(true), TABLE(false)]),
    available('payer_mix', 'Payer mix', 'Transports by the primary payer group.', 'transports', 'payer', [COLUMN(true), TABLE(false)]),
    available('clean_claim_rate', 'Clean claim rate', 'The share of claims accepted first time.', 'percent', 'claims', [TILE(true), LINE(false), TABLE(false)], true),
    available('transport_mix_by_call_type', 'Transport mix by call type', 'Transports by call type, with the month before and a trend.', 'transports', 'transports', [COLUMN(true), STACKED(false), DONUT(false), LINE(false), TABLE(false)], true),
    available('transports_by_pickup_location', 'Transports by pickup location', 'Transports by where the patient was picked up, with facilities.', 'transports', 'transports', [COLUMN(true), DONUT(false), TABLE(false)]),
    available('payer_mix_financial_effect', 'Payer mix financial effect', 'The expected effect on cash of a change in payer mix.', 'USD', 'payer', [COLUMN(true), TILE(false), TABLE(false)]),
    available('aircraft_performance', 'Aircraft performance', 'Transports, loaded miles and net cash by tail number.', 'USD', 'fleet', [TABLE(true), COLUMN(false)]),
    available('base_analysis', 'Base analysis', 'Transports, loaded miles and net cash by reporting division, with the aircraft used.', 'USD', 'fleet', [TABLE(true), COLUMN(false)]),
    available('loaded_miles_by_aircraft', 'Loaded miles by aircraft', 'Loaded miles by tail number, with the average per transport and a trend.', 'miles', 'fleet', [COLUMN(true), LINE(false), TABLE(false)], true),
    available('cash_contribution_by_aircraft', 'Cash contribution by aircraft', 'The change in net cash for each tail number, adding up to the total change.', 'USD', 'fleet', [TABLE(true), COLUMN(false)]),
    available('cash_contribution_by_division', 'Cash contribution by reporting division', 'The change in net cash for each reporting division, adding up to the total change.', 'USD', 'fleet', [TABLE(true), COLUMN(false)]),
    available('cash_contribution_by_payer', 'Cash contribution by payer', 'The change in net cash for each payer group, adding up to the total change.', 'USD', 'payer', [TABLE(true), COLUMN(false)]),
    { ...planned('what_changed', 'What changed', 'A written summary of the findings behind a change in net cash.', 'text', 'insights'), status: 'available' },
    planned('ar_days', 'AR days', 'Days in accounts receivable, with the trend.', 'days', 'claims'),
    planned('denied_claims', 'Denied claims', 'Denied claims with payer, reason and deadline.', 'claims', 'claims', true),
  ],
};

type StoredView = { id: number; name: string; description: string | null; ownerName: string; isOwner: boolean; layout: unknown; version: number; updatedAt: string };

const w = (id: string, metricKey: string, presentation: string, x: number, y: number, ww: number, h: number, options?: Record<string, string>) => ({ id, metricKey, presentation, x, y, w: ww, h, ...(options ? { options } : {}) });

/** Views the mocked user starts with: one of their own with two metrics, one shared with them, and one that names a metric the catalog no longer has. */
function seedViews(): StoredView[] {
  const base = { description: null, updatedAt: '2026-11-22T10:00:00Z' };
  return [
    { ...base, id: 5, name: 'Cash overview', ownerName: 'jdoe', isOwner: true, version: 1, layout: { version: 1, density: 'comfortable', widgets: [w('w1', 'net_collections', 'line', 0, 0, 6, 3), w('w2', 'clean_claim_rate', 'tile', 6, 0, 3, 2)] } },
    { ...base, id: 6, name: 'Executive summary', ownerName: 'asmith', isOwner: false, version: 1, layout: { version: 1, density: 'comfortable', widgets: [w('w1', 'transport_volume', 'tile', 0, 0, 3, 2)] } },
    { ...base, id: 7, name: 'Old layout', ownerName: 'jdoe', isOwner: true, version: 1, layout: { version: 1, density: 'comfortable', widgets: [w('w1', 'retired_metric', 'line', 0, 0, 6, 3), w('w2', 'net_collections', 'table', 6, 0, 6, 3)] } },
  ];
}

/** The saved views routes, backed by a small in-memory store so a test can save, reload and delete. A test overrides a route for a failure case. */
async function mockSavedViews(page: import('@playwright/test').Page): Promise<void> {
  const views = new Map(seedViews().map((v) => [v.id, v]));
  let next = 8;
  const etag = (v: StoredView) => ({ ETag: `"${v.version}"` });
  const body = (v: StoredView) => ({ ...v, version: String(v.version) });
  const summary = (v: StoredView) => ({ id: v.id, name: v.name, description: v.description, ownerName: v.ownerName, isOwner: v.isOwner, widgetCount: (v.layout as { widgets: unknown[] }).widgets.length, updatedAt: v.updatedAt });

  await page.route('**/api/v1/providers/*/metrics/catalog', (route) => route.fulfill({ json: CATALOG }));
  await page.route(/\/api\/v1\/providers\/[^/]+\/saved-views(\?.*)?$/, async (route) => {
    const request = route.request();
    if (request.method() === 'GET') {
      const all = [...views.values()];
      return route.fulfill({ json: { mine: all.filter((v) => v.isOwner).map(summary), shared: all.filter((v) => !v.isOwner).map(summary) } });
    }
    const input = request.postDataJSON() as { name: string; description: string | null; layout: unknown };
    if ([...views.values()].some((v) => v.isOwner && v.name === input.name)) {
      return route.fulfill({ status: 409, json: { title: 'You already have a view with this name.' } });
    }
    const created: StoredView = { id: next++, name: input.name, description: input.description, ownerName: 'jdoe', isOwner: true, layout: input.layout, version: 1, updatedAt: new Date().toISOString() };
    views.set(created.id, created);
    return route.fulfill({ status: 201, headers: etag(created), json: body(created) });
  });
  await page.route(/\/api\/v1\/providers\/[^/]+\/saved-views\/(\d+)$/, async (route) => {
    const request = route.request();
    const id = Number(request.url().match(/saved-views\/(\d+)$/)![1]);
    const view = views.get(id);
    if (!view) {
      return route.fulfill({ status: 404, json: { title: 'Not Found' } });
    }
    if (request.method() === 'GET') {
      return route.fulfill({ headers: etag(view), json: body(view) });
    }
    if (request.method() === 'DELETE') {
      views.delete(id);
      return route.fulfill({ status: 204 });
    }
    if (request.headers()['if-match'] !== `"${view.version}"`) {
      return route.fulfill({ status: 412, json: { title: 'This view was saved by someone else since you opened it.' } });
    }
    const input = request.postDataJSON() as { name: string; description: string | null; layout: unknown };
    Object.assign(view, { name: input.name, description: input.description, layout: input.layout, version: view.version + 1 });
    return route.fulfill({ headers: etag(view), json: body(view) });
  });
  await page.route(/\/saved-views\/\d+\/shares$/, (route) => route.fulfill({ json: { recipients: [], pending: [] } }));
  await page.route(/\/saved-views\/\d+\/invitations$/, (route) => {
    const emails = (route.request().postDataJSON() as { emails: string[] }).emails;
    return route.fulfill({ json: { sent: emails.map((email, i) => ({ invitationId: i + 1, email, expiresAt: '2026-11-29T00:00:00Z', isExpired: false })), failed: [] } });
  });
  await page.route('**/api/v1/saved-view-invitations/redeem', (route) => {
    const token = (route.request().postDataJSON() as { token?: string } | null)?.token;
    return token === 'T'.repeat(43) ? route.fulfill({ json: { providerId: '526', viewId: 6 } }) : route.fulfill({ status: 404, json: { title: 'Not Found' } });
  });
}

const claim = (id: number, over: Record<string, unknown> = {}) => ({
  id: String(id), claimRef: `FW-26104${id}`, serviceDate: '2026-10-02', payer: 'Meridian Health Plan', payerCategory: 'Insurance', vehicle: 'N418CV',
  reportingDivision: 'Sky Harbor base', status: 'Submitted', denialCode: 'CO-16', denialReason: 'Missing documentation', balanceDue: 24600,
  filingDueDate: '2026-11-26', daysLeft: 21, ...over,
});
const DENIED = [claim(18), claim(57, { payer: 'Northstar Health', vehicle: 'N227CV', balanceDue: 23600, filingDueDate: '2026-11-29', daysLeft: 24 })];
const HELD = [
  claim(61, { denialCode: null, denialReason: null, status: 'On_Hold', balanceDue: 1200, daysLeft: -3, filingDueDate: '2026-11-02' }),
  claim(62, { denialCode: null, denialReason: null, status: 'Submitted', balanceDue: 800, daysLeft: 200, filingDueDate: '2027-06-08' }),
];
const OTHERS = [claim(71, { denialCode: null, denialReason: null, balanceDue: 90, daysLeft: 300 }), claim(72, { denialCode: null, denialReason: null, balanceDue: 75, daysLeft: 310 })];
const ALL = [...DENIED, ...HELD, ...OTHERS, claim(81, { denialCode: null, denialReason: null, balanceDue: 10, daysLeft: 320 }), claim(82, { denialCode: null, denialReason: null, balanceDue: 20, daysLeft: 330 })];
export const CLAIM_LISTS: Record<string, unknown[]> = { denied: DENIED, needs_attention: [...DENIED, ...HELD], all: ALL };

/** A denial reason that holds markup. It must reach the page as literal text. */
export const HOSTILE_REASON = '<img src=x onerror=alert(1)>';

/** What the counts route adds for the summary above the list: a rate, the top reasons with an Other row, overlapping categories and the four filing bands. */
export const ATTENTION_SUMMARY = {
  deniedRate: 0.25,
  reasons: [
    { reason: 'Missing documentation', isOther: false, count: 1, balanceDue: 24600 },
    { reason: HOSTILE_REASON, isOther: false, count: 1, balanceDue: 23600 },
    { reason: 'Other', isOther: true, count: 3, balanceDue: 3200 },
  ],
  categories: [
    { key: 'denied', name: 'Denied', count: 2, balanceDue: 48200 },
    { key: 'on_hold', name: 'On hold', count: 1, balanceDue: 1200 },
    { key: 'in_collections', name: 'In collections', count: 0, balanceDue: 0 },
  ],
  filingBands: [
    { key: 'past_due', name: 'Past due', claims: 1, balanceDue: 1200 },
    { key: 'due_within_10_days', name: 'Due within 10 days', claims: 0, balanceDue: 0 },
    { key: 'due_within_30_days', name: 'Due within 30 days', claims: 2, balanceDue: 48200 },
    { key: 'due_later', name: 'Due later', claims: 1, balanceDue: 800 },
  ],
};

/** The same summary for a month with no denied claims: no rate, no reasons, zero in every category and band. */
export const ATTENTION_EMPTY_SUMMARY = {
  deniedRate: null,
  reasons: [],
  categories: ATTENTION_SUMMARY.categories.map((c) => ({ ...c, count: 0, balanceDue: 0 })),
  filingBands: ATTENTION_SUMMARY.filingBands.map((b) => ({ ...b, claims: 0, balanceDue: 0 })),
};

/** Answers the counts route with the empty summary. Call before opening the page. */
export async function mockNoDeniedClaims(page: Page): Promise<void> {
  await page.route(/\/claims\/attention\/counts(\?.*)?$/, (route) =>
    route.fulfill({
      json: {
        providerId: '526', period: '202610', dataThrough: '2026-11-05', deniedBalanceDue: 0,
        counts: [{ key: 'denied', name: 'Denied', count: 2 }, { key: 'needs_attention', name: 'Needs attention', count: 4 }, { key: 'all', name: 'All', count: 8 }],
        ...ATTENTION_EMPTY_SUMMARY,
      },
    }),
  );
}

/** The claims-needing-attention routes: counts, a list per check, and the CSV export. */
async function mockClaims(page: Page): Promise<void> {
  const base = /\/api\/v1\/providers\/[^/]+\/claims\/attention\//;
  // Playwright tries the most recently added route first, so the general list route goes first and counts and export override it.
  await page.route(new RegExp(`${base.source}[a-z_]+(\\?.*)?$`), (route) => {
    const check = /attention\/([a-z_]+)/.exec(route.request().url())![1]!;
    const claims = CLAIM_LISTS[check] ?? [];
    return route.fulfill({ json: { providerId: '526', check, period: '202610', dataThrough: '2026-11-05', total: claims.length, page: 1, pageSize: 25, claims } });
  });
  await page.route(new RegExp(`${base.source}counts(\\?.*)?$`), (route) =>
    route.fulfill({
      json: {
        providerId: '526', period: '202610', dataThrough: '2026-11-05', deniedBalanceDue: 48200,
        counts: [{ key: 'denied', name: 'Denied', count: 2 }, { key: 'needs_attention', name: 'Needs attention', count: 4 }, { key: 'all', name: 'All', count: 8 }],
        ...ATTENTION_SUMMARY,
      },
    }),
  );
  await page.route(new RegExp(`${base.source}[a-z_]+/export(\\?.*)?$`), (route) => {
    const check = /attention\/([a-z_]+)\/export/.exec(route.request().url())![1]!;
    return route.fulfill({
      status: 200,
      headers: { 'content-type': 'text/csv; charset=utf-8', 'content-disposition': `attachment; filename=prism-needs-attention-${check.replace('_', '-')}-2026-11-22.csv` },
      body: '"Claim"\r\n"FW-1"\r\n',
    });
  });
}

export const test = base.extend({
  page: async ({ page }, use) => {
    if (!process.env['PRISM_E2E_STORAGE_STATE']) {
      await page.route('**/api/v1/me', (route) => route.fulfill({ json: MOCK_ME }));
      await mockNetCollections(page);
      await mockMetricSix(page);
      await mockSavedViews(page);
      await mockClaims(page);
    }
    await use(page);
  },
});

export { expect };
