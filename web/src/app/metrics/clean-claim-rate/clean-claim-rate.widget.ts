import { NONE, WidgetData, limit, monthLabel, percent, whole } from '../../saved-views/widget-data';
import { CleanClaimRateDto } from './clean-claim-rate.dto';

export function fromCleanClaims(dto: CleanClaimRateDto): WidgetData {
  const caption = 'Clean claim rate by month of service';
  const points = dto.points.filter((p) => p.rate !== null);
  if (points.length === 0) {
    return NONE(caption, 'percent');
  }
  const last = points[points.length - 1]!;
  return {
    summary: limit(`Clean claim rate, ${points.length} months to ${monthLabel(last.label, last.year)}. ${monthLabel(last.label, last.year)} was ${percent(last.rate!)}.`),
    table: {
      caption,
      columns: ['Month', 'Clean claim rate', 'Clean claims', 'Rejected claims', 'All claims'],
      rows: dto.points.map((p) => [monthLabel(p.label, p.year), p.rate === null ? 'n/a' : percent(p.rate), whole(p.cleanClaims), whole(p.rejectedClaims), whole(p.totalClaims)]),
    },
    categories: dto.points.map((p) => monthLabel(p.label, p.year)),
    series: [{ name: 'Clean claim rate', values: dto.points.map((p) => (p.rate === null ? null : p.rate * 100)) }],
    headline: { value: percent(last.rate!), note: 'First-pass resolution' },
    dataThrough: dto.dataThrough ?? dto.snapshotDate,
    caveat: 'Covers all service lines: the data is not split by air or ground.',
    empty: false,
    unit: 'percent',
  };
}
