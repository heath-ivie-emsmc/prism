import { NONE, WidgetData, limit, money, moneyExact, signedMoney, signedPercentUnits, whole } from '../../saved-views/widget-data';
import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { shortMonthYear } from '../../shared/data-through';
import { CashPerTransportDto } from './cash-per-transport-by-payer.dto';

/** "Transports from Jan 2025 to Dec 2025, cash posted through Nov 2026", in the user's locale. Empty when the API sent no cohorts. */
export function cohortLine(dto: Pick<CashPerTransportDto, 'cohorts'>): string {
  const c = dto.cohorts;
  if (!c) return '';
  const first = shortMonthYear(c.firstServiceMonth);
  const last = shortMonthYear(c.lastServiceMonth);
  const through = shortMonthYear(c.cashThroughPeriod);
  return first && last && through ? `Transports from ${first} to ${last}, cash posted through ${through}` : '';
}

export function fromCashPerTransport(dto: CashPerTransportDto): WidgetData {
  const caption = 'Matured net cash per transport by payer group';
  const payers = dto.payers.filter((p) => p.transports > 0);
  if (payers.length === 0) {
    return NONE(caption);
  }
  const overall = dto.overall?.cashPerTransport ?? null;
  const overallSentence = overall === null ? '' : ` The overall average is ${money(overall)}.`;
  return {
    summary: limit(`Matured net cash per transport across ${payers.length} payer groups, from ${money(Math.min(...payers.map((p) => p.cashPerTransport ?? 0)))} to ${money(Math.max(...payers.map((p) => p.cashPerTransport ?? 0)))}.${overallSentence}`),
    table: {
      caption,
      columns: ['Payer group', 'Cash per transport', 'Difference from overall', '% from overall', 'Transports', 'Net cash'],
      rows: [
        ...dto.payers.map((p) => [
          p.name,
          p.cashPerTransport === null ? 'n/a' : moneyExact(p.cashPerTransport),
          p.differenceFromOverall === null ? 'n/a' : signedMoney(p.differenceFromOverall),
          signedPercentUnits(p.percentFromOverall),
          whole(p.transports),
          moneyExact(p.cash),
        ]),
        ...(dto.overall
          ? [['All payer groups (overall)', dto.overall.cashPerTransport === null ? 'n/a' : moneyExact(dto.overall.cashPerTransport), '', '', whole(dto.overall.transports), moneyExact(dto.overall.cash)]]
          : []),
      ],
    },
    categories: payers.map((p) => p.name),
    categoryKeys: payers.map((p) => p.key),
    series: [{ name: 'Cash per transport', values: payers.map((p) => p.cashPerTransport) }],
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: cohortLine(dto) || (dto.cohortFrom && dto.cohortThrough ? `Transports from ${apPeriodLabel(dto.cohortFrom)} to ${apPeriodLabel(dto.cohortThrough)}.` : ''),
    empty: false,
    unit: 'usd',
  };
}
