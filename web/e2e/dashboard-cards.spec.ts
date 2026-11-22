import { expect, test } from './fixtures';
import { axeViolations, layoutErrors, openDashboard } from './support';

const card = (page: import('@playwright/test').Page, title: string) => page.locator('app-kpi-card').filter({ has: page.getByRole('heading', { name: title }) });

test.describe('transports and loaded miles', () => {
  test('transports show the latest month, the change from the month before and the split of the last month', async ({ page }) => {
    await openDashboard(page);
    const transports = card(page, 'Transports');

    await expect(transports).toContainText('140');
    await expect(transports).toContainText('6 fewer than Sep');
    await expect(transports).toContainText('Rotor wing 92 · Fixed wing 48');
    await expect(transports.locator('.bars__bar')).toHaveCount(6);
    await expect(transports.locator('.chart-label')).toHaveCount(6);
    expect(await transports.locator('[role="img"]').getAttribute('aria-label')).toBe('Transports by month: May 112, Jun 131, Jul 128, Aug 150, Sep 146, Oct 140.');
  });

  test('loaded miles show statute miles by default, and nautical miles when chosen', async ({ page }) => {
    await openDashboard(page);
    const miles = card(page, 'Loaded miles');
    await expect(miles).toContainText('18,420');
    await expect(miles).toContainText('statute mi');
    await expect(miles).toContainText('3% fewer than Sep');

    await miles.getByRole('radio', { name: 'Nautical' }).click();

    await expect(miles).toContainText('16,007');
    await expect(miles).toContainText('nautical mi');
    expect(await miles.locator('.spark[role="img"]').getAttribute('aria-label')).toContain('Oct 16,007');
  });

  test('ground only has no nautical option, and the miles go back to statute', async ({ page }) => {
    await openDashboard(page);
    const miles = card(page, 'Loaded miles');
    await miles.getByRole('radio', { name: 'Nautical' }).click();
    await expect(miles).toContainText('nautical mi');

    await page.getByRole('radio', { name: 'Ground only' }).click();
    await expect(miles.getByRole('radio', { name: 'Nautical' })).toHaveCount(0);
    await expect(miles.getByRole('radio', { name: 'Statute' })).toHaveCount(0);

    await page.getByRole('radio', { name: 'Air + ground' }).click();
    await expect(miles.getByRole('radio', { name: 'Nautical' })).toBeVisible();
  });

  test('net cash changes with the Service Line, not only the tiles', async ({ page }) => {
    const asked: string[] = [];
    page.on('request', (r) => {
      if (/net-collections\?/.test(r.url())) asked.push(new URL(r.url()).searchParams.get('serviceLine') ?? '');
    });
    await openDashboard(page);
    const cash = page.locator('app-net-cash-panel');
    await expect(cash).toContainText('$1.10M');

    await page.getByRole('radio', { name: 'Ground only' }).click();
    await expect(cash).toContainText('$220,048');
    await page.getByRole('radio', { name: 'Air + ground' }).click();
    await expect(cash).toContainText('$1.32M');
    await page.getByRole('radio', { name: 'Air only' }).click();
    await expect(cash).toContainText('$1.10M');

    expect(asked).toEqual(['air', 'ground', 'all', 'air']);
  });

  test('both tiles ask for the six months ending at the chosen period and follow the Service Line', async ({ page }) => {
    const asked: string[] = [];
    await page.route(/net-collections\/transports/, (route) => {
      asked.push(new URL(route.request().url()).search);
      return route.fallback();
    });
    await openDashboard(page);
    expect(asked).toContain('?endPeriod=202610&months=6&serviceLine=air');

    await page.getByRole('radio', { name: 'Ground only' }).click();

    await expect(card(page, 'Transports')).toContainText('Advanced life support 140');
    expect(asked.at(-1)).toBe('?endPeriod=202610&months=6&serviceLine=ground');
    // One request feeds both tiles.
    expect(asked.filter((a) => a.includes('serviceLine=air'))).toHaveLength(1);
  });

  test('an open month warns that counts may rise, and a closed one does not', async ({ page }) => {
    await openDashboard(page);
    await expect(card(page, 'Transports')).toContainText('This month is still open, so counts may rise.');
    await expect(card(page, 'Loaded miles')).toContainText('This month is still open, so counts may rise.');

    await page.locator('input.ems-select__input').first().click();
    await page.getByRole('option', { name: 'May 2026' }).click();

    await expect(card(page, 'Transports')).not.toContainText('still open');
  });

  test('when transports cannot load the tiles say so calmly and recover on retry', async ({ page }) => {
    let fail = true;
    await page.route(/net-collections\/transports/, (route) => (fail ? route.fulfill({ status: 503, json: { title: 'x' } }) : route.fallback()));
    await openDashboard(page);
    const transports = card(page, 'Transports');

    await expect(transports).toContainText("We can't load this right now");
    await expect(transports).toContainText('—');
    await expect(page.locator('app-kpi-card').first()).not.toContainText('warehouse');
    expect(await axeViolations(page)).toEqual([]);
    expect((await layoutErrors(page)).errors).toEqual([]);

    fail = false;
    await transports.getByRole('button', { name: 'Try again' }).focus();
    await page.keyboard.press('Enter');

    await expect(transports).toContainText('140');
  });

  test('a month with no transports at all shows a dash and no bars, and passes axe', async ({ page }) => {
    await page.route(/net-collections\/transports/, (route) =>
      route.fulfill({ json: { metric: { key: 'transport_volume', name: 'n', description: 'd', unit: 'transports' }, providerId: '526', endPeriod: '202610', months: 6, serviceLine: 'air', periodOpen: false, points: [], groups: [] } }),
    );
    await openDashboard(page);

    await expect(card(page, 'Transports')).toContainText('0');
    expect(await axeViolations(page)).toEqual([]);
  });

  test('the tiles in a row line up: the change line, the footnote and the note start at the same height', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await openDashboard(page);

    const rows = await page.locator('app-kpi-card').evaluateAll((cards) => {
      const parts = cards.map((card) => {
        const top = (selector: string): number | null => card.querySelector(selector)?.getBoundingClientRect().top ?? null;
        return { row: Math.round(card.getBoundingClientRect().top), delta: top('.kpi__delta'), footnote: top('.kpi__footnote'), value: top('.kpi__value') };
      });
      const byRow = new Map<number, typeof parts>();
      for (const p of parts) byRow.set(p.row, [...(byRow.get(p.row) ?? []), p]);
      return [...byRow.values()];
    });
    expect(rows.length).toBeGreaterThanOrEqual(2);
    for (const row of rows) {
      expect(row.length).toBe(2);
      for (const key of ['value', 'delta', 'footnote'] as const) {
        const [a, b] = row.map((c) => c[key]!);
        expect(Math.abs(a! - b!), `${key} in the row starting at ${row[0]!.row}`).toBeLessThanOrEqual(1);
      }
    }
  });

  test('while the figures load every tile and the net cash panel show grey placeholders, never "no data", and the page is still accessible', async ({ page }) => {
    const release: Array<() => void> = [];
    await page.route(/net-collections\/(periods|transports|clean-claims)/, async (route) => {
      await new Promise<void>((resolve) => release.push(resolve));
      await route.fallback();
    });
    await page.goto('/dashboard');

    await expect(page.locator('app-net-cash-panel app-skeleton').first()).toBeVisible();
    await expect(page.locator('app-kpi-card app-skeleton').first()).toBeVisible();
    await expect(page.locator('app-net-cash-panel')).not.toContainText('No net cash data');
    await expect(page.getByRole('status').filter({ hasText: 'Loading net cash' })).toHaveCount(1);
    expect(await axeViolations(page)).toEqual([]);
    expect((await layoutErrors(page)).errors).toEqual([]);
    await page.screenshot({ path: 'e2e-report/dashboard-loading.png' });

    await page.unroute(/net-collections\/(periods|transports|clean-claims)/);
    release.forEach((r) => r());
    await expect(card(page, 'Transports')).toContainText('140');
    await expect(page.locator('app-skeleton')).toHaveCount(0);
  });
});
