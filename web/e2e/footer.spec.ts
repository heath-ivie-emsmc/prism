import { expect, test } from './fixtures';
import { REPORT_DIR, SIZES, openPayerDetail } from './support';

/** The footer is fixed to the bottom of the window, so the last line of content must be able to scroll clear of it (WCAG 2.4.11 focus not obscured, 1.4.10 reflow). */
test.describe('footer does not hide the end of the page', () => {
  for (const size of SIZES) {
    test(`payer breakdown: last content scrolls clear of the footer at ${size.name} ${size.width}px`, async ({ page }) => {
      await page.setViewportSize({ width: size.width, height: size.height });
      await openPayerDetail(page);
      await page.locator('app-metric-help button.help__toggle').waitFor();
      await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
      await page.screenshot({ path: `${REPORT_DIR}/footer-bottom-${size.name}-${size.width}.png` });
      const gap = await page.evaluate(() => {
        const footer = document.querySelector('footer')!.getBoundingClientRect();
        const last = document.querySelector('app-metric-help')!.getBoundingClientRect();
        return footer.top - last.bottom;
      });
      expect(gap, 'the help text ends above the footer').toBeGreaterThanOrEqual(0);
    });
  }
});
