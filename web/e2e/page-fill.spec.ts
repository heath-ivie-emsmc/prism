import { expect, test } from './fixtures';
import { openDashboard } from './support';

/** No browser-default margin showing the body's white around the themed page, in either color scheme. */
for (const colorScheme of ['light', 'dark'] as const) {
  test(`the page fills the window with no border: ${colorScheme}`, async ({ page }) => {
    await page.emulateMedia({ colorScheme });
    await openDashboard(page);

    const box = await page.evaluate(() => {
      const r = document.querySelector('.ems-ui--page')!.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, viewport: innerWidth, fills: r.height >= innerHeight };
    });

    expect(box).toEqual({ x: 0, y: 0, width: box.viewport, viewport: box.viewport, fills: true });
  });
}
