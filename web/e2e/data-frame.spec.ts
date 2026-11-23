import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from './fixtures';
import { REPORT_DIR, SIZES, axeViolations, layoutErrors, openDashboard, openView } from './support';

/**
 * The shared data frame: every widget on a Saved view, and the net cash panel, can be expanded to full screen with the button at the top left of its header.
 * The browser's Fullscreen API does the work, so Escape is the browser's own and the tests check the result: which element is full screen and what the page shows.
 */
const frameButton = (page: import('@playwright/test').Page) => page.locator('app-widget-host .frame__expand').first();
/**
 * Escape is the browser's own key for leaving full screen, and headless Chromium does not act on a key press we send it. So these tests leave the way the page
 * can (the button, or exitFullscreen), and the component test covers the Escape path by sending the same `fullscreenchange` the browser sends for it.
 */
const leave = (page: import('@playwright/test').Page) => page.evaluate(() => document.exitFullscreen());
const fullscreenTag = (page: import('@playwright/test').Page) => page.evaluate(() => document.fullscreenElement?.tagName.toLowerCase() ?? null);

test.describe('data frame', () => {
  test('a widget has an expand button named after it, and none while the layout is being arranged', async ({ page }) => {
    await openView(page, 5);
    await expect(frameButton(page)).toHaveAccessibleName(/^Expand .+ to full screen$/);

    await openView(page, 5, { edit: true });
    await expect(page.locator('app-widget-host .frame__expand')).toHaveCount(0);
  });

  test('the button takes a widget full screen, the chart grows, and the period and Service Line are shown', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openView(page, 5);
    const chart = page.locator('app-widget-host app-chart .chart').first();
    const before = (await chart.boundingBox())!;

    await frameButton(page).click();

    await expect.poll(() => fullscreenTag(page)).toBe('app-data-frame');
    await expect(frameButton(page)).toHaveAccessibleName('Exit full screen');
    await expect(page.locator('app-widget-host app-data-frame:fullscreen')).toContainText(/\d{4} · (Air|Ground)/);
    await expect.poll(async () => (await chart.boundingBox())!.height).toBeGreaterThan(before.height);
  });

  test('keyboard: Enter expands, leaving full screen keeps focus on the button', async ({ page }) => {
    await openView(page, 5);
    await frameButton(page).focus();

    await page.keyboard.press('Enter');
    await expect.poll(() => fullscreenTag(page)).toBe('app-data-frame');

    await leave(page);
    await expect.poll(() => fullscreenTag(page)).toBeNull();
    await expect(frameButton(page)).toBeFocused();
    await expect(frameButton(page)).toHaveAccessibleName(/^Expand /);
  });

  test('the button collapses it as well', async ({ page }) => {
    await openView(page, 5);
    await frameButton(page).click();
    await expect.poll(() => fullscreenTag(page)).toBe('app-data-frame');

    await frameButton(page).click();

    await expect.poll(() => fullscreenTag(page)).toBeNull();
  });

  test('the net cash panel expands too, and the chart type still switches while it is full screen', async ({ page }) => {
    await openDashboard(page);
    const expand = page.getByRole('button', { name: 'Expand Net cash collected to full screen' });

    await expand.click();
    await expect.poll(() => fullscreenTag(page)).toBe('app-data-frame');
    await page.getByRole('radio', { name: 'Table' }).first().click();

    await expect(page.locator('app-net-cash-panel table')).toBeVisible();
    await leave(page);
    await expect.poll(() => fullscreenTag(page)).toBeNull();
  });

  test('the Transports and Loaded miles tiles expand, and the tiles with no picture to enlarge do not', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openDashboard(page);
    await expect(page.getByRole('button', { name: /^Expand .+ to full screen$/ })).toHaveCount(3);
    await expect(page.locator('app-kpi-card', { hasText: 'Clean claim rate' }).locator('.frame__expand')).toHaveCount(0);
    await expect(page.locator('app-kpi-card', { hasText: 'Open negotiation' }).locator('.frame__expand')).toHaveCount(0);

    const bars = page.locator('app-kpi-card', { hasText: 'Transports' }).locator('.bars');
    const before = (await bars.boundingBox())!;
    await page.getByRole('button', { name: 'Expand Transports to full screen' }).click();

    await expect.poll(() => fullscreenTag(page)).toBe('app-data-frame');
    await expect(page.locator('app-kpi-card app-data-frame:fullscreen')).toContainText(/\d{4} · (Air|Ground)/);
    await expect.poll(async () => (await bars.boundingBox())!.height).toBeGreaterThan(before.height * 3);
    await leave(page);
  });
});

test.describe('data frame on a touch screen', () => {
  test.use({ hasTouch: true, isMobile: true, viewport: { width: 390, height: 844 } });

  test('the button is at least 44px', async ({ page }) => {
    await openView(page, 5);
    const box = (await frameButton(page).boundingBox())!;
    expect(Math.min(box.width, box.height)).toBeGreaterThanOrEqual(44);
  });
});

// A widget in full screen, at every size: it fills the screen, nothing spills, and axe finds nothing.
for (const size of SIZES) {
  test(`widget full screen layout: ${size.name} ${size.width}px`, async ({ page }) => {
    await page.setViewportSize({ width: size.width, height: size.height });
    await openView(page, 5);
    await frameButton(page).click();
    await expect.poll(() => fullscreenTag(page)).toBe('app-data-frame');

    mkdirSync(REPORT_DIR, { recursive: true });
    const label = `fullscreen-${size.name}-${size.width}`;
    await page.screenshot({ path: join(REPORT_DIR, `${label}.png`) });

    const { errors, issues } = await layoutErrors(page);
    const axe = await axeViolations(page);
    writeFileSync(join(REPORT_DIR, `${label}.json`), JSON.stringify({ size, issues, axe }, null, 2));

    expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
    expect(axe, `axe violations at ${label}`).toEqual([]);
  });
}

// A KPI tile in full screen, at every size.
for (const size of SIZES) {
  test(`tile full screen layout: ${size.name} ${size.width}px`, async ({ page }) => {
    await page.setViewportSize({ width: size.width, height: size.height });
    await openDashboard(page);
    await page.getByRole('button', { name: 'Expand Loaded miles to full screen' }).click();
    await expect.poll(() => fullscreenTag(page)).toBe('app-data-frame');

    mkdirSync(REPORT_DIR, { recursive: true });
    const label = `fullscreen-tile-${size.name}-${size.width}`;
    await page.screenshot({ path: join(REPORT_DIR, `${label}.png`) });

    const { errors } = await layoutErrors(page);
    const axe = await axeViolations(page);
    expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
    expect(axe, `axe violations at ${label}`).toEqual([]);
  });
}
