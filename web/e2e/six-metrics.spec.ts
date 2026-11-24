import { expect, HOSTILE_NAME, test } from './fixtures';
import { SIX_METRICS, axeViolations, expandHelp, lastAnnouncement, openMetricView, openMetricViewGroundOnly, openPaletteUnderGround, openSixMetricView, openView } from './support';

/** The six Metrics that came with the catalog's appliesTo field, as pages, widgets and palette entries. The API is mocked. */
test.describe('metric views', () => {
  for (const metric of SIX_METRICS) {
    test(`${metric.name} opens through the widget path with its table, help, data stamp and context badge`, async ({ page }) => {
      await openMetricView(page, metric.key);

      await expect(page.getByRole('heading', { level: 1, name: metric.name })).toBeFocused();
      const region = page.locator('app-metric-widget [role="region"]').first();
      await expect(region).toHaveAttribute('tabindex', '0');
      await expect(region.locator('table caption')).not.toBeEmpty();
      expect(await region.locator('thead th').evaluateAll((els) => els.every((e) => e.getAttribute('scope') === 'col'))).toBe(true);
      await expect(page.locator('app-metric-view app-freshness-stamp')).toContainText('Data through');
      await expect(page.locator('app-metric-view app-metric-help')).toBeVisible();
      await expandHelp(page);
      expect(await axeViolations(page)).toEqual([]);

      const badge = page.locator('app-metric-view app-context-badge');
      await expect(badge).toBeVisible();
      if (metric.airOnly) await expect(badge).toHaveText('Air · All bases · Air only');
      else await expect(badge).toHaveText('Air · All bases');
    });
  }

  test('cash per transport is labelled as posted cash and never as an expected payment', async ({ page }) => {
    await openMetricView(page, 'aircraft_performance');

    const heading = 'Net cash per transport (cash posted in the month ÷ transports)';
    await expect(page.locator('app-metric-widget thead th', { hasText: heading }).first()).toBeVisible();
    await expect(page.locator('app-metric-widget thead th').first()).toHaveText('Tail number');
    await expect(page.locator('app-metric-widget .widget__foot').first()).toContainText('not an expected payment');
  });

  test('the base analysis names the Reporting Division from the Label, and the payer mix effect says it is expected, not posted cash', async ({ page }) => {
    await openMetricView(page, 'base_analysis');
    await expect(page.locator('app-metric-widget thead th').first()).toHaveText('Base location');
    await openMetricView(page, 'base_analysis', 'period=202610&line=all');
    await expect(page.locator('app-metric-widget thead th').first()).toHaveText('Reporting division');

    await openMetricView(page, 'payer_mix_financial_effect');
    await expect(page.locator('app-metric-widget .widget__foot').first()).toContainText('expected effect, not posted cash');
    await expect(page.locator('app-metric-widget .widget__foot').first()).toContainText('the same amount repeats each month');
    await expect(page.locator('app-metric-widget .widget__foot').first()).toContainText('cash posted through');
  });

  test('an air-only Metric under Ground only shows the not-available state and never calls the API', async ({ page }) => {
    const calls: string[] = [];
    page.on('request', (r) => {
      if (/aircraft-performance|loaded-miles-by-aircraft/.test(r.url())) calls.push(r.url());
    });
    await openMetricViewGroundOnly(page);

    await expect(page.locator('app-metric-view .widget__note')).toContainText("isn't available for ground");
    await expect(page.locator('app-metric-view table')).toHaveCount(0);
    expect(calls).toEqual([]);
    expect(await axeViolations(page)).toEqual([]);
  });
});

test.describe('hostile warehouse text', () => {
  test('a tail number, a facility name and an aircraft under a base are drawn literally, never as markup', async ({ page }) => {
    const dialogs: string[] = [];
    page.on('dialog', (dialog) => {
      dialogs.push(dialog.message());
      void dialog.dismiss();
    });
    for (const key of ['aircraft_performance', 'loaded_miles_by_aircraft', 'base_analysis', 'transports_by_pickup_location']) {
      await openMetricView(page, key);
      await expect(page.locator('app-metric-widget table').getByText(HOSTILE_NAME, { exact: true }).first()).toBeVisible();
      expect(await page.locator('app-metric-widget img, app-metric-widget script').count()).toBe(0);
    }
    expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();
    expect(dialogs).toEqual([]);
  });

  test('the chart tooltip shows a hostile tail literally and creates no element', async ({ page }) => {
    const dialogs: string[] = [];
    page.on('dialog', (dialog) => {
      dialogs.push(dialog.message());
      void dialog.dismiss();
    });
    await page.setViewportSize({ width: 1440, height: 900 });
    await openSixMetricView(page);
    const canvas = page.locator('[data-widget-id="s4"] canvas').first();
    await canvas.scrollIntoViewIfNeeded();
    const box = (await canvas.boundingBox())!;

    const tooltip = page.locator('div[style*="pointer-events"]').filter({ hasText: HOSTILE_NAME });
    let found = false;
    for (let step = 1; step < 40 && !found; step += 1) {
      await page.mouse.move(box.x + (box.width * step) / 40, box.y + box.height * 0.7, { steps: 2 });
      found = (await tooltip.count()) > 0 && (await tooltip.first().isVisible());
    }
    expect(found, 'a tooltip showing the hostile tail').toBe(true);
    expect(await tooltip.first().locator('img, script').count()).toBe(0);
    expect(await page.locator('img[src="x"]').count()).toBe(0);
    expect(dialogs).toEqual([]);
  });
});

