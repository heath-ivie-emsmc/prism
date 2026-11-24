import { MetricDto } from '../shared/metric.dto';

/** Transports and loaded miles (statute) in one month of service. */
export interface TransportVolumePointDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly transports: number;
  readonly loadedMiles: number;
}

/** The last month's transports for one aircraft type or level of service. */
export interface TransportVolumeGroupDto {
  readonly key: string;
  readonly name: string;
  readonly serviceLine: 'air' | 'ground';
  readonly transports: number;
  /** The month before the last, same basis and filters. */
  readonly priorTransports: number;
  readonly changeTransports: number;
  /** Percent units (12.5 is 12.5%), null when the prior month had none. */
  readonly changePercent: number | null;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/transports. */
export interface TransportVolumeDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly endPeriod: string | null;
  readonly months: number;
  readonly serviceLine: 'all' | 'air' | 'ground';
  /** True while the last month's accounting period is open, so transports for it may still be added. */
  readonly periodOpen: boolean;
  readonly points: readonly TransportVolumePointDto[];
  readonly groups: readonly TransportVolumeGroupDto[];
}
