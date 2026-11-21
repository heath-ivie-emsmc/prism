import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from './fixtures';
import { REPORT_DIR, SIZES, axeViolations, layoutErrors, openDashboard } from './support';

async function openBreakdown(page: import('@playwright/test').Page, line: 'air' | 'ground' = 'air'): Promise<void> {
  await page.goto(`/dashboard/net-cash?period=202610&line=${line}`);
  await page.locator('app-net-cash-breakdown table').waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(200);
}

test.describe('net cash breakdown', () => {
  test('the headline amount opens the breakdown for the month and Service Line on screen', async ({ page }) => {
    await openDashboard(page);

    await page.getByRole('link', { name: /see what drove aircraft type/i }).click();

    await expect(page).toHaveURL(/\/dashboard\/net-cash\?period=202610&line=air$/);
    await expect(page.getByRole('heading', { level: 2, name: 'Air net cash by aircraft type' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: /By base and aircraft, October 2026/ })).toBeVisible();
  });

  test('the headline link is reachable and usable from the keyboard', async ({ page }) => {
    await openDashboard(page);
    const link = page.getByRole('link', { name: /see what drove/i });

    await link.focus();
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(/net-cash/);
  });

  test('breadcrumbs name the page and lead back to the dashboard', async ({ page }) => {
    await openBreakdown(page);
    const crumbs = page.getByRole('navigation', { name: /breadcrumb/i });

    await expect(crumbs).toContainText('Air net cash, October 2026');
    await crumbs.getByRole('link', { name: 'Dashboard' }).click();

    await expect(page).toHaveURL(/\/dashboard$/);
  });

  test('air breaks down by aircraft type, with the drop explained in words', async ({ page }) => {
    await openBreakdown(page);

    await expect(page.getByRole('heading', { level: 3, name: 'Rotor wing' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 3, name: 'Fixed wing' })).toBeVisible();
    await expect(page.getByText('Fixed wing accounts for $137,770.00 of the $150,220.00 drop (92%).')).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Tail number' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'North', exact: true })).toBeVisible();
  });

  test('an open accounting period says cash is still posting, and a closed one does not', async ({ page }) => {
    await openBreakdown(page);
    await expect(page.getByText('October 2026 is still open, so cash is still posting.')).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);

    await page.goto('/dashboard/net-cash?period=202608&line=air');
    await page.locator('app-net-cash-breakdown table').waitFor();

    await expect(page.getByText(/is still open/)).toHaveCount(0);
  });

  test('ground breaks down by level of service and vehicle', async ({ page }) => {
    await openBreakdown(page, 'ground');

    await expect(page.getByRole('heading', { level: 2, name: 'Ground net cash by level of service' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 3, name: 'Basic life support' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Unit' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Station' })).toBeVisible();
  });

  test('changing the Period changes the month in the address and the page', async ({ page }) => {
    await openBreakdown(page);

    await page.locator('input.ems-select__input').first().click();
    await page.getByRole('option', { name: 'September 2026' }).click();

    await expect(page).toHaveURL(/period=202609/);
    await expect(page.getByRole('heading', { level: 2, name: /September 2026/ })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Change vs Aug' })).toBeVisible();
  });

  test('the page asks the API for the month and Service Line in the address', async ({ page }) => {
    const asked: string[] = [];
    await page.route(/net-collections\/breakdown/, (route) => {
      asked.push(new URL(route.request().url()).search);
      return route.fallback();
    });

    await openBreakdown(page, 'ground');

    expect(asked[0]).toBe('?period=202610&serviceLine=ground');
  });

  test('when the breakdown cannot load it says so and stays accessible', async ({ page }) => {
    await page.route(/net-collections\/breakdown/, (route) => route.fulfill({ status: 503, json: { title: 'Service Unavailable' } }));
    await page.goto('/dashboard/net-cash?period=202610&line=air');

    await expect(page.getByText("We can't load the breakdown right now")).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

  test('a month with no net cash says so', async ({ page }) => {
    await page.route(/net-collections\/breakdown/, (route) =>
      route.fulfill({ json: { metric: { key: 'net_cash_breakdown', name: 'n', description: 'd', unit: 'USD' }, providerId: '526', period: '202610', priorPeriod: '202609', serviceLine: 'air', groups: [], rows: [] } }),
    );
    await page.goto('/dashboard/net-cash?period=202610&line=air');

    await expect(page.getByText('There is no net cash for October 2026.')).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

  for (const line of ['air', 'ground'] as const) {
    for (const size of SIZES) {
      test(`${line} breakdown layout and accessibility: ${size.name} ${size.width}px`, async ({ page }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        await openBreakdown(page, line);

        mkdirSync(REPORT_DIR, { recursive: true });
        await page.screenshot({ path: join(REPORT_DIR, `breakdown-${line}-${size.name}-${size.width}.png`), fullPage: true });

        expect((await layoutErrors(page)).errors).toEqual([]);
        expect(await axeViolations(page)).toEqual([]);
      });
    }
  }
});
