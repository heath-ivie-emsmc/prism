import { FigureDto, MetricEnvelopeDto } from '../shared/figures.dto';

/** One aircraft that flew from a Reporting Division, with its transports. Tail numbers are warehouse text. */
export interface DivisionAircraftDto {
  readonly tail: string;
  readonly isOther: boolean;
  readonly transports: number;
}

/** One Reporting Division, or the Other row. The key is null for transports with no division. The name is warehouse text. */
export interface DivisionRowDto {
  readonly key: string | null;
  readonly name: string;
  readonly isOther: boolean;
  readonly figures: FigureDto;
  readonly aircraft: readonly DivisionAircraftDto[];
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/base-analysis. */
export interface BaseAnalysisDto extends MetricEnvelopeDto {
  readonly totals: FigureDto;
  readonly divisions: readonly DivisionRowDto[];
}
