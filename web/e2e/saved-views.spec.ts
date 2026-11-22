import { expect, test } from './fixtures';
import { axeViolations, lastAnnouncement, layoutErrors, openSavedViews, openView } from './support';

const position = (page: import('@playwright/test').Page, id: string) =>
  page.locator(`[data-widget-id="${id}"]`).evaluate((el) => [(el as HTMLElement).style.getPropertyValue('--x'), (el as HTMLElement).style.getPropertyValue('--y')]);

/** What the saved views do in a browser: list, read, arrange by keyboard and pointer, save, share, and every refusal. The API is mocked. */
test.describe('saved views', () => {
  test('the list shows my views and the ones shared with me as links', async ({ page }) => {
    await openSavedViews(page);

    await expect(page.getByRole('heading', { level: 1, name: 'Saved views' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'My views' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Cash overview' })).toHaveAttribute('href', '/saved-views/5');
    await expect(page.getByRole('link', { name: 'Executive summary' })).toHaveAttribute('href', '/saved-views/6');
    await expect(page.getByText('Shared by asmith')).toBeVisible();
    await expect(page.getByRole('navigation').getByRole('link', { name: 'Saved views' })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

  test('a user with no views is invited to create one', async ({ page }) => {
    await page.route(/\/api\/v1\/providers\/[^/]+\/saved-views(\?.*)?$/, (route) => route.fulfill({ json: { mine: [], shared: [] } }));
    await page.goto('/saved-views');

    await expect(page.getByRole('heading', { name: 'No saved views yet' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Create a view' }).first()).toHaveAttribute('href', '/saved-views/new');
    expect(await axeViolations(page)).toEqual([]);
  });

  test('a list that cannot load says so calmly and recovers on retry', async ({ page }) => {
    let fail = true;
    await page.route(/\/api\/v1\/providers\/[^/]+\/saved-views(\?.*)?$/, (route) => (fail ? route.fulfill({ status: 503, json: { title: 'x', traceId: '77114806214001843f6761e5254e96e3' } }) : route.fallback()));
    await page.goto('/saved-views');

    await expect(page.getByText("We can't load your saved views right now")).toBeVisible();
    fail = false;
    await page.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByRole('link', { name: 'Cash overview' })).toBeVisible();
  });

  test('opening a view shows its metrics, read-only, with the page filters and Data Through', async ({ page }) => {
    await openView(page, 5);

    await expect(page.getByRole('heading', { level: 1, name: 'Cash overview' })).toBeVisible();
    await expect(page.locator('[role="toolbar"]')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Edit', exact: true })).toBeVisible();
    await expect(page.getByRole('form', { name: 'View filters' })).toBeVisible();
    await expect(page.getByText('Filters apply to every metric on this page.')).toBeVisible();
    await expect(page.locator('.builder__subtitle')).toContainText('Data through 10/31/2026');
    expect(await page.locator('app-metric-widget app-chart [role="img"]').getAttribute('aria-label')).toMatch(/Net cash collected, 6 months to Oct 2026/);
    await expect(page.locator('#division-reason')).toContainText("can't be split by");
  });

  test('every chart has the same numbers as a table one control away', async ({ page }) => {
    await openView(page, 5);

    await page.getByRole('button', { name: 'Table', exact: true }).first().click();
    const rows = await page.locator('app-metric-widget table tbody tr').allTextContents();

    expect(rows).toHaveLength(6);
    expect(rows[5]).toContain('Oct 2026');
    expect(rows[5]).toContain('$1,100,240.00');
  });

  test('a metric the catalog no longer has is a placeholder, not a crash', async ({ page }) => {
    await openView(page, 7);

    await expect(page.getByText('Metric no longer available').first()).toBeVisible();
    await expect(page.locator('table').first()).toBeVisible();
    await expect(page.getByText('{{')).toHaveCount(0);
  });

  test('a view shared with me is read-only and says who shared it', async ({ page }) => {
    await openView(page, 6);

    await expect(page.getByText('Shared by asmith').first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Edit', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Share', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Save a copy' })).toBeVisible();
  });

  test('a view that is not there, or not mine to see, is one calm message', async ({ page }) => {
    await page.goto('/saved-views/999');

    await expect(page.getByRole('heading', { level: 1, name: "We couldn't find this view for your account" })).toBeVisible();
    await page.getByRole('button', { name: 'Back to saved views' }).click();
    await expect(page).toHaveURL(/\/saved-views$/);
  });

  test('keyboard: add a metric with Enter, hear where it went, and land on its Move button', async ({ page }) => {
    await openView(page, 5, { edit: true });
    const before = await page.locator('app-grid-canvas li.item').count();

    await page.getByRole('button', { name: 'Add Payer mix to view' }).focus();
    await page.keyboard.press('Enter');

    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(before + 1);
    await expect.poll(() => lastAnnouncement(page)).toMatch(/^Payer mix added at row \d+, column \d+, size M, 6 columns wide\. 3 metrics on this view\.$/);
    await expect(page.locator(':focus')).toHaveAttribute('aria-label', 'Move Payer mix');
  });

  test('keyboard: move a widget with the arrow keys, drop with Enter, cancel with Escape', async ({ page }) => {
    await openView(page, 5, { edit: true });
    const move = page.getByRole('button', { name: 'Move Clean claim rate', exact: true });
    const start = await position(page, 'w2');

    await move.focus();
    await page.keyboard.press('Enter');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('ArrowRight');
    await expect.poll(() => lastAnnouncement(page)).toMatch(/^Row \d+, column \d+\.$/);
    await page.keyboard.press('Escape');
    expect(await position(page, 'w2')).toEqual(start);
    await expect.poll(() => lastAnnouncement(page)).toMatch(/^Move cancelled\./);

    await page.keyboard.press('Enter');
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Enter');
    await expect.poll(() => lastAnnouncement(page)).toMatch(/dropped at row/);
    expect(await position(page, 'w2')).not.toEqual(start);
  });

  test('auto arrange fills the gaps a move leaves, and one undo puts it back', async ({ page }) => {
    await openView(page, 5, { edit: true });
    const move = page.getByRole('button', { name: 'Move Clean claim rate', exact: true });
    await move.focus();
    await page.keyboard.press('Enter');
    for (let i = 0; i < 4; i += 1) {
      await page.keyboard.press('PageDown');
    }
    await page.keyboard.press('Enter');
    const scattered = await position(page, 'w2');

    await page.getByRole('button', { name: 'Auto arrange' }).click();
    await expect.poll(() => lastAnnouncement(page)).toMatch(/arranged to fill the gaps/);
    expect(await position(page, 'w2')).not.toEqual(scattered);

    await page.getByRole('button', { name: /^Undo/ }).click();
    expect(await position(page, 'w2')).toEqual(scattered);
  });

  test('download image saves a real PNG named after the view, with no controls in it', async ({ page }) => {
    await openView(page, 5);
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download image' }).click();
    const file = await download;

    expect(file.suggestedFilename()).toMatch(/^prism-Cash-overview-\d{4}-\d{2}-\d{2}\.png$/);
    const path = await file.path();
    const bytes = (await import('node:fs')).readFileSync(path);
    expect(bytes.subarray(0, 8).toString('hex')).toBe('89504e470d0a1a0a');
    expect(bytes.length).toBeGreaterThan(5_000);
    await expect.poll(() => lastAnnouncement(page)).toMatch(/^Image saved as prism-Cash-overview-/);
    await expect(page.locator('.snapshot-caption')).toHaveCount(0);
  });

  test('a move is cancelled when focus leaves, so Tab never traps', async ({ page }) => {
    await openView(page, 5, { edit: true });
    await page.getByRole('button', { name: 'Move Clean claim rate', exact: true }).focus();
    await page.keyboard.press('Enter');

    await page.keyboard.press('Tab');

    await expect.poll(() => lastAnnouncement(page)).toMatch(/^Move cancelled\./);
    await expect(page.getByText('Moving', { exact: true })).toHaveCount(0);
  });

  test('without dragging: the settings pane sets size and position with plain buttons', async ({ page }) => {
    await openView(page, 5, { edit: true });
    await page.getByRole('button', { name: 'Settings for Clean claim rate' }).click();
    const pane = page.getByRole('complementary', { name: 'Widget settings' });
    await expect(pane.getByRole('heading', { name: 'Settings: Clean claim rate' })).toBeFocused();

    await pane.getByRole('button', { name: 'Move down one row' }).click();
    await pane.getByRole('button', { name: 'Increase width' }).click();

    expect((await position(page, 'w2'))[1]).toBe('1');
    expect(await page.locator('[data-widget-id="w2"]').evaluate((el) => (el as HTMLElement).style.getPropertyValue('--w'))).toBe('4');
    await pane.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByRole('button', { name: 'Settings for Clean claim rate' })).toBeFocused();
  });

  test('a pointer can drag a metric from the palette onto the grid', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await openView(page, 5, { edit: true });
    const before = await page.locator('app-grid-canvas li.item').count();
    const grip = page.locator('app-metric-palette li', { hasText: 'Projected net cash' }).locator('.palette__grip');
    const canvas = page.locator('#view-canvas');
    const box = (await canvas.boundingBox())!;
    const from = (await grip.boundingBox())!;

    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + 200, box.y + 360, { steps: 12 });
    await expect(page.getByText(/Drop here: row \d+, column \d+/)).toBeVisible();
    await page.mouse.up();

    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(before + 1);
  });

  test('saving sends only the name, the description and the layout, with the version it loaded', async ({ page }) => {
    await openView(page, 5, { edit: true });
    await page.getByRole('button', { name: 'Remove Clean claim rate' }).click();
    await expect(page.getByText('Unsaved changes').first()).toBeVisible();

    const [request] = await Promise.all([
      page.waitForRequest((r) => r.method() === 'PUT' && /saved-views\/5$/.test(r.url())),
      page.getByRole('button', { name: 'Save', exact: true }).click(),
    ]);
    const body = request.postDataJSON() as { name: string; description: unknown; layout: { version: number; density: string; widgets: Record<string, unknown>[] } };

    expect(Object.keys(body).sort()).toEqual(['description', 'layout', 'name']);
    expect(Object.keys(body.layout).sort()).toEqual(['density', 'version', 'widgets']);
    expect(Object.keys(body.layout.widgets[0]!).sort()).toEqual(['h', 'id', 'metricKey', 'presentation', 'w', 'x', 'y']);
    expect(JSON.stringify(body)).not.toMatch(/providerId|ownerId|\$[0-9]|Seminole/);
    expect(request.headers()['if-match']).toBe('"1"');
    await expect(page.getByText('Unsaved changes')).toHaveCount(0);
    await expect.poll(() => lastAnnouncement(page)).toBe('Cash overview saved.');
  });

  test('a saved change is still there after a reload', async ({ page }) => {
    await openView(page, 5, { edit: true });
    await page.getByRole('button', { name: 'Remove Clean claim rate' }).click();
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByText('Unsaved changes')).toHaveCount(0);

    await page.reload();
    await page.locator('app-grid-canvas li.item').first().waitFor();

    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(1);
  });

  test('a new view needs a name, then saves and opens', async ({ page }) => {
    await openView(page, 'new');
    await expect(page.getByRole('heading', { level: 1, name: 'New view' })).toBeVisible();
    await expect(page.getByText('This view is empty')).toBeVisible();
    await page.getByRole('button', { name: 'Add Net cash collected to view' }).click();

    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.locator('#name-error')).toHaveText('Give the view a name.');
    await page.getByLabel('View name').fill('Board pack');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page).toHaveURL(/\/saved-views\/8$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Board pack' })).toBeVisible();
  });

  test('a name already in use is reported beside the name field', async ({ page }) => {
    await openView(page, 'new');
    await page.getByRole('button', { name: 'Add Net cash collected to view' }).click();
    await page.getByLabel('View name').fill('Cash overview');

    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page.locator('#name-error')).toContainText('You already have a view with this name.');
    await expect(page.getByLabel('View name')).toBeFocused();
  });

  test('when someone else saved first, the choices are safe and nothing is overwritten', async ({ page }) => {
    await openView(page, 5, { edit: true });
    await page.getByRole('button', { name: 'Remove Clean claim rate' }).click();
    await page.route(/saved-views\/5$/, (route) => (route.request().method() === 'PUT' ? route.fulfill({ status: 412, json: { title: 'x' } }) : route.fallback()));

    await page.getByRole('button', { name: 'Save', exact: true }).click();

    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: 'This view changed while you were editing' })).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Save as a copy' })).toBeFocused();
    expect(await axeViolations(page)).toEqual([]);
    await dialog.getByRole('button', { name: 'Keep editing' }).click();
    await expect(page.getByText('Unsaved changes').first()).toBeVisible();
  });

  test('a save that fails keeps the draft and says so calmly, with a reference', async ({ page }) => {
    await openView(page, 5, { edit: true });
    await page.getByRole('button', { name: 'Remove Clean claim rate' }).click();
    await page.route(/saved-views\/5$/, (route) => (route.request().method() === 'PUT' ? route.fulfill({ status: 503, json: { title: 'SECRET', traceId: '77114806214001843f6761e5254e96e3' } }) : route.fallback()));

    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page.getByText('Your changes are still on this page. Try again.')).toBeVisible();
    await expect(page.getByText('Reference: 77114806214001843f6761e5254e96e3')).toBeVisible();
    await expect(page.getByText('SECRET')).toHaveCount(0);
    await expect(page.getByText('Unsaved changes').first()).toBeVisible();
  });

  test('leaving with unsaved changes asks first, and Keep editing stays', async ({ page }) => {
    await openView(page, 5, { edit: true });
    await page.getByRole('button', { name: 'Remove Clean claim rate' }).click();

    await page.getByRole('navigation').getByRole('link', { name: 'Dashboard' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: 'Leave without saving?' })).toBeVisible();
    await dialog.getByRole('button', { name: 'Keep editing' }).click();

    await expect(page).toHaveURL(/\/saved-views\/5$/);
    await page.getByRole('navigation').getByRole('link', { name: 'Dashboard' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Discard changes' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
  });

  test('a view with no changes leaves without asking', async ({ page }) => {
    await openView(page, 5, { edit: true });

    await page.getByRole('navigation').getByRole('link', { name: 'Dashboard' }).click();

    await expect(page).toHaveURL(/\/dashboard$/);
  });

  test('undo and redo bring a removed metric back and take it away again', async ({ page }) => {
    await openView(page, 5, { edit: true });
    await page.getByRole('button', { name: 'Remove Clean claim rate' }).click();
    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(1);

    await page.getByRole('button', { name: /^Undo/ }).click();
    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(2);
    await page.getByRole('button', { name: 'Redo' }).click();
    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(1);
  });

  test('every metric on the page asks again when the Service Line changes, with no other wiring', async ({ page }) => {
    await openView(page, 5);
    const lines: (string | null)[] = [];
    page.on('request', (r) => {
      if (/metrics\/net-collections(\?|$)/.test(r.url())) lines.push(new URL(r.url()).searchParams.get('serviceLine'));
    });

    await page.getByRole('radio', { name: 'Ground only' }).click();
    await expect.poll(() => lines.length).toBeGreaterThan(0);

    expect(lines).toContain('ground');
    await expect.poll(() => lastAnnouncement(page)).toMatch(/^(Updating 2 metrics for .+, ground\.|All 2 metrics updated\. Data through .+\.)$/);
  });

  test('the share dialog sends invitations and says who is waiting', async ({ page }) => {
    await openView(page, 5);

    await page.getByRole('button', { name: 'Share', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: 'Share Cash overview' })).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Send invitation' })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);

    const [request] = await Promise.all([
      page.waitForRequest((r) => r.method() === 'POST' && /invitations$/.test(r.url())),
      (async () => {
        await dialog.getByLabel('Add people').fill('Guest@Example.org');
        await dialog.getByRole('button', { name: 'Send invitation' }).click();
      })(),
    ]);
    expect(request.postDataJSON()).toEqual({ emails: ['guest@example.org'] });
  });

  test('an emailed link is used once, its token leaves the address bar, and the shared view opens', async ({ page }) => {
    await page.goto(`/saved-view-invitations/redeem?token=${'T'.repeat(43)}`);

    await expect(page).toHaveURL(/\/saved-views\/6$/);
    expect(page.url()).not.toContain('token');
    await expect(page.getByRole('heading', { level: 1, name: 'Executive summary' })).toBeVisible();
  });

  test('a link that is wrong, used or expired reads the same, and never reaches the API when it has the wrong shape', async ({ page }) => {
    let calls = 0;
    page.on('request', (r) => {
      if (r.method() === 'POST' && r.url().includes('saved-view-invitations/redeem')) calls += 1;
    });

    await page.goto('/saved-view-invitations/redeem?token=short');
    await expect(page.getByRole('heading', { name: 'This invitation is no longer valid' })).toBeVisible();
    expect(calls).toBe(0);

    await page.goto(`/saved-view-invitations/redeem?token=${'X'.repeat(43)}`);
    await expect(page.getByRole('heading', { name: 'This invitation is no longer valid' })).toBeVisible();
    await expect(page.getByText('Ask the sender for a new one.')).toBeVisible();
    expect(calls).toBe(1);
    expect(page.url()).not.toContain('token');
  });

  test('deleting a view asks first and then it is gone from the list', async ({ page }) => {
    await openView(page, 7);

    await page.getByRole('button', { name: 'More actions' }).click();
    await page.getByRole('menuitem', { name: 'Delete view' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: 'Delete "Old layout"?' })).toBeVisible();
    await dialog.getByRole('button', { name: 'Delete view' }).click();

    await expect(page).toHaveURL(/\/saved-views$/);
    await expect(page.getByRole('link', { name: 'Old layout' })).toHaveCount(0);
  });

  test('two widgets of the same metric get their own names', async ({ page }) => {
    await openView(page, 5, { edit: true });
    await page.getByRole('button', { name: 'Add Net cash collected to view' }).click();

    await expect(page.getByRole('toolbar', { name: 'Net cash collected (1 of 2) actions' })).toBeVisible();
    await expect(page.getByRole('toolbar', { name: 'Net cash collected (2 of 2) actions' })).toBeVisible();
  });

  test('a planned metric is listed as coming soon, can be focused, and cannot be added', async ({ page }) => {
    await openView(page, 5, { edit: true });
    const planned = page.getByRole('button', { name: 'Add AR days to view' });

    await planned.focus();
    await page.keyboard.press('Enter');

    await expect(planned).toHaveAttribute('aria-disabled', 'true');
    await expect.poll(() => lastAnnouncement(page)).toBe("AR days is coming soon and can't be added yet.");
    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(2);
    await expect(page.getByText('What changed')).toHaveCount(0);
  });

  test('on a phone the palette is a dialog of Add buttons and the grid is one column', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await openView(page, 5, { edit: true });

    await expect(page.locator('app-metric-palette')).toHaveCount(0);
    await page.getByRole('button', { name: 'Add metrics' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: 'Add metrics' })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
    await dialog.getByRole('button', { name: 'Add Payer mix to view' }).click();
    await dialog.getByRole('button', { name: 'Done' }).click();

    await expect(page.locator('app-grid-canvas li.item')).toHaveCount(3);
    const columns = await page.locator('#view-canvas').evaluate((el) => getComputedStyle(el).gridTemplateColumns.split(' ').length);
    expect(columns).toBe(1);
    await expect(page.getByRole('button', { name: 'Move Net cash collected up' })).toBeVisible();
    expect((await layoutErrors(page)).errors).toEqual([]);
  });
});
