import type { Page } from '@playwright/test';
import { LayoutIssue, analyzeLayout } from './layout-analysis';
import { whatChangedFor } from './fixtures';

/** The window sizes every page is checked at: small phone to wide desktop, plus a phone turned sideways. */
export const SIZES = [
  { name: 'phone-small', width: 320, height: 700 },
  { name: 'phone', width: 390, height: 844 },
  { name: 'phone-landscape', width: 844, height: 390 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'laptop-small', width: 1024, height: 768 },
  { name: 'laptop', width: 1280, height: 800 },
  { name: 'desktop', width: 1440, height: 900 },
  { name: 'wide', width: 1920, height: 1080 },
] as const;

export const SCHEMES = ['light', 'dark'] as const;
export const REPORT_DIR = 'e2e-report';

/** WCAG 2.0, 2.1 and 2.2 level A and AA. Target size (2.5.8) is a 2.2 rule. */
export const WCAG_AA_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/**
 * Known problem in the shared library, not in Prism: label-content-name-mismatch on .ems-profile (its fixed aria-label "Account: Name"
 * does not contain the visible initials). Fix it in shared-ui-component-library, then delete this exclusion.
 */
const KNOWN_LIBRARY_RULES = { 'label-content-name-mismatch': { enabled: false } };

export async function openDashboard(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await page.locator('app-net-cash-panel').waitFor();
  // The chart draws on a canvas after ECharts loads.
  await page.locator('app-chart canvas').waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(400);
}

/** The dashboard with the projection panel showing, waiting until its table has drawn. `months` picks the horizon (the default is 3) and `line` the Service Line. */
export async function openProjection(page: Page, options: { months?: 1 | 2 | 3; ground?: boolean } = {}): Promise<void> {
  await openDashboard(page);
  if (options.ground) await page.getByRole('radio', { name: 'Ground only' }).click();
  const panel = page.locator('app-projection-panel');
  await panel.locator('table').waitFor();
  if (options.months && options.months !== 3) {
    await panel.getByRole('radio', { name: options.months === 1 ? '1 month' : `${options.months} months` }).click();
    await page.waitForFunction((n) => document.querySelectorAll('app-projection-panel thead th').length === n + 1, options.months);
  }
  await page.waitForTimeout(300);
}

/** The Transports tile in full screen, with its latest-month split table showing. */
export async function openTransportsFullScreen(page: Page): Promise<void> {
  await openDashboard(page);
  await page.getByRole('button', { name: 'Expand Transports to full screen' }).click();
  await page.locator('app-kpi-card .split table').waitFor();
  await page.waitForTimeout(300);
}

/** The payer breakdown (the drill target of the payer mix), waiting until its chart has drawn. */
export async function openPayerDetail(page: Page, query = 'period=202610&line=air', group = 'rotor-wing', payer = 'commercial'): Promise<void> {
  await page.goto(`/dashboard/metrics/payer-mix/${group}/${payer}?${query}`);
  await page.locator('app-payer-mix-detail h1').waitFor();
  await page.locator('app-payer-mix-detail app-chart canvas').waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(400);
}

/** The payer mix of one group, waiting until its chart has drawn. */
export async function openPayerMixPage(page: Page, group = 'rotor-wing', query = 'period=202610&line=air'): Promise<void> {
  await page.goto(`/dashboard/net-cash/${group}?${query}`);
  await page.locator('app-payer-mix app-chart canvas').waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(400);
}

/** The saved views list, waiting until its rows are in. */
export async function openSavedViews(page: Page): Promise<void> {
  await page.goto('/saved-views');
  await page.locator('app-saved-views-list h1').waitFor();
  await page.locator('app-saved-views-list li a').first().waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(200);
}

/** The claims review page, waiting until its rows are in. */
export async function openNeedsAttention(page: Page, query = 'check=denied&period=202610&line=air&group=fixed-wing'): Promise<void> {
  await page.goto(`/dashboard/needs-attention?${query}`);
  await page.locator('app-needs-attention h1').waitFor();
  await page.locator('app-needs-attention .claims tbody tr').first().waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(300);
}

