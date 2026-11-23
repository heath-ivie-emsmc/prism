import { NONE, WidgetData, limit, money, moneyExact, monthLabel } from '../../saved-views/widget-data';
import { NetCashProjectionDto } from './net-cash-projection.dto';

export function fromProjection(dto: NetCashProjectionDto): WidgetData {
  const caption = 'Projected net cash by month';
  if (dto.months.length === 0) {
    return NONE(caption);
  }
  return {
    summary: limit(`Projected net cash for the next ${dto.months.length} months: ${money(dto.total)} in total, likely between ${money(dto.low)} and ${money(dto.high)}.`),
    table: {
      caption,
      columns: ['Month', 'Projected', 'Likely low', 'Likely high'],
      rows: dto.months.map((m) => [monthLabel(m.label, m.year), moneyExact(m.value), moneyExact(m.low), moneyExact(m.high)]),
    },
    categories: dto.months.map((m) => monthLabel(m.label, m.year)),
    series: [
      { name: 'Projected', values: dto.months.map((m) => m.value) },
      { name: 'Likely low', values: dto.months.map((m) => m.low) },
      { name: 'Likely high', values: dto.months.map((m) => m.high) },
    ],
    headline: { value: money(dto.total), note: `Likely ${money(dto.low)} to ${money(dto.high)}` },
    dataThrough: dto.dataThrough ?? dto.snapshotDate,
    caveat: '',
    empty: false,
    unit: 'usd',
  };
}
