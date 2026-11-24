import { FigureDto, MetricEnvelopeDto } from '../shared/figures.dto';

/** One tail number, or the single Other row that holds the rest. */
export interface AircraftRowDto {
  readonly tail: string;
  readonly isOther: boolean;
  readonly figures: FigureDto;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/aircraft-performance. Air only: Ground only is a 400. Top 100 tails plus one Other row. Tail numbers are warehouse text. */
export interface AircraftPerformanceDto extends MetricEnvelopeDto {
  readonly totals: FigureDto;
  readonly aircraft: readonly AircraftRowDto[];
}
