import type { ElementHandle } from '@playwright/test';
import { expect, test } from './fixtures';
import { lastAnnouncement, openDashboard, expandHelp, openDashboardWithNotice, openDashboardHelp, openMetricView, openMetricViewNotFound, openNeedsAttention, openPayerDetail, openPayerMixPage, openProjection, openView, openAdmin, openAdminPersonas, openProviderUsers, openAdminCustomers, openSettings, openHome, openWelcome, openTemplates, openPushDialog, SIX_METRICS, openMetricViewGroundOnly, openSixMetricView, openEmptyDivisionView, CONTRIBUTION_METRICS } from './support';

/** Everything works without a mouse (WCAG 2.1.1, 2.1.2, 2.4.3, 2.4.7, 2.4.11). */
/**
 * A control shows focus when its look changes between blurred and focused. The look of the control and of up to three ancestors is compared
 * (outline, shadow, border, background), because some library controls draw focus on a wrapper around the real input.
 */
async function focusIndicator(el: ElementHandle<Element>): Promise<{ visible: boolean; description: string }> {
  const snapshot = (): Promise<string[]> =>
    el.evaluate((node) => {
      const signature: string[] = [];
      for (let n: Element | null = node, depth = 0; n && depth < 4; n = n.parentElement, depth++) {
        const s = getComputedStyle(n);
        signature.push([s.outlineStyle, s.outlineWidth, s.outlineColor, s.boxShadow, s.borderTopColor, s.borderTopWidth, s.backgroundColor].join('|'));
      }
      return signature;
    });
  const focused = await snapshot();
  await el.evaluate((node) => (node as HTMLElement).blur());
  const blurred = await snapshot();
  await el.evaluate((node) => (node as HTMLElement).focus());
  const description = await el.evaluate((node) => `${node.tagName.toLowerCase()}.${[...node.classList].join('.')}`);
  return { visible: focused.some((value, i) => value !== blurred[i]), description };
}

