import { PAYER_MIX_DETAIL_HELP } from './payer-mix-detail/help.en';
import { MetricHelp } from './help';

/** A Metric reached only by following a link from another Metric, never placed on a Saved View. */
export interface DrillTargetEntry {
  readonly help: MetricHelp;
}

/**
 * The Metrics that are drill targets, keyed by Metric Catalog key. They are not in METRIC_RENDERERS because a Saved View cannot place them,
 * and the catalog fixture lists only placeable Metrics. Each has its own page, route and loader (`dashboard/metrics/payer-mix/:group/:payerKey`).
 */
export const DRILL_TARGETS: Readonly<Record<string, DrillTargetEntry>> = {
  payer_mix_detail: { help: PAYER_MIX_DETAIL_HELP },
};
