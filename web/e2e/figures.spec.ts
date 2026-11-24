import { expect, test, HOSTILE_REASON, mockNoDeniedClaims } from './fixtures';
import { axeViolations, layoutErrors, openDashboard, openMetricView, openNeedsAttention } from './support';

const panel = (page: import('@playwright/test').Page) => page.locator('app-projection-panel');
const horizon = (page: import('@playwright/test').Page) => panel(page).getByRole('radiogroup', { name: 'Months to project' });
const row = (page: import('@playwright/test').Page, name: string) => panel(page).locator('tbody tr', { has: page.getByRole('rowheader', { name, exact: true }) });

test.describe('projection panel', () => {
  test('shows three months by default with the change, the running total, the range and the components', async ({ page }) => {
    const asked: string[] = [];
    page.on('request', (r) => {
      if (/net-collections\/projection/.test(r.url())) asked.push(new URL(r.url()).searchParams.get('horizon') ?? '');
    });
    await openDashboard(page);
    await panel(page).locator('table').waitFor();

    expect(asked).toContain('3');
    await expect(horizon(page).getByRole('radio', { name: '3 months' })).toHaveAttribute('aria-checked', 'true');
    await expect(panel(page).locator('thead th')).toHaveText(['Figure', 'Nov 2026', 'Dec 2026', 'Jan 2027']);
    await expect(row(page, 'Change from the month before')).toContainText('+$109,760.00 (+10.0%)');
    await expect(row(page, 'Change from the month before')).toContainText('vs Oct 2026 actual');
    await expect(row(page, 'Cumulative expected net cash')).toContainText('$3,610,000.00');
    await expect(row(page, 'Forecast range')).toContainText('$1,150,000.00 to $1,280,000.00');
  });

  test('labels the two parts with the database team wording, shows each share as its own figure, and has the footnote', async ({ page }) => {
    await openDashboard(page);
    await panel(page).locator('table').waitFor();

    await expect(panel(page).getByText('Expected collections on trips that already existed').first()).toBeVisible();
    await expect(panel(page).getByText('Assumed run rate of trips that do not exist yet').first()).toBeVisible();
    await expect(panel(page)).not.toContainText(/already billed/i);
    await expect(panel(page).getByText('The two parts are shown as reported by the warehouse and may not add up to the expected total.')).toBeVisible();
    await expect(panel(page).getByText('The range is across the forecast components, not a statistical interval.')).toBeVisible();
    // November's shares are 82.6% and 49.6%: together they pass 100%, and the page shows them as separate figures, never a stacked 100% bar.
    await expect(row(page, 'Share of expected net cash from trips that already existed')).toContainText('82.6%');
    await expect(row(page, 'Share of expected net cash from trips that do not exist yet')).toContainText('49.6%');
    await expect(panel(page).locator('[role="img"]')).toHaveCount(0);
    // January has no figure for the first part.
    await expect(row(page, 'Expected collections on trips that already existed')).toContainText('n/a');
  });

  test('the horizon is chosen with the arrow keys, asks the API for that many months and redraws the table', async ({ page }) => {
    await openDashboard(page);
    await panel(page).locator('table').waitFor();
    const radios = horizon(page).getByRole('radio');
    await radios.nth(2).focus();

    const asked = page.waitForRequest((r) => /net-collections\/projection/.test(r.url()) && new URL(r.url()).searchParams.get('horizon') === '2');
    await page.keyboard.press('ArrowLeft');
    await asked;

    await expect(radios.nth(1)).toBeFocused();
    await expect(radios.nth(1)).toHaveAttribute('aria-checked', 'true');
    await expect(panel(page).locator('thead th')).toHaveText(['Figure', 'Nov 2026', 'Dec 2026']);
    expect(await radios.evaluateAll((els) => els.map((e) => (e as HTMLElement).tabIndex))).toEqual([-1, 0, -1]);

    await page.keyboard.press('ArrowLeft');
    await expect(radios.nth(0)).toHaveAttribute('aria-checked', 'true');
    await expect(panel(page).locator('thead th')).toHaveText(['Figure', 'Nov 2026']);
    await expect(panel(page).locator('[role="status"]').first()).toHaveText('Showing the next month.');
  });

  test('a month with no earlier actual month to compare with says n/a, not a made-up change', async ({ page }) => {
    await openDashboard(page);
    await page.getByRole('radio', { name: 'Ground only' }).click();
    await panel(page).locator('table').waitFor();

    await expect(row(page, 'Change from the month before').locator('td').first()).toContainText('n/a');
    await expect(row(page, 'Change from the month before').locator('td').first()).toContainText('No actual month to compare with');
  });

  test('the help says the range is not a statistical interval, and the table passes axe', async ({ page }) => {
    await openDashboard(page);
    await panel(page).locator('table').waitFor();
    await panel(page).locator('button.help__toggle').click();

    await expect(panel(page).locator('.help__panel')).toContainText('not a statistical interval');
    await expect(panel(page).locator('.help__panel')).toContainText('may not add up to the expected total');
    expect(await axeViolations(page, 'app-projection-panel')).toEqual([]);
    expect((await layoutErrors(page)).errors).toEqual([]);
  });
});

