import { MetricDto } from '../shared/metric.dto';

/** Cash posted in one month for one aircraft type or level of service. */
export interface NetCashByGroupPointDto {
  readonly groupKey: string;
  readonly groupName: string;
  readonly serviceLine: 'air' | 'ground';
  readonly period: string;
  readonly cash: number;
  readonly transports: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/by-group. */
export interface NetCashByGroupDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly points: readonly NetCashByGroupPointDto[];
}
