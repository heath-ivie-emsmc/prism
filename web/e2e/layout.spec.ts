import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from './fixtures';
import { REPORT_DIR, SCHEMES, SIZES, axeViolations, chooseNetCashView, layoutErrors, openDashboard, openNeedsAttention, openSavedViews, openView } from './support';

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
