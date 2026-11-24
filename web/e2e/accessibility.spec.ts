import { expect, test } from './fixtures';
import type { Page } from '@playwright/test';
import { axeViolations, chooseNetCashView, expandHelp, layoutErrors, openDashboard, openDashboardWithNotice, openDashboardHelp, openViewHelp, openMetricView, openMetricViewNotFound, openNeedsAttention, openPayerDetail, openPayerMixPage, openProjection, openSavedViews, openTransportsFullScreen, openView, SIX_METRICS, openMetricViewGroundOnly, openPaletteUnderGround, openSixMetricView, CONTRIBUTION_METRICS, openInsightNewKinds } from './support';

/** WCAG 2.2 AA beyond the static page: other states, other pages, user settings. Every test also gates on axe. */
test.describe('accessibility', () => {
  test('the page has a title, a language, one h1 and the landmarks screen readers jump between', async ({ page }) => {
    await openDashboard(page);

    await expect(page).toHaveTitle(/Prism/);
    expect(await page.locator('html').getAttribute('lang')).toBe('en');
    await expect(page.locator('h1')).toHaveCount(1);
    await expect(page.locator('main')).toHaveCount(1);
    await expect(page.locator('nav[aria-label]')).not.toHaveCount(0);
    // Every region of content sits inside a landmark.
    const outside = await page.evaluate(() =>
      [...document.querySelectorAll('h1, h2, button, a[href], input, [role="radio"]')]
        .filter((el) => !el.closest('main, nav, header, footer, .skip-link, [role="main"], [role="navigation"], .cdk-overlay-container'))
        .map((el) => el.tagName.toLowerCase() + ':' + (el.textContent ?? '').trim().slice(0, 20)),
    );
    expect(outside).toEqual([]);
  });

  test('headings do not skip levels', async ({ page }) => {
    await openDashboard(page);

    const levels = await page.evaluate(() => [...document.querySelectorAll('h1, h2, h3, h4, h5, h6')].map((h) => Number(h.tagName[1])));
    const skips = levels.filter((level, i) => i > 0 && level - levels[i - 1]! > 1);
    expect(skips, `heading levels: ${levels.join(',')}`).toEqual([]);
  });

  test('the login page passes axe', async ({ page }) => {
    await page.unroute('**/api/v1/me');
    await page.route('**/api/v1/me', (route) => route.fulfill({ status: 401, json: { title: 'Unauthorized' } }));
    await page.goto('/login');
    await page.locator('main, [role="main"]').first().waitFor();

    expect(await axeViolations(page)).toEqual([]);
    expect((await layoutErrors(page)).errors).toEqual([]);
  });

  test('the account menu passes axe when open', async ({ page }) => {
    await openDashboard(page);
    await page.locator('button.ems-profile').click();
    await page.locator('[role="menu"]').waitFor();

    expect(await axeViolations(page)).toEqual([]);
  });

  for (const view of ['bar', 'table'] as const) {
    test(`the ${view} view passes axe and has no layout errors`, async ({ page }) => {
      await openDashboard(page);
      await chooseNetCashView(page, view);

      expect(await axeViolations(page)).toEqual([]);
      expect((await layoutErrors(page)).errors).toEqual([]);
    });
  }

  test('the net cash table lists the same months as the chart', async ({ page }) => {
    await openDashboard(page);
    await chooseNetCashView(page, 'table');

    const rows = await page.locator('app-net-cash-panel tbody tr').allTextContents();
    expect(rows).toHaveLength(6);
    expect(rows[0]).toContain('May 2026');
    expect(rows[5]).toContain('Oct 2026');
    expect(rows[5]).toContain('$1,100,240.00');
  });

  test('the projection switch adds three projected months to the chart and the expected total', async ({ page }) => {
    await openDashboard(page);
    const projection = page.getByRole('switch', { name: 'Show 3-month projection' });
    await expect(projection).toHaveAttribute('aria-checked', 'false');
    await expect(page.getByText('Expected, next 3 months')).toBeHidden();

    await projection.click();

    await expect(projection).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByText('Expected, next 3 months')).toBeVisible();
    expect(await page.locator('app-net-cash-panel [role="img"]').getAttribute('aria-label')).toMatch(/Projected Nov/);
    expect(await axeViolations(page)).toEqual([]);
    expect((await layoutErrors(page)).errors).toEqual([]);
  });

  test('the projection asks the API for the chosen period and Service Line, and says so when it fails', async ({ page }) => {
    const asked: string[] = [];
    await page.route(/net-collections\/projection/, (route) => {
      asked.push(new URL(route.request().url()).search);
      return route.fulfill({ status: 503, json: { title: 'Service Unavailable' } });
    });
    await openDashboard(page);

    await page.getByRole('switch', { name: 'Show 3-month projection' }).click();

    await expect(page.getByText("We can't load the projection right now. Try again in a moment.", { exact: true })).toBeVisible();
    await expect(page.getByText('{{')).toHaveCount(0);
    expect(asked[0]).toBe('?endPeriod=202610&serviceLine=air&horizon=3');
    await expect(page.getByText('Expected, next 3 months')).toBeHidden();
    expect(await axeViolations(page)).toEqual([]);
  });

  test('when the data cannot load the dashboard says so calmly, with a reference, and recovers on retry', async ({ page }) => {
    let fail = true;
    await page.route(/net-collections\/periods/, (route) =>
      fail
        ? route.fulfill({ status: 503, json: { title: 'The data warehouse is not available. Try again shortly.', traceId: '00-77114806214001843f6761e5254e96e3-cfc570a3d3f659d3-03' } })
        : route.fallback(),
    );
    await page.goto('/dashboard');
    const problem = page.locator('app-net-cash-panel app-load-problem');
    await problem.waitFor();

    await expect(problem).toContainText("We can't load net cash right now");
    await expect(problem).toContainText('Reference: 77114806214001843f6761e5254e96e3');
    await expect(page.locator('app-net-cash-panel')).not.toContainText('No net cash data');
    await expect(page.locator('app-net-cash-panel')).not.toContainText('warehouse');
    expect(await axeViolations(page)).toEqual([]);
    expect((await layoutErrors(page)).errors).toEqual([]);
    await page.screenshot({ path: 'e2e-report/dashboard-load-problem.png' });

    fail = false;
    await problem.getByRole('button', { name: 'Try again' }).focus();
    await page.keyboard.press('Enter');

    await expect(problem).toHaveCount(0);
    await page.locator('app-chart canvas').waitFor();
  });

  test('the load problem fits at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 700 });
    await page.route(/net-collections\/periods/, (route) => route.fulfill({ status: 503, json: { title: 'x', traceId: '77114806214001843f6761e5254e96e3' } }));
    await page.goto('/dashboard');
    await page.locator('app-load-problem').waitFor();
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(400);

    expect((await layoutErrors(page)).errors).toEqual([]);
    expect(await axeViolations(page)).toEqual([]);
  });

  test('the load problem is readable in forced colors', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.route(/net-collections\/periods/, (route) => route.fulfill({ status: 503, json: { title: 'x', traceId: '77114806214001843f6761e5254e96e3' } }));
    await page.goto('/dashboard');
    await page.locator('app-load-problem').waitFor();
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(400);

    expect((await layoutErrors(page)).errors).toEqual([]);
    expect(await axeViolations(page, undefined, ['color-contrast'])).toEqual([]);
  });

  test('no page shows raw template text, even when a part of it fails to load', async ({ page }) => {
    await page.route(/(net-collections\/(projection|transports|clean-claims|breakdown|payer-mix)|insights\/what-changed)/, (route) => route.fulfill({ status: 503, json: { title: 'x' } }));
    for (const path of ['/dashboard', '/dashboard/net-cash?period=202610&line=air', '/dashboard/net-cash/fixed-wing?period=202610&line=air']) {
      await page.goto(path);
      await page.locator('main').first().waitFor();
      if (path === '/dashboard') await page.getByRole('switch', { name: 'Show 3-month projection' }).click();
      await page.waitForTimeout(500);

      const text = await page.locator('body').innerText();
      expect(text, path).not.toContain('{{');
      expect(text, path).not.toContain('}}');
    }
  });

  test('Last 12 months shows twelve months in the table', async ({ page }) => {
    await openDashboard(page);
    await page.getByRole('radio', { name: 'Last 12 months' }).click();
    await chooseNetCashView(page, 'table');

    await expect(page.locator('app-net-cash-panel tbody tr')).toHaveCount(12);
  });

  test('the Period drop-down lists accounting periods from the data, newest first', async ({ page }) => {
    await openDashboard(page);

    await expect(page.locator('input.ems-select__input').first()).toHaveValue('October 2026');
  });

  test('every chart has a text alternative: a description and a table view', async ({ page }) => {
    await openDashboard(page);

    for (const view of ['line', 'bar'] as const) {
      await chooseNetCashView(page, view);
      expect(await page.locator('app-net-cash-panel [role="img"]').getAttribute('aria-label')).toMatch(/Latest month Oct/);
    }
    // The same numbers are one control away, as a real table.
    await chooseNetCashView(page, 'table');
    await expect(page.locator('app-net-cash-panel table')).toHaveCount(1);
  });

  test('forced colors (Windows high contrast) still shows every control and passes axe', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await openDashboard(page);

    // axe cannot read forced-colors system colors, so its contrast rule would only report false results here.
    expect(await axeViolations(page, undefined, ['color-contrast'])).toEqual([]);
    expect((await layoutErrors(page)).errors).toEqual([]);
  });

  test('text spacing overrides (WCAG 1.4.12) do not cut off or overlap text', async ({ page }) => {
    await openDashboard(page);
    await page.addStyleTag({
      content: `* { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; }
                p { margin-bottom: 2em !important; }`,
    });
    await page.waitForTimeout(200);

    expect((await layoutErrors(page)).errors).toEqual([]);
  });

  test('200% text size does not cut off or overlap text', async ({ page }) => {
    await openDashboard(page);
    await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
    await page.waitForTimeout(200);

    expect((await layoutErrors(page)).errors).toEqual([]);
  });

  test('the page works zoomed to 400% (reflows at 320px with no sideways scroll)', async ({ page }) => {
    // 400% zoom of a 1280px window is a 320 CSS pixel viewport.
    await page.setViewportSize({ width: 320, height: 256 });
    await openDashboard(page);

    expect((await layoutErrors(page)).errors).toEqual([]);
    expect(await axeViolations(page)).toEqual([]);
  });

  test('no animation runs when the user asks for reduced motion', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await openDashboard(page);

    const running = await page.evaluate(() => document.getAnimations().filter((a) => a.playState === 'running' && a.effect?.getTiming().iterations === Infinity).length);
    expect(running).toBe(0);
  });

  test.describe('needs attention', () => {
    test('has a title, one h1, one main, headings that do not skip, and every control inside a landmark', async ({ page }) => {
      await openNeedsAttention(page);

      await expect(page).toHaveTitle(/^Needs attention · Prism$/);
      await expect(page.locator('h1')).toHaveCount(1);
      await expect(page.locator('main')).toHaveCount(1);
      const levels = await page.evaluate(() => [...document.querySelectorAll('h1, h2, h3, h4, h5, h6')].map((h) => Number(h.tagName[1])));
      expect(levels.filter((level, i) => i > 0 && level - levels[i - 1]! > 1), `heading levels: ${levels.join(',')}`).toEqual([]);
      const outside = await page.evaluate(() =>
        [...document.querySelectorAll('h1, h2, button, a[href], input, textarea, [role="radio"]')]
          .filter((el) => !el.closest('main, nav, header, footer, .skip-link, [role="main"], [role="navigation"], .cdk-overlay-container'))
          .map((el) => el.tagName.toLowerCase() + ':' + (el.textContent ?? '').trim().slice(0, 20)),
      );
      expect(outside).toEqual([]);
    });

    test('the table has a caption, scoped headers and a focusable scroll region, and every status and deadline is in words', async ({ page }) => {
      await openNeedsAttention(page, 'check=needs_attention&period=202610&line=air&group=fixed-wing');

      await expect(page.locator('app-needs-attention .claims table caption')).toHaveCount(1);
      expect(await page.locator('app-needs-attention .claims thead th').evaluateAll((els) => els.map((e) => e.getAttribute('scope')))).toEqual(Array(7).fill('col'));
      await expect(page.locator('app-needs-attention .claims tbody th[scope="row"]')).toHaveCount(4);
      await expect(page.locator('app-needs-attention [role="region"]')).toHaveAttribute('tabindex', '0');
      await expect(page.locator('app-needs-attention .claims tbody')).toContainText('On hold');
      await expect(page.locator('app-needs-attention .claims tbody')).toContainText('Overdue by 3 days');
    });

    test('ticking claims is announced with the count', async ({ page }) => {
      await openNeedsAttention(page);

      await page.getByRole('checkbox', { name: 'Select claim FW-2610418' }).check();
      await expect(page.locator('app-needs-attention [role="status"]').first()).toHaveText('1 claim selected');
    });

    test('forced colors keep the selected row, the badge and the drawer visible', async ({ page }) => {
      await page.emulateMedia({ forcedColors: 'active' });
      await openNeedsAttention(page);
      await page.getByRole('checkbox', { name: 'Select all 2 claims on this page' }).check();
      await expect(page.locator('.send__badge')).toBeVisible();
      await page.locator('app-needs-attention button.send').click();

      await expect(page.getByRole('dialog', { name: 'Send to your CSE' })).toBeVisible();
      expect((await layoutErrors(page)).errors).toEqual([]);
    });
  });

  test.describe('saved views', () => {
    test('each page has a title, one h1, one main, and headings that do not skip', async ({ page }) => {
      for (const [open, title] of [
        [() => openSavedViews(page), /^Saved views · Prism$/],
        [() => openView(page, 5), /^Cash overview · Saved views · Prism$/],
        [() => openView(page, 5, { edit: true }), /Cash overview · Saved views · Prism$/],
      ] as const) {
        await open();
        await expect(page).toHaveTitle(title);
        await expect(page.locator('h1')).toHaveCount(1);
        await expect(page.locator('main')).toHaveCount(1);
        const levels = await page.evaluate(() => [...document.querySelectorAll('h1, h2, h3, h4, h5, h6')].map((h) => Number(h.tagName[1])));
        expect(levels.filter((level, i) => i > 0 && level - levels[i - 1]! > 1), `heading levels: ${levels.join(',')}`).toEqual([]);
        const outside = await page.evaluate(() =>
          [...document.querySelectorAll('h1, h2, button, a[href], input, textarea, [role="radio"]')]
            .filter((el) => !el.closest('main, nav, header, footer, .skip-link, [role="main"], [role="navigation"], .cdk-overlay-container'))
            .map((el) => el.tagName.toLowerCase() + ':' + (el.textContent ?? '').trim().slice(0, 20)),
        );
        expect(outside).toEqual([]);
      }
    });

    test('an unsaved view shows in the page title', async ({ page }) => {
      await openView(page, 5, { edit: true });

      await page.getByRole('button', { name: 'Remove Clean claim rate' }).click();

      await expect(page).toHaveTitle(/^• Cash overview/);
    });

    test('the widget settings pane passes axe in light and dark', async ({ page }) => {
      for (const scheme of ['light', 'dark'] as const) {
        await page.emulateMedia({ colorScheme: scheme });
        await openView(page, 5, { edit: true });
        await page.getByRole('button', { name: 'Settings for Net cash collected' }).click();
        await page.getByRole('complementary', { name: 'Widget settings' }).waitFor();

        expect(await axeViolations(page), scheme).toEqual([]);
        expect((await layoutErrors(page)).errors, scheme).toEqual([]);
      }
    });

    test('every icon-only button has a name and every chart has a text description and a table', async ({ page }) => {
      await openView(page, 5, { edit: true });

      const nameless = await page.evaluate(() => [...document.querySelectorAll('button')].filter((b) => !(b.getAttribute('aria-label') || b.textContent || '').replace(/[a-z_]+$/u, '').trim()).map((b) => b.className));
      expect(nameless).toEqual([]);
      const label = (await page.locator('app-metric-widget app-chart [role="img"]').first().getAttribute('aria-label')) ?? '';
      expect(label.length).toBeGreaterThan(20);
      expect(label.length).toBeLessThanOrEqual(250);
      await page.getByRole('radio', { name: 'Table', exact: true }).first().click();
      await expect(page.locator('app-metric-widget table caption')).toHaveText('Net cash collected by month');
    });

    test('forced colors (Windows high contrast) keeps the grid, the toolbar and the palette usable', async ({ page }) => {
      await page.emulateMedia({ forcedColors: 'active' });
      await openView(page, 5, { edit: true });

      await expect(page.getByRole('toolbar', { name: 'Net cash collected actions' })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Add Payer mix to view' })).toBeVisible();
      const border = await page.locator('li.item').first().evaluate((el) => getComputedStyle(el.querySelector('ems-card') ?? el).borderTopWidth);
      expect(border).not.toBe('');
      expect((await layoutErrors(page)).errors).toEqual([]);
      await page.screenshot({ path: 'e2e-report/saved-views-forced-colors.png', fullPage: true });
    });

    test('at 200% text nothing is cut off or covered', async ({ page }) => {
      await openView(page, 5, { edit: true });
      await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
      await page.waitForTimeout(300);

      expect((await layoutErrors(page)).errors).toEqual([]);
      expect(await axeViolations(page)).toEqual([]);
    });

    test('at 400% zoom (a 320 by 256 window) the page reflows with no sideways scroll', async ({ page }) => {
      await page.setViewportSize({ width: 320, height: 256 });
      await openView(page, 5, { edit: true });

      expect((await layoutErrors(page)).errors).toEqual([]);
    });

    test('WCAG text-spacing overrides do not cut text off', async ({ page }) => {
      await openView(page, 5, { edit: true });
      await page.addStyleTag({ content: '* { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; } p { margin-bottom: 2em !important; }' });
      await page.waitForTimeout(300);

      expect((await layoutErrors(page)).errors).toEqual([]);
    });

    test('with reduced motion nothing animates while arranging', async ({ page }) => {
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await openView(page, 5, { edit: true });
      await page.getByRole('button', { name: 'Remove Clean claim rate' }).click();

      const running = await page.evaluate(() => document.getAnimations().filter((a) => a.playState === 'running' && (a.effect?.getTiming().iterations ?? 1) === Infinity).length);
      expect(running).toBe(0);
    });

    test('a long view name wraps instead of running off the page', async ({ page }) => {
      await page.route(/\/api\/v1\/providers\/[^/]+\/saved-views\/5$/, (route) =>
        route.fulfill({
          headers: { ETag: '"1"' },
          json: {
            id: 5,
            name: 'A very long view name that goes on and on about quarterly cash and payer mix for every base and every aircraft type we fly',
            description: null,
            ownerName: 'jdoe',
            isOwner: true,
            version: '1',
            updatedAt: '2026-11-22T10:00:00Z',
            layout: { version: 1, density: 'comfortable', widgets: [{ id: 'w1', metricKey: 'net_collections', presentation: 'line', x: 0, y: 0, w: 6, h: 3 }] },
          },
        }),
      );
      await page.setViewportSize({ width: 320, height: 700 });
      await openView(page, 5);

      expect((await layoutErrors(page)).errors).toEqual([]);
      expect(await axeViolations(page)).toEqual([]);
    });
  });
});

