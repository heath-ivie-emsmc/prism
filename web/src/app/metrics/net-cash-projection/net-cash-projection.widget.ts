import { NONE, WidgetData, limit, money, monthLabel } from '../../saved-views/widget-data';
import { NetCashProjectionDto } from './net-cash-projection.dto';
import { COMPONENTS_FOOTNOTE, EXISTING_TRIPS_LABEL, FUTURE_TRIPS_LABEL, RANGE_NOTE, toProjectionView } from './net-cash-projection.model';

export function fromProjection(dto: NetCashProjectionDto): WidgetData {
  const caption = 'Projected net cash by month';
  if (dto.months.length === 0) {
    return NONE(caption);
  }
  const view = toProjectionView(dto);
  return {
    summary: limit(`Projected net cash for the next ${dto.months.length} months: ${money(dto.total)} in total, with a range across the forecast components of ${money(dto.low)} to ${money(dto.high)}.`),
    table: {
      caption,
      columns: ['Month', 'Projected', 'Change from previous', 'Range across components', 'Cumulative', 'Cumulative range', EXISTING_TRIPS_LABEL, 'Share of projected (existing trips)', FUTURE_TRIPS_LABEL, 'Share of projected (future trips)'],
      rows: view.months.map((m) => [m.label, m.expected, m.change, m.range, m.cumulative, m.cumulativeRange, m.existing, m.existingShare, m.future, m.futureShare]),
    },
    categories: dto.months.map((m) => monthLabel(m.label, m.year)),
    series: [
      { name: 'Projected', values: dto.months.map((m) => m.value) },
      { name: 'Range low', values: dto.months.map((m) => m.low) },
      { name: 'Range high', values: dto.months.map((m) => m.high) },
    ],
    headline: { value: money(dto.total), note: `Range ${money(dto.low)} to ${money(dto.high)}` },
    dataThrough: dto.dataThrough ?? dto.snapshotDate,
    caveat: `${RANGE_NOTE} ${COMPONENTS_FOOTNOTE}`,
    empty: false,
    unit: 'usd',
  };
}
