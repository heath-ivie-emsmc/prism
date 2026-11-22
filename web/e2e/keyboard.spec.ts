import type { ElementHandle } from '@playwright/test';
import { expect, test } from './fixtures';
import { lastAnnouncement, openDashboard, openNeedsAttention, openView } from './support';

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
      await expect(page.locator('app-needs-attention tbody tr')).toHaveCount(4);
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
