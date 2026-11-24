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
  /** The Reporting Division key when one is applied, else null. */
  readonly division?: string | null;
  readonly divisionName?: string | null;
  /** Says that cash on encounters with no trip row or no division belongs to no division. Present only when a division is applied. Warehouse-adjacent text: shown as text only. */
  readonly divisionNote?: string | null;
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

/** One Reporting Division of the Provider: `key` is what the `division` query parameter takes, `name` is the warehouse's name (untrusted text, shown as text only). */
export interface ReportingDivisionDto {
  readonly key: string;
  readonly name: string;
}

/** GET .../metrics/net-collections/divisions. */
export interface ReportingDivisionsDto {
  readonly providerId: string;
  readonly divisions: readonly ReportingDivisionDto[];
}
