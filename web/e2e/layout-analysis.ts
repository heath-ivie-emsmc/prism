/**
 * Layout checks that run INSIDE the page (self-contained: no imports, no closures), because they need real layout.
 * Every issue names the elements involved so a failing run says what to fix, not just that something is wrong.
 */
export type IssueKind = 'horizontal-overflow' | 'text-overlap' | 'text-clipped' | 'text-escapes-container' | 'small-target';

export interface LayoutIssue {
  readonly kind: IssueKind;
  /** error fails the test; warning is reported only. */
  readonly severity: 'error' | 'warning';
  readonly message: string;
  readonly selectors: string[];
}

export function analyzeLayout(): LayoutIssue[] {
  const issues: LayoutIssue[] = [];
  const seen = new Set<string>();
  const add = (issue: LayoutIssue): void => {
    const key = `${issue.kind}|${issue.selectors.join('|')}`;
    if (seen.has(key) || issues.length >= 200) return;
    seen.add(key);
    issues.push(issue);
  };

  const describe = (el: Element): string => {
    const parts: string[] = [];
    let node: Element | null = el;
    while (node && node !== document.body && parts.length < 4) {
      const cls = [...node.classList].slice(0, 2).map((c) => `.${c}`).join('');
      parts.unshift(`${node.tagName.toLowerCase()}${node.id ? `#${node.id}` : ''}${cls}`);
      node = node.parentElement;
    }
    return parts.join(' > ');
  };
  const sample = (text: string): string => (text.length > 40 ? `${text.slice(0, 37)}...` : text);

  // 1. Page wider than the window.
  const viewportWidth = document.documentElement.clientWidth;
  if (document.documentElement.scrollWidth > viewportWidth + 1) {
    const wide = [...document.body.querySelectorAll('*')]
      .filter((el) => el.getBoundingClientRect().right > viewportWidth + 1 && getComputedStyle(el).position !== 'fixed')
      .slice(0, 5);
    add({
      kind: 'horizontal-overflow',
      severity: 'error',
      message: `Page scrolls sideways: content is ${document.documentElement.scrollWidth}px wide in a ${viewportWidth}px window.`,
      selectors: wide.map(describe),
    });
  }

  // 2. Visible text, one rectangle per line of each text node.
  interface TextBox {
    el: Element;
    text: string;
    rect: DOMRect;
  }
  const boxes: TextBox[] = [];
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  const range = document.createRange();
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = (node.textContent ?? '').trim();
    const el = node.parentElement;
    if (!text || !el || ['SCRIPT', 'STYLE', 'NOSCRIPT'].includes(el.tagName)) continue;
    const style = getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0) continue;
    // Closed <details> content and anything collapsed to a clip rectangle is not on screen (.ems-select__live is the library's 1px clipped live region, whose single long line is not drawn).
    if (el.closest('details:not([open])') && !el.closest('summary')) continue;
    if (el.closest('.cdk-visually-hidden, .ems-visually-hidden, .panel__sr, .ems-select__live')) continue;
    // Icon fonts: the text node is a ligature name ("light_mode") that draws as one glyph, so its text box is not what is on screen.
    if (el.closest('.material-symbols-rounded, [class*="material-symbols"], ems-icon')) continue;
    range.selectNodeContents(node);
    // A text rectangle is the font's full box, which is taller than the line box when line-height is tight.
    // Shrink it to the line box, centered, so only what is really drawn on top of other text counts.
    const lineHeight = parseFloat(style.lineHeight);
    for (const raw of range.getClientRects()) {
      if (raw.width <= 1 || raw.height <= 1) continue;
      const height = Number.isFinite(lineHeight) ? Math.min(raw.height, lineHeight) : raw.height;
      const top = raw.top + (raw.height - height) / 2;
      // Wholly off screen to the top or left: a skip link waiting for focus, not content.
      if (raw.bottom <= 0 || raw.right <= 0) continue;
      // Material's outline draws a second copy of the label inside the notch only to measure the gap it cuts in the border: the one that is read is drawn elsewhere in the field.
      if (el.closest('.mdc-notched-outline__notch')) continue;
      // A floated select label sits on the field border by design.
      if (el.closest('.ems-select__anchor--floated') && el.matches('label')) continue;
      boxes.push({ el, text, rect: new DOMRect(raw.left, top, raw.width, height) });
    }
  }

  // A sticky or fixed bar (the page footer) is opaque and slides over content as the page scrolls, which is its job. Content is still
  // reachable: the bar sits after it in the flow, so at the end of the page nothing is under it.
  const floating = (el: Element): boolean => {
    for (let node: Element | null = el; node && node !== document.body; node = node.parentElement) {
      const position = getComputedStyle(node).position;
      if (position === 'sticky' || position === 'fixed') return true;
    }
    return false;
  };

  // 3. Two pieces of text drawn on top of each other. Skip text inside the same element, and ancestors (a label wrapping its own child).
  for (let i = 0; i < boxes.length; i++) {
    for (let j = i + 1; j < boxes.length; j++) {
      const a = boxes[i]!;
      const b = boxes[j]!;
      if (a.el === b.el || a.el.contains(b.el) || b.el.contains(a.el)) continue;
      if (floating(a.el) || floating(b.el)) continue;
      const width = Math.min(a.rect.right, b.rect.right) - Math.max(a.rect.left, b.rect.left);
      const height = Math.min(a.rect.bottom, b.rect.bottom) - Math.max(a.rect.top, b.rect.top);
      if (width <= 1 || height <= 1) continue;
      const smaller = Math.min(a.rect.width * a.rect.height, b.rect.width * b.rect.height);
      if ((width * height) / smaller < 0.1) continue;
      add({
        kind: 'text-overlap',
        severity: 'error',
        message: `"${sample(a.text)}" overlaps "${sample(b.text)}" by ${Math.round(width)}x${Math.round(height)}px.`,
        selectors: [describe(a.el), describe(b.el)],
      });
    }
  }

  // 4. Text cut off by a clipping ancestor, or spilling out of the card or box it sits in.
  const hasOwnBox = (el: Element): boolean => {
    const style = getComputedStyle(el);
    // An inline element (a custom element host) is not a container: its box is only where its first and last line happen to sit.
    if (style.display === 'inline') return false;
    const border = ['Top', 'Right', 'Bottom', 'Left'].some((side) => parseFloat(style.getPropertyValue(`border-${side.toLowerCase()}-width`)) > 0);
    return border || (style.backgroundColor !== 'rgba(0, 0, 0, 0)' && style.backgroundColor !== 'transparent');
  };
  for (const { el, text, rect } of boxes) {
    let clipped = false;
    let escaped = false;
    for (let anc: Element | null = el; anc && anc !== document.documentElement; anc = anc.parentElement) {
      const style = getComputedStyle(anc);
      const box = anc.getBoundingClientRect();
      const out = rect.left < box.left - 1 || rect.right > box.right + 1 || rect.top < box.top - 1 || rect.bottom > box.bottom + 1;
      if (!out || anc === el) continue;
      // Text beyond a scroll container's edge is scrolled out of view, not broken. Stop looking further up.
      if (['auto', 'scroll'].includes(style.overflowX) && (rect.left < box.left - 1 || rect.right > box.right + 1)) break;
      if (['auto', 'scroll'].includes(style.overflowY) && (rect.top < box.top - 1 || rect.bottom > box.bottom + 1)) break;
      if (!clipped && ['hidden', 'clip'].includes(style.overflowX) && (rect.left < box.left - 1 || rect.right > box.right + 1)) clipped = true;
      if (!clipped && ['hidden', 'clip'].includes(style.overflowY) && (rect.top < box.top - 1 || rect.bottom > box.bottom + 1)) clipped = true;
      if (!escaped && !clipped && hasOwnBox(anc) && anc !== document.body && !['auto', 'scroll'].includes(style.overflowX)) {
        escaped = true;
        add({
          kind: 'text-escapes-container',
          severity: 'error',
          message: `"${sample(text)}" extends outside its box.`,
          selectors: [describe(el), describe(anc)],
        });
      }
    }
    if (clipped) {
      add({ kind: 'text-clipped', severity: 'error', message: `"${sample(text)}" is cut off.`, selectors: [describe(el)] });
    }
    // A single-line ellipsis that is truncating is deliberate, so only report hard clipping above.
  }

  // 5. Controls too small to hit (WCAG 2.2 target size minimum is 24px).
  for (const el of document.querySelectorAll('button, a[href], input, select, textarea, [role="radio"], [role="switch"], [role="tab"]')) {
    const rect = el.getBoundingClientRect();
    const style = getComputedStyle(el);
    if (rect.width === 0 || rect.height === 0 || style.visibility === 'hidden') continue;
    if (rect.width < 24 || rect.height < 24) {
      add({
        kind: 'small-target',
        severity: 'warning',
        message: `Control is ${Math.round(rect.width)}x${Math.round(rect.height)}px, under the 24px minimum.`,
        selectors: [describe(el)],
      });
    }
  }

  return issues;
}
