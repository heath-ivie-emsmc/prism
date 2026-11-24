import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { NONE, WidgetData, limit, miles, orNa, whole } from '../../saved-views/widget-data';
import { CHART_BARS_MAX, CHART_SERIES_MAX } from '../shared/figures';
import { LoadedMilesByAircraftDto } from './loaded-miles-by-aircraft.dto';

export const LOADED_MILES_CAVEAT = 'Loaded miles are statute miles, counted by date of service. The chart shows the aircraft with the most miles; the table lists them all.';

const average = (v: number | null): string => orNa(v, (x) => x.toFixed(1));

export function fromLoadedMilesByAircraft(dto: LoadedMilesByAircraftDto): WidgetData {
  const caption = 'Loaded miles by tail number (statute)';
  const tails = dto.aircraft.filter((a) => !a.isOther);
  const other = dto.aircraft.find((a) => a.isOther);
  const total = dto.aircraft.reduce((t, a) => t + a.loadedMiles, 0);
  if (dto.aircraft.length === 0 || total === 0) {
    return NONE(caption, 'count');
  }
  const byMiles = [...tails].sort((a, b) => b.loadedMiles - a.loadedMiles);
  const bars = other ? [...byMiles.slice(0, CHART_BARS_MAX), other] : byMiles.slice(0, CHART_BARS_MAX);
  const lines = byMiles.slice(0, CHART_SERIES_MAX);
  const months = [...new Map(dto.aircraft.flatMap((a) => a.trend.map((p) => [p.period, `${p.label} ${p.year}`] as const))).entries()].sort(([a], [b]) => a.localeCompare(b));
  const tailName = (a: { tail: string; isOther: boolean }): string => (a.isOther ? 'Other' : a.tail);
  const milesIn = (a: LoadedMilesByAircraftDto['aircraft'][number], period: string): number | null => a.trend.find((p) => p.period === period)?.loadedMiles ?? null;
  const when = dto.period ? apPeriodLabel(dto.period) : 'the month chosen';
  return {
    summary: limit(`Loaded miles by tail number, ${dto.months} months to ${when}: ${miles(total)} statute miles across ${whole(tails.length)} aircraft. The chart shows the ${Math.min(bars.length, CHART_BARS_MAX)} with the most miles.`),
    table: {
      caption,
      columns: ['Tail number', 'Transports', 'Loaded miles (statute)', 'Average loaded miles per transport'],
      rows: dto.aircraft.map((a) => [tailName(a), whole(a.transports), miles(a.loadedMiles), average(a.averageLoadedMiles)]),
    },
    extraTables:
      months.length === 0
        ? []
        : [
            {
              caption: `Loaded miles (statute) by tail number, month by month (${dto.months} months)`,
              columns: ['Tail number', ...months.map(([, label]) => label)],
              rows: dto.aircraft.map((a) => [tailName(a), ...months.map(([period]) => orNa(milesIn(a, period), miles))]),
            },
          ],
    categories: bars.map(tailName),
    series: [{ name: 'Loaded miles (statute)', values: bars.map((a) => a.loadedMiles) }],
    trend: { categories: months.map(([, label]) => label), series: lines.map((a) => ({ name: tailName(a), values: months.map(([period]) => milesIn(a, period)) })) },
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: LOADED_MILES_CAVEAT,
    empty: false,
    unit: 'count',
  };
}
