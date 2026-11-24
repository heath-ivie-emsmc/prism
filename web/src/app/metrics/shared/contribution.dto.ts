import { LinkDto } from '../payer-mix/payer-mix.dto';
import { MetricEnvelopeDto } from './figures.dto';

/** The change in net cash across every contributor: this month, the month before and the difference. `changePercent` is in percent units, null when the month before was 0. */
export interface ContributionTotalDto {
  readonly netCash: number;
  readonly priorNetCash: number;
  readonly change: number;
  readonly changePercent: number | null;
}

/** One contributor's change in net cash, or the single Other row. The key is null for a name the Provider's directory does not hold. The name is warehouse text. `links` are issued by the server. */
export interface ContributionRowDto {
  readonly key: string | null;
  readonly name: string;
  readonly isOther: boolean;
  readonly netCash: number;
  readonly priorNetCash: number;
  readonly change: number;
  /** This row's change divided by the total change, as a fraction. Null when the total did not change. Can be above 1 or below 0. */
  readonly shareOfChange: number | null;
  readonly links?: readonly LinkDto[];
}

/** Response of GET .../metrics/net-collections/cash-contribution/{aircraft|division|payer}. The rows, Other included, add up exactly to `total.change`. */
export interface CashContributionDto extends MetricEnvelopeDto {
  readonly total: ContributionTotalDto;
  readonly rows: readonly ContributionRowDto[];
}
