import { NONE, WidgetData, change, limit, money, moneyExact, whole } from '../../saved-views/widget-data';
import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { drillRoute } from '../drill-link';
import { NetCashBreakdownDto } from './net-cash-breakdown.dto';

export function fromBreakdown(dto: NetCashBreakdownDto): WidgetData {
  const caption = 'Net cash by aircraft type or level of service';
  if (dto.groups.length === 0) {
    return NONE(caption);
  }
  const total = dto.groups.reduce((sum, g) => sum + g.current.cash, 0);
  return {
    summary: limit(`Net cash for ${apPeriodLabel(dto.period ?? '')} across ${dto.groups.length} groups: ${money(total)} in total.`),
    table: {
      caption,
      columns: ['Group', 'Net cash', 'Change on month before', 'Transports', 'Loaded miles'],
      rows: dto.groups.map((g) => [g.name, moneyExact(g.current.cash), change(g.current.cash, g.prior.cash), whole(g.current.transports), whole(Math.round(g.current.loadedMiles))]),
    },
    categories: dto.groups.map((g) => g.name),
    ...(dto.groups.some((g) => drillRoute(g.links)) ? { drills: dto.groups.map((g) => drillRoute(g.links)) } : {}),
    series: [
      { name: 'Month before', values: dto.groups.map((g) => g.prior.cash) },
      { name: 'This month', values: dto.groups.map((g) => g.current.cash) },
    ],
    headline: { value: money(total), note: apPeriodLabel(dto.period ?? '') },
    dataThrough: dto.dataThrough ?? null,
    caveat: dto.periodOpen ? 'This month is still open, so figures are not final.' : '',
    empty: false,
    unit: 'usd',
  };
}
