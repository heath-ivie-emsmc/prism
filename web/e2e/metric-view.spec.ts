import { expect, test } from './fixtures';
import { openMetricView, openMetricViewNotFound } from './support';

test.describe('generic metric view', () => {
  test('a Metric with no page of its own opens with a heading that has focus, the data stamp, context badge and help', async ({ page }) => {
    await openMetricView(page);

    await expect(page.getByRole('heading', { level: 1, name: 'Net cash collected' })).toBeFocused();
    await expect(page.locator('app-metric-view app-freshness-stamp')).toContainText('Data through');
    await expect(page.locator('app-metric-view app-context-badge')).toBeVisible();
    await expect(page.getByRole('navigation', { name: /breadcrumb/i })).toContainText('Dashboard');
    await expect(page.locator('app-metric-view app-metric-help')).toBeVisible();
  });

  test('an unknown key is one calm message with a way back', async ({ page }) => {
    await openMetricViewNotFound(page);

    await expect(page.getByText('This view does not exist')).toBeVisible();
    await page.getByRole('link', { name: 'Back to the dashboard' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
  });

  test('a Metric that has a page of its own is opened there, and Back does not stop on the generic address', async ({ page }) => {
    await page.goto('/dashboard');
    await page.goto('/dashboard/metrics/payer_mix_detail?group=rotor-wing&payerKey=commercial&period=202610&line=air');

    await expect(page).toHaveURL(/\/dashboard\/metrics\/payer-mix\/rotor-wing\/commercial\?period=202610&line=air$/);
    await page.locator('app-payer-mix-detail h1').waitFor();
    await page.goBack();
    await expect(page).toHaveURL(/\/dashboard$/);
  });

  test('the older addresses keep working', async ({ page }) => {
    await page.goto('/dashboard/metrics/payer_mix?group=fixed-wing&period=202610&line=air');
    await expect(page).toHaveURL(/\/dashboard\/net-cash\/fixed-wing\?period=202610&line=air$/);
    await page.locator('app-payer-mix app-chart canvas').waitFor();
  });
});
