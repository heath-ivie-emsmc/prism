import { MetricDto } from '../shared/metric.dto';

/** One month: `period` is yyyyMM, `label` the short month name. */
export interface NetCollectionsPointDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly value: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections. */
export interface NetCollectionsDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly endPeriod: string | null;
  readonly months: number;
  /** Date of the warehouse load the figures come from, yyyy-MM-dd. */
  readonly snapshotDate: string | null;
  readonly points: readonly NetCollectionsPointDto[];
}

export interface PeriodDto {
  readonly period: string;
  readonly label: string;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/periods. */
export interface NetCollectionsPeriodsDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly providerId: string;
  readonly periods: readonly PeriodDto[];
}
