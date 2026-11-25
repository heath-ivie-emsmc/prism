import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, mockNoDeniedClaims, test } from './fixtures';
import { REPORT_DIR, SCHEMES, SIZES, axeViolations, chooseNetCashView, layoutErrors, expandHelp, openDashboard, openDashboardWithNotice, openDashboardHelp, openViewHelp, openMetricView, openMetricViewNotFound, openNeedsAttention, openPayerDetail, openBreakdownPage, openProjection, openTransportsFullScreen, openPayerMixPage, openSavedViews, openView, openAdmin, openAdminPersonas, openProviderUsers, openAdminCustomers, openSettings, openHome, openWelcome, openTemplates, openPushDialog, openTemplateEditor, SIX_METRICS, openMetricViewGroundOnly, openPaletteUnderGround, openEmptyDivisionView, openSixMetricView, CONTRIBUTION_METRICS, openInsightNewKinds } from './support';

/**
 * Responsive layout and accessibility of the dashboard at every window size, light and dark. In a real browser it checks that:
 * the page does not scroll sideways, no text is drawn over other text, no text is cut off or spills out of its card,
 * and axe finds no WCAG 2.2 AA problem (contrast and target size included).
 * Screenshots and a JSON report per run go to e2e-report/ (not committed).
 */
for (const scheme of SCHEMES) {
  for (const size of SIZES) {
    test(`dashboard layout: ${size.name} ${size.width}px, ${scheme}`, async ({ page }) => {
      await page.setViewportSize({ width: size.width, height: size.height });
      await page.emulateMedia({ colorScheme: scheme });
      await openDashboard(page);

      mkdirSync(REPORT_DIR, { recursive: true });
      const label = `${size.name}-${size.width}-${scheme}`;
      await page.screenshot({ path: join(REPORT_DIR, `${label}.png`), fullPage: true });

      const { errors, issues } = await layoutErrors(page);
      const axe = await axeViolations(page);
      writeFileSync(join(REPORT_DIR, `${label}.json`), JSON.stringify({ size, scheme, issues, axe }, null, 2));

      expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
      expect(axe, `axe violations at ${label}`).toEqual([]);
    });
  }
}

// The bar and table views of the net cash panel, at every size.
for (const view of ['bar', 'table'] as const) {
  for (const size of SIZES) {
    test(`net cash ${view} view: ${size.name} ${size.width}px`, async ({ page }) => {
      await page.setViewportSize({ width: size.width, height: size.height });
      await openDashboard(page);
      await chooseNetCashView(page, view);

      mkdirSync(REPORT_DIR, { recursive: true });
      const label = `${view}-${size.name}-${size.width}`;
      await page.screenshot({ path: join(REPORT_DIR, `${label}.png`), fullPage: true });

      const { errors, issues } = await layoutErrors(page);
      const axe = await axeViolations(page);
      writeFileSync(join(REPORT_DIR, `${label}.json`), JSON.stringify({ size, view, issues, axe }, null, 2));

      expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
      expect(axe, `axe violations at ${label}`).toEqual([]);
    });
  }
}

// The admin home, as an Internal User with every tier and with none. Every size, light and dark.
const ADMIN_STATES = [
  { name: 'all-tiers', open: (p: import('@playwright/test').Page) => openAdmin(p) },
  { name: 'no-tier', open: (p: import('@playwright/test').Page) => openAdmin(p, []) },
] as const;
for (const state of ADMIN_STATES) {
  for (const scheme of SCHEMES) {
    for (const size of SIZES) {
      test(`admin ${state.name}: ${size.name} ${size.width}px, ${scheme}`, async ({ page }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        await page.emulateMedia({ colorScheme: scheme });
        await state.open(page);

        mkdirSync(REPORT_DIR, { recursive: true });
        const label = `admin-${state.name}-${size.name}-${size.width}-${scheme}`;
        await page.screenshot({ path: join(REPORT_DIR, `${label}.png`), fullPage: true });

        const { errors, issues } = await layoutErrors(page);
        const axe = await axeViolations(page);
        writeFileSync(join(REPORT_DIR, `${label}.json`), JSON.stringify({ size, scheme, issues, axe }, null, 2));

        expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
        expect(axe, `axe violations at ${label}`).toEqual([]);
      });
    }
  }
}