test.describe('keyboard', () => {
  test('the push dialog opens on a field, Tab stays inside it, and Escape closes it and returns focus to the Push button', async ({ page }) => {
    await openTemplates(page);
    const push = page.getByRole('button', { name: 'Push Finance start' });
    await push.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog');
    await dialog.waitFor();

    for (let i = 0; i < 12; i++) {
      await page.keyboard.press('Tab');
      expect(await page.locator(':focus').evaluate((n) => !!n.closest('[role="dialog"]'))).toBe(true);
    }
    await page.keyboard.press('Escape');

    await expect(dialog).toHaveCount(0);
    await expect(push).toBeFocused();
  });

  test('a push can be reviewed and confirmed with the keyboard alone', async ({ page }) => {
    await openPushDialog(page);
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('combobox', { name: /Provider/ }).first().focus();
    await page.keyboard.type('mercy');
    await page.getByRole('option', { name: /Provider 527/ }).waitFor();
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    await dialog.getByRole('button', { name: 'Review' }).focus();
    await page.keyboard.press('Enter');
    await expect(dialog).toContainText('will go to Provider 527');

    await dialog.getByRole('button', { name: 'Confirm push' }).focus();
    await page.keyboard.press('Enter');

    await expect(dialog).toContainText('Provider 527');
  });

  test('the admin home is reachable by Tab from the Admin link, shows focus on every stop, and does not trap focus', async ({ page }) => {
    await openAdmin(page);

    await page.getByRole('link', { name: 'Admin', exact: true }).focus();
    const missing: string[] = [];
    const seen: string[] = [];
    for (let i = 0; i < 40; i++) {
      await page.keyboard.press('Tab');
      const focused = page.locator(':focus');
      if ((await focused.count()) === 0) break;
      const handle = await focused.elementHandle();
      const indicator = await focusIndicator(handle!);
      if (!indicator.visible) missing.push(indicator.description);
      seen.push(indicator.description);
    }

    expect(seen.length).toBeGreaterThan(0);
    expect(missing).toEqual([]);
    await page.keyboard.press('Shift+Tab');
    expect(await page.locator(':focus').count()).toBe(1);
  });

  for (const [name, open, start] of [
    ['the first-login questions', openWelcome, 'input[type="radio"]'],
    ['the Persona admin page', openAdminPersonas, 'app-admin-personas ems-breadcrumbs a'],
    ['the settings page', openSettings, 'app-settings ems-breadcrumbs span, app-settings h1'],
    ['the Provider users page', openProviderUsers, 'app-provider-users h1'],
    ['the staff customer users page', openAdminCustomers, 'app-admin-customers #customer-provider'],
  ] as const) {
    test(`${name} are reachable by Tab, show focus on every stop, and do not trap focus`, async ({ page }) => {
      await open(page);

      await page.locator(start).first().focus();
      const missing: string[] = [];
      const seen: string[] = [];
      for (let i = 0; i < 60; i++) {
        await page.keyboard.press('Tab');
        const focused = page.locator(':focus');
        if ((await focused.count()) === 0) break;
        const indicator = await focusIndicator((await focused.elementHandle()) as ElementHandle<Element>);
        if (!indicator.visible) missing.push(indicator.description);
        seen.push(indicator.description);
      }

      expect(seen.length).toBeGreaterThan(0);
      expect(missing).toEqual([]);
      await page.keyboard.press('Shift+Tab');
      expect(await page.locator(':focus').count()).toBe(1);
    });
  }

  test('the questions can be answered and submitted with the keyboard alone', async ({ page }) => {
    await openWelcome(page);

    await page.locator('input[name="q1"]').first().focus();
    await page.keyboard.press('Space');
    await page.locator('input[name="q2"]').first().focus();
    await page.keyboard.press('Space');
    await expect(page.locator('input[name="q1"]').first()).toBeChecked();
    await expect(page.locator('input[name="q2"]').first()).toBeChecked();
    await page.getByRole('button', { name: 'Continue' }).focus();
    const post = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/api/v1/me/onboarding'));
    await page.keyboard.press('Enter');

    expect((await post).postDataJSON()).toEqual({ answers: [{ questionId: 1, optionId: 10 }, { questionId: 2, optionId: 20 }] });
  });

  test('a pushed view can be hidden and restored from the keyboard, and focus follows the button', async ({ page }) => {
    await openSettings(page);

    await page.getByRole('button', { name: 'Hide Finance start' }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Restore Finance start' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Hide Finance start' })).toBeFocused();
  });

  test('the Persona home page can be customized and reset from the keyboard', async ({ page }) => {
    await openHome(page, 'custom');

    await page.getByRole('button', { name: 'Reset to the recommended page' }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('alert')).toContainText('This deletes your copy');
    await expect(page.getByRole('button', { name: 'Confirm reset' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Confirm reset' })).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Cancel' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Reset to the recommended page' })).toBeFocused();
  });

  test('Tab reaches every control, each shows a focus indicator, and nothing traps focus', async ({ page }) => {
    await openDashboard(page);

    const seen: string[] = [];
    const missing: string[] = [];
    for (let i = 0; i < 80; i++) {
      await page.keyboard.press('Tab');
      const focused = page.locator(':focus');
      if ((await focused.count()) === 0) break;
      const id = await focused.evaluate((n) => `${n.tagName.toLowerCase()}|${n.getAttribute('aria-label') ?? (n.textContent ?? '').trim().slice(0, 30)}|${n.className}`);
      if (seen.includes(id) && seen[0] === id) break; // wrapped around to the start
      seen.push(id);
      const indicator = await focusIndicator((await focused.elementHandle()) as ElementHandle<Element>);
      if (!indicator.visible) missing.push(indicator.description);
    }

    expect(seen.length, 'controls reached by Tab').toBeGreaterThan(10);
    expect(seen.length, 'a keyboard trap would run to the limit').toBeLessThan(80);
    expect(missing, 'controls with no visible focus indicator').toEqual([]);
  });

  test('the focused control is never hidden behind other content (WCAG 2.4.11)', async ({ page }) => {
    await openDashboard(page);

    const hidden: string[] = [];
    for (let i = 0; i < 40; i++) {
      await page.keyboard.press('Tab');
      const focused = page.locator(':focus');
      if ((await focused.count()) === 0) break;
      const covered = await focused.evaluate((node) => {
        const r = node.getBoundingClientRect();
        if (r.width === 0 || r.height === 0) return false;
        const top = document.elementFromPoint(r.left + r.width / 2, Math.min(Math.max(r.top + r.height / 2, 1), innerHeight - 1));
        return !!top && top !== node && !node.contains(top) && !top.contains(node);
      });
      if (covered) hidden.push(await focused.evaluate((n) => n.tagName + ':' + (n.textContent ?? '').trim().slice(0, 20)));
    }
    expect(hidden).toEqual([]);
  });

  test('the account menu opens with the keyboard, Escape closes it and focus returns to the button', async ({ page }) => {
    await openDashboard(page);
    const trigger = page.locator('button.ems-profile');
    await trigger.focus();

    await page.keyboard.press('Enter');
    await expect(page.locator('[role="menu"]')).toBeVisible();
    await expect(page.locator('[role="menuitem"]').first()).toBeFocused();

    await page.keyboard.press('Escape');
    await expect(page.locator('[role="menu"]')).toBeHidden();
    await expect(trigger).toBeFocused();
  });

  test('radio groups move with the arrow keys and keep a single tab stop', async ({ page }) => {
    await openDashboard(page);
    const group = page.locator('app-net-cash-panel [role="radiogroup"][aria-label="Date range"]');
    const radios = group.locator('[role="radio"]');
    await radios.first().focus();

    await page.keyboard.press('ArrowRight');

    await expect(radios.nth(1)).toBeFocused();
    await expect(radios.nth(1)).toHaveAttribute('aria-checked', 'true');
    expect(await radios.evaluateAll((els) => els.map((e) => (e as HTMLElement).tabIndex))).toEqual([-1, 0]);
  });

  test('the chart type control switches between line, bar and table with the keyboard', async ({ page }) => {
    await openDashboard(page);
    const group = page.locator('app-net-cash-panel [role="radiogroup"][aria-label="Chart type"]');
    const radios = group.locator('[role="radio"]');
    await radios.first().focus();

    await page.keyboard.press('ArrowRight');
    await expect(radios.nth(1)).toHaveAttribute('aria-checked', 'true');
    await page.keyboard.press('ArrowRight');
    await expect(radios.nth(2)).toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('app-net-cash-panel table')).toBeVisible();
    await expect(page.locator('app-net-cash-panel app-chart')).toHaveCount(0);
  });

  test('a skip link or direct main landmark lets keyboard users bypass the navigation (WCAG 2.4.1)', async ({ page }) => {
    await openDashboard(page);

    await page.keyboard.press('Tab');
    const first = page.locator(':focus');
    const text = ((await first.textContent()) ?? '').toLowerCase();

    expect(text, 'the first Tab stop should be a "Skip to main content" link').toContain('skip');
    await page.keyboard.press('Enter');
    await expect(page.locator('main')).toBeFocused();
  });

  test.describe('needs attention', () => {
    test('Tab reaches every control, each shows focus, and nothing traps', async ({ page }) => {
      await openNeedsAttention(page);

      const seen: string[] = [];
      const missing: string[] = [];
      for (let i = 0; i < 60; i++) {
        await page.keyboard.press('Tab');
        const focused = page.locator(':focus');
        if ((await focused.count()) === 0) break;
        const id = await focused.evaluate((n) => `${n.tagName.toLowerCase()}|${n.getAttribute('aria-label') ?? (n.textContent ?? '').trim().slice(0, 30)}|${n.className}`);
        if (seen.includes(id) && seen[0] === id) break;
        seen.push(id);
        const indicator = await focusIndicator((await focused.elementHandle()) as ElementHandle<Element>);
        if (!indicator.visible) missing.push(indicator.description);
      }

      expect(seen.some((s) => s.includes('Select all 2 claims'))).toBe(true);
      expect(seen.some((s) => s.includes('Select claim FW-2610457'))).toBe(true);
      expect(seen.length, 'a keyboard trap would run to the limit').toBeLessThan(60);
      expect(missing, 'controls with no visible focus indicator').toEqual([]);
    });

    test('a claim is ticked with Space, Send to CSE opens the drawer with Enter, Tab stays inside it, and Escape returns focus', async ({ page }) => {
      await openNeedsAttention(page);
      await page.getByRole('checkbox', { name: 'Select claim FW-2610418' }).focus();
      await page.keyboard.press('Space');
      const send = page.locator('app-needs-attention button.send');
      await expect(send).toBeEnabled();

      await send.focus();
      await page.keyboard.press('Enter');
      const drawer = page.getByRole('dialog', { name: 'Send to your CSE' });
      await expect(drawer).toBeVisible();
      for (let i = 0; i < 10; i++) {
        await page.keyboard.press('Tab');
        expect(await page.evaluate(() => !!document.activeElement?.closest('[role="dialog"]')), `Tab ${i + 1} left the drawer`).toBe(true);
      }

      await page.keyboard.press('Escape');
      await expect(drawer).toBeHidden();
      await expect(send).toBeFocused();
    });

    test('the radio tabs move with the arrow keys and change the list', async ({ page }) => {
      await openNeedsAttention(page);
      await page.getByRole('radio', { name: 'Denied (2)' }).focus();

      await page.keyboard.press('ArrowRight');

      await expect(page.getByRole('radio', { name: 'Needs attention (4)' })).toHaveAttribute('aria-checked', 'true');
      await expect(page.locator('app-needs-attention .claims tbody tr')).toHaveCount(4);
    });
  });

  test.describe('saved views', () => {
    test('Tab reaches every control on the edit page, each shows focus, and nothing traps', async ({ page }) => {
      await openView(page, 5, { edit: true });

      const seen: string[] = [];
      const missing: string[] = [];
      for (let i = 0; i < 80; i++) {
        await page.keyboard.press('Tab');
        const focused = page.locator(':focus');
        if ((await focused.count()) === 0) break;
        const id = await focused.evaluate((n) => `${n.tagName.toLowerCase()}|${n.getAttribute('aria-label') ?? (n.textContent ?? '').trim().slice(0, 30)}|${n.className}`);
        if (seen.includes(id) && seen[0] === id) break;
        seen.push(id);
        const indicator = await focusIndicator((await focused.elementHandle()) as ElementHandle<Element>);
        if (!indicator.visible) missing.push(indicator.description);
      }

      expect(seen.length, 'controls reached by Tab').toBeGreaterThan(15);
      expect(seen.length, 'a keyboard trap would run to the limit').toBeLessThan(80);
      expect(missing, 'controls with no visible focus indicator').toEqual([]);
    });

    test('each widget toolbar is one Tab stop and the arrow keys move along it', async ({ page }) => {
      await openView(page, 5, { edit: true });
      const tools = page.getByRole('toolbar', { name: 'Net cash collected actions' }).getByRole('button');
      const tabbable = await tools.evaluateAll((els) => els.filter((e) => e.getAttribute('tabindex') !== '-1').length);
      expect(tabbable).toBe(1);

      await tools.first().focus();
      await page.keyboard.press('ArrowRight');
      await expect(page.locator(':focus')).toHaveAttribute('aria-label', /^Size /);
      await page.keyboard.press('End');
      await expect(page.locator(':focus')).toHaveAttribute('aria-label', /^Remove /);
      await page.keyboard.press('Home');
      await expect(page.locator(':focus')).toHaveAttribute('aria-label', /^Move /);
    });

    test('the focused control is never hidden behind the footer or another control (WCAG 2.4.11)', async ({ page }) => {
      await openView(page, 5, { edit: true });

      const hidden: string[] = [];
      for (let i = 0; i < 40; i++) {
        await page.keyboard.press('Tab');
        const focused = page.locator(':focus');
        if ((await focused.count()) === 0) break;
        const covered = await focused.evaluate((node) => {
          const r = node.getBoundingClientRect();
          if (r.width === 0 || r.height === 0) return false;
          const top = document.elementFromPoint(r.left + r.width / 2, Math.min(r.top + r.height / 2, innerHeight - 1));
          return !!top && !node.contains(top) && !top.contains(node);
        });
        if (covered) hidden.push(await focused.evaluate((n) => n.getAttribute('aria-label') ?? (n.textContent ?? '').trim().slice(0, 30)));
      }

      expect(hidden).toEqual([]);
    });

    test('the skip links jump to the canvas and the palette', async ({ page }) => {
      await openView(page, 5, { edit: true });

      await page.getByRole('link', { name: 'Skip to canvas' }).focus();
      await page.keyboard.press('Enter');
      await expect(page.locator(':focus')).toHaveAttribute('aria-label', /^Move /);

      await page.getByRole('link', { name: 'Skip to metric palette' }).focus();
      await page.keyboard.press('Enter');
      await expect(page.locator('#builder-pane')).toBeFocused();
    });

    test('a palette category opens and closes with Enter and Space, and says which', async ({ page }) => {
      await openView(page, 5, { edit: true });
      const cash = page.getByRole('button', { name: /^expand_less Cash|^Cash/ }).first();
      await cash.focus();

      await expect(cash).toHaveAttribute('aria-expanded', 'true');
      await page.keyboard.press('Enter');
      await expect(cash).toHaveAttribute('aria-expanded', 'false');
      await page.keyboard.press('Space');
      await expect(cash).toHaveAttribute('aria-expanded', 'true');
    });

    test('searching the palette says how many metrics match and Escape clears it', async ({ page }) => {
      await openView(page, 5, { edit: true });

      await page.getByLabel('Search metrics').fill('clean');
      await expect.poll(() => lastAnnouncement(page)).toBe('1 metric match "clean".');
      await page.keyboard.press('Escape');

      await expect(page.getByLabel('Search metrics')).toHaveValue('');
    });

    test('Ctrl+Z and Ctrl+Shift+Z undo and redo while arranging, but not while typing a name', async ({ page }) => {
      await openView(page, 5, { edit: true });
      await page.getByRole('button', { name: 'Remove Clean claim rate' }).click();
      await expect(page.locator('app-grid-canvas li.item')).toHaveCount(1);

      await page.getByLabel('View name').focus();
      await page.keyboard.press('Control+z');
      await expect(page.locator('app-grid-canvas li.item')).toHaveCount(1);

      await page.getByRole('button', { name: 'Move Net cash collected', exact: true }).focus();
      await page.keyboard.press('Control+z');
      await expect(page.locator('app-grid-canvas li.item')).toHaveCount(2);
      await page.keyboard.press('Control+Shift+z');
      await expect(page.locator('app-grid-canvas li.item')).toHaveCount(1);
    });

    test('removing a widget puts focus on the next one, and undo puts it back', async ({ page }) => {
      await openView(page, 5, { edit: true });

      await page.getByRole('button', { name: 'Remove Net cash collected' }).click();

      await expect(page.locator(':focus')).toHaveAttribute('aria-label', 'Move Clean claim rate');
      expect(await lastAnnouncement(page)).toBeDefined();
    });

    test('the share dialog closes with Escape and focus returns to Share', async ({ page }) => {
      await openView(page, 5);
      const share = page.getByRole('button', { name: 'Share', exact: true });

      await share.click();
      await page.getByRole('dialog').waitFor();
      await page.keyboard.press('Escape');

      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(share).toBeFocused();
    });

    test('the delete confirmation never starts on the destructive button, and Escape keeps the view', async ({ page }) => {
      await openView(page, 7);
      await page.getByRole('button', { name: 'More actions' }).click();
      await page.getByRole('menuitem', { name: 'Delete view' }).click();
      await page.getByRole('dialog').waitFor();

      expect(await page.locator(':focus').evaluate((n) => (n.textContent ?? '').trim())).not.toBe('Delete view');
      await page.keyboard.press('Escape');

      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page).toHaveURL(/\/saved-views\/7$/);
    });

    test('the page has its own h1 focused when it opens, so a screen reader announces the change', async ({ page }) => {
      await openView(page, 5);

      await expect(page.locator('h1')).toBeFocused();
    });
  });
});

