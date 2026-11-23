import { MetricDto } from '../shared/metric.dto';

/** A payer group with its transport counts for the chosen month and the one before. */
export interface PayerMixShareDto {
  readonly key: string;
  readonly name: string;
  readonly current: number;
  readonly prior: number;
  /** Where to go for the next level of detail. Absent or empty when the API offers none for this group. */
  readonly links?: readonly LinkDto[];
}

/** A hypermedia link on a data point. The href is a path under /api/v1/providers/{providerId}/, built by the API. */
export interface LinkDto {
  readonly rel: string;
  readonly href: string;
  readonly title: string;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/payer-mix. */
export interface PayerMixDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly priorPeriod: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly group: string | null;
  readonly groupName: string | null;
  readonly shares: readonly PayerMixShareDto[];
}