// The View Template pages: the list, the push dialog, the Provider chooser and the editor with figures. Every size, light and dark.
const TEMPLATE_STATES = [
  { name: 'list', open: (p: import('@playwright/test').Page) => openTemplates(p) },
  { name: 'push', open: (p: import('@playwright/test').Page) => openPushDialog(p) },
  { name: 'chooser', open: (p: import('@playwright/test').Page) => openTemplateEditor(p, 'new') },
  { name: 'editor', open: (p: import('@playwright/test').Page) => openTemplateEditor(p, 1, '526') },
] as const;
for (const state of TEMPLATE_STATES) {
  for (const scheme of SCHEMES) {
    for (const size of SIZES) {
      test(`templates ${state.name}: ${size.name} ${size.width}px, ${scheme}`, async ({ page }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        await page.emulateMedia({ colorScheme: scheme });
        await state.open(page);

        mkdirSync(REPORT_DIR, { recursive: true });
        const label = `templates-${state.name}-${size.name}-${size.width}-${scheme}`;
        await page.screenshot({ path: join(REPORT_DIR, `${label}.png`), fullPage: true });

        const { errors, issues } = await layoutErrors(page);
        const axe = await axeViolations(page);
        writeFileSync(join(REPORT_DIR, `${label}.json`), JSON.stringify({ size, scheme, issues, axe }, null, 2));

        expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
        expect(axe, `axe violations at ${label}`).toEqual([]);
      });
    }
  }
}

// The home page as a Persona page and as the user's own copy, the first-login questions, and the Persona admin page. Every size, light and dark.
const PERSONA_PAGE_STATES = [
  { name: 'home-persona', open: (p: import('@playwright/test').Page) => openHome(p, 'persona') },
  { name: 'home-custom', open: (p: import('@playwright/test').Page) => openHome(p, 'custom') },
  { name: 'provider-users', open: (p: import('@playwright/test').Page) => openProviderUsers(p) },
  { name: 'admin-customers', open: (p: import('@playwright/test').Page) => openAdminCustomers(p) },
  { name: 'settings', open: (p: import('@playwright/test').Page) => openSettings(p) },
  { name: 'welcome', open: (p: import('@playwright/test').Page) => openWelcome(p) },
  { name: 'admin-personas', open: (p: import('@playwright/test').Page) => openAdminPersonas(p) },
  {
    name: 'admin-personas-forms',
    open: async (p: import('@playwright/test').Page) => {
      await openAdminPersonas(p);
      await p.getByRole('button', { name: 'Add a Persona' }).click();
      await p.getByRole('button', { name: 'Add a question' }).click();
      await p.locator('#question-prompt').waitFor();
      await p.waitForTimeout(200);
    },
  },
] as const;
for (const state of PERSONA_PAGE_STATES) {
  for (const scheme of SCHEMES) {
    for (const size of SIZES) {
      test(`personas ${state.name}: ${size.name} ${size.width}px, ${scheme}`, async ({ page }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        await page.emulateMedia({ colorScheme: scheme });
        await state.open(page);

        mkdirSync(REPORT_DIR, { recursive: true });
        const label = `personas-${state.name}-${size.name}-${size.width}-${scheme}`;
        await page.screenshot({ path: join(REPORT_DIR, `${label}.png`), fullPage: true });

        const { errors, issues } = await layoutErrors(page);
        const axe = await axeViolations(page);
        writeFileSync(join(REPORT_DIR, `${label}.json`), JSON.stringify({ size, scheme, issues, axe }, null, 2));

        expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
        expect(axe, `axe violations at ${label}`).toEqual([]);
      });
    }
  }
}

