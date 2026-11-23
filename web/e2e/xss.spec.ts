import { expect, test } from './fixtures';

const HOSTILE = '<img src=x onerror="window.__xss=1">"><script>window.__xss=1</script>';

test.describe('hostile warehouse text', () => {
  test('a payer name is shown literally in the chart tooltip and never becomes markup', async ({ page }) => {
    await page.route(/\/api\/v1\/providers\/[^/]+\/metrics\/net-collections\/payer-mix(\?.*)?$/, (route) => {
      const url = new URL(route.request().url());
      return route.fulfill({
        json: {
          metric: { key: 'payer_mix', name: 'Payer mix', description: 'Transports by primary payer group.', unit: 'transports' },
          providerId: '526',
          period: '202610',
          priorPeriod: '202609',
          serviceLine: 'air',
          group: url.searchParams.get('group'),
          groupName: 'Fixed Wing',
          dataThrough: '2026-10-31',
          shares: [
            { key: 'commercial', name: HOSTILE, prior: 50, current: 60 },
            { key: 'medicare', name: 'Medicare', prior: 50, current: 40 },
          ],
        },
      });
    });
    const dialogs: string[] = [];
    page.on('dialog', (dialog) => {
      dialogs.push(dialog.message());
      void dialog.dismiss();
    });

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/dashboard/net-cash/fixed-wing?period=202610&line=air');
    const canvas = page.locator('app-payer-mix app-chart canvas');
    await canvas.waitFor();
    await page.waitForTimeout(500);
    const box = (await canvas.boundingBox())!;
    // The first payer's bars sit in the left half of the plot.
    await page.mouse.move(box.x + box.width * 0.2, box.y + box.height / 2);
    await page.mouse.move(box.x + box.width * 0.25, box.y + box.height / 2 + 5, { steps: 5 });

    // ECharts draws its tooltip as a div it marks with pointer-events; its text is the formatter's HTML.
    const tooltip = page.locator('div[style*="pointer-events"]').filter({ hasText: 'Sep:' });
    await expect(tooltip).toContainText(HOSTILE);
    expect(await tooltip.locator('img, script').count()).toBe(0);
    await page.waitForTimeout(300);

    expect(await page.locator('app-payer-mix app-chart img[src="x"], app-payer-mix app-chart script').count()).toBe(0);
    expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();
    expect(dialogs).toEqual([]);
  });
});
