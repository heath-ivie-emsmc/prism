import { NONE, WidgetData, limit, moneyExact } from '../../saved-views/widget-data';
import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { NetCashByGroupDto } from './net-cash-by-group.dto';

export function fromByGroup(dto: NetCashByGroupDto): WidgetData {
  const caption = 'Net cash by month and aircraft type or level of service';
  if (dto.points.length === 0) {
    return NONE(caption);
  }
  const periods = [...new Set(dto.points.map((p) => p.period))].sort();
  const groups = [...new Map(dto.points.map((p) => [p.groupKey, p.groupName])).entries()];
  const cash = (group: string, period: string): number | null => dto.points.find((p) => p.groupKey === group && p.period === period)?.cash ?? null;
  return {
    summary: limit(`Net cash by month for ${groups.length} groups, ${periods.length} months to ${apPeriodLabel(periods[periods.length - 1]!)}.`),
    table: {
      caption,
      columns: ['Month', ...groups.map(([, name]) => name)],
      rows: periods.map((period) => [apPeriodLabel(period), ...groups.map(([key]) => (cash(key, period) === null ? 'n/a' : moneyExact(cash(key, period)!)))]),
    },
    categories: periods.map(apPeriodLabel),
    series: groups.map(([key, name]) => ({ name, values: periods.map((period) => cash(key, period)) })),
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: '',
    empty: false,
    unit: 'usd',
  };
}
