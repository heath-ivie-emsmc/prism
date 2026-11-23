import { MetricDto } from '../shared/metric.dto';

/** Cash posted in one month for transports of one payer group that were a given number of months old when paid. */
export interface PaymentAgeCellDto {
  readonly payerKey: string;
  readonly payerName: string;
  readonly postingPeriod: string;
  readonly ageMonths: number;
  readonly cash: number;
  readonly transports: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/payment-age. */
export interface PaymentAgeDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly group: string | null;
  readonly cells: readonly PaymentAgeCellDto[];
}
