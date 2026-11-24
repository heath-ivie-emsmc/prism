import { MetricEnvelopeDto } from '../shared/figures.dto';

/** One month of one aircraft's loaded miles (statute). */
export interface AircraftMilesPointDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly transports: number;
  readonly loadedMiles: number;
  /** Loaded miles divided by transports. Null when there were no transports. */
  readonly averageLoadedMiles: number | null;
}

/** One tail number, or the single Other row. Tail numbers are warehouse text. */
export interface AircraftMilesDto {
  readonly tail: string;
  readonly isOther: boolean;
  readonly transports: number;
  readonly loadedMiles: number;
  readonly averageLoadedMiles: number | null;
  readonly trend: readonly AircraftMilesPointDto[];
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/loaded-miles-by-aircraft. Air only. Has no prior month. */
export interface LoadedMilesByAircraftDto extends MetricEnvelopeDto {
  readonly months: number;
  readonly aircraft: readonly AircraftMilesDto[];
}
