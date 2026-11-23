import { describe, expect, it } from 'vitest';
import { DRILL_TARGETS } from './drill-targets';
import { METRIC_HELP } from './help-registry';
import { METRIC_RENDERERS } from './metric-renderers';

describe('DRILL_TARGETS', () => {
  it('are not placeable Saved View Metrics', () => {
    expect(Object.keys(DRILL_TARGETS).filter((key) => key in METRIC_RENDERERS)).toEqual([]);
  });

  it('each have help text in the help registry', () => {
    expect(Object.keys(DRILL_TARGETS).filter((key) => !METRIC_HELP[key]?.summary)).toEqual([]);
  });
});