test.describe('transports split', () => {
  test('the full screen Transports tile adds the latest month next to the month before, with the change and percent', async ({ page }) => {
    await openDashboard(page);
    await page.getByRole('button', { name: 'Expand Transports to full screen' }).click();

    const table = page.locator('app-kpi-card .split table');
    await expect(table.locator('thead th')).toHaveText(['Aircraft type or level of service', 'Oct', 'Sep', 'Change', 'Change %']);
    await expect(table.locator('tbody tr').first()).toContainText('Rotor wing');
    await expect(table.locator('tbody tr').first()).toContainText('-3');
    await expect(table.locator('tbody tr').first()).toContainText('-3.2%');
    await expect(table.locator('tbody tr').nth(1)).toContainText('+48');
    await expect(table.locator('tbody tr').nth(1)).toContainText('n/a');
    expect(await axeViolations(page, 'app-kpi-card .split')).toEqual([]);
  });

  test('a Saved View style table of Transports lists the split under the months', async ({ page }) => {
    await openMetricView(page, 'transport_volume');

    const tables = page.locator('app-metric-widget table');
    await expect(tables).toHaveCount(2);
    await expect(tables.nth(1).locator('thead th')).toHaveText(['Aircraft type or level of service', 'Oct 2026', 'Sep 2026', 'Change', 'Change %']);
    await expect(tables.nth(1).locator('tbody tr')).toHaveCount(2);
  });
});

test.describe('cash per transport', () => {
  test('shows the difference from the overall average and the service months and payment cut-off used', async ({ page }) => {
    await openMetricView(page, 'cash_per_transport_by_payer');

    const table = page.locator('app-metric-widget table');
    await expect(table.locator('thead th')).toContainText(['Payer group', 'Cash per transport', 'Difference from overall', '% from overall']);
    await expect(table.locator('tbody tr').first()).toContainText('+$200.00');
    await expect(table.locator('tbody tr').first()).toContainText('+28.6%');
    await expect(table.locator('tbody tr').nth(1)).toContainText('-$100.00');
    await expect(table.locator('tbody tr').nth(3)).toContainText('n/a');
    await expect(page.locator('app-metric-widget .widget__foot')).toHaveText('Transports from Jan 2025 to Dec 2025, cash posted through Nov 2026');
    expect(await axeViolations(page, 'app-metric-widget')).toEqual([]);
  });
});

