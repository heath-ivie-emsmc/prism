import {
  CashPerTransportDto,
  CleanClaimRateDto,
  NetCashBreakdownDto,
  NetCashByGroupDto,
  NetCashProjectionDto,
  NetCollectionsDto,
  PayerMixDto,
  PaymentAgeDto,
  TransportVolumeDto,
} from '../metrics/net-collections.service';
import { apPeriodLabel, formatDate } from '../dashboard/dashboard-format';

/** What a widget needs to draw itself, whatever the Metric: a sentence for the chart's accessible name, the same numbers as a table, and series for the chart. */

export interface WidgetTable {
  readonly caption: string;
  readonly columns: readonly string[];
  /** Cells are already formatted text. The first cell of a row is its header. */
  readonly rows: readonly (readonly string[])[];
}

export interface WidgetSeries {
  readonly name: string;
  readonly values: readonly (number | null)[];
}

export interface WidgetData {
  /** One or two sentences, at most 250 characters: the chart's accessible name. */
  readonly summary: string;
  readonly table: WidgetTable;
  readonly categories: readonly string[];
  /** When each category is a payer group, its key, in the order of `categories`, so a payer keeps one colour on every chart. */
  readonly categoryKeys?: readonly string[];
  readonly series: readonly WidgetSeries[];
  /** The one figure a tile shows, with a short note under it. */
  readonly headline: { readonly value: string; readonly note: string } | null;
  /** The warehouse load the figures come from, yyyy-MM-dd, when the API says. */
  readonly dataThrough: string | null;
  readonly caveat: string;
  readonly empty: boolean;
  /** Formats a number in the unit of the series, for axis and value labels. */
  readonly unit: 'usd' | 'count' | 'percent';
}

export const SUMMARY_MAX = 250;

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
const usdCents = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
const count = new Intl.NumberFormat('en-US');

export const money = (value: number): string => usd.format(value);
export const moneyExact = (value: number): string => usdCents.format(value);
export const whole = (value: number): string => count.format(value);
export const percent = (ratio: number): string => `${(ratio * 100).toFixed(1)}%`;

/** A sentence kept under the accessible-name limit. */
export function limit(text: string): string {
  return text.length <= SUMMARY_MAX ? text : `${text.slice(0, SUMMARY_MAX - 1).trimEnd()}…`;
}

function monthLabel(label: string, year: number): string {
  return `${label} ${year}`;
}

function change(current: number, prior: number): string {
  if (prior === 0) {
    return 'n/a';
  }
  const ratio = (current - prior) / Math.abs(prior);
  return `${ratio >= 0 ? '+' : ''}${(ratio * 100).toFixed(1)}%`;
}

const NONE = (caption: string, unit: WidgetData['unit'] = 'usd'): WidgetData => ({
  summary: 'There is no data for this period.',
  table: { caption, columns: [], rows: [] },
  categories: [],
  series: [],
  headline: null,
  dataThrough: null,
  caveat: '',
  empty: true,
  unit,
});

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
    dataThrough: dto.snapshotDate,
    caveat: '',
    empty: false,
    unit: 'usd',
  };
}

export function fromProjection(dto: NetCashProjectionDto): WidgetData {
  const caption = 'Projected net cash by month';
  if (dto.months.length === 0) {
    return NONE(caption);
  }
  return {
    summary: limit(`Projected net cash for the next ${dto.months.length} months: ${money(dto.total)} in total, likely between ${money(dto.low)} and ${money(dto.high)}.`),
    table: {
      caption,
      columns: ['Month', 'Projected', 'Likely low', 'Likely high'],
      rows: dto.months.map((m) => [monthLabel(m.label, m.year), moneyExact(m.value), moneyExact(m.low), moneyExact(m.high)]),
    },
    categories: dto.months.map((m) => monthLabel(m.label, m.year)),
    series: [
      { name: 'Projected', values: dto.months.map((m) => m.value) },
      { name: 'Likely low', values: dto.months.map((m) => m.low) },
      { name: 'Likely high', values: dto.months.map((m) => m.high) },
    ],
    headline: { value: money(dto.total), note: `Likely ${money(dto.low)} to ${money(dto.high)}` },
    dataThrough: dto.snapshotDate,
    caveat: '',
    empty: false,
    unit: 'usd',
  };
}

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
    dataThrough: null,
    caveat: dto.periodOpen ? 'This month is still open, so counts may rise.' : '',
    empty: false,
    unit: 'count',
  };
}

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
    dataThrough: dto.snapshotDate,
    caveat: 'Covers all service lines: the data is not split by air or ground.',
    empty: false,
    unit: 'percent',
  };
}

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
    series: [
      { name: dto.priorPeriod ? apPeriodLabel(dto.priorPeriod) : 'Month before', values: dto.shares.map((s) => s.prior) },
      { name: dto.period ? apPeriodLabel(dto.period) : 'This month', values: dto.shares.map((s) => s.current) },
    ],
    headline: { value: share(top.current, total), note: `${top.name}, the largest group` },
    dataThrough: null,
    caveat: '',
    empty: false,
    unit: 'count',
  };
}

