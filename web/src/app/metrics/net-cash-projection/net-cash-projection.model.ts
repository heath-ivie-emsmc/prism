import { moneyExact, signedMoney, signedPercentUnits, monthLabel } from '../../saved-views/widget-data';
import { shortMonthYear } from '../../shared/data-through';
import { NetCashProjectionDto, ProjectionChangeDto } from './net-cash-projection.dto';

/** The database team's names for the two parts of the forecast. They are not "claims already billed": a trip that already existed may not be billed yet. */
export const EXISTING_TRIPS_LABEL = 'Expected collections on trips that already existed';
export const FUTURE_TRIPS_LABEL = 'Assumed run rate of trips that do not exist yet';

/** Said wherever the two parts are shown, because the warehouse's parts do not reconcile to its total yet. */
export const COMPONENTS_FOOTNOTE = 'The two parts are shown as reported by the warehouse and may not add up to the expected total.';

/** Said beside every low and high: the range is the spread of the warehouse's own forecast parts. */
export const RANGE_NOTE = 'The range is across the forecast components, not a statistical interval.';

export const HORIZONS = [1, 2, 3] as const;
export type Horizon = (typeof HORIZONS)[number];

/** One projected month as text, ready to print. Nothing here is a number the template has to format. */
export interface ProjectionMonthView {
  readonly key: string;
  readonly label: string;
  readonly expected: string;
  /** "+$12,000.00 (+1.0%)", or "n/a" when there is no month to compare with. */
  readonly change: string;
  /** What the change is measured against: "Oct 2026 actual" for the first month, the month before for the others. */
  readonly changeBase: string;
  readonly range: string;
  readonly cumulative: string;
  readonly cumulativeRange: string;
  readonly existing: string;
  readonly existingShare: string;
  readonly future: string;
  readonly futureShare: string;
}

export interface ProjectionView {
  readonly months: readonly ProjectionMonthView[];
  readonly horizonMonths: number;
  readonly expected: string;
  readonly range: string;
  readonly existing: string;
  readonly existingShare: string;
  readonly future: string;
  readonly futureShare: string;
  /** The month the first projected month is compared with ("Oct 2026"), or an empty string when there is none. */
  readonly latestActualMonth: string;
  readonly latestActual: string;
}

const NA = 'n/a';

/** A dollar amount, or n/a when the warehouse has none for it. */
export const amountOrNa = (value: number | null): string => (value === null ? NA : moneyExact(value));

/** A share of the expected total as a percent. It can pass 100% because the two parts are not reconciled to the total. Null is n/a. */
export const shareOrNa = (share: number | null): string => (share === null ? NA : `${(share * 100).toFixed(1)}%`);

export const rangeText = (low: number, high: number): string => `${moneyExact(low)} to ${moneyExact(high)}`;

export function changeText(change: ProjectionChangeDto | null): string {
  if (!change) return NA;
  return change.percent === null ? signedMoney(change.amount) : `${signedMoney(change.amount)} (${signedPercentUnits(change.percent)})`;
}

/** The dashboard-ready view of a projection response. The shares are separate figures and are never turned into a stacked 100% bar. */
export function toProjectionView(dto: NetCashProjectionDto): ProjectionView {
  const actual = dto.latestActual;
  const actualLabel = actual ? shortMonthYear(actual.period) : '';
  return {
    horizonMonths: dto.horizon.months,
    expected: moneyExact(dto.horizon.value),
    range: rangeText(dto.horizon.low, dto.horizon.high),
    existing: amountOrNa(dto.horizon.fromExistingTrips),
    existingShare: shareOrNa(dto.horizon.existingTripsShare),
    future: amountOrNa(dto.horizon.fromFutureTrips),
    futureShare: shareOrNa(dto.horizon.futureTripsShare),
    latestActualMonth: actual && actualLabel ? actualLabel : '',
    latestActual: actual && actualLabel ? moneyExact(actual.value) : '',
    months: dto.months.map((m, i) => ({
      key: m.period,
      label: monthLabel(m.label, m.year),
      expected: moneyExact(m.value),
      change: changeText(m.changeFromPrevious),
      changeBase: i === 0 ? (actualLabel ? `${actualLabel} actual` : 'No actual month to compare with') : `${dto.months[i - 1]!.label} ${dto.months[i - 1]!.year}`,
      range: rangeText(m.low, m.high),
      cumulative: moneyExact(m.cumulative.value),
      cumulativeRange: rangeText(m.cumulative.low, m.cumulative.high),
      existing: amountOrNa(m.fromExistingTrips),
      existingShare: shareOrNa(m.existingTripsShare),
      future: amountOrNa(m.fromFutureTrips),
      futureShare: shareOrNa(m.futureTripsShare),
    })),
  };
}