// The saved views pages: the list, a view to read, a view being arranged, and a new empty one. Every size, light and dark.
const SAVED_VIEW_STATES = [
  { name: 'list', open: openSavedViews },
  { name: 'view', open: (p: import('@playwright/test').Page) => openView(p, 5) },
  { name: 'edit', open: (p: import('@playwright/test').Page) => openView(p, 5, { edit: true }) },
  { name: 'new', open: (p: import('@playwright/test').Page) => openView(p, 'new') },
] as const;
for (const state of SAVED_VIEW_STATES) {
  for (const scheme of SCHEMES) {
    for (const size of SIZES) {
      test(`saved views ${state.name}: ${size.name} ${size.width}px, ${scheme}`, async ({ page }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        await page.emulateMedia({ colorScheme: scheme });
        await state.open(page);

        mkdirSync(REPORT_DIR, { recursive: true });
        const label = `saved-views-${state.name}-${size.name}-${size.width}-${scheme}`;
        await page.screenshot({ path: join(REPORT_DIR, `${label}.png`), fullPage: true });

        const { errors, issues } = await layoutErrors(page);
        const axe = await axeViolations(page);
        writeFileSync(join(REPORT_DIR, `${label}.json`), JSON.stringify({ size, scheme, issues, axe }, null, 2));

        expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
        expect(axe, `axe violations at ${label}`).toEqual([]);
      });
    }
  }
}

// The claims review page: the list, with claims ticked and the Send to CSE drawer open. Every size, light and dark.
const ATTENTION_STATES = [
  { name: 'list', open: (p: import('@playwright/test').Page) => openNeedsAttention(p) },
  {
    name: 'drawer',
    open: async (p: import('@playwright/test').Page) => {
      await openNeedsAttention(p, 'check=needs_attention&period=202610&line=air&group=fixed-wing');
      await p.getByRole('checkbox', { name: 'Select all 4 claims on this page' }).check();
      await p.locator('app-needs-attention button.send').click();
      await p.getByRole('dialog', { name: 'Send to your CSE' }).waitFor();
      await p.waitForTimeout(300);
    },
  },
] as const;
for (const state of ATTENTION_STATES) {
  for (const scheme of SCHEMES) {
    for (const size of SIZES) {
      test(`needs attention ${state.name}: ${size.name} ${size.width}px, ${scheme}`, async ({ page }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        await page.emulateMedia({ colorScheme: scheme });
        await state.open(page);

        mkdirSync(REPORT_DIR, { recursive: true });
        const label = `needs-attention-${state.name}-${size.name}-${size.width}-${scheme}`;
        await page.screenshot({ path: join(REPORT_DIR, `${label}.png`), fullPage: true });

        const { errors, issues } = await layoutErrors(page);
        const axe = await axeViolations(page);
        writeFileSync(join(REPORT_DIR, `${label}.json`), JSON.stringify({ size, scheme, issues, axe }, null, 2));

        expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
        expect(axe, `axe violations at ${label}`).toEqual([]);
      });
    }
  }
}

test.describe('app icons', () => {
  test('the page links an SVG, an ICO and an Apple touch icon, and each one loads', async ({ page, request }) => {
    await page.goto('/login');
    const hrefs = await page.locator('link[rel~="icon"], link[rel="apple-touch-icon"]').evaluateAll((links) => links.map((l) => (l as HTMLLinkElement).href));
    expect(hrefs.map((h) => new URL(h).pathname).sort()).toEqual(['/apple-touch-icon.png', '/favicon.ico', '/favicon.svg']);
    for (const href of hrefs) {
      const response = await request.get(href);
      expect(response.ok(), href).toBe(true);
      expect(response.headers()['content-type'], href).toMatch(/image\/|icon/);
    }
  });
});

