import { describe, expect, it } from 'vitest';
import catalog from './catalog.fixture.json';
import { DRILL_TARGETS } from './drill-targets';
import { resolveHelp, resolveHelpText } from './help';
import { METRIC_HELP } from './help-registry';

const KEYS = [...(catalog.metrics as readonly { key: string }[]).map((m) => m.key), ...Object.keys(DRILL_TARGETS)];
/** Abbreviations a billing reader may not know. Each must be spelled out the first time it appears, or not used. */
const DENYLIST = ['AR', 'CSE', 'ETL', 'KPI', 'DOS'];
/** The expansion that must appear within 80 characters after the first use of an abbreviation. */
const EXPANSIONS: Readonly<Record<string, string>> = { AR: 'accounts receivable' };
const words = (text: string): number => text.trim().split(/\s+/).length;

describe('METRIC_HELP against the catalog fixture and the drill targets', () => {
  it('has help text for every metric', () => {
    const missing = KEYS.filter((key) => !METRIC_HELP[key]?.summary);
    expect(missing, `Metrics missing help text: ${missing.join(', ')}`).toEqual([]);
  });

  it.each(KEYS)('%s has a short summary and 3 to 7 distinct, non-empty glossary terms', (key) => {
    const help = METRIC_HELP[key]!;
    expect(words(help.summary)).toBeLessThanOrEqual(65);
    expect(help.glossary.length).toBeGreaterThanOrEqual(3);
    expect(help.glossary.length).toBeLessThanOrEqual(7);
    const terms = help.glossary.map((g) => g.term.trim().toLowerCase());
    expect(terms.every((t) => t.length > 0)).toBe(true);
    expect(help.glossary.every((g) => g.text.trim().length > 0)).toBe(true);
    expect(new Set(terms).size).toBe(terms.length);
  });

  it.each(KEYS)('%s uses no unexplained abbreviation, one spelling of payer and no hard-coded division word', (key) => {
    const help = METRIC_HELP[key]!;
    const all = [help.summary, ...help.glossary.flatMap((g) => [g.term, g.text])].join(' ');
    for (const abbreviation of DENYLIST) {
      const first = new RegExp(`\\b${abbreviation}\\b`).exec(all);
      if (!first) continue;
      const expansion = EXPANSIONS[abbreviation];
      expect(expansion && all.slice(first.index, first.index + 80).includes(expansion), `${key} uses ${abbreviation} without spelling it out`).toBe(true);
    }
    expect(all).not.toMatch(/\bpayor/i);
    expect(all).not.toMatch(/\bbases?\b/i);
    expect(all).not.toMatch(/reporting division/i);
  });

  it('fills the Label placeholders, lower case in a sentence', () => {
    expect(resolveHelpText('Pick a {reportingDivision}. {ReportingDivision}.', { reportingDivision: 'Base location' })).toBe('Pick a base location. Base location.');
    const resolved = resolveHelp(METRIC_HELP['net_cash_breakdown']!, { reportingDivision: 'Station' });
    expect(resolved.summary).toContain('station');
    expect(JSON.stringify(resolved)).not.toMatch(/\{[A-Za-z]+\}/);
  });
});
