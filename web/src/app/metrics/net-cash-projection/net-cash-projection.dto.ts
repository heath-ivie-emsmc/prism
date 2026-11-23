import { MetricDto } from '../shared/metric.dto';

/** One projected month: the projected cash and the likely range. */
export interface ProjectionMonthDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly value: number;
  readonly low: number;
  readonly high: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/projection. Total, low and high are sums over the months. */
export interface NetCashProjectionDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly afterPeriod: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly snapshotDate: string | null;
  readonly total: number;
  readonly low: number;
  readonly high: number;
  readonly months: readonly ProjectionMonthDto[];
}
