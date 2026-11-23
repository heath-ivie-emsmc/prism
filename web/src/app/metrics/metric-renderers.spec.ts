import { describe, expect, it } from 'vitest';
import catalog from './catalog.fixture.json';
import { METRIC_RENDERERS, rendererFor } from './metric-renderers';

interface FixtureMetric {
  readonly key: string;
  readonly status: string;
  readonly placeable: boolean;
  readonly presentations: readonly string[];
}

const placeable = (catalog.metrics as readonly FixtureMetric[]).filter((m) => m.status === 'available' && m.placeable);

describe('METRIC_RENDERERS against the catalog fixture', () => {
  it('has a renderer for every available, placeable metric', () => {
    const missing = placeable.filter((m) => !(m.key in METRIC_RENDERERS)).map((m) => m.key);
    expect(missing).toEqual([]);
  });

  it('has no renderer for a metric the catalog does not list as placeable', () => {
    const known = new Set(placeable.map((m) => m.key));
    expect(Object.keys(METRIC_RENDERERS).filter((key) => !known.has(key))).toEqual([]);
  });

  it.each(placeable.map((m) => [m.key, m] as const))('%s lists only presentations the catalog offers', (key, metric) => {
    const unlisted = METRIC_RENDERERS[key]!.presentations.filter((p) => !metric.presentations.includes(p));
    expect(unlisted).toEqual([]);
  });

  it('does not treat inherited object properties as renderers', () => {
    expect(rendererFor('constructor')).toBeUndefined();
    expect(rendererFor('toString')).toBeUndefined();
  });
});