/**
 * The payer breakdown, the payer mix, their trust cues (Data Through stamp, context badge, help panel) and the data-updated notice,
 * under every user setting the other pages are checked with.
 */
const TRUST_STATES: { name: string; open: (p: Page) => Promise<void> }[] = [
  { name: 'payer breakdown, bar', open: (p) => openPayerDetail(p) },
  {
    name: 'payer breakdown, donuts',
    open: async (p) => {
      await openPayerDetail(p);
      await p.getByRole('radio', { name: 'Donut charts' }).click();
      await p.locator('app-payer-donuts svg').first().waitFor();
    },
  },
  {
    name: 'payer breakdown, table',
    open: async (p) => {
      await openPayerDetail(p);
      await p.getByRole('radio', { name: 'Table' }).click();
      await p.locator('app-payer-mix-detail table').waitFor();
    },
  },
  {
    name: 'payer breakdown, help open',
    open: async (p) => {
      await openPayerDetail(p);
      await expandHelp(p);
    },
  },
  {
    name: 'payer mix, help open',
    open: async (p) => {
      await openPayerMixPage(p);
      await expandHelp(p);
    },
  },
  { name: 'dashboard, card help open', open: (p) => openDashboardHelp(p) },
  { name: 'saved view, widget help open', open: (p) => openViewHelp(p) },
  { name: 'dashboard, update notice', open: (p) => openDashboardWithNotice(p) },
  { name: 'metric view', open: (p) => openMetricView(p) },
  { name: 'metric view, not found', open: (p) => openMetricViewNotFound(p) },
  ...SIX_METRICS.map((m) => ({ name: `metric view, ${m.name}`, open: (p: Page) => openMetricView(p, m.key) })),
  { name: 'metric view, air-only Metric under Ground only', open: (p: Page) => openMetricViewGroundOnly(p) },
  { name: 'saved view, the six new Metrics (charts)', open: (p: Page) => openSixMetricView(p, 'a') },
  { name: 'saved view, the six new Metrics (tables and tiles)', open: (p: Page) => openSixMetricView(p, 'b') },
  ...CONTRIBUTION_METRICS.map((m) => ({ name: `metric view, ${m.name}`, open: (p: Page) => openMetricView(p, m.key) })),
  { name: 'metric view, air-only contribution under Ground only', open: (p: Page) => openMetricViewGroundOnly(p, 'cash_contribution_by_aircraft') },
  { name: 'saved view, the contribution Metrics', open: (p: Page) => openSixMetricView(p, 'c') },
  { name: 'payer mix, newer insight card kinds', open: (p: Page) => openInsightNewKinds(p) },
  { name: 'palette, air-only Metrics under Ground only', open: (p: Page) => openPaletteUnderGround(p) },
];

