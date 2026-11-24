import { MetricHelp } from './help';
import { CLEAN_CLAIM_RATE_HELP } from './clean-claim-rate/help.en';
import { NET_COLLECTIONS_HELP } from './net-collections/help.en';
import { PAYER_MIX_HELP } from './payer-mix/help.en';
import { CASH_PER_TRANSPORT_BY_PAYER_HELP } from './cash-per-transport-by-payer/help.en';
import { NET_CASH_BREAKDOWN_HELP } from './net-cash-breakdown/help.en';
import { NET_CASH_BY_GROUP_HELP } from './net-cash-by-group/help.en';
import { NET_CASH_PROJECTION_HELP } from './net-cash-projection/help.en';
import { PAYMENT_AGE_HELP } from './payment-age/help.en';
import { DRILL_TARGETS } from './drill-targets';
import { TRANSPORT_VOLUME_HELP } from './transport-volume/help.en';
import { TRANSPORT_MIX_BY_CALL_TYPE_HELP } from './transport-mix-by-call-type/help.en';
import { AIRCRAFT_PERFORMANCE_HELP } from './aircraft-performance/help.en';
import { BASE_ANALYSIS_HELP } from './base-analysis/help.en';
import { LOADED_MILES_BY_AIRCRAFT_HELP } from './loaded-miles-by-aircraft/help.en';
import { PAYER_MIX_FINANCIAL_EFFECT_HELP } from './payer-mix-financial-effect/help.en';
import { TRANSPORTS_BY_PICKUP_LOCATION_HELP } from './transports-by-pickup-location/help.en';
import { CASH_CONTRIBUTION_BY_AIRCRAFT_HELP } from './cash-contribution-by-aircraft/help.en';
import { CASH_CONTRIBUTION_BY_DIVISION_HELP } from './cash-contribution-by-division/help.en';
import { CASH_CONTRIBUTION_BY_PAYER_HELP } from './cash-contribution-by-payer/help.en';

/** Help text by Metric key. A spec checks every Metric in the catalog fixture has an entry. */
export const METRIC_HELP: Readonly<Record<string, MetricHelp>> = {
  net_collections: NET_COLLECTIONS_HELP,
  net_cash_projection: NET_CASH_PROJECTION_HELP,
  net_cash_breakdown: NET_CASH_BREAKDOWN_HELP,
  net_cash_by_group: NET_CASH_BY_GROUP_HELP,
  cash_per_transport_by_payer: CASH_PER_TRANSPORT_BY_PAYER_HELP,
  payment_age: PAYMENT_AGE_HELP,
  payer_mix: PAYER_MIX_HELP,
  transport_volume: TRANSPORT_VOLUME_HELP,
  clean_claim_rate: CLEAN_CLAIM_RATE_HELP,
  transport_mix_by_call_type: TRANSPORT_MIX_BY_CALL_TYPE_HELP,
  aircraft_performance: AIRCRAFT_PERFORMANCE_HELP,
  base_analysis: BASE_ANALYSIS_HELP,
  loaded_miles_by_aircraft: LOADED_MILES_BY_AIRCRAFT_HELP,
  payer_mix_financial_effect: PAYER_MIX_FINANCIAL_EFFECT_HELP,
  transports_by_pickup_location: TRANSPORTS_BY_PICKUP_LOCATION_HELP,
  cash_contribution_by_aircraft: CASH_CONTRIBUTION_BY_AIRCRAFT_HELP,
  cash_contribution_by_division: CASH_CONTRIBUTION_BY_DIVISION_HELP,
  cash_contribution_by_payer: CASH_CONTRIBUTION_BY_PAYER_HELP,
  ...Object.fromEntries(Object.entries(DRILL_TARGETS).map(([key, target]) => [key, target.help])),
};
