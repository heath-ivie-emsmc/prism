import { NONE, WidgetData, limit, percent, whole } from '../../saved-views/widget-data';
import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { drillRoute } from '../drill-link';
import { PayerMixDto } from './payer-mix.dto';

export function fromPayerMix(dto: PayerMixDto): WidgetData {
  const caption = 'Transports by payer group';
  const total = dto.shares.reduce((sum, s) => sum + s.current, 0);
  const priorTotal = dto.shares.reduce((sum, s) => sum + s.prior, 0);
  if (dto.shares.length === 0 || total === 0) {
    return NONE(caption, 'count');
  }
  const share = (value: number, all: number): string => (all === 0 ? 'n/a' : percent(value / all));
  const top = [...dto.shares].sort((a, b) => b.current - a.current)[0]!;
  return {
    summary: limit(`Payer mix for ${apPeriodLabel(dto.period ?? '')}: ${top.name} is the largest group at ${share(top.current, total)} of ${whole(total)} transports.`),
    table: {
      caption,
      columns: ['Payer group', 'Transports', 'Share', 'Share the month before'],
      rows: dto.shares.map((s) => [s.name, whole(s.current), share(s.current, total), share(s.prior, priorTotal)]),
    },
    categories: dto.shares.map((s) => s.name),
    categoryKeys: dto.shares.map((s) => s.key),
    ...(dto.shares.some((s) => drillRoute(s.links, dto.group ?? '')) ? { drills: dto.shares.map((s) => drillRoute(s.links, dto.group ?? '')) } : {}),
    series: [
      { name: dto.priorPeriod ? apPeriodLabel(dto.priorPeriod) : 'Month before', values: dto.shares.map((s) => s.prior) },
      { name: dto.period ? apPeriodLabel(dto.period) : 'This month', values: dto.shares.map((s) => s.current) },
    ],
    headline: { value: share(top.current, total), note: `${top.name}, the largest group` },
    dataThrough: dto.dataThrough ?? null,
    caveat: '',
    empty: false,
    unit: 'count',
  };
}
