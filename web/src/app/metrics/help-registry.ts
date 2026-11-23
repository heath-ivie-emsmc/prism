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
  ...Object.fromEntries(Object.entries(DRILL_TARGETS).map(([key, target]) => [key, target.help])),
};
