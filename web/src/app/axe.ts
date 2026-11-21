import axe from 'axe-core';

/**
 * Runs axe against rendered markup and returns the violations as short readable lines, so a failing test names the rule and element.
 * WCAG 2.1 A and AA rules only. Colour contrast is skipped: jsdom has no layout or computed colour, so that rule cannot give a true answer here.
 */
export async function axeViolations(root: HTMLElement): Promise<string[]> {
  document.body.append(root);
  try {
    const results = await axe.run(root, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
      rules: { 'color-contrast': { enabled: false } },
    });
    return results.violations.map(
      (violation) => `${violation.id}: ${violation.help} (${violation.nodes.map((node) => node.target.join(' ')).join(' | ')})`,
    );
  } finally {
    root.remove();
  }
}
