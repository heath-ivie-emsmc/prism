import { LinkDto } from '../payer-mix/payer-mix.dto';
import { MetricDto } from '../shared/metric.dto';

/** Net cash, transports (encounters with cash posted in the month), loaded miles and the two mix counts (scene and interfacility for air, emergency and non-emergency for ground). */
export interface BreakdownFiguresDto {
  readonly cash: number;
  readonly transports: number;
  readonly loadedMiles: number;
  readonly primaryMix: number;
  readonly secondaryMix: number;
}

/** An aircraft type or level of service. */
export interface BreakdownGroupDto {
  readonly key: string;
  readonly name: string;
  readonly serviceLine: 'air' | 'ground';
  readonly current: BreakdownFiguresDto;
  readonly prior: BreakdownFiguresDto;
  /** Where to go for the next level of detail (the group's payer mix). Absent or empty when the API offers none. */
  readonly links?: readonly LinkDto[];
}

/** A vehicle (tail number or unit) at a base or station. */
export interface BreakdownRowDto {
  readonly groupKey: string;
  readonly groupName: string;
  readonly serviceLine: 'air' | 'ground';
  readonly base: string | null;
  readonly vehicle: string;
  readonly current: BreakdownFiguresDto;
  readonly prior: BreakdownFiguresDto;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/breakdown. */
export interface NetCashBreakdownDto {
  /** Date the data is complete through, yyyy-MM-dd. */
  readonly dataThrough?: string | null;
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly priorPeriod: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  /** True while the accounting period is still open: cash is still posting into it, so the figures are not final. */
  readonly periodOpen: boolean;
  readonly groups: readonly BreakdownGroupDto[];
  readonly rows: readonly BreakdownRowDto[];
}
