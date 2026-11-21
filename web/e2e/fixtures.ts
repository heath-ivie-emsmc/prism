import { test as base, expect } from '@playwright/test';

/** What `GET /api/v1/me` returns for a signed-in user with one Provider. Used only when no real session is supplied. */
const MOCK_ME = {
  userName: 'jdoe',
  displayName: 'Jane Doe',
  providers: [{ id: '526', name: 'Seminole County Fire Department', facilityCount: 3 }],
  permissions: ['dashboard.view'],
};

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
    groups: groups.map((g) => ({ ...g, serviceLine: line, current: scaled(g.current) })),
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
function payerMixFor(): { key: string; name: string; prior: number; current: number }[] {
  const rows: [string, string, number, number][] = [
    ['commercial', 'Commercial', 30, 26],
    ['medicare', 'Medicare', 40, 38],
    ['medicaid', 'Medicaid', 18, 23],
    ['other-gov', 'Other government', 6, 6],
    ['va', 'VA', 3, 4],
    ['facility', 'Facility', 2, 1],
    ['patient', 'Patient responsibility', 1, 2],
  ];
  return rows.map(([key, name, prior, current]) => ({ key, name, prior, current }));
}

/** Answers the net cash routes the way the API does, for the provider the mocked user has. */
async function mockNetCollections(page: import('@playwright/test').Page): Promise<void> {
  await page.route('**/api/v1/providers/*/metrics/net-collections/periods', (route) =>
    route.fulfill({ json: { providerId: '526', periods: [...MONTHS].reverse().map((m) => ({ period: m.period, label: `${m.label} ${m.year}` })) } }),
  );
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/projection(\?.*)?$/, (route) => {
    const months = [
      { period: '202611', label: 'Nov', year: 2026, value: 1_210_000, low: 1_150_000, high: 1_280_000 },
      { period: '202612', label: 'Dec', year: 2026, value: 1_200_000, low: 1_120_000, high: 1_290_000 },
      { period: '202701', label: 'Jan', year: 2027, value: 1_200_000, low: 1_080_000, high: 1_320_000 },
    ];
    return route.fulfill({
      json: {
        metric: { key: 'net_cash_projection', name: 'Projected net cash', description: 'Forecast cash.', unit: 'USD' },
        providerId: '526',
        afterPeriod: '202610',
        serviceLine: 'air',
        snapshotDate: '2026-11-05',
        total: 3_610_000,
        low: 3_350_000,
        high: 3_890_000,
        months,
      },
    });
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
        // September is still open in the mock, the earlier months are closed.
        periodOpen: period >= '202609',
        ...breakdownFor(line, period),
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
        groupName: group === 'fixed-wing' ? 'Fixed Wing' : group === 'als' ? 'ALS' : group,
        shares: payerMixFor(),
      },
    });
  });
  await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections(\?.*)?$/, (route) => {
    const url = new URL(route.request().url());
    const end = url.searchParams.get('endPeriod') ?? '202610';
    const months = Number(url.searchParams.get('months') ?? '6');
    const endIndex = MONTHS.findIndex((m) => m.period === end);
    const points = endIndex < 0 ? [] : MONTHS.slice(Math.max(0, endIndex - months + 1), endIndex + 1);
    return route.fulfill({
      json: {
        metric: { key: 'net_collections', name: 'Net cash collected', description: 'Cash collected in the accounting period.', unit: 'USD' },
        providerId: '526',
        endPeriod: end,
        months,
        snapshotDate: '2026-11-05',
        points,
      },
    });
  });
}

export const test = base.extend({
  page: async ({ page }, use) => {
    if (!process.env['PRISM_E2E_STORAGE_STATE']) {
      await page.route('**/api/v1/me', (route) => route.fulfill({ json: MOCK_ME }));
      await mockNetCollections(page);
    }
    await use(page);
  },
});

export { expect };