/** One saved view, read-only or in edit mode, after its widgets have loaded and drawn. */
export async function openView(page: Page, id: number | 'new', options: { edit?: boolean; widgets?: number } = {}): Promise<void> {
  await page.goto(`/saved-views/${id}`);
  await page.locator('app-view-builder h1').waitFor();
  if (id !== 'new') {
    await page.locator('app-grid-canvas li.item').first().waitFor();
    await page.locator('app-grid-canvas app-chart canvas, app-grid-canvas .widget__value, app-grid-canvas table').first().waitFor();
  }
  if (options.edit && id !== 'new') {
    await page.getByRole('button', { name: 'Edit', exact: true }).click();
    await page.locator('[role="toolbar"]').first().waitFor();
  }
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(400);
}

/** The text most recently spoken through the live region, for checking what a screen reader would hear. */
export async function lastAnnouncement(page: Page): Promise<string> {
  return page.evaluate(() => [...document.querySelectorAll('.cdk-live-announcer-element')].map((e) => (e.textContent ?? '').trim()).filter(Boolean).pop() ?? '');
}

/** Axe violations at WCAG 2.2 AA, as short readable lines. Scope to a selector to check one part of the page. */
/** Switch the net cash panel between its line, bar and table views with the chart-type control. */
export async function chooseNetCashView(page: Page, view: 'line' | 'bar' | 'table'): Promise<void> {
  const names = { line: 'Line chart', bar: 'Bar chart', table: 'Table' };
  await page.locator('app-net-cash-panel [role="radiogroup"][aria-label="Chart type"]').getByRole('radio', { name: names[view] }).click();
  if (view === 'table') {
    await page.locator('app-net-cash-panel table').waitFor();
  } else {
    await page.locator('app-net-cash-panel app-chart canvas').waitFor();
  }
  await page.waitForTimeout(500);
}