// The payer mix and its drill target: every size, light and dark. The detail page also in its table view, which has the widest content.
const PAYER_PAGES = [
  { name: 'payer-mix', open: (p: import('@playwright/test').Page) => openPayerMixPage(p) },
  { name: 'payer-detail', open: (p: import('@playwright/test').Page) => openPayerDetail(p) },
  // The Reporting Division control on the pages that follow the dashboard's choice, with a division key in the address whose name is hostile text.
  { name: 'payer-mix-division', open: (p: import('@playwright/test').Page) => openPayerMixPage(p, 'rotor-wing', 'period=202610&line=air&division=x-img') },
  { name: 'payer-detail-division', open: (p: import('@playwright/test').Page) => openPayerDetail(p, 'period=202610&line=air&division=x-img') },
  { name: 'net-cash-breakdown', open: (p: import('@playwright/test').Page) => openBreakdownPage(p) },
  { name: 'net-cash-breakdown-division', open: (p: import('@playwright/test').Page) => openBreakdownPage(p, 'period=202610&line=air&division=x-img') },
  {
    name: 'payer-detail-table',
    open: async (p: import('@playwright/test').Page) => {
      await openPayerDetail(p);
      await p.getByRole('radio', { name: 'Table' }).click();
      await p.locator('app-payer-mix-detail table').waitFor();
    },
  },
  {
    name: 'payer-detail-donut',
    open: async (p: import('@playwright/test').Page) => {
      await openPayerDetail(p);
      await p.getByRole('radio', { name: 'Donut charts' }).click();
      await p.locator('app-payer-donuts svg').first().waitFor();
    },
  },
  {
    name: 'payer-detail-help',
    open: async (p: import('@playwright/test').Page) => {
      await openPayerDetail(p);
      await expandHelp(p);
    },
  },
  {
    name: 'payer-mix-help',
    open: async (p: import('@playwright/test').Page) => {
      await openPayerMixPage(p);
      await expandHelp(p);
    },
  },
  {
    name: 'payer-mix-table',
    open: async (p: import('@playwright/test').Page) => {
      await openPayerMixPage(p);
      await p.getByRole('radio', { name: 'Table' }).click();
      await p.locator('app-payer-mix table').waitFor();
    },
  },
  {
    name: 'payer-mix-donut',
    open: async (p: import('@playwright/test').Page) => {
      await openPayerMixPage(p);
      await p.getByRole('radio', { name: 'Donut charts' }).click();
      await p.locator('app-payer-donuts svg').first().waitFor();
    },
  },
  // The figures the metric catalog added: the projection panel at each horizon, with no actual month, with its help; the Transports split; payer timing; cash per transport; and the summary of claims needing attention.
  { name: 'projection-one-month', open: (p: import('@playwright/test').Page) => openProjection(p, { months: 1 }) },
  { name: 'projection-two-months', open: (p: import('@playwright/test').Page) => openProjection(p, { months: 2 }) },
  { name: 'projection-no-actual', open: (p: import('@playwright/test').Page) => openProjection(p, { ground: true }) },
  {
    name: 'projection-help',
    open: async (p: import('@playwright/test').Page) => {
      await openProjection(p);
      await expandHelp(p);
    },
  },
  {
    name: 'projection-failed',
    open: async (p: import('@playwright/test').Page) => {
      await p.route(/net-collections\/projection/, (route) => route.fulfill({ status: 503, json: { title: 'x' } }));
      await openDashboard(p);
      await p.locator('app-projection-panel app-load-problem').waitFor();
      await p.waitForTimeout(300);
    },
  },
  { name: 'transports-split', open: (p: import('@playwright/test').Page) => openTransportsFullScreen(p) },
  { name: 'metric-view-payment-timing', open: (p: import('@playwright/test').Page) => openMetricView(p, 'payment_age') },
  { name: 'metric-view-cash-per-transport', open: (p: import('@playwright/test').Page) => openMetricView(p, 'cash_per_transport_by_payer') },
  { name: 'metric-view-projection', open: (p: import('@playwright/test').Page) => openMetricView(p, 'net_cash_projection') },
  { name: 'metric-view-transports', open: (p: import('@playwright/test').Page) => openMetricView(p, 'transport_volume') },
  {
    name: 'attention-no-denied',
    open: async (p: import('@playwright/test').Page) => {
      await mockNoDeniedClaims(p);
      await openNeedsAttention(p);
    },
  },
  { name: 'dashboard-help-open', open: (p: import('@playwright/test').Page) => openDashboardHelp(p) },
  { name: 'saved-view-help-open', open: (p: import('@playwright/test').Page) => openViewHelp(p) },
  { name: 'dashboard-notice', open: (p: import('@playwright/test').Page) => openDashboardWithNotice(p) },
  { name: 'metric-view', open: (p: import('@playwright/test').Page) => openMetricView(p) },
  { name: 'metric-view-not-found', open: (p: import('@playwright/test').Page) => openMetricViewNotFound(p) },
  // The six Metrics that came with the appliesTo field: each one's page, an air-only one under Ground only, saved views holding all of them, and the palette under Ground only.
  ...SIX_METRICS.map((m) => ({ name: `metric-view-${m.key.replaceAll('_', '-')}`, open: (p: import('@playwright/test').Page) => openMetricView(p, m.key) })),
  { name: 'metric-view-air-only-under-ground', open: (p: import('@playwright/test').Page) => openMetricViewGroundOnly(p) },
  { name: 'six-metrics-view-a', open: (p: import('@playwright/test').Page) => openSixMetricView(p, 'a') },
  { name: 'six-metrics-view-b', open: (p: import('@playwright/test').Page) => openSixMetricView(p, 'b') },
  ...CONTRIBUTION_METRICS.map((m) => ({ name: `metric-view-${m.key.replaceAll('_', '-')}`, open: (p: import('@playwright/test').Page) => openMetricView(p, m.key) })),
  { name: 'metric-view-contribution-under-all-lines', open: (p: import('@playwright/test').Page) => openMetricView(p, 'cash_contribution_by_payer', 'period=202610&line=all') },
  { name: 'metric-view-contribution-air-only-under-ground', open: (p: import('@playwright/test').Page) => openMetricViewGroundOnly(p, 'cash_contribution_by_aircraft') },
  { name: 'contribution-view', open: (p: import('@playwright/test').Page) => openSixMetricView(p, 'c') },
  { name: 'insight-new-card-kinds', open: (p: import('@playwright/test').Page) => openInsightNewKinds(p) },
  { name: 'view-empty-division', open: (p: import('@playwright/test').Page) => openEmptyDivisionView(p) },
  { name: 'palette-under-ground', open: (p: import('@playwright/test').Page) => openPaletteUnderGround(p) },
] as const;
for (const state of PAYER_PAGES) {
  for (const scheme of SCHEMES) {
    for (const size of SIZES) {
      test(`${state.name} layout: ${size.name} ${size.width}px, ${scheme}`, async ({ page }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        await page.emulateMedia({ colorScheme: scheme });
        await state.open(page);

        mkdirSync(REPORT_DIR, { recursive: true });
        const label = `${state.name}-${size.name}-${size.width}-${scheme}`;
        await page.screenshot({ path: join(REPORT_DIR, `${label}.png`), fullPage: true });

        const { errors, issues } = await layoutErrors(page);
        const axe = await axeViolations(page);
        writeFileSync(join(REPORT_DIR, `${label}.json`), JSON.stringify({ size, scheme, issues, axe }, null, 2));

        expect(errors, `layout errors at ${label} (screenshot: ${REPORT_DIR}/${label}.png)`).toEqual([]);
        expect(axe, `axe violations at ${label}`).toEqual([]);
      });
    }
  }
}