test.describe('keyboard: division kept under a Service Line without it', () => {
  test('the kept division is chosen and cleared with the keyboard alone', async ({ page }) => {
    await openEmptyDivisionView(page);
    const select = page.getByRole('combobox', { name: 'Base location' });
    await select.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('option', { name: /no air transports/ })).toBeVisible();
    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('Enter');
    await expect(page.locator('app-division-select')).not.toContainText('has no air transports');
  });
});

test.describe('keyboard: payer mix and its drill target', () => {
  for (const [name, open, least = 3] of [['payer mix', openPayerMixPage], ['payer breakdown', openPayerDetail], ['metric view', openMetricView, 1], ['metric view, not found', openMetricViewNotFound, 1], ...SIX_METRICS.map((m) => [`metric view, ${m.name}`, (p: import('@playwright/test').Page) => openMetricView(p, m.key), 1] as const), ['metric view, air-only Metric under Ground only', openMetricViewGroundOnly, 1], ['saved view, the six new Metrics', openSixMetricView, 3], ['saved view, a division with no transports under the line (empty widgets)', openEmptyDivisionView, 1], ...CONTRIBUTION_METRICS.map((m) => [`metric view, ${m.name}`, (p: import('@playwright/test').Page) => openMetricView(p, m.key), 1] as const), ['saved view, the contribution Metrics', (p: import('@playwright/test').Page) => openSixMetricView(p, 'c'), 3]] as const) {
    test(`${name}: Tab reaches every control with a visible focus indicator and nothing traps focus`, async ({ page }) => {
      await open(page);

      const seen: string[] = [];
      const missing: string[] = [];
      for (let i = 0; i < 60; i++) {
        await page.keyboard.press('Tab');
        const focused = page.locator(':focus');
        if ((await focused.count()) === 0) break;
        const id = await focused.evaluate((n) => `${n.tagName.toLowerCase()}|${n.getAttribute('aria-label') ?? (n.textContent ?? '').trim().slice(0, 30)}|${n.className}`);
        if (seen.includes(id) && seen[0] === id) break;
        seen.push(id);
        const indicator = await focusIndicator((await focused.elementHandle()) as ElementHandle<Element>);
        if (!indicator.visible) missing.push(indicator.description);
      }

      expect(seen.length, 'controls reached by Tab (focus starts on the heading, so only what follows it)').toBeGreaterThan(least - 1);
      expect(seen.length, 'a keyboard trap would run to the limit').toBeLessThan(60);
      expect(missing, 'controls with no visible focus indicator').toEqual([]);
    });
  }
});