export function fromBreakdown(dto: NetCashBreakdownDto): WidgetData {
  const caption = 'Net cash by aircraft type or level of service';
  if (dto.groups.length === 0) {
    return NONE(caption);
  }
  const total = dto.groups.reduce((sum, g) => sum + g.current.cash, 0);
  return {
    summary: limit(`Net cash for ${apPeriodLabel(dto.period ?? '')} across ${dto.groups.length} groups: ${money(total)} in total.`),
    table: {
      caption,
      columns: ['Group', 'Net cash', 'Change on month before', 'Transports', 'Loaded miles'],
      rows: dto.groups.map((g) => [g.name, moneyExact(g.current.cash), change(g.current.cash, g.prior.cash), whole(g.current.transports), whole(Math.round(g.current.loadedMiles))]),
    },
    categories: dto.groups.map((g) => g.name),
    series: [
      { name: 'Month before', values: dto.groups.map((g) => g.prior.cash) },
      { name: 'This month', values: dto.groups.map((g) => g.current.cash) },
    ],
    headline: { value: money(total), note: apPeriodLabel(dto.period ?? '') },
    dataThrough: null,
    caveat: dto.periodOpen ? 'This month is still open, so figures are not final.' : '',
    empty: false,
    unit: 'usd',
  };
}

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
    dataThrough: null,
    caveat: '',
    empty: false,
    unit: 'usd',
  };
}

export function fromCashPerTransport(dto: CashPerTransportDto): WidgetData {
  const caption = 'Matured net cash per transport by payer group';
  const payers = dto.payers.filter((p) => p.transports > 0);
  if (payers.length === 0) {
    return NONE(caption);
  }
  return {
    summary: limit(`Matured net cash per transport across ${payers.length} payer groups, from ${money(Math.min(...payers.map((p) => p.cashPerTransport ?? 0)))} to ${money(Math.max(...payers.map((p) => p.cashPerTransport ?? 0)))}.`),
    table: {
      caption,
      columns: ['Payer group', 'Cash per transport', 'Transports', 'Net cash'],
      rows: dto.payers.map((p) => [p.name, p.cashPerTransport === null ? 'n/a' : moneyExact(p.cashPerTransport), whole(p.transports), moneyExact(p.cash)]),
    },
    categories: payers.map((p) => p.name),
    categoryKeys: payers.map((p) => p.key),
    series: [{ name: 'Cash per transport', values: payers.map((p) => p.cashPerTransport) }],
    headline: null,
    dataThrough: null,
    caveat: dto.cohortFrom && dto.cohortThrough ? `Transports from ${apPeriodLabel(dto.cohortFrom)} to ${apPeriodLabel(dto.cohortThrough)}.` : '',
    empty: false,
    unit: 'usd',
  };
}

export function fromPaymentAge(dto: PaymentAgeDto): WidgetData {
  const caption = 'Net cash by payer group and how old the transport was when paid';
  if (dto.cells.length === 0) {
    return NONE(caption);
  }
  const payers = [...new Map(dto.cells.map((c) => [c.payerKey, c.payerName])).entries()];
  const cashFor = (key: string): number => dto.cells.filter((c) => c.payerKey === key).reduce((sum, c) => sum + c.cash, 0);
  const meanAge = (key: string): string => {
    const cells = dto.cells.filter((c) => c.payerKey === key && c.cash > 0);
    const cash = cells.reduce((sum, c) => sum + c.cash, 0);
    return cash === 0 ? 'n/a' : `${(cells.reduce((sum, c) => sum + c.ageMonths * c.cash, 0) / cash).toFixed(1)} months`;
  };
  return {
    summary: limit(`Net cash by payer group over the last 12 months, with how long each took to pay. ${payers.length} payer groups.`),
    table: {
      caption,
      columns: ['Payer group', 'Net cash', 'Average age when paid'],
      rows: payers.map(([key, name]) => [name, moneyExact(cashFor(key)), meanAge(key)]),
    },
    categories: payers.map(([, name]) => name),
    categoryKeys: payers.map(([key]) => key),
    series: [{ name: 'Net cash', values: payers.map(([key]) => cashFor(key)) }],
    headline: null,
    dataThrough: null,
    caveat: '',
    empty: false,
    unit: 'usd',
  };
}

/** The date line under a widget: when the warehouse load the figures come from finished, if known. */
export function dataThroughText(data: Pick<WidgetData, 'dataThrough'>): string {
  return data.dataThrough ? `Data through ${formatDate(data.dataThrough)}` : '';
}