test.describe('accessibility: trust cues and the breakdown page', () => {
  for (const state of TRUST_STATES) {
    test.describe(state.name, () => {
      test('passes axe and has no layout errors', async ({ page }) => {
        await state.open(page);
        expect(await axeViolations(page)).toEqual([]);
        expect((await layoutErrors(page)).errors).toEqual([]);
      });

      test('forced colors keep everything visible', async ({ page }) => {
        await page.emulateMedia({ forcedColors: 'active' });
        await state.open(page);
        expect(await axeViolations(page, undefined, ['color-contrast'])).toEqual([]);
        expect((await layoutErrors(page)).errors).toEqual([]);
      });

      test('text spacing overrides do not cut off or overlap text', async ({ page }) => {
        await state.open(page);
        await page.addStyleTag({ content: '* { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; } p { margin-bottom: 2em !important; }' });
        await page.waitForTimeout(250);
        expect((await layoutErrors(page)).errors).toEqual([]);
      });

      test('200% text size does not cut off or overlap text', async ({ page }) => {
        await state.open(page);
        await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
        await page.waitForTimeout(250);
        expect((await layoutErrors(page)).errors).toEqual([]);
      });

      test('400% zoom (a 320 by 256 window) reflows with no sideways scroll', async ({ page }) => {
        await page.setViewportSize({ width: 320, height: 256 });
        await state.open(page);
        expect((await layoutErrors(page)).errors).toEqual([]);
        expect(await axeViolations(page)).toEqual([]);
      });

      test('reduced motion leaves nothing animating', async ({ page }) => {
        await page.emulateMedia({ reducedMotion: 'reduce' });
        await state.open(page);
        const running = await page.evaluate(() => document.getAnimations().filter((a) => a.playState === 'running' && (a.effect?.getTiming().iterations ?? 1) === Infinity).length);
        expect(running).toBe(0);
      });
    });
  }

  test('the breakdown page has a title, one h1, one main, headings that do not skip and every control inside a landmark', async ({ page }) => {
    await openPayerDetail(page);
    await expandHelp(page);

    await expect(page).toHaveTitle(/Prism/);
    await expect(page.locator('h1')).toHaveCount(1);
    await expect(page.locator('main')).toHaveCount(1);
    const levels = await page.evaluate(() => [...document.querySelectorAll('h1, h2, h3, h4, h5, h6')].map((h) => Number(h.tagName[1])));
    expect(levels.filter((level, i) => i > 0 && level - levels[i - 1]! > 1), `heading levels: ${levels.join(',')}`).toEqual([]);
    const outside = await page.evaluate(() =>
      [...document.querySelectorAll('h1, h2, button, a[href], input, [role="radio"]')]
        .filter((el) => !el.closest('main, nav, header, footer, .skip-link, [role="main"], [role="navigation"], .cdk-overlay-container'))
        .map((el) => el.tagName.toLowerCase() + ':' + (el.textContent ?? '').trim().slice(0, 20)),
    );
    expect(outside).toEqual([]);
  });

  test('the Data Through stamp and the context badge are visible text, not tooltips, and the help toggle says whether it is open', async ({ page }) => {
    await openPayerDetail(page);

    await expect(page.locator('app-freshness-stamp')).toContainText('Data through Nov 22');
    await expect(page.locator('app-context-badge')).toBeVisible();
    expect(((await page.locator('app-context-badge').innerText()) ?? '').trim().length).toBeGreaterThan(3);
    expect(await page.locator('app-freshness-stamp, app-context-badge').evaluateAll((els) => els.some((e) => e.hasAttribute('title') || e.querySelector('[title]')))).toBe(false);
    const toggle = page.locator('app-metric-help button.help__toggle');
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const controls = await toggle.getAttribute('aria-controls');
    await expect(page.locator(`#${controls}`)).toBeVisible();
  });

  test('the notice is a polite status, not an alert, and its button has a name', async ({ page }) => {
    await openDashboardWithNotice(page);

    await expect(page.locator('app-data-updated-notice [role="status"]')).toHaveAttribute('aria-live', 'polite');
    await expect(page.locator('app-data-updated-notice [role="alert"]')).toHaveCount(0);
    await expect(page.locator('app-data-updated-notice').getByRole('button', { name: 'Update view' })).toBeVisible();
  });

  test.describe('figures from the metric catalog', () => {
    test('the projection is a captioned table with scoped headers and a focusable, named scroll region, and a calm message when it fails', async ({ page }) => {
      await openProjection(page);

      await expect(page.locator('app-projection-panel table caption')).toHaveCount(1);
      expect(await page.locator('app-projection-panel thead th').evaluateAll((els) => els.map((e) => e.getAttribute('scope')))).toEqual(Array(4).fill('col'));
      await expect(page.locator('app-projection-panel tbody th[scope="row"]')).toHaveCount(9);
      await expect(page.locator('app-projection-panel .panel__table-wrap')).toHaveAttribute('tabindex', '0');
      await expect(page.locator('app-projection-panel .panel__table-wrap')).toHaveAccessibleName('Projection by month');
      await expect(page.locator('app-projection-panel [role="radiogroup"]')).toHaveAccessibleName('Months to project');
      // Not a chart, so there is nothing to describe: the figures are text and no stacked bar stands for the shares.
      await expect(page.locator('app-projection-panel [role="img"], app-projection-panel canvas')).toHaveCount(0);
      expect(await axeViolations(page, 'app-projection-panel')).toEqual([]);

      await page.route(/net-collections\/projection/, (route) => route.fulfill({ status: 503, json: { title: 'x' } }));
      await page.locator('app-projection-panel').getByRole('radio', { name: '2 months' }).click();
      await expect(page.locator('app-projection-panel app-load-problem')).toBeVisible();
      expect(await axeViolations(page, 'app-projection-panel')).toEqual([]);
    });

    test('the Transports split has a caption, scoped headers and states its change with a sign, not only a colour', async ({ page }) => {
      await openTransportsFullScreen(page);
      const table = page.locator('app-kpi-card .split table');

      await expect(table.locator('caption')).toContainText('Transports by aircraft type or level of service');
      expect(await table.locator('thead th').evaluateAll((els) => els.map((e) => e.getAttribute('scope')))).toEqual(Array(5).fill('col'));
      await expect(table.locator('tbody th[scope="row"]')).toHaveCount(2);
      await expect(page.locator('app-kpi-card .split')).toHaveAttribute('role', 'region');
      await expect(page.locator('app-kpi-card .split')).toHaveAccessibleName('Transports by aircraft type or level of service');
      expect(await axeViolations(page)).toEqual([]);
    });

    test('the claims summary has named tables, scoped headers and headings that do not skip', async ({ page }) => {
      await openNeedsAttention(page);
      const summary = page.locator('app-attention-summary');

      await expect(summary.locator('table')).toHaveCount(3);
      for (const [i, name] of ['Top denial reasons', 'Claim categories', 'Filing deadlines (estimated)'].entries()) {
        await expect(summary.locator('table').nth(i)).toHaveAccessibleName(name);
      }
      expect(await summary.locator('thead th').evaluateAll((els) => els.every((e) => e.getAttribute('scope') === 'col'))).toBe(true);
      expect(await summary.locator('tbody th').evaluateAll((els) => els.every((e) => e.getAttribute('scope') === 'row'))).toBe(true);
      expect(await axeViolations(page, 'app-attention-summary')).toEqual([]);
    });

    test('payer timing and cash per transport tables in a view are named regions with scoped headers', async ({ page }) => {
      await openMetricView(page, 'payment_age');

      const regions = page.locator('app-metric-widget [role="region"]');
      await expect(regions).toHaveCount(2);
      await expect(regions.nth(1)).toHaveAccessibleName('Payer timing: typical months to payment and share of cash by age band');
      await expect(regions.nth(1)).toHaveAttribute('tabindex', '0');
      expect(await page.locator('app-metric-widget thead th').evaluateAll((els) => els.every((e) => e.getAttribute('scope') === 'col'))).toBe(true);
      expect(await axeViolations(page)).toEqual([]);

      await openMetricView(page, 'cash_per_transport_by_payer');
      await expect(page.locator('app-metric-widget .widget__foot').first()).toContainText('cash posted through');
      expect(await axeViolations(page)).toEqual([]);
    });
  });
});
