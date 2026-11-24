import { FigureDto, PayerShareDto } from './figures.dto';
import { WidgetTable, miles, moneyExact, orNa, sharePercent, signedPercentUnits, signedWhole, whole } from '../../saved-views/widget-data';

/** The phrase every Metric that uses posting-month cash must carry, so nobody reads it as an expected payment. */
export const NET_CASH_PER_TRANSPORT = 'Net cash per transport (cash posted in the month ÷ transports)';

/** Where a month trend of one kind of figure is cut in the chart: more series than this are unreadable. The table keeps all of them. */
export const CHART_SERIES_MAX = 6;
/** The most bars a column chart draws. The table keeps every row. */
export const CHART_BARS_MAX = 15;

/** A table of payer mix within each slice, for the table presentation. A slice with no transports adds no rows. */
export function payerMixTable(caption: string, sliceHeading: string, slices: readonly { readonly name: string; readonly payerMix: readonly PayerShareDto[] }[], priorLabel: string): WidgetTable {
  return {
    caption,
    columns: [sliceHeading, 'Payer group', 'Transports', 'Share', `Share ${priorLabel}`],
    rows: slices.flatMap((slice) => slice.payerMix.filter((p) => p.transports > 0 || p.priorTransports > 0).map((p) => [slice.name, p.name, whole(p.transports), sharePercent(p.share), sharePercent(p.priorShare)])),
  };
}

/** The columns of a figure row, after the row's own name: transports, miles and net cash with their changes, and net cash per transport. */
export const FIGURE_COLUMNS = [
  'Transports', 'Transports the month before', 'Change', 'Change %',
  'Loaded miles (statute)', 'Change in miles %',
  'Net cash posted in the month', 'Change in net cash %',
  NET_CASH_PER_TRANSPORT, `${NET_CASH_PER_TRANSPORT}, the month before`,
] as const;

/** The cells for one figure row, in the order of FIGURE_COLUMNS. */
export function figureCells(f: FigureDto): string[] {
  return [
    whole(f.transports), whole(f.priorTransports), signedWhole(f.changeTransports), signedPercentUnits(f.changeTransportsPercent),
    miles(f.loadedMiles), signedPercentUnits(f.changeLoadedMilesPercent),
    moneyExact(f.netCash), signedPercentUnits(f.changeNetCashPercent),
    orNa(f.netCashPerTransport, moneyExact), orNa(f.priorNetCashPerTransport, moneyExact),
  ];
}
