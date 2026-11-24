import { MetricDto } from '../shared/metric.dto';

/** One projected month: the projected cash and the likely range. */
export interface ProjectionMonthDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly value: number;
  readonly low: number;
  readonly high: number;
  /** Change from the month before. The first month is compared with `latestActual`. Null when there is nothing to compare with. */
  readonly changeFromPrevious: ProjectionChangeDto | null;
  /** The running total from the first month through this one. */
  readonly cumulative: ProjectionCumulativeDto;
  /** Expected collections on trips that already existed. Null when the warehouse has no figure. */
  readonly fromExistingTrips: number | null;
  /** The assumed run rate of trips that do not exist yet. Null when the warehouse has no figure. */
  readonly fromFutureTrips: number | null;
  /** Each part divided by this month's total, 0 to 1. The two can add to more than 1. Null when unknown. */
  readonly existingTripsShare: number | null;
  readonly futureTripsShare: number | null;
}

/** `amount` in USD, `percent` in percent units (12.5 is 12.5%), null when the base is zero. */
export interface ProjectionChangeDto {
  readonly amount: number;
  readonly percent: number | null;
}

export interface ProjectionCumulativeDto {
  readonly value: number;
  readonly low: number;
  readonly high: number;
}

/** The totals across the months returned. */
export interface ProjectionHorizonDto {
  readonly value: number;
  readonly low: number;
  readonly high: number;
  readonly months: number;
  readonly fromExistingTrips: number | null;
  readonly fromFutureTrips: number | null;
  readonly existingTripsShare: number | null;
  readonly futureTripsShare: number | null;
}

/** The newest month with posted net cash that the first projected month is compared with. */
export interface ProjectionLatestActualDto {
  /** yyyyMM */
  readonly period: string;
  readonly value: number;
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
  readonly horizon: ProjectionHorizonDto;
  readonly latestActual: ProjectionLatestActualDto | null;
}
