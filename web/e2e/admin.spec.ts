import { expect, test } from './fixtures';
import {
  INTERNAL_ME,
  openAdmin,
  openPushDialog,
  openTemplateEditor,
  openTemplates,
  pickProvider,
  signInAsInternal,
} from './support';

/** The admin area is for Internal Users only. The SPA hides it from everyone else; the API refuses them on its own. */
test.describe('admin area', () => {
  test('an internal user sees the Admin link and the tiers they hold, and no others', async ({
    page,
  }) => {
    await openAdmin(page, ['prism.admin.templates.publish']);

    await expect(page.getByRole('link', { name: 'Admin', exact: true })).toBeVisible();
    await expect(page.locator('app-admin-home li strong')).toHaveText(['Template publisher']);
  });

  test('an internal user with no tier is told so', async ({ page }) => {
    await openAdmin(page, []);

    await expect(page.locator('app-admin-home h2')).toHaveText('No admin tools yet');
  });

  test('a customer user has no Admin link and is sent to the dashboard from /admin', async ({
    page,
  }) => {
    await page.goto('/admin');

    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByRole('link', { name: 'Admin', exact: true })).toHaveCount(0);
  });

  test('a response that says internal but names no tier shows no tool', async ({ page }) => {
    await page.route('**/api/v1/me', (route) => route.fulfill({ json: { ...INTERNAL_ME } }));
    await page.goto('/admin');

    await expect(page.locator('app-admin-home .admin__list li')).toHaveCount(0);
  });

  test('the templates list shows Create to an author and Push to a publisher, and hides each from the other', async ({
    page,
  }) => {
    await openTemplates(page, ['prism.admin.templates.author']);
    await expect(page.getByRole('link', { name: 'Create a template' })).toBeVisible();
    await expect(page.getByRole('button', { name: /^Push / })).toHaveCount(0);
  });

  test('a publisher sees Push on a published template and Publish on a draft, and publishing a draft turns it into Push', async ({
    page,
  }) => {
    await openTemplates(page, ['prism.admin.templates.publish']);
    await expect(page.getByRole('link', { name: 'Create a template' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Push Finance start' })).toBeVisible();

    await page.getByRole('button', { name: 'Publish Chief start' }).click();

    await expect(page.getByRole('button', { name: 'Push Chief start' })).toBeVisible();
  });

  test('a customer user is sent away from the templates pages', async ({ page }) => {
    await page.goto('/admin/templates');
    await expect(page).toHaveURL(/\/dashboard$/);
    await page.goto('/admin/templates/1');
    await expect(page).toHaveURL(/\/dashboard$/);
  });

  test('the editor asks for a Provider, which is found by name, and shows nothing until one is picked', async ({
    page,
  }) => {
    await signInAsInternal(page);
    await page.goto('/admin/templates/new');
    await expect(page.locator('app-view-builder')).toHaveCount(0);

    await pickProvider(page, 'abbev', 'Abbeville County Emergency Services');

    await expect(page.locator('app-template-editor')).toContainText(
      'Abbeville County Emergency Services (Provider 9001)',
    );
  });

  test('an author lays out a template with figures from the preview Provider and saves it without a Provider in the request', async ({
    page,
  }) => {
    await openTemplateEditor(page, 1, '526');
    await expect(
      page.getByText('Previewing Seminole County Fire Department (Provider 526)'),
    ).toBeVisible();
    await page.getByRole('button', { name: 'Edit', exact: true }).click();
    const save = page.waitForRequest(
      (r) => r.method() === 'PUT' && /\/admin\/view-templates\/1$/.test(r.url()),
    );

    await page.getByRole('button', { name: 'Save', exact: true }).click();

    const request = await save;
    expect(request.url()).not.toContain('526');
    expect(JSON.stringify(request.postDataJSON())).not.toContain('526');
    expect(request.headers()['if-match']).toBe('"1"');
  });

  test('pushing is two steps: review shows what will happen, confirm sends the reviewed widget count', async ({
    page,
  }) => {
    await openPushDialog(page);
    const dialog = page.getByRole('dialog');
    await pickProvider(dialog, 'mercy', 'Provider 527');
    await dialog.getByRole('button', { name: 'Review' }).click();
    await expect(dialog).toContainText('will go to Provider 527');
    const push = page.waitForRequest(
      (r) => r.method() === 'POST' && /\/view-templates\/1\/assignments$/.test(r.url()),
    );

    await dialog.getByRole('button', { name: 'Confirm push' }).click();

    expect((await push).postDataJSON()).toEqual({
      scope: { kind: 'Provider', providerId: 527 },
      confirmedWidgetCount: 2,
    });
    await expect(dialog).toContainText('Provider 527');
  });

  test('stopping a push keeps it in the list as stopped', async ({ page }) => {
    await openPushDialog(page);
    const dialog = page.getByRole('dialog');

    await dialog.getByRole('button', { name: 'Stop the push to Provider 526' }).click();

    await expect(dialog.getByRole('button', { name: 'Stop the push to Provider 526' })).toHaveCount(
      0,
    );
    await expect(dialog).toContainText('Stopped');
  });

  test('a customer-facing page is unaffected: the preview Provider is cleared when the editor is left', async ({
    page,
  }) => {
    await openTemplateEditor(page, 1, '526');
    await page.getByRole('link', { name: 'Dashboard', exact: true }).first().click();
    await expect(page).toHaveURL(/\/dashboard$/);

    await expect(page.getByText('Previewing Provider')).toHaveCount(0);
  });

  test('on the template editor the menu starts closed, floats over the page when opened, and closes on a click away or Escape', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openTemplateEditor(page, 1, '526');
    const toggle = page.getByRole('button', { name: 'Admin menu' });
    const menu = page.getByRole('navigation', { name: 'Admin' });
    await expect(menu).toHaveCount(0);
    const before = await page.locator('app-view-builder').boundingBox();

    await toggle.click();

    await expect(menu).toBeVisible();
    expect((await page.locator('app-view-builder').boundingBox())?.x).toBe(before?.x);

    await page.mouse.click(1240, 500);
    await expect(menu).toHaveCount(0);

    await toggle.click();
    await expect(menu).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(menu).toHaveCount(0);
  });

  test('on the other admin pages the menu sits beside the page and can be hidden', async ({
    page,
  }) => {
    await openAdmin(page);
    const menu = page.getByRole('navigation', { name: 'Admin' });
    await expect(menu).toBeVisible();

    await page.getByRole('button', { name: 'Hide menu' }).click();

    await expect(menu).toHaveCount(0);
  });
});