test.describe('the six Metrics on a saved view', () => {
  test('every widget draws, tables have text alternatives and the page passes axe', async ({ page }) => {
    await openSixMetricView(page, 'a');

    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(6);
    await expect(page.locator('app-metric-widget app-chart [role="img"]')).toHaveCount(5);
    for (const label of await page.locator('app-metric-widget app-chart [role="img"]').evaluateAll((els) => els.map((e) => e.getAttribute('aria-label') ?? ''))) {
      expect(label.length).toBeGreaterThan(20);
    }
    // The base analysis table and the table of the aircraft that flew from each base.
    await expect(page.locator('app-metric-widget [role="region"] table')).toHaveCount(2);
    expect(await axeViolations(page)).toEqual([]);

    await openSixMetricView(page, 'b');
    await expect(page.locator('app-metric-widget .widget__value')).toContainText('$1,000.00');
    await expect(page.locator('app-metric-widget').filter({ hasText: 'expected effect, not posted cash' }).first()).toBeVisible();
    expect(await page.locator('app-metric-widget [role="region"]').count()).toBeGreaterThanOrEqual(2);
    expect(await axeViolations(page)).toEqual([]);
  });

  test('the two air-only widgets show Air only beside the badge and the others show nothing extra', async ({ page }) => {
    await openSixMetricView(page, 'a');

    const badges = await page.locator('app-metric-widget app-context-badge').allTextContents();
    expect(badges.filter((t) => t.endsWith('Air only'))).toHaveLength(2);
    expect(badges.filter((t) => t.includes('only'))).toHaveLength(2);
  });

  test('under Ground only the air-only widgets say so and stop asking, and the others stay', async ({ page }) => {
    await openSixMetricView(page, 'a');
    const calls: string[] = [];
    page.on('request', (r) => {
      if (/aircraft-performance|loaded-miles-by-aircraft/.test(r.url())) calls.push(new URL(r.url()).searchParams.get('serviceLine') ?? '');
    });

    await page.getByRole('radio', { name: 'Ground only' }).click();

    await expect(page.locator('app-metric-widget .widget__note', { hasText: "isn't available for ground" })).toHaveCount(2);
    expect(calls).toEqual([]);
    await expect(page.locator('app-metric-widget app-chart')).toHaveCount(3);
    expect(await axeViolations(page)).toEqual([]);
  });
});

test.describe('the palette and the Service Line', () => {
  test('every Metric names its scope, as text', async ({ page }) => {
    await openView(page, 5, { edit: true });

    const item = (name: string) => page.locator('app-metric-palette li', { hasText: name }).first();
    await expect(item('Aircraft performance').locator('.palette__scope')).toHaveText('Air only');
    await expect(item('Loaded miles by aircraft').locator('.palette__scope')).toHaveText('Air only');
    await expect(item('Base analysis').locator('.palette__scope')).toHaveText('Air and ground');
    await expect(item('Payer mix financial effect').locator('.palette__scope')).toHaveText('Air and ground');
  });

  test('keyboard: an air-only Metric is listed under Ground only but cannot be added, and says why', async ({ page }) => {
    await openPaletteUnderGround(page);
    const before = await page.locator('app-grid-canvas li.item').count();
    const add = page.getByRole('button', { name: 'Add Aircraft performance to view' });

    await add.focus();
    await expect(add).toBeFocused();
    await page.keyboard.press('Enter');

    await expect(add).toHaveAttribute('aria-disabled', 'true');
    const reason = page.locator('#line-reason-aircraft_performance');
    await expect(reason).toBeVisible();
    await expect(reason).toHaveText('Covers air only');
    await expect(add).toHaveAttribute('aria-describedby', 'line-reason-aircraft_performance');
    await expect.poll(() => lastAnnouncement(page)).toBe("Aircraft performance can't be added under this Service Line. Covers air only.");
    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(before);
    // A Metric for both lines is still addable under Ground only.
    await page.getByRole('button', { name: 'Add Base analysis to view' }).focus();
    await page.keyboard.press('Enter');
    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(before + 1);
    expect(await axeViolations(page)).toEqual([]);
  });

  test('back on Air only the same Metric adds, and the saved layout holds only keys, presentations and positions', async ({ page }) => {
    await openPaletteUnderGround(page);
    await page.getByRole('radio', { name: 'Air only' }).click();
    await page.locator('.palette__reason').first().waitFor({ state: 'detached' });
    const before = await page.locator('app-grid-canvas li.item').count();

    await page.getByRole('button', { name: 'Add Aircraft performance to view' }).click();
    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(before + 1);
    const [request] = await Promise.all([
      page.waitForRequest((r) => r.method() === 'PUT' || (r.method() === 'POST' && /saved-views(\/\d+)?$/.test(r.url()))),
      page.getByRole('button', { name: 'Save', exact: true }).click(),
    ]);

    const body = request.postDataJSON() as { layout: { widgets: Record<string, unknown>[] } };
    const added = body.layout.widgets.find((w) => w['metricKey'] === 'aircraft_performance')!;
    expect(Object.keys(added).sort()).toEqual(['h', 'id', 'metricKey', 'presentation', 'w', 'x', 'y']);
    expect(JSON.stringify(body)).not.toContain('N611CV');
    expect(JSON.stringify(body)).not.toContain('Base location');
  });
});
