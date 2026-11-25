import { readFileSync } from 'node:fs';
import { expect, test } from './fixtures';
import { axeViolations, layoutErrors, openNeedsAttention } from './support';

const rows = (page: import('@playwright/test').Page) => page.locator('app-needs-attention .claims tbody tr');
const send = (page: import('@playwright/test').Page) => page.locator('app-needs-attention button.send');

test.describe('needs attention', () => {
  test('lists the denied claims with the tabs, counts and a sort note', async ({ page }) => {
    await openNeedsAttention(page);

    await expect(page.locator('#claims-title')).toContainText('Fixed-wing claims, October 2026');
    await expect(page.getByRole('radio')).toHaveText(['Denied (2)', 'Needs attention (4)', 'All (8)']);
    await expect(rows(page)).toHaveCount(2);
    await expect(rows(page).first()).toContainText('FW-2610418');
    await expect(rows(page).first()).toContainText('Denied');
    await expect(rows(page).first()).toContainText('$24,600.00');
    await expect(page.locator('app-needs-attention')).toContainText('Sorted by estimated filing deadline, soonest first');
  });

  test('choosing another tab shows that check and puts it in the address', async ({ page }) => {
    await openNeedsAttention(page);

    await page.getByRole('radio', { name: 'Needs attention (4)' }).click();
    await expect(rows(page)).toHaveCount(4);
    await expect(page).toHaveURL(/check=needs_attention/);
    await expect(rows(page).nth(2)).toContainText('On hold');
    await expect(rows(page).nth(2)).toContainText('Overdue by 3 days');
  });

  test('Send to CSE is off until a claim is ticked, then shows a count badge and carries the count in its name', async ({ page }) => {
    await openNeedsAttention(page);
    await expect(send(page)).toBeDisabled();
    await expect(page.locator('.send__badge')).toHaveCount(0);

    await page.getByRole('checkbox', { name: 'Select claim FW-2610418' }).check();
    await expect(send(page)).toBeEnabled();
    await expect(page.locator('.send__badge')).toHaveText('1');
    await expect(send(page)).toHaveAccessibleName('Send to CSE, 1 claim selected');

    await page.getByRole('checkbox', { name: 'Select claim FW-2610457' }).check();
    await expect(page.locator('.send__badge')).toHaveText('2');
    await expect(send(page)).toHaveAccessibleName('Send to CSE, 2 claims selected');
  });

  test('the badge sits on the button corner and does not cover its label', async ({ page }) => {
    await openNeedsAttention(page);
    await page.getByRole('checkbox', { name: 'Select all 2 claims on this page' }).check();

    const button = (await send(page).boundingBox())!;
    const badge = (await page.locator('.send__badge').boundingBox())!;
    expect(badge.x + badge.width / 2).toBeGreaterThan(button.x + button.width - 20);
    expect(badge.y + badge.height / 2).toBeLessThan(button.y + 12);
  });

  test('the drawer lists the ticked claims and their total, keeps Send visible, and Escape returns focus to the button', async ({ page }) => {
    await openNeedsAttention(page);
    await page.getByRole('checkbox', { name: 'Select all 2 claims on this page' }).check();

    await send(page).click();
    const drawer = page.getByRole('dialog', { name: 'Send to your CSE' });
    await expect(drawer).toBeVisible();
    await expect(drawer).toContainText('Claims attached (2) · $48,200.00');
    await expect(drawer.getByRole('listitem')).toHaveCount(2);
    const sendInDrawer = drawer.getByRole('button', { name: 'Send', exact: true });
    await expect(sendInDrawer).toBeInViewport();
    await expect(sendInDrawer).toHaveAttribute('aria-disabled', 'true');
    await expect(drawer).toContainText('Sending is not available yet');
    const box = (await drawer.boundingBox())!;
    const viewport = page.viewportSize()!;
    expect(Math.round(box.x + box.width)).toBe(viewport.width);

    await page.keyboard.press('Escape');
    await expect(drawer).toBeHidden();
    await expect(send(page)).toBeFocused();
  });

  test('ticked claims stay ticked when the tab changes', async ({ page }) => {
    await openNeedsAttention(page);
    await page.getByRole('checkbox', { name: 'Select claim FW-2610418' }).check();

    await page.getByRole('radio', { name: 'All (8)' }).click();
    await expect(rows(page)).toHaveCount(8);
    await expect(page.getByRole('checkbox', { name: 'Select claim FW-2610418' })).toBeChecked();
    await expect(page.locator('.send__badge')).toHaveText('1');
  });

  test('the download button names the tab, asks for that tab, and saves the file the server named', async ({ page }) => {
    await openNeedsAttention(page);
    await page.getByRole('radio', { name: 'Needs attention (4)' }).click();
    await expect(rows(page)).toHaveCount(4);
    const button = page.getByRole('button', { name: 'Download CSV for Needs attention (4)' });

    const [download, request] = await Promise.all([page.waitForEvent('download'), page.waitForRequest(/\/needs_attention\/export/), button.click()]);

    expect(request.url()).toContain('period=202610');
    expect(request.url()).toContain('group=fixed-wing');
    expect(download.suggestedFilename()).toBe('prism-needs-attention-needs-attention-2026-11-22.csv');
    expect(readFileSync((await download.path())!, 'utf8')).toContain('"Claim"');
  });

  test('says calmly when the claims cannot load, and offers to try again', async ({ page }) => {
    await page.route(/claims\/attention\/denied/, (route) => route.fulfill({ status: 503, json: { title: 'SECRET' } }));
    await page.goto('/dashboard/needs-attention?check=denied&period=202610&line=air');

    await expect(page.getByRole('button', { name: 'Try again' })).toBeVisible();
    await expect(page.locator('app-needs-attention')).not.toContainText('SECRET');
  });

  test('a user without the claims permission is told so and no claim is requested', async ({ page }) => {
    await page.route('**/api/v1/me', (route) =>
      route.fulfill({ json: { userName: 'jdoe', displayName: 'Jane Doe', providers: [{ id: '526', name: 'Seminole County Fire Department', facilityCount: 0, permissions: ['prism.dashboard.view'] }], permissions: ['prism.app'] } }),
    );
    let asked = false;
    await page.route(/claims\/attention/, (route) => {
      asked = true;
      return route.fulfill({ status: 403, json: {} });
    });

    await page.goto('/dashboard/needs-attention?check=denied&period=202610&line=air');

    await expect(page.locator('app-needs-attention')).toContainText("You don't have access to claim details");
    expect(asked).toBe(false);
    await expect(page.locator('app-needs-attention table')).toHaveCount(0);
  });

  test('the Insights card points at the denied claims for users who hold the permission', async ({ page }) => {
    await page.goto('/dashboard/net-cash/fixed-wing?period=202610&line=air');
    const link = page.getByRole('link', { name: 'Review 2 denied claims' });

    await expect(page.getByText('2 denied claims need attention')).toBeVisible();
    await expect(link).toBeVisible();
    await link.click();
    await expect(page).toHaveURL(/dashboard\/needs-attention\?check=denied&period=202610&line=air&group=fixed-wing/);
    await expect(rows(page)).toHaveCount(2);
  });

  test('the Insights card is absent without the permission', async ({ page }) => {
    await page.route('**/api/v1/me', (route) =>
      route.fulfill({ json: { userName: 'jdoe', displayName: 'Jane Doe', providers: [{ id: '526', name: 'Seminole County Fire Department', facilityCount: 0, permissions: ['prism.dashboard.view'] }], permissions: ['prism.app'] } }),
    );
    await page.goto('/dashboard/net-cash/fixed-wing?period=202610&line=air');
    await page.locator('app-what-changed .insight__headline').waitFor();

    await expect(page.getByText('denied claims need attention')).toHaveCount(0);
  });

  test('has no layout or axe problems with claims ticked and the drawer open', async ({ page }) => {
    await openNeedsAttention(page);
    await page.getByRole('checkbox', { name: 'Select all 2 claims on this page' }).check();
    expect((await layoutErrors(page)).errors).toEqual([]);
    expect(await axeViolations(page)).toEqual([]);

    await send(page).click();
    await page.getByRole('dialog', { name: 'Send to your CSE' }).waitFor();
    expect(await axeViolations(page)).toEqual([]);
  });
});
