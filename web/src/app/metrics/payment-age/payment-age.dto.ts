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

/** The age bands, in whole months. */
export type PaymentAgeBandKey = '0-1' | '2-3' | '4-6' | '7-12' | '13+';

/** The share of a payer group's cash that arrived at ages in the band. Shares are 0 to 1 and add to 1 within rounding. */
export interface PaymentAgeBandDto {
  readonly key: PaymentAgeBandKey;
  readonly fromMonths: number;
  /** Null for the open-ended last band. */
  readonly toMonths: number | null;
  readonly share: number;
}

/** How long one payer group takes to pay. Ages are whole months only: the ledger has no payment day. */
export interface PaymentAgePayerDto {
  readonly payerKey: string;
  readonly payerName: string;
  /** The cash-weighted median age in months. Null when the group has no cash. */
  readonly typicalAgeMonths: number | null;
  /** Empty when the group has no cash. */
  readonly bands: readonly PaymentAgeBandDto[];
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
  readonly payers: readonly PaymentAgePayerDto[];
}
