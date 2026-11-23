import { MetricDto } from '../shared/metric.dto';

/** Claims accepted first time, rejected and in total for one month of service, and the rate (0 to 1, null when there were no claims). */
export interface CleanClaimPointDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly cleanClaims: number;
  readonly rejectedClaims: number;
  readonly totalClaims: number;
  readonly rate: number | null;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/clean-claims. The newest point can be older than `endPeriod`. */
export interface CleanClaimRateDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly endPeriod: string | null;
  readonly months: number;
  readonly snapshotDate: string | null;
  readonly points: readonly CleanClaimPointDto[];
}
