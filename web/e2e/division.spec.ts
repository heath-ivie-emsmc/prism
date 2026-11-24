import { expect, test } from './fixtures';
import { GROUND_ONLY_DIVISION } from './fixtures';
import { openDashboard, openEmptyDivisionView, openPayerMixPage } from './support';

/** The Reporting Division control: the Provider's real divisions from the API, shown as text, applied by key, and the same control on the pages that follow the dashboard. */
test.describe('Reporting Division control', () => {
  test('lists the Provider\'s divisions from the API, with a hostile name shown as plain text', async ({ page }) => {
    await openDashboard(page);
    const select = page.getByRole('combobox', { name: 'Base location' });
    await select.click();

    const options = page.getByRole('option');
    await expect(options.filter({ hasText: 'Riverside Base' })).toHaveCount(1);
    await expect(options.filter({ hasText: 'Summit Base' })).toHaveCount(1);
    await expect(options.filter({ hasText: '<img src=x' })).toHaveCount(1);
    await expect(page.locator('app-division-select img')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __divisionXss?: number }).__divisionXss)).toBeUndefined();
  });

  test('choosing a division asks the Metrics for its key, from the keyboard', async ({ page }) => {
    await openDashboard(page);
    const asked = page.waitForRequest((r) => /net-collections\/transports/.test(r.url()) && new URL(r.url()).searchParams.get('division') === 'summit');
    const select = page.getByRole('combobox', { name: 'Base location' });
    await select.focus();
    await page.keyboard.press('Enter');
    await page.getByRole('option', { name: 'Summit Base' }).click();
    await asked;
  });

  test('the payer mix follows the division key in the address, off, with the reason and the division name', async ({ page }) => {
    await openPayerMixPage(page, 'rotor-wing', 'period=202610&line=air&division=summit');
    const select = page.getByRole('combobox', { name: 'Base location' });
    await expect(select).toBeDisabled();
    await expect(select).toHaveValue('Summit Base');
    await expect(page.locator('app-division-select')).toContainText('Set on the dashboard');
  });

  test('says plainly when the list of divisions cannot load, and offers to try again', async ({ page }) => {
    await page.route('**/api/v1/providers/*/metrics/net-collections/divisions**', (route) => route.fulfill({ status: 503, json: { title: 'x' } }));
    await openDashboard(page);
    const control = page.locator('app-division-select');
    await expect(control).toContainText('could not be loaded');
    await expect(control.getByRole('button', { name: 'Try again' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Base location' })).toBeDisabled();
  });
});

test.describe('Reporting Division scope on the dashboard', () => {
  test('choosing a division names it in the Context Badge and shows the note about cash that belongs to no division', async ({ page }) => {
    await openDashboard(page);
    const asked = page.waitForRequest((r) => /net-collections(\?|$)/.test(new URL(r.url()).pathname + new URL(r.url()).search) && new URL(r.url()).searchParams.get('division') === 'summit');
    await page.getByRole('combobox', { name: 'Base location' }).click();
    await page.getByRole('option', { name: 'Summit Base' }).click();
    await asked;

    await expect(page.locator('app-net-cash-panel app-context-badge, app-net-cash-panel .badge').first()).toContainText('Summit Base');
    await expect(page.locator('#net-cash-division-note')).toContainText('belongs to no division');
    await expect(page.locator('app-context-badge').first()).not.toContainText('Selected');
  });

  test('a hostile division name appears in the badge as text and nothing runs', async ({ page }) => {
    await openDashboard(page);
    await page.getByRole('combobox', { name: 'Base location' }).click();
    await page.getByRole('option').filter({ hasText: '<img src=x' }).click();
    await expect(page.locator('app-context-badge').first()).toContainText('<img src=x');
    await expect(page.locator('app-context-badge img')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __divisionXss?: number }).__divisionXss)).toBeUndefined();
  });
});

test.describe('Reporting Division and Service Line', () => {
  test('the picker lists only the divisions that have transports under the chosen Service Line, and all of them under Air + ground', async ({ page }) => {
    await openDashboard(page);
    const names = async (label: string): Promise<string[]> => {
      await page.getByRole('combobox', { name: label }).click();
      await expect(page.getByRole('option').first()).toBeVisible();
      const texts = await page.getByRole('option').allTextContents();
      await page.keyboard.press('Escape');
      return texts.map((t) => t.replace(/^check|keyboard_return$/g, '').trim());
    };
    expect(await names('Base location')).not.toContain('Mercy Flights - Ground Critical Care');

    await page.getByRole('radio', { name: 'Ground only' }).click();
    await expect(page.getByRole('combobox', { name: 'Reporting division' })).toBeVisible();
    expect(await names('Reporting division')).toEqual(['All reporting divisions', 'Summit Base', 'Mercy Flights - Ground Critical Care']);

    await page.getByRole('radio', { name: 'Air + ground' }).click();
    expect(await names('Reporting division')).toContain('Riverside Base');
    expect(await names('Reporting division')).toContain('Mercy Flights - Ground Critical Care');
  });

  test('a division chosen under Ground only is kept, shown as having no air transports, announced, and cleared by choosing all', async ({ page }) => {
    await openEmptyDivisionView(page);
    const control = page.locator('app-division-select');

    await expect(page.getByRole('combobox', { name: 'Base location' })).toHaveValue('Mercy Flights - Ground Critical Care (no air transports)');
    await expect(control).toContainText('has no air transports, so its figures are empty');
    await expect(control.locator('[role="status"]')).toContainText('has no air transports');

    await page.getByRole('combobox', { name: 'Base location' }).click();
    await page.getByRole('option', { name: 'All bases' }).click();
    await expect(control).not.toContainText('has no air transports');
    await expect(page.locator('[data-testid="widget-empty"]')).toHaveCount(0);
  });

  test('every widget of the saved view names the division and the line when the answer is empty, and keeps the help', async ({ page }) => {
    await openEmptyDivisionView(page);
    const empties = page.locator('[data-testid="widget-empty"]');

    await expect(empties).toHaveCount(4);
    for (const text of await empties.allTextContents()) {
      expect(text).toMatch(/^No .+ for Mercy Flights - Ground Critical Care under Air only in October 2026\. Try All bases or Air \+ ground\.$/);
    }
    await expect(page.locator('app-metric-widget app-metric-help').first()).toBeVisible();
    expect(GROUND_ONLY_DIVISION).toBe('ground-critical-care');
  });

  test('a Metric with no division support keeps its answer and its requests when a division is chosen, and says all bases', async ({ page }) => {
    await openDashboard(page);
    const sent: string[] = [];
    page.on('request', (r) => {
      if (/net-collections\/(projection|clean-claims)/.test(r.url())) sent.push(r.url());
    });
    await page.getByRole('combobox', { name: 'Base location' }).click();
    await page.getByRole('option', { name: 'Summit Base' }).click();
    await page.waitForTimeout(500);

    expect(sent).toEqual([]);
    await expect(page.locator('app-projection-panel app-context-badge')).toContainText('All bases');
  });
});
