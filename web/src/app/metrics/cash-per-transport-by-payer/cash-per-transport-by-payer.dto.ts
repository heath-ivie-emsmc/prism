import { MetricDto } from '../shared/metric.dto';

/** Matured net cash per transport for one payer group. `cashPerTransport` is null when there are no transports. */
export interface PayerCashPerTransportDto {
  readonly key: string;
  readonly name: string;
  readonly transports: number;
  readonly cash: number;
  readonly cashPerTransport: number | null;
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
  readonly payers: readonly PayerCashPerTransportDto[];
}
