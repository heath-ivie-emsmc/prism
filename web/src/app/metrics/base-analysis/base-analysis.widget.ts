import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { defaultLabels } from '../../dashboard/service-line';
import { NONE, WidgetData, limit, whole } from '../../saved-views/widget-data';
import { CHART_BARS_MAX, FIGURE_COLUMNS, NET_CASH_PER_TRANSPORT, figureCells } from '../shared/figures';
import { BaseAnalysisDto } from './base-analysis.dto';

export const BASE_ANALYSIS_CAVEAT = `${NET_CASH_PER_TRANSPORT} is not an expected payment.`;

/** The Label for the Reporting Division in the line the response covers: Air Providers call it a base location. The term is never written as a literal. */
export function divisionLabel(dto: Pick<BaseAnalysisDto, 'serviceLine'>): string {
  return defaultLabels(dto.serviceLine === 'air' ? 'air' : 'both').reportingDivision;
}

export function fromBaseAnalysis(dto: BaseAnalysisDto): WidgetData {
  const label = divisionLabel(dto);
  const caption = `Transports, loaded miles and net cash by ${label.toLowerCase()}`;
  if (dto.divisions.length === 0 || (dto.totals.transports === 0 && dto.totals.priorTransports === 0)) {
    return NONE(caption, 'count');
  }
  const now = dto.period ? apPeriodLabel(dto.period) : 'This month';
  const before = dto.priorPeriod ? apPeriodLabel(dto.priorPeriod) : 'Month before';
  const name = (d: { name: string; isOther: boolean }): string => (d.isOther ? 'Other' : d.name);
  const shown = [...dto.divisions].filter((d) => !d.isOther).sort((a, b) => b.figures.transports - a.figures.transports).slice(0, CHART_BARS_MAX);
  const other = dto.divisions.find((d) => d.isOther);
  const bars = other ? [...shown, other] : shown;
  const aircraftRows = dto.divisions.flatMap((d) => d.aircraft.map((a) => [name(d), a.isOther ? 'Other' : a.tail, whole(a.transports)]));
  return {
    summary: limit(`Transports by ${label.toLowerCase()} for ${now}: ${whole(dto.totals.transports)} across ${whole(dto.divisions.filter((d) => !d.isOther).length)} ${label.toLowerCase()} rows.`),
    table: {
      caption,
      columns: [label, ...FIGURE_COLUMNS],
      rows: [...dto.divisions.map((d) => [name(d), ...figureCells(d.figures)]), [`All ${label.toLowerCase()} rows`, ...figureCells(dto.totals)]],
    },
    extraTables:
      aircraftRows.length === 0
        ? []
        : [{ caption: `Aircraft that flew from each ${label.toLowerCase()}, ${now}`, columns: [label, 'Tail number', 'Transports'], rows: aircraftRows }],
    categories: bars.map(name),
    series: [
      { name: before, values: bars.map((d) => d.figures.priorTransports) },
      { name: now, values: bars.map((d) => d.figures.transports) },
    ],
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: BASE_ANALYSIS_CAVEAT,
    empty: false,
    unit: 'count',
  };
}