test.describe('payment timing', () => {
  test('lists typical months to payment and the share of cash by age band, and says age is whole months', async ({ page }) => {
    await openMetricView(page, 'payment_age');

    const timing = page.locator('app-metric-widget table').nth(1);
    await expect(timing.locator('thead th')).toHaveText(['Payer group', 'Typical months to payment', '0-1 months', '2-3 months', '4-6 months', '7-12 months', '13+ months']);
    await expect(timing.locator('tbody tr').first()).toContainText('1 month');
    await expect(timing.locator('tbody tr').first()).toContainText('50.0%');
    await expect(timing.locator('tbody tr').nth(1)).toContainText('3 months');
    // A payer group with no cash has no typical age and an empty bands list.
    await expect(timing.locator('tbody tr').nth(2)).toContainText('n/a');
    await expect(page.locator('app-metric-widget .widget__foot')).toContainText('whole months');
    await page.locator('app-metric-help button.help__toggle').click();
    await expect(page.locator('app-metric-help')).toContainText('whole months only, never days');
    expect(await axeViolations(page, 'app-metric-widget')).toEqual([]);
  });

  test('the projection table in a view lists the components, their shares and the change from the previous month', async ({ page }) => {
    await openMetricView(page, 'net_cash_projection');

    const table = page.locator('app-metric-widget table');
    await expect(table.locator('thead th')).toContainText(['Change from previous', 'Expected collections on trips that already existed', 'Assumed run rate of trips that do not exist yet']);
    await expect(table.locator('tbody tr').first()).toContainText('82.6%');
    await expect(page.locator('app-metric-widget .widget__foot')).toContainText('may not add up to the expected total');
  });
});

test.describe('needs attention summary', () => {
  const summary = (page: import('@playwright/test').Page) => page.locator('app-attention-summary');

  test('shows the denial rate, the top reasons with an Other row, overlapping categories and four estimated filing bands, above the list', async ({ page }) => {
    await openNeedsAttention(page);

    await expect(summary(page).getByRole('heading', { level: 2 })).toHaveText('Summary, October 2026');
    await expect(summary(page)).toContainText('25.0%');
    await expect(summary(page).locator('table').first().locator('tbody tr')).toHaveCount(3);
    await expect(summary(page)).toContainText('Missing documentation');
    await expect(summary(page)).toContainText('All other reasons');
    await expect(summary(page)).toContainText('overlap and are not added together');
    await expect(summary(page).locator('table').nth(2).locator('tbody tr')).toHaveText([/Past due/, /Due within 10 days/, /Due within 30 days/, /Due later/]);
    await expect(summary(page)).toContainText('These are estimates. The filing limits are a placeholder');
    const above = await page.evaluate(() => document.querySelector('app-attention-summary')!.getBoundingClientRect().bottom <= document.querySelector('#claims-title')!.getBoundingClientRect().top);
    expect(above).toBe(true);
    // No control that has no source behind it.
    await expect(page.locator('app-needs-attention')).not.toContainText(/\bassign|\bIDR\b|patient due/i);
  });

  test('a hostile denial reason is shown as literal text: no element, no script', async ({ page }) => {
    const dialogs: string[] = [];
    page.on('dialog', (dialog) => {
      dialogs.push(dialog.message());
      void dialog.dismiss();
    });
    await openNeedsAttention(page);

    await expect(summary(page).getByText(HOSTILE_REASON, { exact: true })).toBeVisible();
    await expect(page.locator('app-needs-attention img')).toHaveCount(0);
    expect(dialogs).toEqual([]);
  });

  test('says there is no rate, not zero, and no denied claims, when the period has none', async ({ page }) => {
    await mockNoDeniedClaims(page);
    await openNeedsAttention(page);

    await expect(summary(page)).toContainText('No claims');
    await expect(summary(page)).toContainText('No denied claims in this period.');
    await expect(summary(page).locator('table')).toHaveCount(2);
  });

  test('claim counts are never kept by the browser', async ({ page }) => {
    await openNeedsAttention(page);

    const stored = await page.evaluate(() => JSON.stringify([{ ...localStorage }, { ...sessionStorage }]));
    expect(stored).not.toContain('Missing documentation');
    expect(stored).not.toContain('deniedRate');
  });
});
