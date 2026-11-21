import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from './fixtures';
import { REPORT_DIR, SIZES, axeViolations, layoutErrors } from './support';

async function openPayerMix(page: import('@playwright/test').Page, group = 'fixed-wing', line: 'air' | 'ground' = 'air'): Promise<void> {
  await page.goto(`/dashboard/net-cash/${group}?period=202610&line=${line}`);
  await page.locator('app-payer-mix app-chart canvas').waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(400);
}

test.describe('payer mix', () => {
  test('the group card on the breakdown page opens that group with the month and Service Line', async ({ page }) => {
    await page.goto('/dashboard/net-cash?period=202610&line=air');
    await page.locator('app-net-cash-breakdown table').waitFor();

    await page.getByRole('link', { name: /see fixed wing detail/i }).click();

    await expect(page).toHaveURL(/\/dashboard\/net-cash\/fixed-wing\?period=202610&line=air$/);
    await expect(page.getByRole('heading', { level: 2, name: 'Fixed wing payer mix' })).toBeVisible();
  });

  test('the group link works from the keyboard', async ({ page }) => {
    await page.goto('/dashboard/net-cash?period=202610&line=air');
    await page.locator('app-net-cash-breakdown table').waitFor();

    await page.getByRole('link', { name: /see rotor wing detail/i }).focus();
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(/net-cash\/rotor-wing/);
  });

  test('breadcrumbs lead back to the breakdown for the same month and line, and to the dashboard', async ({ page }) => {
    await openPayerMix(page, 'als', 'ground');
    const crumbs = page.getByRole('navigation', { name: /breadcrumb/i });

    await expect(crumbs).toContainText('Ground net cash, October 2026');
    await crumbs.getByRole('link', { name: /Ground net cash/ }).click();

    await expect(page).toHaveURL(/\/dashboard\/net-cash\?period=202610&line=ground$/);
  });

  test('says what the payer mix is and names the group that moved', async ({ page }) => {
    await openPayerMix(page);

    await expect(page.getByText(/Medicaid rose 5 points, from 18% in Sep to 23%/)).toBeVisible();
    await expect(page.getByText('Medicaid up 5 points, to 23% of transports.')).toBeVisible();
    expect(await page.locator('app-payer-mix [role="img"]').getAttribute('aria-label')).toContain('VA 4% in Oct, 3% in Sep');
  });

  test('hovering a payer keeps both of its bars visible', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openPayerMix(page);
    const canvas = page.locator('app-payer-mix app-chart canvas');
    const box = (await canvas.boundingBox())!;
    const dpr = await page.evaluate(() => devicePixelRatio);
    // Medicaid is the third payer. Its September bar (grey) is the left of the pair, and 18% tall, so a point just above the baseline is inside it.
    const barX = box.width * 0.36;
    const barY = box.height - 60;
    const pixel = () =>
      canvas.evaluate((c: HTMLCanvasElement, at: [number, number]) => Array.from(c.getContext('2d')!.getImageData(at[0], at[1], 1, 1).data.slice(0, 3)), [Math.round(barX * dpr), Math.round(barY * dpr)] as [number, number]);
    const before = await pixel();

    await page.mouse.move(box.x + barX, box.y + barY);
    await page.waitForTimeout(500);

    // The bar keeps its color under the hover highlight (within a few levels), instead of being painted over with the highlight grey.
    const after = await pixel();
    expect(Math.max(...after.map((v, i) => Math.abs(v - before[i]!)))).toBeLessThan(25);
  });

  test('the stacked bars show each month adding up to 100%, with the key beside them', async ({ page }) => {
    await openPayerMix(page);

    await page.getByRole('radio', { name: 'Stacked bars' }).click();

    const september = page.locator('app-payer-stacked [role="img"]').first();
    const october = page.locator('app-payer-stacked [role="img"]').nth(1);
    await expect(september).toHaveAttribute('aria-label', /^September: Commercial 30%, Medicare 40%, Medicaid 18%/);
    await expect(october).toHaveAttribute('aria-label', /^October: Commercial 26%, Medicare 38%, Medicaid 23%/);
    const widths = await october.locator('span').evaluateAll((spans) => spans.reduce((sum, el) => sum + el.getBoundingClientRect().width, 0) / (spans[0]!.parentElement!.getBoundingClientRect().width));
    expect(widths).toBeCloseTo(1, 1);
    const medicaid = page.locator('app-payer-legend li').filter({ hasText: 'Medicaid' });
    await expect(medicaid).toContainText('23%');
    await expect(medicaid).toContainText('Oct · 18% Sep');
    await expect(page.locator('app-payer-mix app-chart')).toHaveCount(0);
    expect(await axeViolations(page)).toEqual([]);
  });

  test('the donuts show both months with the payer that moved called out in the middle', async ({ page }) => {
    await openPayerMix(page);

    await page.getByRole('radio', { name: 'Donut charts' }).click();

    const donuts = page.locator('app-payer-donuts svg');
    await expect(donuts).toHaveCount(2);
    await expect(donuts.first()).toContainText('18%');
    await expect(donuts.nth(1)).toContainText('23%');
    await expect(donuts.nth(1)).toContainText('Medicaid');
    await expect(donuts.first()).toHaveAttribute('aria-label', /^September: Commercial 30%/);
    await expect(page.getByText('September', { exact: true })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

  test('the name in the middle of a donut fits inside the hole, even a long one', async ({ page }) => {
    await page.route(/net-collections\/payer-mix/, (route) =>
      route.fulfill({
        json: {
          metric: { key: 'payer_mix', name: 'Payer mix', description: 'd', unit: 'transports' },
          providerId: '526', period: '202610', priorPeriod: '202609', serviceLine: 'air', group: 'fixed-wing', groupName: 'Fixed Wing',
          shares: [
            { key: 'commercial', name: 'Commercial', current: 4, prior: 6 },
            { key: 'medicare', name: 'Medicare', current: 26, prior: 52 },
            { key: 'medicaid', name: 'Medicaid', current: 23, prior: 36 },
            { key: 'patient', name: 'Patient responsibility', current: 47, prior: 6 },
          ],
        },
      }),
    );
    await page.goto('/dashboard/net-cash/fixed-wing?period=202610&line=air');
    await page.getByRole('radio', { name: 'Donut charts' }).click();

    const svg = page.locator('app-payer-donuts svg').nth(1);
    await expect(svg).toContainText('47%');
    // Every corner of the text, in the drawing's own units, is inside the hole (radius 40 around the centre at 60, 60).
    const farthest = await svg.evaluate((el: SVGSVGElement) =>
      Math.max(
        ...[...el.querySelectorAll<SVGTextElement>('text')].flatMap((t) => {
          const b = t.getBBox();
          return [[b.x, b.y], [b.x + b.width, b.y], [b.x, b.y + b.height], [b.x + b.width, b.y + b.height]].map(([x, y]) => Math.hypot(x! - 60, y! - 60));
        }),
      ),
    );
    expect(farthest).toBeLessThanOrEqual(40);
    await svg.screenshot({ path: join(REPORT_DIR, 'payer-mix-donut-long-name.png') });
  });

  test('the key lines up: swatches, names and figures sit in the same columns on every row', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openPayerMix(page);
    await page.getByRole('radio', { name: 'Donut charts' }).click();
    await page.locator('app-payer-legend li').first().waitFor();

    const columns = await page.locator('app-payer-legend li').evaluateAll((items) =>
      items.map((li) => {
        const left = (selector: string): number => li.querySelector(selector)!.getBoundingClientRect().left;
        const right = (selector: string): number => li.querySelector(selector)!.getBoundingClientRect().right;
        return { row: Math.round(li.getBoundingClientRect().left), swatch: left('.legend__swatch') - li.getBoundingClientRect().left, name: left('.legend__name') - li.getBoundingClientRect().left, current: li.getBoundingClientRect().right - right('.legend__current'), prior: left('.legend__prior') - li.getBoundingClientRect().left };
      }),
    );
    for (const key of ['swatch', 'name', 'current', 'prior'] as const) {
      const values = columns.map((c) => c[key]);
      expect(Math.max(...values) - Math.min(...values), `${key} column`).toBeLessThanOrEqual(1);
    }
  });

  test('the chart type control moves between the four views from the keyboard', async ({ page }) => {
    await openPayerMix(page);
    const bar = page.getByRole('radio', { name: 'Bar chart' });

    await bar.focus();
    await page.keyboard.press('ArrowRight');
    await expect(page.getByRole('radio', { name: 'Stacked bars' })).toBeFocused();
    await page.keyboard.press('ArrowRight');
    await expect(page.getByRole('radio', { name: 'Donut charts' })).toBeFocused();
    await page.keyboard.press('ArrowRight');
    await expect(page.getByRole('radio', { name: 'Table' })).toBeFocused();
    await expect(page.locator('app-payer-mix table')).toBeVisible();
  });

  for (const view of ['Stacked bars', 'Donut charts', 'Table'] as const) {
    for (const width of [320, 390, 1280]) {
      test(`the ${view.toLowerCase()} view fits and passes axe at ${width}px`, async ({ page }) => {
        await page.setViewportSize({ width, height: 800 });
        await openPayerMix(page);
        await page.getByRole('radio', { name: view }).click();
        await page.waitForTimeout(300);

        mkdirSync(REPORT_DIR, { recursive: true });
        await page.screenshot({ path: join(REPORT_DIR, `payer-mix-${view.toLowerCase().replace(' ', '-')}-${width}.png`), fullPage: true });

        expect((await layoutErrors(page)).errors).toEqual([]);
        expect(await axeViolations(page)).toEqual([]);
      });
    }
  }

  test('the table view lists every payer group with both months', async ({ page }) => {
    await openPayerMix(page);

    await page.getByRole('radio', { name: 'Table' }).click();

    const table = page.locator('app-payer-mix table');
    await expect(table.getByRole('row')).toHaveCount(8);
    await expect(table.getByRole('row', { name: /^Medicaid/ })).toContainText('+5 pts');
    await expect(table.getByRole('row', { name: /^VA/ })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

  test('the page asks the API for the group, month and Service Line in the address', async ({ page }) => {
    const asked: string[] = [];
    await page.route(/net-collections\/payer-mix/, (route) => {
      asked.push(new URL(route.request().url()).search);
      return route.fallback();
    });

    await openPayerMix(page, 'fixed-wing', 'air');

    expect(asked[0]).toBe('?period=202610&serviceLine=air&group=fixed-wing');
  });

  test('changing the Period changes the month in the address', async ({ page }) => {
    await openPayerMix(page);

    await page.locator('input.ems-select__input').first().click();
    await page.getByRole('option', { name: 'September 2026' }).click();

    await expect(page).toHaveURL(/period=202609/);
    await expect(page.getByRole('heading', { level: 2, name: 'Fixed wing payer mix' })).toBeVisible();
  });

  test('the explanation panel says it is not built yet', async ({ page }) => {
    await openPayerMix(page);

    await expect(page.getByRole('heading', { level: 2, name: 'What changed in fixed wing' })).toBeVisible();
    await expect(page.getByText(/is planned and is not available yet/)).toBeVisible();
  });

  test('when the payer mix cannot load it says so and stays accessible', async ({ page }) => {
    await page.route(/net-collections\/payer-mix/, (route) => route.fulfill({ status: 503, json: { title: 'Service Unavailable' } }));
    await page.goto('/dashboard/net-cash/fixed-wing?period=202610&line=air');

    await expect(page.getByText("We can't load the payer mix right now")).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

  test('a group with no transports in the month says so', async ({ page }) => {
    await page.route(/net-collections\/payer-mix/, (route) =>
      route.fulfill({ json: { metric: { key: 'payer_mix', name: 'n', description: 'd', unit: 'transports' }, providerId: '526', period: '202610', priorPeriod: '202609', serviceLine: 'air', group: 'fixed-wing', groupName: null, shares: [] } }),
    );
    await page.goto('/dashboard/net-cash/fixed-wing?period=202610&line=air');

    await expect(page.getByText('There are no fixed wing transports for October 2026.')).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

  test('a group key that is not a key is not sent to the API', async ({ page }) => {
    const asked: string[] = [];
    await page.route(/net-collections\/payer-mix/, (route) => {
      asked.push(route.request().url());
      return route.fallback();
    });

    await page.goto('/dashboard/net-cash/Fixed%20Wing%27%3B%20DROP?period=202610&line=air');
    await page.waitForTimeout(500);

    expect(asked).toEqual([]);
  });

  for (const size of SIZES) {
    test(`payer mix layout and accessibility: ${size.name} ${size.width}px`, async ({ page }) => {
      await page.setViewportSize({ width: size.width, height: size.height });
      await openPayerMix(page);

      mkdirSync(REPORT_DIR, { recursive: true });
      await page.screenshot({ path: join(REPORT_DIR, `payer-mix-${size.name}-${size.width}.png`), fullPage: true });

      expect((await layoutErrors(page)).errors).toEqual([]);
      expect(await axeViolations(page)).toEqual([]);
    });
  }
});
