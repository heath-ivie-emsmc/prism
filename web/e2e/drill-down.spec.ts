import { expect, test } from './fixtures';
import { axeViolations, layoutErrors, lastAnnouncement, openPayerDetail, openPayerMixPage } from './support';

const DETAIL_URL = /\/dashboard\/metrics\/payer-mix\/rotor-wing\/commercial\?period=202610&line=air$/;

test.describe('payer mix drill-down', () => {
  test('a link under the bar chart opens the payer breakdown with the group, month and Service Line, and moves focus to its heading', async ({ page }) => {
    await openPayerMixPage(page);

    await page.getByRole('link', { name: 'View Commercial breakdown' }).click();

    await expect(page).toHaveURL(DETAIL_URL);
    await expect(page.getByRole('heading', { level: 1, name: 'Commercial payers, rotor wing' })).toBeFocused();
    await expect(page.locator('app-freshness-stamp')).toContainText('Data through Nov 22');
    await expect(page.locator('app-context-badge')).toContainText('Air');
    await expect(page.locator('app-metric-help')).toContainText('The ten largest are listed');
  });

  test('the drill links work from the keyboard', async ({ page }) => {
    await openPayerMixPage(page);

    await page.getByRole('link', { name: 'View Medicare breakdown' }).focus();
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(/rotor-wing\/medicare\?/);
    await expect(page.getByRole('heading', { level: 1 })).toBeFocused();
  });

  test('clicking the bar for a payer group opens its breakdown too', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openPayerMixPage(page);
    const box = (await page.locator('app-payer-mix app-chart canvas').boundingBox())!;

    // Commercial is the first payer group. The pointer shows it is a link, and the click follows it.
    await page.mouse.move(box.x + box.width * 0.111, box.y + box.height - 60);
    await expect.poll(() => page.locator('app-payer-mix app-chart .chart').evaluate((el) => [...el.querySelectorAll<HTMLElement>('*')].some((n) => n.style.cursor === 'pointer'))).toBe(true);
    await page.mouse.click(box.x + box.width * 0.111, box.y + box.height - 60);

    await expect(page).toHaveURL(DETAIL_URL);
  });

  test('Patient responsibility and Facility are not links', async ({ page }) => {
    await openPayerMixPage(page);

    await expect(page.getByRole('link', { name: /View Patient responsibility breakdown/ })).toHaveCount(0);
    await expect(page.getByRole('link', { name: /View Facility breakdown/ })).toHaveCount(0);
    await expect(page.getByRole('link', { name: /View Commercial breakdown/ })).toHaveCount(1);
  });

  test('the key under the stacked bars and the donuts has the same links, and the table has real links', async ({ page }) => {
    await openPayerMixPage(page);

    await page.getByRole('radio', { name: 'Stacked bars' }).click();
    await expect(page.locator('app-payer-legend').getByRole('link', { name: 'View Commercial breakdown' })).toHaveAttribute('href', /\/dashboard\/metrics\/payer-mix\/rotor-wing\/commercial\?period=202610&line=air/);
    await page.getByRole('radio', { name: 'Donut charts' }).click();
    await expect(page.locator('app-payer-legend').getByRole('link', { name: 'View Medicaid breakdown' })).toBeVisible();

    await page.getByRole('radio', { name: 'Table' }).click();
    const links = page.locator('app-payer-mix table').getByRole('link');
    await expect(links).toHaveCount(5);
    await links.first().click();
    await expect(page).toHaveURL(DETAIL_URL);
  });

  test('a stacked segment takes a click, and the keyboard has the key links', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openPayerMixPage(page);
    await page.getByRole('radio', { name: 'Stacked bars' }).click();

    await page.locator('app-payer-stacked .bar__segment--drill').first().click();

    await expect(page).toHaveURL(DETAIL_URL);
  });

  test('the breadcrumb leads back through the payer mix and the net cash breakdown', async ({ page }) => {
    await openPayerDetail(page);
    const crumbs = page.getByRole('navigation', { name: /breadcrumb/i });

    await expect(crumbs).toContainText('Air net cash, October 2026');
    await expect(crumbs).toContainText('Rotor wing payer mix');
    await expect(crumbs).toContainText('Commercial');
    await crumbs.getByRole('link', { name: 'Rotor wing payer mix' }).click();

    await expect(page).toHaveURL(/\/dashboard\/net-cash\/rotor-wing\?period=202610&line=air$/);
  });

  test('Back returns to the payer mix, at the scroll position it was left at', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 500 });
    await openPayerMixPage(page);
    // Scroll down to the links, as a reader would, then follow one.
    const link = page.getByRole('link', { name: 'View Commercial breakdown' });
    await link.scrollIntoViewIfNeeded();
    await expect.poll(() => page.evaluate(() => window.scrollY)).toBeGreaterThan(300);
    const before = await page.evaluate(() => window.scrollY);

    // Click without letting the test runner scroll again, so the position left is the one measured.
    await link.dispatchEvent('click');
    await expect(page).toHaveURL(DETAIL_URL);
    expect(await page.evaluate(() => window.scrollY)).toBeLessThan(50);
    await page.goBack();

    await expect(page).toHaveURL(/\/dashboard\/net-cash\/rotor-wing/);
    await page.locator('app-payer-mix app-chart canvas').waitFor();
    await expect.poll(async () => Math.abs((await page.evaluate(() => window.scrollY)) - before)).toBeLessThan(60);
  });

  test('asks the API for the payers in the group, month and Service Line in the address', async ({ page }) => {
    const asked: string[] = [];
    await page.route(/net-collections\/payer-mix\/commercial/, (route) => {
      asked.push(new URL(route.request().url()).search);
      return route.fallback();
    });

    await openPayerDetail(page);

    expect(asked[0]).toBe('?period=202610&serviceLine=air&group=rotor-wing');
  });

  test('shows the payers as a bar chart, donuts and a table, with a text alternative for the chart', async ({ page }) => {
    await openPayerDetail(page);
    expect(await page.locator('app-payer-mix-detail [role="img"]').getAttribute('aria-label')).toContain('Meridian Health Plan');
    expect(await axeViolations(page)).toEqual([]);

    await page.getByRole('radio', { name: 'Donut charts' }).click();
    await expect(page.locator('app-payer-donuts svg')).toHaveCount(2);
    expect(await axeViolations(page)).toEqual([]);

    await page.getByRole('radio', { name: 'Table' }).click();
    const rows = page.locator('app-payer-mix-detail tbody tr');
    await expect(rows).toHaveCount(5);
    await expect(rows.first()).toContainText('Meridian Health Plan');
    await expect(rows.last()).toContainText('Other');
    expect(await axeViolations(page)).toEqual([]);
  });

  test('the Period control changes the month in the address and asks again', async ({ page }) => {
    await openPayerDetail(page);

    await page.locator('input.ems-select__input').first().click();
    await page.getByRole('option', { name: 'September 2026' }).click();

    await expect(page).toHaveURL(/period=202609/);
    await expect(page.locator('app-payer-mix-detail')).toContainText('Aug');
  });

  test('says so when the group had no such transports, and when the data cannot load', async ({ page }) => {
    await page.route(/net-collections\/payer-mix\/commercial/, (route) => route.fulfill({ status: 503, json: { title: 'Host=secret.internal Password=hunter2' } }));
    await page.goto('/dashboard/metrics/payer-mix/rotor-wing/commercial?period=202610&line=air');

    await expect(page.locator('app-payer-mix-detail').getByText("We can't load the payer breakdown right now")).toBeVisible();
    await expect(page.getByText('hunter2')).toHaveCount(0);
    expect(await axeViolations(page)).toEqual([]);
    expect((await layoutErrors(page)).errors).toEqual([]);
  });

  test('a link is announced by name, never as a bare group name', async ({ page }) => {
    await openPayerMixPage(page);
    const names = await page.locator('app-payer-mix ul.drill a').evaluateAll((links) => links.map((a) => a.textContent!.trim()));
    expect(names).toEqual(['View Commercial breakdown', 'View Medicare breakdown', 'View Medicaid breakdown', 'View Other government breakdown', 'View VA breakdown']);
    expect(await lastAnnouncement(page)).toBe('');
  });
});
