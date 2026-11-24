import { MetricDto } from './metric.dto';

/** One payer group's share of a slice (a call type or a pickup type), this month and the month before. Shares are fractions from 0 to 1, null when the total is 0. */
export interface PayerShareDto {
  readonly key: string;
  readonly name: string;
  readonly transports: number;
  readonly priorTransports: number;
  readonly share: number | null;
  readonly priorShare: number | null;
}

/** What every response of the net-collections Metrics repeats: which Metric, for whom, which period and under which scope. */
export interface MetricEnvelopeDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  /** The month before `period`. Absent on the loaded miles route, which has no prior month. */
  readonly priorPeriod?: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly group: string | null;
  /** The Reporting Division key when one is selected, else null. */
  readonly division?: string | null;
  readonly divisionName?: string | null;
}

/** The service months and the payment cut-off an expected figure uses, as yyyyMM. */
export interface CohortsDto {
  readonly firstServiceMonth: string;
  readonly lastServiceMonth: string;
  readonly cashThroughPeriod: string;
}

/** Transports, loaded miles and posted net cash for one row, with the month before and the change. `*Percent` figures are percent units (12.5 is 12.5%), null when the month before was 0. */
export interface FigureDto {
  readonly transports: number;
  readonly priorTransports: number;
  readonly changeTransports: number;
  readonly changeTransportsPercent: number | null;
  readonly loadedMiles: number;
  readonly priorLoadedMiles: number;
  readonly changeLoadedMiles: number;
  readonly changeLoadedMilesPercent: number | null;
  readonly netCash: number;
  readonly priorNetCash: number;
  readonly changeNetCash: number;
  readonly changeNetCashPercent: number | null;
  /** Cash posted in the month divided by the transports of the month. Not an expected payment. Two decimals, null when there are no transports. */
  readonly netCashPerTransport: number | null;
  readonly priorNetCashPerTransport: number | null;
}
