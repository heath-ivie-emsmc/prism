import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from './fixtures';
import { REPORT_DIR, SCHEMES, SIZES, axeViolations, chooseNetCashView, layoutErrors, openDashboard } from './support';

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
