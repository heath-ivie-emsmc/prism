import { defineConfig, devices } from '@playwright/test';

/**
 * Browser tests, fully automatic: the config starts the web app (`npm start`) and waits for it. If one is already running on the
 * port (VS Code "Run all (API + Web)"), it is reused and left running. Point at another host with PRISM_WEB_URL, which turns the
 * automatic start off.
 *
 * Sign-in: by default `/api/v1/me` is mocked, so the layout tests need no account and no API.
 * Set PRISM_E2E_STORAGE_STATE to a saved Playwright storage state to run against the real API and a real session instead.
 */
export default defineConfig({
  testDir: 'e2e',
  outputDir: 'e2e-report/artifacts',
  fullyParallel: true,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env['PRISM_WEB_URL'] ?? 'http://localhost:4200',
    ...devices['Desktop Chrome'],
    storageState: process.env['PRISM_E2E_STORAGE_STATE'] || undefined,
    // Motion off, so screenshots and measurements are stable.
    reducedMotion: 'reduce',
  },
  webServer: process.env['PRISM_WEB_URL']
    ? undefined
    : {
        command: 'npm start',
        url: 'http://localhost:4200',
        reuseExistingServer: true,
        timeout: 180_000,
      },
  projects: [{ name: 'chromium' }],
});
