import { MetricDto } from '../shared/metric.dto';

/** Matured net cash per transport for one payer group. `cashPerTransport` is null when there are no transports. */
export interface PayerCashPerTransportDto {
  readonly key: string;
  readonly name: string;
  readonly transports: number;
  readonly cash: number;
  readonly cashPerTransport: number | null;
  /** This group's cash per transport minus the overall average, in USD. Null when either has no transports. */
  readonly differenceFromOverall: number | null;
  /** The same difference in percent units (12.5 is 12.5%). Null when the overall average is zero or unknown. */
  readonly percentFromOverall: number | null;
}

/** The same measure across all payer groups, for the same period, Service Line and Division. */
export interface OverallCashPerTransportDto {
  readonly transports: number;
  readonly cash: number;
  readonly cashPerTransport: number | null;
}

/** The service months and the payment cut-off the figures use, as yyyyMM. */
export interface CashPerTransportCohortsDto {
  readonly firstServiceMonth: string;
  readonly lastServiceMonth: string;
  readonly cashThroughPeriod: string;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/cash-per-transport. */
export interface CashPerTransportDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly cohortFrom: string | null;
  readonly cohortThrough: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly group: string | null;
  readonly overall: OverallCashPerTransportDto | null;
  readonly cohorts: CashPerTransportCohortsDto | null;
  readonly payers: readonly PayerCashPerTransportDto[];
}
