import type { ElementHandle } from '@playwright/test';
import { expect, test } from './fixtures';
import { openDashboard } from './support';

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
});
