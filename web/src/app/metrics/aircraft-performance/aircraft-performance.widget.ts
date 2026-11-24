import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { NONE, WidgetData, limit, whole } from '../../saved-views/widget-data';
import { CHART_BARS_MAX, FIGURE_COLUMNS, NET_CASH_PER_TRANSPORT, figureCells } from '../shared/figures';
import { AircraftPerformanceDto } from './aircraft-performance.dto';

export const AIRCRAFT_PERFORMANCE_CAVEAT = `${NET_CASH_PER_TRANSPORT} is not an expected payment. The top 100 tail numbers are listed and the rest are in Other.`;

export function fromAircraftPerformance(dto: AircraftPerformanceDto): WidgetData {
  const caption = 'Transports, loaded miles and net cash by tail number';
  if (dto.aircraft.length === 0 || (dto.totals.transports === 0 && dto.totals.priorTransports === 0)) {
    return NONE(caption, 'count');
  }
  const now = dto.period ? apPeriodLabel(dto.period) : 'This month';
  const before = dto.priorPeriod ? apPeriodLabel(dto.priorPeriod) : 'Month before';
  // The chart draws the busiest tails. The table keeps every one.
  const shown = [...dto.aircraft].filter((a) => !a.isOther).sort((a, b) => b.figures.transports - a.figures.transports).slice(0, CHART_BARS_MAX);
  const other = dto.aircraft.find((a) => a.isOther);
  const bars = other ? [...shown, other] : shown;
  return {
    summary: limit(`Transports by tail number for ${now}: ${whole(dto.totals.transports)} across ${whole(dto.aircraft.filter((a) => !a.isOther).length)} aircraft. The chart shows the ${shown.length} busiest.`),
    table: {
      caption,
      columns: ['Tail number', ...FIGURE_COLUMNS],
      rows: [...dto.aircraft.map((a) => [a.isOther ? 'Other' : a.tail, ...figureCells(a.figures)]), ['All aircraft', ...figureCells(dto.totals)]],
    },
    categories: bars.map((a) => (a.isOther ? 'Other' : a.tail)),
    series: [
      { name: before, values: bars.map((a) => a.figures.priorTransports) },
      { name: now, values: bars.map((a) => a.figures.transports) },
    ],
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: AIRCRAFT_PERFORMANCE_CAVEAT,
    empty: false,
    unit: 'count',
  };
}
