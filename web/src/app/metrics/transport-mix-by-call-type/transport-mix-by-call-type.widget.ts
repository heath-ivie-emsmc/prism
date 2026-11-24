import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { NONE, WidgetData, WidgetSeries, limit, miles, moneyExact, sharePercent, signedPoints, signedWhole, whole } from '../../saved-views/widget-data';
import { payerMixTable } from '../shared/figures';
import { TransportMixByCallTypeDto } from './transport-mix-by-call-type.dto';

/** Counts and miles are by the date of service. Cash is a different clock, so the widget says so under the figures. */
export const CALL_TYPE_CAVEAT = 'Transports and miles are counted by date of service. Net cash is the cash posted in the month, so it is a separate figure. Expected payment is not available here.';

export function fromTransportMixByCallType(dto: TransportMixByCallTypeDto): WidgetData {
  const caption = 'Transports by call type';
  const callTypes = dto.callTypes.filter((c) => c.transports > 0 || c.priorTransports > 0);
  if (dto.totalTransports === 0 && dto.priorTotalTransports === 0) {
    return NONE(caption, 'count');
  }
  const now = dto.period ? apPeriodLabel(dto.period) : 'This month';
  const before = dto.priorPeriod ? apPeriodLabel(dto.priorPeriod) : 'Month before';
  const top = [...callTypes].sort((a, b) => b.transports - a.transports)[0];
  const trendTypes = [...new Map(dto.trend.flatMap((p) => p.callTypes.map((c) => [c.key, c.name] as const))).entries()];
  const trendSeries: WidgetSeries[] = trendTypes.map(([key, name]) => ({ name, values: dto.trend.map((p) => p.callTypes.find((c) => c.key === key)?.transports ?? 0) }));
  return {
    summary: limit(`Transports by call type for ${now}: ${whole(dto.totalTransports)} in all${top ? `, most of them ${top.name.toLowerCase()} at ${sharePercent(top.share)}` : ''}.`),
    table: {
      caption,
      columns: ['Call type', `Transports ${now}`, `Transports ${before}`, 'Change', 'Share', `Share ${before}`, 'Change in share', 'Loaded miles (statute)', 'Net cash posted in the month', `Net cash posted ${before}`],
      rows: [
        ...dto.callTypes.map((c) => [c.name, whole(c.transports), whole(c.priorTransports), signedWhole(c.changeTransports), sharePercent(c.share), sharePercent(c.priorShare), signedPoints(c.changePoints), miles(c.loadedMiles), moneyExact(c.netCash), moneyExact(c.priorNetCash)]),
        ['All call types', whole(dto.totalTransports), whole(dto.priorTotalTransports), signedWhole(dto.totalTransports - dto.priorTotalTransports), '', '', '', miles(dto.callTypes.reduce((t, c) => t + c.loadedMiles, 0)), moneyExact(dto.callTypes.reduce((t, c) => t + c.netCash, 0)), moneyExact(dto.callTypes.reduce((t, c) => t + c.priorNetCash, 0))],
      ],
    },
    extraTables: [
      payerMixTable(`Payer mix within each call type, ${now}`, 'Call type', dto.callTypes, before),
      {
        caption: `Transports by call type, month by month (${dto.months} months)`,
        columns: ['Month', ...trendTypes.map(([, name]) => name), 'All call types'],
        rows: dto.trend.map((p) => [`${p.label} ${p.year}`, ...trendTypes.map(([key]) => whole(p.callTypes.find((c) => c.key === key)?.transports ?? 0)), whole(p.transports)]),
      },
    ],
    categories: callTypes.map((c) => c.name),
    series: [
      { name: before, values: callTypes.map((c) => c.priorTransports) },
      { name: now, values: callTypes.map((c) => c.transports) },
    ],
    trend: { categories: dto.trend.map((p) => `${p.label} ${p.year}`), series: trendSeries },
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: CALL_TYPE_CAVEAT,
    empty: false,
    unit: 'count',
  };
}

