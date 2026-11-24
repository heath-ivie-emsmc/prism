import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { NONE, WidgetData, limit, miles, moneyExact, sharePercent, signedPoints, signedWhole, whole } from '../../saved-views/widget-data';
import { payerMixTable } from '../shared/figures';
import { TransportsByPickupLocationDto } from './transports-by-pickup-location.dto';

export const PICKUP_CAVEAT = 'Pickup types follow the origin modifier on the claim. Transports and miles are counted by date of service. Net cash is the cash posted in the month, a separate figure.';

export function fromTransportsByPickupLocation(dto: TransportsByPickupLocationDto): WidgetData {
  const caption = 'Transports by pickup location';
  const sources = dto.sources.filter((s) => s.transports > 0 || s.priorTransports > 0);
  if (dto.totalTransports === 0 && dto.priorTotalTransports === 0) {
    return NONE(caption, 'count');
  }
  const now = dto.period ? apPeriodLabel(dto.period) : 'This month';
  const before = dto.priorPeriod ? apPeriodLabel(dto.priorPeriod) : 'Month before';
  const top = [...sources].sort((a, b) => b.transports - a.transports)[0];
  const sourceName = (key: string | null): string => (key === null ? 'Not grouped' : (dto.sources.find((s) => s.key === key)?.name ?? 'Other'));
  return {
    summary: limit(`Transports by pickup location for ${now}: ${whole(dto.totalTransports)} in all${top ? `, most from ${top.name.toLowerCase()} at ${sharePercent(top.share)}` : ''}.`),
    table: {
      caption,
      columns: ['Pickup type', `Transports ${now}`, `Transports ${before}`, 'Change', 'Share', `Share ${before}`, 'Change in share', 'Loaded miles (statute)', 'Net cash posted in the month', `Net cash posted ${before}`],
      rows: [
        ...dto.sources.map((s) => [s.name, whole(s.transports), whole(s.priorTransports), signedWhole(s.changeTransports), sharePercent(s.share), sharePercent(s.priorShare), signedPoints(s.changePoints), miles(s.loadedMiles), moneyExact(s.netCash), moneyExact(s.priorNetCash)]),
        ['All pickup types', whole(dto.totalTransports), whole(dto.priorTotalTransports), signedWhole(dto.totalTransports - dto.priorTotalTransports), '', '', '', miles(dto.sources.reduce((t, s) => t + s.loadedMiles, 0)), moneyExact(dto.sources.reduce((t, s) => t + s.netCash, 0)), moneyExact(dto.sources.reduce((t, s) => t + s.priorNetCash, 0))],
      ],
    },
    extraTables: [
      ...(dto.facilities.length === 0
        ? []
        : [
            {
              caption: `Facilities, ${now}`,
              columns: ['Facility', 'Pickup type', `Transports ${now}`, `Transports ${before}`, 'Change', 'Share', 'Loaded miles (statute)'],
              rows: dto.facilities.map((f) => [f.isOther ? 'Other facilities' : f.name, sourceName(f.sourceKey), whole(f.transports), whole(f.priorTransports), signedWhole(f.changeTransports), sharePercent(f.share), miles(f.loadedMiles)]),
            },
          ]),
      payerMixTable(`Payer mix within each pickup type, ${now}`, 'Pickup type', dto.sources, before),
    ],
    categories: sources.map((s) => s.name),
    series: [
      { name: before, values: sources.map((s) => s.priorTransports) },
      { name: now, values: sources.map((s) => s.transports) },
    ],
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: PICKUP_CAVEAT,
    empty: false,
    unit: 'count',
  };
}
