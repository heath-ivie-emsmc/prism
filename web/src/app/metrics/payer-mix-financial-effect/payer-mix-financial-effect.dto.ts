import { CohortsDto, MetricEnvelopeDto } from '../shared/figures.dto';

/** One payer group's share of transports, this month and the month before, with what a transport has earned and its expected effect. */
export interface PayerEffectGroupDto {
  readonly key: string;
  readonly name: string;
  readonly transports: number;
  readonly priorTransports: number;
  /** Fractions 0..1, null when the month had no transports. */
  readonly share: number | null;
  readonly priorShare: number | null;
  /** Percentage points. */
  readonly changePoints: number | null;
  /** What a transport of this group has earned once paid, matured cohorts. Null when the group has no matured transports. */
  readonly cashPerTransport: number | null;
  /** The expected effect on cash of this group's change in share, in USD. Null when it cannot be worked out. */
  readonly effect: number | null;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/payer-mix-financial-effect. The effect is expected, not posted cash. */
export interface PayerMixFinancialEffectDto extends MetricEnvelopeDto {
  readonly basis: 'expected';
  readonly transports: number;
  readonly priorTransports: number;
  readonly cohorts: CohortsDto | null;
  readonly groups: readonly PayerEffectGroupDto[];
  /** The expected effect of the shift in payer mix, once those claims are paid. */
  readonly estimatedEffect: number | null;
  /** The same amount, each month, if the shift lasts. It is the same number as `estimatedEffect`. */
  readonly recurringMonthlyEffect: number | null;
}
