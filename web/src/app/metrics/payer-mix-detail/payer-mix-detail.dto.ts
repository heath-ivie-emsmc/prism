import { MetricDto } from '../shared/metric.dto';

/** One payer inside the group, with its transport counts for the chosen month and the one before. The last entry may be "Other". */
export interface PayerMixDetailPayerDto {
  readonly name: string;
  readonly current: number;
  readonly prior: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/payer-mix/{payerGroupKey}. */
export interface PayerMixDetailDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  /** The aircraft type or level of service the transports belong to. */
  readonly group: string | null;
  readonly groupName: string | null;
  /** The payer group the payers belong to, such as Commercial. */
  readonly payerGroup: { readonly key: string; readonly name: string };
  readonly period: string | null;
  readonly priorPeriod: string | null;
  /** The Service Line the counts were read for. */
  readonly serviceLine?: 'all' | 'air' | 'ground';
  /** The Reporting Division key the counts were limited to, and its display name; null when the request had no division. */
  readonly division?: string | null;
  readonly divisionName?: string | null;
  readonly payers: readonly PayerMixDetailPayerDto[];
  readonly dataThrough?: string | null;
}