export async function axeViolations(page: Page, include?: string, disableRules: string[] = []): Promise<string[]> {
  if (!(await page.evaluate(() => 'axe' in window))) {
    await page.addScriptTag({ path: require.resolve('axe-core') });
  }
  return page.evaluate(
    async ({ tags, rules, scope }) => {
      const axe = (window as unknown as { axe: { run: (c: unknown, o: unknown) => Promise<{ violations: { id: string; help: string; impact: string; nodes: { target: string[] }[] }[] }> } }).axe;
      const results = await axe.run(scope ? { include: [[scope]] } : document, { rules, runOnly: { type: 'tag', values: tags } });
      return results.violations.map((v) => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.slice(0, 3).map((n) => n.target.join(' ')).join(' | ')})`);
    },
    { tags: WCAG_AA_TAGS, rules: { ...KNOWN_LIBRARY_RULES, ...Object.fromEntries(disableRules.map((id) => [id, { enabled: false }])) }, scope: include ?? null },
  );
}

export async function layoutErrors(page: Page): Promise<{ errors: string[]; issues: LayoutIssue[] }> {
  const issues: LayoutIssue[] = await page.evaluate(analyzeLayout);
  const errors = issues.filter((i) => i.severity === 'error').map((i) => `${i.kind}: ${i.message} [${i.selectors.join(' | ')}]`);
  return { errors, issues };
}

/** The page is open, then the warehouse "loads" again: the periods route now answers with a newer Data Through. */
export const NEWER_DATA_THROUGH = '2026-11-23';
export async function warehouseLoadsAgain(page: Page): Promise<void> {
  await page.route(/net-collections\/periods(\?.*)?$/, (route) =>
    route.fulfill({
      json: { providerId: '526', dataThrough: NEWER_DATA_THROUGH, periods: [{ period: '202610', label: 'Oct 2026' }, { period: '202609', label: 'Sep 2026' }] },
    }),
  );
}

/** Tab regained focus: the page checks for newer data. */
export const regainFocus = (page: Page): Promise<void> => page.evaluate(() => void window.dispatchEvent(new Event('focus')));

/** Open the "What these numbers mean" panel of the first Metric help on the page and wait for its glossary. */
export async function expandHelp(page: Page): Promise<void> {
  const toggle = page.locator('app-metric-help button.help__toggle').first();
  await toggle.scrollIntoViewIfNeeded();
  await toggle.click();
  await page.locator('app-metric-help .help__panel dl').first().waitFor();
  await page.waitForTimeout(200);
}

/** Open every compact help disclosure on a page (dashboard cards, the net cash panel, Saved View widgets), so their panels are laid out and checked. */
export async function expandAllHelp(page: Page): Promise<void> {
  const toggles = page.locator('app-metric-help button.help__toggle');
  await toggles.first().waitFor();
  for (const toggle of await toggles.all()) {
    await toggle.scrollIntoViewIfNeeded();
    if ((await toggle.getAttribute('aria-expanded')) !== 'true') await toggle.click();
  }
  await page.locator('app-metric-help .help__panel dl').first().waitFor();
  await page.waitForTimeout(300);
}

/** The dashboard with every card's help open. */
export async function openDashboardHelp(page: Page): Promise<void> {
  await openDashboard(page);
  await expandAllHelp(page);
}

/** A Saved View with every widget's help open. */
export async function openViewHelp(page: Page): Promise<void> {
  await openView(page, 5);
  await expandAllHelp(page);
}

/** Show the newer-data notice on the dashboard, with its Update view button. */
export async function openDashboardWithNotice(page: Page): Promise<void> {
  await openDashboard(page);
  await warehouseLoadsAgain(page);
  await regainFocus(page);
  await page.locator('app-data-updated-notice').getByRole('button', { name: 'Update view' }).waitFor();
  await page.waitForTimeout(200);
}

/** The generic Metric view (`/dashboard/metrics/:key`) for a Metric with no page of its own, waiting until its table or chart has drawn. */
export async function openMetricView(page: Page, key = 'net_collections', query = 'period=202610&line=air'): Promise<void> {
  await page.goto(`/dashboard/metrics/${key}?${query}`);
  await page.locator('app-metric-view h1').waitFor();
  await page.locator('app-metric-view app-chart canvas, app-metric-view table').first().waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(400);
}

/** The generic Metric view for a key the SPA does not know: the calm not-found state. */
export async function openMetricViewNotFound(page: Page): Promise<void> {
  await page.goto('/dashboard/metrics/no_such_metric');
  await page.getByRole('heading', { level: 1, name: 'View not found' }).waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(200);
}

/** The keys of the six Metrics added with the catalog's appliesTo field, with the SPA's name for each. */
export const SIX_METRICS = [
  { key: 'transport_mix_by_call_type', name: 'Transport mix by call type', airOnly: false },
  { key: 'aircraft_performance', name: 'Aircraft performance', airOnly: true },
  { key: 'base_analysis', name: 'Base analysis', airOnly: false },
  { key: 'loaded_miles_by_aircraft', name: 'Loaded miles by aircraft', airOnly: true },
  { key: 'payer_mix_financial_effect', name: 'Payer mix financial effect', airOnly: false },
  { key: 'transports_by_pickup_location', name: 'Transports by pickup location', airOnly: false },
] as const;

/** The three Metrics that split the change in net cash by contributor. */
export const CONTRIBUTION_METRICS = [
  { key: 'cash_contribution_by_aircraft', name: 'Cash contribution by aircraft', airOnly: true, header: 'Tail number' },
  { key: 'cash_contribution_by_division', name: 'Cash contribution by reporting division', airOnly: false, header: 'Base location' },
  { key: 'cash_contribution_by_payer', name: 'Cash contribution by payer', airOnly: false, header: 'Payer group' },
] as const;

/** An air-only Metric asked for under Ground only: the not-available state, with no request. */
export async function openMetricViewGroundOnly(page: Page, key = 'aircraft_performance'): Promise<void> {
  await page.goto(`/dashboard/metrics/${key}?period=202610&line=ground`);
  await page.locator('app-metric-view h1').waitFor();
  await page.locator('app-metric-view .widget__note').waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(300);
}

const sixWidget = (id: string, metricKey: string, presentation: string, x: number, y: number) => ({ id, metricKey, presentation, x, y, w: 6, h: 4 });
/** Two arrangements of the six new Metrics, so every presentation they offer is drawn on a saved view. */
const SIX_LAYOUTS = {
  a: [
    sixWidget('s1', 'transport_mix_by_call_type', 'stacked', 0, 0), sixWidget('s2', 'transports_by_pickup_location', 'donut', 6, 0),
    sixWidget('s3', 'payer_mix_financial_effect', 'column', 0, 4), sixWidget('s4', 'aircraft_performance', 'column', 6, 4),
    sixWidget('s5', 'base_analysis', 'table', 0, 8), sixWidget('s6', 'loaded_miles_by_aircraft', 'line', 6, 8),
  ],
  b: [
    sixWidget('s1', 'transport_mix_by_call_type', 'donut', 0, 0), sixWidget('s2', 'transports_by_pickup_location', 'column', 6, 0),
    sixWidget('s3', 'payer_mix_financial_effect', 'tile', 0, 4), sixWidget('s4', 'aircraft_performance', 'table', 6, 4),
    sixWidget('s5', 'base_analysis', 'column', 0, 8), sixWidget('s6', 'loaded_miles_by_aircraft', 'column', 6, 8),
    sixWidget('s7', 'transport_mix_by_call_type', 'line', 0, 12), sixWidget('s8', 'loaded_miles_by_aircraft', 'table', 6, 12),
  ],
  c: [
    sixWidget('s1', 'cash_contribution_by_aircraft', 'column', 0, 0), sixWidget('s2', 'cash_contribution_by_division', 'column', 6, 0),
    sixWidget('s3', 'cash_contribution_by_payer', 'column', 0, 4), sixWidget('s4', 'cash_contribution_by_aircraft', 'table', 6, 4),
    sixWidget('s5', 'cash_contribution_by_division', 'table', 0, 8), sixWidget('s6', 'cash_contribution_by_payer', 'table', 6, 8),
  ],
} as const;

/** A saved view that holds the six new Metrics, after every widget has loaded and drawn. The view is served by a route override, so the seeded views stay as they are. */
export async function openSixMetricView(page: Page, arrangement: keyof typeof SIX_LAYOUTS = 'a'): Promise<void> {
  await page.route(/\/api\/v1\/providers\/[^/]+\/saved-views\/5$/, async (route) => {
    if (route.request().method() !== 'GET') return route.fallback();
    return route.fulfill({
      headers: { ETag: '"1"' },
      json: { id: 5, name: 'Fleet and mix', description: null, ownerName: 'jdoe', isOwner: true, version: '1', updatedAt: '2026-11-22T10:00:00Z', layout: { version: 1, density: 'comfortable', widgets: SIX_LAYOUTS[arrangement] } },
    });
  });
  await openView(page, 5);
  await page.locator('app-grid-canvas app-skeleton').first().waitFor({ state: 'detached' }).catch(() => undefined);
  await page.waitForFunction(() => document.querySelectorAll('app-grid-canvas li.item').length > 0 && !document.querySelector('app-grid-canvas app-skeleton'));
  await page.waitForTimeout(500);
}

/** The edit page with Ground only chosen, so the palette lists the air-only Metrics with their reason. */
export async function openPaletteUnderGround(page: Page): Promise<void> {
  await openView(page, 5, { edit: true });
  await page.getByRole('radio', { name: 'Ground only' }).click();
  // On a narrow screen the palette is a dialog opened by the Add metrics button.
  if ((await page.locator('app-metric-palette').count()) === 0) await page.getByRole('button', { name: 'Add metrics' }).click();
  await page.locator('.palette__reason').first().waitFor();
  await page.waitForTimeout(300);
}

/** The what-changed card kinds the API can add besides the older ones: aircraft mix and base mix as drivers, base mix beside them, and a kind this app does not know. Titles carry markup to be drawn as text. */
export const NEW_KIND_CARDS = {
  drivers: [
    { kind: 'aircraft-mix', label: 'watch', title: 'Cash moved between aircraft: <img src=x onerror=alert(1)> flew less', body: 'Net cash on N611CV fell $21,000.00 while N612CV rose $9,000.00.', impact: -12000 },
    { kind: 'base-mix', label: 'structural', title: 'Sky Harbor base brought in less', body: 'Net cash at Sky Harbor base fell $37,000.00 from September.', impact: -37000 },
  ],
  expected: [
    { kind: 'base-mix', label: 'watch', title: 'More transports came from the North base', body: 'Its share rose from 41% to 46%.', impact: 6200 },
    { kind: 'a-kind-added-later', label: 'info', title: 'A finding this version does not know', body: 'It is still shown with its own words.', impact: -300 },
  ],
} as const;

/** The payer mix page with what-changed answering the newer card kinds, once its cards have drawn. */
export async function openInsightNewKinds(page: Page): Promise<void> {
  await page.route(/insights\/what-changed/, (route) => {
    const body = whatChangedFor(new URL(route.request().url()));
    return route.fulfill({ json: { ...body, drivers: [...body.drivers, ...NEW_KIND_CARDS.drivers], expected: [...body.expected, ...NEW_KIND_CARDS.expected] } });
  });
  await openPayerMixPage(page, 'fixed-wing');
  await page.locator('app-what-changed .card:not(.card--attention)').nth(5).waitFor();
  await page.waitForTimeout(200);
}
