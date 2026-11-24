import { apPeriodLabel } from '../../dashboard/dashboard-format';
import { NONE, WidgetData, limit, moneyExact, sharePercent, signedMoney, signedPercentUnits } from '../../saved-views/widget-data';
import { drillRoute } from '../drill-link';
import { CHART_BARS_MAX } from './figures';
import { CashContributionDto, ContributionRowDto } from './contribution.dto';

export const CONTRIBUTION_CAVEAT = 'Net cash is the cash posted in the month. Each row is its change from the month before, and the rows add up to the total change.';

/** What differs between the three contribution Metrics. */
export interface ContributionOptions {
  /** The column header and caption noun for a contributor: "Tail number", a Reporting Division Label, "Payer group". */
  readonly contributor: string;
  /** What the rows are called in a sentence, plural. */
  readonly plural: string;
  /** The label of the total row. */
  readonly totalLabel: string;
  /** Rows carry server-issued drill links to a payer breakdown. Each is read by the strict drill-link parser. */
  readonly drills?: boolean;
  /** A key for a row, so a payer keeps its colour on every chart. */
  readonly keyed?: boolean;
}

const rowName = (r: Pick<ContributionRowDto, 'name' | 'isOther'>): string => (r.isOther ? 'Other' : r.name);

/** The one mapping for the three contribution Metrics: a table of every contributor and the total, and a column chart of each one's change. */
export function fromContribution(dto: CashContributionDto, o: ContributionOptions): WidgetData {
  const periodNow = dto.period ? apPeriodLabel(dto.period) : 'this month';
  const periodBefore = dto.priorPeriod ? apPeriodLabel(dto.priorPeriod) : 'the month before';
  const caption = `Change in net cash by ${o.contributor.toLowerCase()}, ${periodNow} against ${periodBefore}. This month is the net cash of ${periodNow}`;
  if (dto.rows.length === 0 || (dto.total.netCash === 0 && dto.total.priorNetCash === 0)) {
    return NONE(caption);
  }
  const now = dto.period ? apPeriodLabel(dto.period) : 'This month';
  const before = dto.priorPeriod ? apPeriodLabel(dto.priorPeriod) : 'Month before';
  // The chart draws the contributors that moved cash most, with Other last. The table keeps every row.
  const shown = dto.rows.filter((r) => !r.isOther).slice(0, CHART_BARS_MAX);
  const other = dto.rows.find((r) => r.isOther);
  const bars = other ? [...shown, other] : shown;
  const drills = o.drills ? bars.map((r) => drillRoute(r.links, dto.group ?? '')) : [];
  return {
    summary: limit(`Net cash changed by ${signedMoney(dto.total.change)} from ${before} to ${now}, split across ${dto.rows.filter((r) => !r.isOther).length} ${o.plural}. The rows add up to the total change.`),
    table: {
      caption,
      columns: [o.contributor, 'Change', 'Share of change', 'This month'],
      rows: [
        ...dto.rows.map((r) => [rowName(r), signedMoney(r.change), sharePercent(r.shareOfChange), moneyExact(r.netCash)]),
        [o.totalLabel, `${signedMoney(dto.total.change)} (${signedPercentUnits(dto.total.changePercent)})`, dto.total.change === 0 ? 'n/a' : '100.0%', moneyExact(dto.total.netCash)],
      ],
    },
    categories: bars.map(rowName),
    ...(o.keyed ? { categoryKeys: bars.map((r) => (r.isOther ? 'other' : (r.key ?? 'not-recorded'))) } : {}),
    ...(drills.some((d) => d !== null) ? { drills } : {}),
    series: [{ name: `Change in net cash, ${before} to ${now}`, values: bars.map((r) => r.change) }],
    headline: null,
    dataThrough: dto.dataThrough ?? null,
    caveat: CONTRIBUTION_CAVEAT,
    empty: false,
    unit: 'usd',
  };
}
