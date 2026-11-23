import { NONE, WidgetData, limit, moneyExact } from '../../saved-views/widget-data';
import { PaymentAgeDto } from './payment-age.dto';

export function fromPaymentAge(dto: PaymentAgeDto): WidgetData {
  const caption = 'Net cash by payer group and how old the transport was when paid';
  if (dto.cells.length === 0) {
    return NONE(caption);
  }
  const payers = [...new Map(dto.cells.map((c) => [c.payerKey, c.payerName])).entries()];
  const cashFor = (key: string): number => dto.cells.filter((c) => c.payerKey === key).reduce((sum, c) => sum + c.cash, 0);
  const meanAge = (key: string): string => {
    const cells = dto.cells.filter((c) => c.payerKey === key && c.cash > 0);
    const cash = cells.reduce((sum, c) => sum + c.cash, 0);
    return cash === 0 ? 'n/a' : `${(cells.reduce((sum, c) => sum + c.ageMonths * c.cash, 0) / cash).toFixed(1)} months`;
  };
  return {
    summary: limit(`Net cash by payer group over the last 12 months, with how long each took to pay. ${payers.length} payer groups.`),
    table: {
      caption,
      columns: ['Payer group', 'Net cash', 'Average age when paid'],
      rows: payers.map(([key, name]) => [name, moneyExact(cashFor(key)), meanAge(key)]),
    },
    categories: payers.map(([, name]) => name),
    categoryKeys: payers.map(([key]) => key),
    series: [{ name: 'Net cash', values: payers.map(([key]) => cashFor(key)) }],
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: '',
    empty: false,
    unit: 'usd',
  };
}
