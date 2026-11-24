import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { NONE, WidgetData, limit, moneyExact, orNa, sharePercent, signedMoney, signedPoints, whole } from '../../saved-views/widget-data';
import { cohortLine } from '../cash-per-transport-by-payer/cash-per-transport-by-payer.widget';
import { PayerMixFinancialEffectDto } from './payer-mix-financial-effect.dto';

/** Said wherever the figure appears: it is what the shift is expected to do once paid, and no cash has moved yet. */
export const EXPECTED_NOT_POSTED = 'This is an expected effect, not posted cash.';
export const RECURRING_NOTE = 'If the shift lasts, the same amount repeats each month.';

export function fromPayerMixFinancialEffect(dto: PayerMixFinancialEffectDto): WidgetData {
  const caption = 'Expected effect of the change in payer mix, by payer group';
  if (dto.groups.length === 0 || (dto.transports === 0 && dto.priorTransports === 0)) {
    return NONE(caption);
  }
  const now = dto.period ? apPeriodLabel(dto.period) : 'This month';
  const before = dto.priorPeriod ? apPeriodLabel(dto.priorPeriod) : 'Month before';
  const effect = dto.estimatedEffect;
  const cohorts = cohortLine(dto);
  const effectSentence = effect === null ? 'No expected effect can be worked out for this period.' : `The change in payer mix is expected to change cash by ${signedMoney(effect)} once those claims are paid.`;
  return {
    summary: limit(`Payer mix effect for ${now}. ${effectSentence} ${EXPECTED_NOT_POSTED}`),
    table: {
      caption,
      columns: ['Payer group', `Transports ${now}`, `Transports ${before}`, 'Share', `Share ${before}`, 'Change in share', 'Cash per transport (matured transports)', 'Expected effect'],
      rows: dto.groups.map((g) => [g.name, whole(g.transports), whole(g.priorTransports), sharePercent(g.share), sharePercent(g.priorShare), signedPoints(g.changePoints), orNa(g.cashPerTransport, moneyExact), orNa(g.effect, signedMoney)]),
    },
    extraTables: [
      {
        caption: 'Expected effect of the shift (not posted cash)',
        columns: ['Figure', 'Amount'],
        rows: [
          ['Expected effect of the shift in payer mix', orNa(dto.estimatedEffect, signedMoney)],
          ['Recurring monthly effect, if the shift lasts', orNa(dto.recurringMonthlyEffect, signedMoney)],
        ],
      },
    ],
    categories: dto.groups.map((g) => g.name),
    categoryKeys: dto.groups.map((g) => g.key),
    series: [{ name: 'Expected effect', values: dto.groups.map((g) => g.effect) }],
    headline: { value: effect === null ? 'n/a' : signedMoney(effect), note: `Expected effect of the shift in payer mix, not posted cash. ${RECURRING_NOTE}` },
    dataThrough: dto.dataThrough ?? null,
    caveat: [EXPECTED_NOT_POSTED, RECURRING_NOTE, cohorts].filter(Boolean).join(' '),
    empty: false,
    unit: 'usd',
  };
}
