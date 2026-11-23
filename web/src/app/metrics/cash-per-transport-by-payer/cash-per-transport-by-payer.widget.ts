import { NONE, WidgetData, limit, money, moneyExact, whole } from '../../saved-views/widget-data';
import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { CashPerTransportDto } from './cash-per-transport-by-payer.dto';

export function fromCashPerTransport(dto: CashPerTransportDto): WidgetData {
  const caption = 'Matured net cash per transport by payer group';
  const payers = dto.payers.filter((p) => p.transports > 0);
  if (payers.length === 0) {
    return NONE(caption);
  }
  return {
    summary: limit(`Matured net cash per transport across ${payers.length} payer groups, from ${money(Math.min(...payers.map((p) => p.cashPerTransport ?? 0)))} to ${money(Math.max(...payers.map((p) => p.cashPerTransport ?? 0)))}.`),
    table: {
      caption,
      columns: ['Payer group', 'Cash per transport', 'Transports', 'Net cash'],
      rows: dto.payers.map((p) => [p.name, p.cashPerTransport === null ? 'n/a' : moneyExact(p.cashPerTransport), whole(p.transports), moneyExact(p.cash)]),
    },
    categories: payers.map((p) => p.name),
    categoryKeys: payers.map((p) => p.key),
    series: [{ name: 'Cash per transport', values: payers.map((p) => p.cashPerTransport) }],
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: dto.cohortFrom && dto.cohortThrough ? `Transports from ${apPeriodLabel(dto.cohortFrom)} to ${apPeriodLabel(dto.cohortThrough)}.` : '',
    empty: false,
    unit: 'usd',
  };
}
