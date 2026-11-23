import { describe, expect, it } from 'vitest';
import { drillLinkName, drillRoute, parseDrillHref } from './drill-link';

const base = '/api/v1/providers/526/metrics/net-collections/payer-mix';
const link = (href: string) => [{ rel: 'drill', href, title: 'x' }];

describe('drill link parser', () => {
  it('maps a group payer mix link to its page with the month, Service Line and Division', () => {
    expect(drillRoute(link(`${base}?group=rotor-wing&period=202610&serviceLine=air&division=north`))).toEqual({
      commands: ['/dashboard/net-cash', 'rotor-wing'],
      queryParams: { period: '202610', line: 'air', division: 'north' },
    });
  });

  it('maps a payer link to the payer breakdown page', () => {
    expect(drillRoute(link(`${base}/commercial?group=rotor-wing&period=202610&serviceLine=ground&division=`))).toEqual({
      commands: ['/dashboard/metrics/payer-mix', 'rotor-wing', 'commercial'],
      queryParams: { period: '202610', line: 'ground' },
    });
  });

  it('uses the fallback group when the link has none, and drops values that are not valid', () => {
    expect(drillRoute(link(`${base}/medicare?period=20261&serviceLine=all&division=a%20b`), 'als')).toEqual({ commands: ['/dashboard/metrics/payer-mix', 'als', 'medicare'], queryParams: {} });
  });

  it.each([
    ['no links', undefined],
    ['an empty list', []],
    ['another relation', [{ rel: 'self', href: `${base}?group=als`, title: 'x' }]],
    ['a group payer mix link without a group', link(base)],
    ['a different host', link(`https://evil.example${base}?group=als`)],
    ['another route', link('/api/v1/providers/526/saved-views/1')],
    ['a script', link('javascript:alert(1)')],
    ['a traversal', link(`${base}/../x?group=als`)],
    ['an upper-case group', link(`${base}?group=ALS`)],
    ['a very long href', link(`${base}?group=als&x=${'a'.repeat(600)}`)],
  ])('rejects %s', (_name, links) => {
    expect(drillRoute(links as never)).toBeNull();
  });

  it('parses a bare href and names the link for assistive technology', () => {
    expect(parseDrillHref(`${base}?group=als`)).toEqual({ target: 'payer-mix', group: 'als' });
    expect(drillLinkName('Commercial')).toBe('View Commercial breakdown');
  });
});
