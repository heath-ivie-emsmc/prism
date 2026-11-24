import { NONE, WidgetData, WidgetTable, limit, moneyExact } from '../../saved-views/widget-data';
import { PaymentAgeBandKey, PaymentAgeDto, PaymentAgePayerDto } from './payment-age.dto';

/** The five age bands in the order they are shown, as column headings. Ages are whole months. */
export const BAND_HEADINGS: Readonly<Record<PaymentAgeBandKey, string>> = {
  '0-1': '0-1 months',
  '2-3': '2-3 months',
  '4-6': '4-6 months',
  '7-12': '7-12 months',
  '13+': '13+ months',
};
export const BAND_ORDER: readonly PaymentAgeBandKey[] = ['0-1', '2-3', '4-6', '7-12', '13+'];

const sharePercent = (share: number): string => `${(share * 100).toFixed(1)}%`;

/** "2 months", "1 month", or "n/a" when the payer group has no cash. Whole months only: the ledger has no payment day. */
export function typicalAgeText(months: number | null): string {
  if (months === null) return 'n/a';
  return `${months} ${months === 1 ? 'month' : 'months'}`;
}

/** One row per payer group: typical months to payment, then the share of its cash that arrived in each age band. A group with no bands shows n/a. */
export function timingTable(payers: readonly PaymentAgePayerDto[]): WidgetTable {
  return {
    caption: 'Payer timing: typical months to payment and share of cash by age band',
    columns: ['Payer group', 'Typical months to payment', ...BAND_ORDER.map((k) => BAND_HEADINGS[k])],
    rows: payers.map((p) => [
      p.payerName,
      typicalAgeText(p.typicalAgeMonths),
      ...BAND_ORDER.map((key) => {
        const band = p.bands.find((b) => b.key === key);
        return band ? sharePercent(band.share) : 'n/a';
      }),
    ]),
  };
}

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
    extraTables: dto.payers.length > 0 ? [timingTable(dto.payers)] : [],
    categories: payers.map(([, name]) => name),
    categoryKeys: payers.map(([key]) => key),
    series: [{ name: 'Net cash', values: payers.map(([key]) => cashFor(key)) }],
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: 'Age is counted in whole months, not days.',
    empty: false,
    unit: 'usd',
  };
}
