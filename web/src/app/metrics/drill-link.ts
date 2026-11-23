import type { LinkDto } from './payer-mix/payer-mix.dto';

/** A drill link read from the API: which known target it leads to, and the scope it carries. Never the API path itself: the SPA has its own routes. */
export interface ParsedDrill {
  readonly target: 'payer-mix' | 'payer-mix-detail';
  readonly group: string;
  /** Present for `payer-mix-detail`. */
  readonly payerKey?: string;
  readonly period?: string;
  readonly line?: 'air' | 'ground';
  readonly division?: string;
}

/** Where a drill link leads in the SPA: router commands and query parameters. */
export interface DrillRoute {
  readonly commands: readonly string[];
  readonly queryParams: Readonly<Record<string, string>>;
}

const MAX_HREF = 500;
const KEY = /^[a-z0-9-]{1,40}$/;
const PERIOD = /^\d{6}$/;
const DIVISION = /^[A-Za-z0-9_-]{1,64}$/;

/**
 * The known drill targets, by the API path they come from (after `/api/v1/providers/{providerId}/metrics/`). A path that is not listed is rejected,
 * so a link can never send the reader anywhere the SPA does not already have a page for. Add a target here and in `routeOf`.
 */
const DRILL_HREF = /^\/api\/v1\/providers\/[^/?#]{1,64}\/metrics\/net-collections\/payer-mix(?:\/([a-z0-9-]{1,40}))?(?:\?([^#]{0,300}))?$/;

/** One drill link, or null when it is not a link to a known target under the API's route (anything else, or too long, is ignored). */
export function parseDrillHref(href: string, fallbackGroup = ''): ParsedDrill | null {
  const match = href.length <= MAX_HREF ? DRILL_HREF.exec(href) : null;
  if (!match) return null;
  const query = new URLSearchParams(match[2] ?? '');
  const payerKey = match[1];
  const group = query.get('group') ?? fallbackGroup;
  if (!KEY.test(group)) return null;
  const period = query.get('period') ?? '';
  const line = query.get('serviceLine');
  const division = query.get('division') ?? '';
  return {
    target: payerKey ? 'payer-mix-detail' : 'payer-mix',
    group,
    ...(payerKey ? { payerKey } : {}),
    ...(PERIOD.test(period) ? { period } : {}),
    ...(line === 'air' || line === 'ground' ? { line } : {}),
    ...(DIVISION.test(division) ? { division } : {}),
  };
}

/** The first drill link in a list that points at a known target, or null. */
export function drillOf(links: readonly LinkDto[] | undefined, fallbackGroup = ''): ParsedDrill | null {
  const link = links?.find((l) => l.rel === 'drill');
  return link ? parseDrillHref(link.href, fallbackGroup) : null;
}

function routeOf(drill: ParsedDrill): readonly string[] {
  return drill.target === 'payer-mix-detail' ? ['/dashboard/metrics/payer-mix', drill.group, drill.payerKey!] : ['/dashboard/net-cash', drill.group];
}

/** The SPA route for a parsed drill, carrying the month, Service Line and Division the link names. */
export function drillRouteOf(drill: ParsedDrill): DrillRoute {
  return {
    commands: routeOf(drill),
    queryParams: { ...(drill.period ? { period: drill.period } : {}), ...(drill.line ? { line: drill.line } : {}), ...(drill.division ? { division: drill.division } : {}) },
  };
}

/** The SPA route for the drill link in a list, or null when there is none or it is not a known target. */
export function drillRoute(links: readonly LinkDto[] | undefined, fallbackGroup = ''): DrillRoute | null {
  const drill = drillOf(links, fallbackGroup);
  return drill ? drillRouteOf(drill) : null;
}

/** The accessible name of a link to a payer group's breakdown. */
export const drillLinkName = (name: string): string => `View ${name} breakdown`;

/**
 * The page a Metric key already has, given the query parameters of a generic Metric view address, or null when the key has no page of its own
 * (or the address lacks what the page needs). Only the keys with a page are listed. Each value is checked before it reaches the route.
 */
export function metricPageRoute(key: string, get: (name: string) => string | null): DrillRoute | null {
  const period = get('period') ?? '';
  const line = get('line') ?? get('serviceLine');
  const division = get('division') ?? '';
  const scope = {
    ...(PERIOD.test(period) ? { period } : {}),
    ...(line === 'air' || line === 'ground' ? { line } : {}),
    ...(DIVISION.test(division) ? { division } : {}),
  } as const;
  const group = get('group') ?? '';
  const payerKey = get('payerKey') ?? '';
  switch (key) {
    case 'net_cash_breakdown':
      return { commands: ['/dashboard/net-cash'], queryParams: scope };
    case 'payer_mix':
      return KEY.test(group) ? { commands: ['/dashboard/net-cash', group], queryParams: scope } : null;
    case 'payer_mix_detail':
      return KEY.test(group) && KEY.test(payerKey) ? { commands: ['/dashboard/metrics/payer-mix', group, payerKey], queryParams: scope } : null;
    default:
      return null;
  }
}
