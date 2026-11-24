import { MetricRenderer } from './metric-renderer';
import { NET_COLLECTIONS_RENDERER } from './net-collections/net-collections.renderer';
import { NET_CASH_PROJECTION_RENDERER } from './net-cash-projection/net-cash-projection.renderer';
import { NET_CASH_BREAKDOWN_RENDERER } from './net-cash-breakdown/net-cash-breakdown.renderer';
import { NET_CASH_BY_GROUP_RENDERER } from './net-cash-by-group/net-cash-by-group.renderer';
import { CASH_PER_TRANSPORT_BY_PAYER_RENDERER } from './cash-per-transport-by-payer/cash-per-transport-by-payer.renderer';
import { PAYMENT_AGE_RENDERER } from './payment-age/payment-age.renderer';
import { PAYER_MIX_RENDERER } from './payer-mix/payer-mix.renderer';
import { TRANSPORT_VOLUME_RENDERER } from './transport-volume/transport-volume.renderer';
import { CLEAN_CLAIM_RATE_RENDERER } from './clean-claim-rate/clean-claim-rate.renderer';
import { TRANSPORT_MIX_BY_CALL_TYPE_RENDERER } from './transport-mix-by-call-type/transport-mix-by-call-type.renderer';
import { AIRCRAFT_PERFORMANCE_RENDERER } from './aircraft-performance/aircraft-performance.renderer';
import { BASE_ANALYSIS_RENDERER } from './base-analysis/base-analysis.renderer';
import { LOADED_MILES_BY_AIRCRAFT_RENDERER } from './loaded-miles-by-aircraft/loaded-miles-by-aircraft.renderer';
import { PAYER_MIX_FINANCIAL_EFFECT_RENDERER } from './payer-mix-financial-effect/payer-mix-financial-effect.renderer';
import { TRANSPORTS_BY_PICKUP_LOCATION_RENDERER } from './transports-by-pickup-location/transports-by-pickup-location.renderer';
import { CASH_CONTRIBUTION_BY_AIRCRAFT_RENDERER } from './cash-contribution-by-aircraft/cash-contribution-by-aircraft.renderer';
import { CASH_CONTRIBUTION_BY_DIVISION_RENDERER } from './cash-contribution-by-division/cash-contribution-by-division.renderer';
import { CASH_CONTRIBUTION_BY_PAYER_RENDERER } from './cash-contribution-by-payer/cash-contribution-by-payer.renderer';

/** Widens a typed renderer so renderers for different response shapes can share one map. The key picks the pair, so loader and mapper always agree. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const entry = <T>(renderer: MetricRenderer<T>): MetricRenderer<any> => renderer;

/** The one place that says which Metrics a Saved View widget can draw, keyed by Metric Catalog key. A key outside this map is shown as no longer available, never fetched. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const METRIC_RENDERERS: Readonly<Record<string, MetricRenderer<any>>> = {
  net_collections: entry(NET_COLLECTIONS_RENDERER),
  net_cash_projection: entry(NET_CASH_PROJECTION_RENDERER),
  net_cash_breakdown: entry(NET_CASH_BREAKDOWN_RENDERER),
  net_cash_by_group: entry(NET_CASH_BY_GROUP_RENDERER),
  cash_per_transport_by_payer: entry(CASH_PER_TRANSPORT_BY_PAYER_RENDERER),
  payment_age: entry(PAYMENT_AGE_RENDERER),
  payer_mix: entry(PAYER_MIX_RENDERER),
  transport_volume: entry(TRANSPORT_VOLUME_RENDERER),
  clean_claim_rate: entry(CLEAN_CLAIM_RATE_RENDERER),
  transport_mix_by_call_type: entry(TRANSPORT_MIX_BY_CALL_TYPE_RENDERER),
  aircraft_performance: entry(AIRCRAFT_PERFORMANCE_RENDERER),
  base_analysis: entry(BASE_ANALYSIS_RENDERER),
  loaded_miles_by_aircraft: entry(LOADED_MILES_BY_AIRCRAFT_RENDERER),
  payer_mix_financial_effect: entry(PAYER_MIX_FINANCIAL_EFFECT_RENDERER),
  transports_by_pickup_location: entry(TRANSPORTS_BY_PICKUP_LOCATION_RENDERER),
  cash_contribution_by_aircraft: entry(CASH_CONTRIBUTION_BY_AIRCRAFT_RENDERER),
  cash_contribution_by_division: entry(CASH_CONTRIBUTION_BY_DIVISION_RENDERER),
  cash_contribution_by_payer: entry(CASH_CONTRIBUTION_BY_PAYER_RENDERER),
};

/** The renderer for a Metric key, or undefined when a widget cannot draw it. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function rendererFor(metricKey: string): MetricRenderer<any> | undefined {
  return Object.hasOwn(METRIC_RENDERERS, metricKey) ? METRIC_RENDERERS[metricKey] : undefined;
}
