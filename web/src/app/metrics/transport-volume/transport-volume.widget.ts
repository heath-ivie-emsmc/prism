import { NONE, WidgetData, change, limit, monthLabel, whole } from '../../saved-views/widget-data';
import { TransportVolumeDto } from './transport-volume.dto';

export function fromTransports(dto: TransportVolumeDto): WidgetData {
  const caption = 'Transports and loaded miles by month';
  if (dto.points.length === 0) {
    return NONE(caption, 'count');
  }
  const last = dto.points[dto.points.length - 1]!;
  const prior = dto.points.length > 1 ? dto.points[dto.points.length - 2]! : null;
  return {
    summary: limit(`Transports, ${dto.points.length} months to ${monthLabel(last.label, last.year)}. ${monthLabel(last.label, last.year)} had ${whole(last.transports)}${prior ? `, ${change(last.transports, prior.transports)} on ${prior.label}` : ''}.`),
    table: {
      caption,
      columns: ['Month', 'Transports', 'Loaded miles (statute)'],
      rows: dto.points.map((p) => [monthLabel(p.label, p.year), whole(p.transports), whole(Math.round(p.loadedMiles))]),
    },
    categories: dto.points.map((p) => monthLabel(p.label, p.year)),
    series: [{ name: 'Transports', values: dto.points.map((p) => p.transports) }],
    headline: { value: whole(last.transports), note: prior ? `${change(last.transports, prior.transports)} on ${prior.label}` : monthLabel(last.label, last.year) },
    dataThrough: dto.dataThrough ?? null,
    caveat: dto.periodOpen ? 'This month is still open, so counts may rise.' : '',
    empty: false,
    unit: 'count',
  };
}
