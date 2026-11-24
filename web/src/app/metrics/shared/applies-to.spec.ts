import { describe, expect, it } from 'vitest';
import { appliesToText, coverageReason, coversLine, metricAppliesTo } from './applies-to';

describe('metric scope', () => {
  it('reads context.appliesTo, and works it out from the served lines for an older catalog', () => {
    expect(metricAppliesTo({ serviceLines: ['air', 'both'], context: { appliesTo: 'air' } })).toBe('air');
    expect(metricAppliesTo({ serviceLines: ['air', 'both'] })).toBe('air');
    expect(metricAppliesTo({ serviceLines: ['ground', 'both'] })).toBe('ground');
    expect(metricAppliesTo({ serviceLines: ['air', 'ground', 'both'] })).toBe('both');
    expect(metricAppliesTo({ serviceLines: [] })).toBe('both');
  });

  it('covers a line the Metric is about, and Air + ground for a one-line Metric, but not the other line', () => {
    expect(coversLine('air', 'air')).toBe(true);
    expect(coversLine('air', 'both')).toBe(true);
    expect(coversLine('air', 'ground')).toBe(false);
    expect(coversLine('ground', 'ground')).toBe(true);
    expect(coversLine('ground', 'both')).toBe(true);
    expect(coversLine('ground', 'air')).toBe(false);
    expect(coversLine('both', 'ground')).toBe(true);
  });

  it('words the scope and the reason', () => {
    expect(appliesToText('air')).toBe('Air only');
    expect(appliesToText('ground')).toBe('Ground only');
    expect(appliesToText('both')).toBe('Air and ground');
    expect(coverageReason('air', 'ground')).toBe('Covers air only');
    expect(coverageReason('ground', 'air')).toBe('Covers ground only');
    expect(coverageReason('air', 'air')).toBeNull();
  });
});
