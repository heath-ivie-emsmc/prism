import { expect, test } from './fixtures';
import { SIZES, openDashboard } from './support';

/** Switching the projection on must not move or resize anything else on the page. */
for (const size of SIZES) {
  test(`the projection switch does not shift other panels: ${size.name} ${size.width}px`, async ({ page }) => {
    await page.setViewportSize({ width: size.width, height: size.height });
    await openDashboard(page);
    const boxes = () => page.evaluate(() => [...document.querySelectorAll('app-kpi-card, app-projection-panel, app-net-cash-panel')].map((el) => { const r = el.getBoundingClientRect(); return [Math.round(r.x + scrollX), Math.round(r.y + scrollY), Math.round(r.width), Math.round(r.height)]; }));
    const before = await boxes();

    await page.getByRole('switch', { name: 'Show 3-month projection' }).click();
    await page.getByText('Expected, next 3 months').waitFor({ state: 'visible' });

    expect(await boxes()).toEqual(before);
  });
}
