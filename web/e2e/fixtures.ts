import { test as base, expect } from '@playwright/test';

/** What `GET /api/v1/me` returns for a signed-in user with one Provider. Used only when no real session is supplied. */
const MOCK_ME = {
  userName: 'jdoe',
  displayName: 'Jane Doe',
  providers: [{ id: '526', name: 'Seminole County Fire Department', facilityCount: 3 }],
  permissions: ['dashboard.view'],
};

export const test = base.extend({
  page: async ({ page }, use) => {
    if (!process.env['PRISM_E2E_STORAGE_STATE']) {
      await page.route('**/api/v1/me', (route) => route.fulfill({ json: MOCK_ME }));
    }
    await use(page);
  },
});

export { expect };
