import { MetricEnvelopeDto, PayerShareDto } from '../shared/figures.dto';

/** One call type: scene, interfacility, emergency, non-emergency or not recorded. Shares are fractions 0..1, `changePoints` are percentage points, all null when there is nothing to divide by. */
export interface CallTypeDto {
  readonly key: string;
  readonly name: string;
  readonly transports: number;
  readonly priorTransports: number;
  readonly changeTransports: number;
  readonly share: number | null;
  readonly priorShare: number | null;
  readonly changePoints: number | null;
  readonly loadedMiles: number;
  readonly priorLoadedMiles: number;
  /** Cash posted in the month: a separate figure from the counts, which are by date of service. */
  readonly netCash: number;
  readonly priorNetCash: number;
  readonly payerMix: readonly PayerShareDto[];
}

export interface CallTypeTrendCallTypeDto {
  readonly key: string;
  readonly name: string;
  readonly transports: number;
  readonly share: number | null;
}

/** One month of the trend, with each call type's count. */
export interface CallTypeTrendPointDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly transports: number;
  readonly callTypes: readonly CallTypeTrendCallTypeDto[];
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/transport-mix-by-call-type. */
export interface TransportMixByCallTypeDto extends MetricEnvelopeDto {
  readonly months: number;
  readonly totalTransports: number;
  readonly priorTotalTransports: number;
  readonly callTypes: readonly CallTypeDto[];
  readonly trend: readonly CallTypeTrendPointDto[];
}
