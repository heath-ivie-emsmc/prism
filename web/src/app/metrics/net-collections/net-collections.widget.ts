import { NONE, WidgetData, change, limit, money, moneyExact, monthLabel } from '../../saved-views/widget-data';
import { NetCollectionsDto } from './net-collections.dto';

export function fromNetCash(dto: NetCollectionsDto): WidgetData {
  const caption = 'Net cash collected by month';
  if (dto.points.length === 0) {
    return NONE(caption);
  }
  const last = dto.points[dto.points.length - 1]!;
  const prior = dto.points.length > 1 ? dto.points[dto.points.length - 2]! : null;
  return {
    summary: limit(`Net cash collected, ${dto.points.length} months to ${monthLabel(last.label, last.year)}. ${monthLabel(last.label, last.year)} was ${money(last.value)}${prior ? `, ${change(last.value, prior.value)} on ${prior.label}` : ''}.`),
    table: {
      caption,
      columns: ['Month', 'Net cash', 'Change on month before'],
      rows: dto.points.map((p, i) => [monthLabel(p.label, p.year), moneyExact(p.value), i === 0 ? 'n/a' : change(p.value, dto.points[i - 1]!.value)]),
    },
    categories: dto.points.map((p) => monthLabel(p.label, p.year)),
    series: [{ name: 'Net cash', values: dto.points.map((p) => p.value) }],
    headline: { value: money(last.value), note: prior ? `${change(last.value, prior.value)} on ${prior.label}` : monthLabel(last.label, last.year) },
    dataThrough: dto.dataThrough ?? dto.snapshotDate,
    caveat: '',
    empty: false,
    unit: 'usd',
  };
}
