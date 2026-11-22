import type { Page } from '@playwright/test';
import { LayoutIssue, analyzeLayout } from './layout-analysis';

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

/** The saved views list, waiting until its rows are in. */
export async function openSavedViews(page: Page): Promise<void> {
  await page.goto('/saved-views');
  await page.locator('app-saved-views-list h1').waitFor();
  await page.locator('app-saved-views-list li a').first().waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(200);
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
