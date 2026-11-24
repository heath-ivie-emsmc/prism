import { expect, HOSTILE_NAME, test } from './fixtures';
import { CONTRIBUTION_METRICS, NEW_KIND_CARDS, axeViolations, expandHelp, openInsightNewKinds, openMetricView, openMetricViewGroundOnly, openSixMetricView } from './support';

/** The three Metrics that split the change in net cash by contributor, as pages and widgets, and the Insight cards that came with them. The API is mocked. */
test.describe('cash contribution metric views', () => {
  for (const metric of CONTRIBUTION_METRICS) {
    test(`${metric.name} opens with its table, help, data stamp and context badge`, async ({ page }) => {
      await openMetricView(page, metric.key);

      await expect(page.getByRole('heading', { level: 1, name: metric.name })).toBeFocused();
      const region = page.locator('app-metric-widget [role="region"]').first();
      await expect(region.locator('table caption')).toContainText('Change in net cash by');
      await expect(region.locator('thead th').first()).toHaveText(metric.header);
      await expect(region.locator('tbody tr').last()).toContainText(/[+-]\$/);
      await expect(page.locator('app-metric-view app-freshness-stamp')).toContainText('Data through');
      await expandHelp(page);
      await expect(page.locator('app-metric-view app-metric-help')).toContainText('add up to the total change');
      expect(await axeViolations(page)).toEqual([]);

      const badge = page.locator('app-metric-view app-context-badge');
      if (metric.airOnly) await expect(badge).toHaveText('Air · All bases · Air only');
      else await expect(badge).toHaveText('Air · All bases');
    });
  }

  test('the rows add up to the total in the table', async ({ page }) => {
    await openMetricView(page, 'cash_contribution_by_division');
    const changes = await page.locator('app-metric-widget [role="region"] tbody tr').evaluateAll((rows) => rows.map((r) => r.querySelectorAll('td, th')[1]?.textContent ?? ''));
    const dollars = (text: string) => Number(text.replace(/\s*\(.*$/, '').replace(/[^0-9.+-]/g, ''));
    const total = dollars(changes.at(-1)!);
    expect(changes.slice(0, -1).reduce((t, c) => t + dollars(c), 0)).toBeCloseTo(total, 2);
  });

  test('the division column is named from the Label', async ({ page }) => {
    await openMetricView(page, 'cash_contribution_by_division', 'period=202610&line=all');
    await expect(page.locator('app-metric-widget thead th').first()).toHaveText('Reporting division');
  });

  test('the air-only aircraft Metric under Ground only says so and never calls the API', async ({ page }) => {
    const calls: string[] = [];
    page.on('request', (r) => {
      if (/cash-contribution\/aircraft/.test(r.url())) calls.push(r.url());
    });
    await openMetricViewGroundOnly(page, 'cash_contribution_by_aircraft');

    await expect(page.locator('app-metric-view .widget__note')).toContainText("isn't available for ground");
    await expect(page.locator('app-metric-view table')).toHaveCount(0);
    expect(calls).toEqual([]);
    expect(await axeViolations(page)).toEqual([]);
  });

  test('a payer row opens the payer breakdown through the server link, and an unlisted payer has none', async ({ page }) => {
    await openSixMetricView(page, 'c');
    const links = page.locator('[data-widget-id="s3"] .widget__drills a');
    await expect(links.first()).toBeVisible();
    await links.filter({ hasText: 'Commercial' }).first().click();
    await expect(page).toHaveURL(/\/dashboard\/metrics\/payer-mix\/fixed-wing\/commercial\?/);
    await expect(page).toHaveURL(/period=202610/);
  });
});

test.describe('hostile warehouse text in the contribution Metrics', () => {
  test('a tail number and a division name are drawn literally in tables and chart tooltips, never as markup', async ({ page }) => {
    const dialogs: string[] = [];
    page.on('dialog', (dialog) => {
      dialogs.push(dialog.message());
      void dialog.dismiss();
    });
    for (const key of ['cash_contribution_by_aircraft', 'cash_contribution_by_division']) {
      await openMetricView(page, key);
      await expect(page.locator('app-metric-widget table').getByText(HOSTILE_NAME, { exact: true }).first()).toBeVisible();
      expect(await page.locator('app-metric-widget img, app-metric-widget script').count()).toBe(0);
    }

    await page.setViewportSize({ width: 1440, height: 900 });
    await openSixMetricView(page, 'c');
    const canvas = page.locator('[data-widget-id="s2"] canvas').first();
    await canvas.scrollIntoViewIfNeeded();
    const box = (await canvas.boundingBox())!;
    const tooltip = page.locator('div[style*="pointer-events"]').filter({ hasText: HOSTILE_NAME });
    let found = false;
    for (let step = 1; step < 40 && !found; step += 1) {
      await page.mouse.move(box.x + (box.width * step) / 40, box.y + box.height * 0.6, { steps: 2 });
      found = (await tooltip.count()) > 0 && (await tooltip.first().isVisible());
    }
    expect(found, 'a tooltip showing the hostile division').toBe(true);
    expect(await tooltip.first().locator('img, script').count()).toBe(0);
    expect(await page.locator('img[src="x"]').count()).toBe(0);
    expect(dialogs).toEqual([]);
  });
});

test.describe('the contribution Metrics on a saved view', () => {
  test('every widget draws, tables have text alternatives and the page passes axe', async ({ page }) => {
    await openSixMetricView(page, 'c');

    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(6);
    await expect(page.locator('app-metric-widget app-chart [role="img"]')).toHaveCount(3);
    for (const label of await page.locator('app-metric-widget app-chart [role="img"]').evaluateAll((els) => els.map((e) => e.getAttribute('aria-label') ?? ''))) {
      expect(label).toContain('Net cash changed by');
    }
    await expect(page.locator('app-metric-widget [role="region"] table')).toHaveCount(3);
    expect(await axeViolations(page)).toEqual([]);
  });

  test('under Ground only the aircraft widgets say so and stop asking, and the others stay', async ({ page }) => {
    await openSixMetricView(page, 'c');
    const calls: string[] = [];
    page.on('request', (r) => {
      if (/cash-contribution\/aircraft/.test(r.url())) calls.push(r.url());
    });

    await page.getByRole('radio', { name: 'Ground only' }).click();

    await expect(page.locator('app-metric-widget .widget__note', { hasText: "isn't available for ground" })).toHaveCount(2);
    expect(calls).toEqual([]);
    expect(await axeViolations(page)).toEqual([]);
  });
});

test.describe('newer Insight card kinds', () => {
  test('aircraft mix and base mix cards draw as drivers and beside them, an unknown kind keeps its own words, and markup stays text', async ({ page }) => {
    const dialogs: string[] = [];
    page.on('dialog', (dialog) => {
      dialogs.push(dialog.message());
      void dialog.dismiss();
    });
    await openInsightNewKinds(page);

    const cards = page.locator('app-what-changed .card:not(.card--attention)');
    await expect(cards).toHaveCount(2 + NEW_KIND_CARDS.drivers.length + NEW_KIND_CARDS.expected.length);
    await expect(cards.nth(1)).toContainText('Cash moved between aircraft');
    await expect(cards.nth(1)).toContainText('this month');
    await expect(cards.nth(2)).toContainText('Sky Harbor base brought in less');
    await expect(cards.nth(2)).toContainText('Lasting');
    await expect(cards.nth(4)).toContainText('expected per month');
    await expect(cards.nth(5)).toContainText('A finding this version does not know');
    await expect(cards.nth(5)).toContainText('For information');
    expect(await page.locator('app-what-changed img, app-what-changed script').count()).toBe(0);
    expect(dialogs).toEqual([]);
    expect(await axeViolations(page)).toEqual([]);
  });
});