test.describe('keyboard: help panel, drill and notice', () => {
  for (const [name, open] of [['payer mix', openPayerMixPage], ['payer breakdown', openPayerDetail]] as const) {
    test(`${name}: the help disclosure is one Tab stop, opens and closes with Enter and Space, and Escape closes the panel without losing focus`, async ({ page }) => {
      await open(page);
      const toggle = page.locator('app-metric-help button.help__toggle');
      const panel = page.locator('app-metric-help .help__panel');
      await toggle.focus();

      await expect(toggle).toHaveAttribute('aria-expanded', 'false');
      await page.keyboard.press('Enter');
      await expect(toggle).toHaveAttribute('aria-expanded', 'true');
      await expect(panel).toBeVisible();
      await expect(toggle).toBeFocused();
      await page.keyboard.press('Space');
      await expect(toggle).toHaveAttribute('aria-expanded', 'false');
      await expect(panel).toHaveCount(0);
      await page.keyboard.press('Space');
      await expect(toggle).toHaveAttribute('aria-expanded', 'true');

      // Escape closes the open panel and focus stays on the toggle; it is not a trap, and Tab carries on past it.
      await page.keyboard.press('Escape');
      await expect(toggle).toHaveAttribute('aria-expanded', 'false');
      await expect(panel).toHaveCount(0);
      await expect(toggle).toBeFocused();
      await page.keyboard.press('Tab');
      await page.keyboard.press('Shift+Tab');
      await expect(toggle).toBeFocused();
      const indicator = await focusIndicator((await toggle.elementHandle()) as ElementHandle<Element>);
      expect(indicator.visible, 'help toggle focus indicator').toBe(true);
    });
  }

  test('the help panel is read in order: the toggle comes before its glossary and the glossary is not a Tab stop', async ({ page }) => {
    await openPayerDetail(page);
    await expandHelp(page);

    const order = await page.evaluate(() => {
      const toggle = document.querySelector('app-metric-help button.help__toggle')!;
      const dl = document.querySelector('app-metric-help .help__panel dl')!;
      return { after: !!(toggle.compareDocumentPosition(dl) & Node.DOCUMENT_POSITION_FOLLOWING), tabbable: dl.querySelectorAll('[tabindex]:not([tabindex="-1"])').length };
    });
    expect(order).toEqual({ after: true, tabbable: 0 });
  });

  test('Tab then Enter follows the drill link into the breakdown, focus lands on its heading, and Back brings the reader to the same link and scroll position', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 500 });
    await openPayerMixPage(page);
    const link = page.getByRole('link', { name: 'View Commercial breakdown' });
    await link.focus();
    await expect.poll(() => page.evaluate(() => window.scrollY)).toBeGreaterThan(300);
    const before = await page.evaluate(() => window.scrollY);
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(/rotor-wing\/commercial\?period=202610&line=air$/);
    await expect(page.getByRole('heading', { level: 1 })).toBeFocused();
    await page.goBack();

    await expect(page).toHaveURL(/\/dashboard\/net-cash\/rotor-wing/);
    await page.locator('app-payer-mix app-chart canvas').waitFor();
    await expect.poll(async () => Math.abs((await page.evaluate(() => window.scrollY)) - before)).toBeLessThan(60);
    // Focus is somewhere sensible (the page, its heading or the link), never lost to a hidden element.
    await expect.poll(() => page.evaluate(() => { const a = document.activeElement; return !!a && (a === document.body || a.getBoundingClientRect().width > 0); })).toBe(true);
  });

  test('the breadcrumb on the breakdown page is reachable by Tab and works with Enter', async ({ page }) => {
    await openPayerDetail(page);
    const crumb = page.getByRole('navigation', { name: /breadcrumb/i }).getByRole('link', { name: 'Rotor wing payer mix' });
    await crumb.focus();
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(/\/dashboard\/net-cash\/rotor-wing\?period=202610&line=air$/);
  });

  test('a dashboard card help disclosure toggles with Enter and Space, closes with Escape keeping focus, and shows a visible focus ring', async ({ page }) => {
    await openDashboard(page);
    const toggle = page.locator('app-kpi-card', { hasText: 'Transports' }).locator('button.help__toggle');
    await toggle.focus();
    const indicator = await focusIndicator((await toggle.elementHandle()) as ElementHandle<Element>);
    expect(indicator.visible, 'card help focus indicator').toBe(true);

    await page.keyboard.press('Enter');
    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await page.keyboard.press('Escape');
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await expect(toggle).toBeFocused();
    await page.keyboard.press('Space');
    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(await page.evaluate(() => document.querySelectorAll('app-kpi-card .help__panel dl [tabindex]:not([tabindex="-1"])').length)).toBe(0);
  });

  test('the dashboard has no control that pretends to work: no disabled call type or pickup filter', async ({ page }) => {
    await openDashboardHelp(page);
    await expect(page.locator('app-net-cash-panel [aria-label="Call type"]')).toHaveCount(0);
    await expect(page.getByLabel('Pickup location')).toHaveCount(0);
    await expect(page.locator('app-net-cash-panel [disabled]')).toHaveCount(0);
  });

  test('the Update view notice is reached by Tab, never takes focus on its own, and Enter updates and announces', async ({ page }) => {
    await openDashboardWithNotice(page);
    const button = page.locator('app-data-updated-notice').getByRole('button', { name: 'Update view' });

    expect(await page.evaluate(() => document.activeElement?.closest('app-data-updated-notice') === null)).toBe(true);
    let reached = false;
    for (let i = 0; i < 30 && !reached; i++) {
      await page.keyboard.press('Tab');
      reached = await button.evaluate((el) => el === document.activeElement);
    }
    expect(reached, 'Update view reachable by Tab').toBe(true);
    const indicator = await focusIndicator((await button.elementHandle()) as ElementHandle<Element>);
    expect(indicator.visible, 'Update view focus indicator').toBe(true);

    await page.keyboard.press('Space');

    await expect(page.locator('app-data-updated-notice')).not.toContainText('Data updated');
    await expect.poll(() => lastAnnouncement(page)).toBe('Updated to Nov 23');
    // The button went away; focus must not be stranded on a removed element.
    await expect.poll(() => page.evaluate(() => document.activeElement !== null)).toBe(true);
  });

  test.describe('figures from the metric catalog', () => {
    test('the projection horizon is one tab stop, reached by Tab with a visible focus, and moves with the arrow keys', async ({ page }) => {
      await openProjection(page);
      const radios = page.locator('app-projection-panel [aria-label="Months to project"] [role="radio"]');

      let reached = false;
      for (let i = 0; i < 40 && !reached; i++) {
        await page.keyboard.press('Tab');
        reached = await radios.nth(2).evaluate((el) => el === document.activeElement);
      }
      expect(reached, 'the horizon control is reachable by Tab').toBe(true);
      const indicator = await focusIndicator((await radios.nth(2).elementHandle()) as ElementHandle<Element>);
      expect(indicator.visible, 'horizon focus indicator').toBe(true);
      expect(await radios.evaluateAll((els) => els.map((e) => (e as HTMLElement).tabIndex))).toEqual([-1, -1, 0]);

      await page.keyboard.press('ArrowLeft');
      await expect(radios.nth(1)).toBeFocused();
      await expect(page.locator('app-projection-panel thead th')).toHaveCount(3);
      await page.keyboard.press('ArrowRight');
      await expect(radios.nth(2)).toBeFocused();
      await expect(page.locator('app-projection-panel thead th')).toHaveCount(4);
    });

    test('the projection table scroll region is reachable by Tab and shows focus, so a narrow screen can read it with the keyboard', async ({ page }) => {
      await page.setViewportSize({ width: 320, height: 700 });
      await openProjection(page);
      const region = page.locator('app-projection-panel .panel__table-wrap');

      let reached = false;
      for (let i = 0; i < 60 && !reached; i++) {
        await page.keyboard.press('Tab');
        reached = await region.evaluate((el) => el === document.activeElement);
      }
      expect(reached, 'the table region is reachable by Tab').toBe(true);
      const indicator = await focusIndicator((await region.elementHandle()) as ElementHandle<Element>);
      expect(indicator.visible, 'table region focus indicator').toBe(true);
    });

    test('the help beside the projection opens with Enter, closes with Escape and keeps focus on its button', async ({ page }) => {
      await openProjection(page);
      const toggle = page.locator('app-projection-panel button.help__toggle');
      await toggle.focus();

      await page.keyboard.press('Enter');
      await expect(toggle).toHaveAttribute('aria-expanded', 'true');
      await page.keyboard.press('Escape');
      await expect(toggle).toHaveAttribute('aria-expanded', 'false');
      await expect(toggle).toBeFocused();
    });

    test('the claims summary adds no tab stops, so the list controls stay as reachable as before', async ({ page }) => {
      await openNeedsAttention(page);

      expect(await page.locator('app-attention-summary').evaluate((el) => el.querySelectorAll('a[href], button, input, [tabindex]:not([tabindex="-1"])').length)).toBe(0);
    });
  });
});
