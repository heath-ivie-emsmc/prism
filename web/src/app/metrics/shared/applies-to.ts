import type { CatalogMetricDto } from '../../saved-views/layout.model';

/** Which Service Line a Metric is about: air only, ground only, or both. It comes from `context.appliesTo` in the catalog. */
export type AppliesTo = 'air' | 'ground' | 'both';

/** The Service Line choices on the page filter. `both` is the "Air + ground" choice. */
export type LineSelection = 'air' | 'ground' | 'both';

/** What the Metric applies to. An older catalog entry has no `appliesTo`, so it is worked out from the lines the Metric serves. */
export function metricAppliesTo(metric: Pick<CatalogMetricDto, 'serviceLines' | 'context'>): AppliesTo {
  const declared = metric.context?.appliesTo;
  if (declared === 'air' || declared === 'ground' || declared === 'both') return declared;
  const air = metric.serviceLines.includes('air');
  const ground = metric.serviceLines.includes('ground');
  if (air && !ground) return 'air';
  if (ground && !air) return 'ground';
  return 'both';
}

/**
 * True when the page filter can show this Metric. An air-only Metric covers Air only and Air + ground (the API treats "all" as air for it),
 * but not Ground only. Ground-only is the mirror image.
 */
export function coversLine(appliesTo: AppliesTo, line: LineSelection): boolean {
  if (appliesTo === 'both') return true;
  return line === 'both' || line === appliesTo;
}

/** The visible words for one Metric's scope. */
export function appliesToText(appliesTo: AppliesTo): string {
  return { air: 'Air only', ground: 'Ground only', both: 'Air and ground' }[appliesTo];
}

/** The reason an add button gives when the page filter excludes the Metric, for example "Covers air only". Null when it can be added. */
export function coverageReason(appliesTo: AppliesTo, line: LineSelection): string | null {
  return coversLine(appliesTo, line) ? null : `Covers ${appliesTo} only`;
}
