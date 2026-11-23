import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import type { Page } from '@playwright/test';
import { DATA_THROUGH, expect, test } from './fixtures';
import { NEWER_DATA_THROUGH, REPORT_DIR, SIZES, axeViolations, lastAnnouncement, layoutErrors, openDashboard, regainFocus, warehouseLoadsAgain } from './support';

const NEWER = NEWER_DATA_THROUGH;

const notice = (page: Page) => page.locator('app-data-updated-notice');

test.describe('data updated notice', () => {
  test('stays out of the way while the data is the same', async ({ page }) => {
    await openDashboard(page);
    await regainFocus(page);
    await page.waitForTimeout(300);

    await expect(notice(page)).not.toContainText('Data updated');
    await expect(notice(page).getByRole('button')).toHaveCount(0);
  });

  test('appears when the warehouse has newer data, leaves the figures alone, and does not take focus', async ({ page }) => {
    await openDashboard(page);
    const figures = await page.locator('app-net-cash-panel').innerText();
    const focusedBefore = await page.evaluate(() => document.activeElement?.tagName);

    await warehouseLoadsAgain(page);
    await regainFocus(page);

    await expect(notice(page).getByRole('status')).toContainText('Data updated through Nov 23');
    await expect(notice(page).getByRole('button', { name: 'Update view' })).toBeVisible();
    expect(await page.evaluate(() => document.activeElement?.tagName)).toBe(focusedBefore);
    expect(await page.locator('app-net-cash-panel').innerText()).toBe(figures);
    await expect(page.locator('app-freshness-stamp').first()).toContainText(`Nov 22`);
    await expect(notice(page).getByRole('status')).toHaveAttribute('aria-live', 'polite');
  });

  test('is also found when the tab becomes visible again', async ({ page }) => {
    await openDashboard(page);
    await warehouseLoadsAgain(page);

    await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));

    await expect(notice(page)).toContainText('Data updated through Nov 23');
  });

  test('Update view works from the keyboard, loads the metrics again and announces the new date', async ({ page }) => {
    await openDashboard(page);
    let seriesRequests = 0;
    await page.route(/net-collections\?/, (route) => {
      seriesRequests++;
      return route.fallback();
    });
    await warehouseLoadsAgain(page);
    await regainFocus(page);
    const button = notice(page).getByRole('button', { name: 'Update view' });
    await button.waitFor();

    await button.focus();
    await page.keyboard.press('Enter');

    await expect(notice(page)).not.toContainText('Data updated');
    await expect.poll(() => seriesRequests).toBeGreaterThan(0);
    await expect.poll(() => lastAnnouncement(page)).toBe('Updated to Nov 23');
    await expect(page.locator('app-freshness-stamp').first()).toContainText('Nov 23');
  });

  test('checks again after moving to another page', async ({ page }) => {
    await openDashboard(page);
    await warehouseLoadsAgain(page);

    await page.getByRole('link', { name: 'Saved views' }).click();

    await expect(notice(page)).toContainText('Data updated through Nov 23');
  });

  for (const size of SIZES) {
    test(`the notice fits and passes axe: ${size.name} ${size.width}px`, async ({ page }) => {
      await page.setViewportSize({ width: size.width, height: size.height });
      await openDashboard(page);
      await warehouseLoadsAgain(page);
      await regainFocus(page);
      await notice(page).getByRole('button').waitFor();

      mkdirSync(REPORT_DIR, { recursive: true });
      await page.screenshot({ path: join(REPORT_DIR, `data-updated-${size.name}-${size.width}.png`), fullPage: false });

      expect((await layoutErrors(page)).errors).toEqual([]);
      expect(await axeViolations(page)).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    });
  }

  test('the fixtures start on the date the notice compares against', () => {
    expect(DATA_THROUGH < NEWER).toBe(true);
  });
});
