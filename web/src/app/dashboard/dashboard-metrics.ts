import { ServiceLineSelection } from './service-line';

export interface MetricBreakdownRow {
  readonly label: string;
  readonly value: string;
}

/** How a Metric shows on a card. Mocked until the Metric Catalog serves real values. */
export interface MetricCardModel {
  /** Stable Metric key. Never changes with a Label. */
  readonly key: string;
  readonly title: string;
  readonly value: string;
  readonly breakdown: readonly MetricBreakdownRow[];
  /** Expected end-of-period value, for Metrics that have one. */
  readonly projection?: { readonly label: string; readonly value: string; readonly change: string };
}

const wholeNumber = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
const dollars = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });

interface Totals {
  readonly cash: number;
  readonly projectedCash: number;
  readonly rotorWing: number;
  readonly fixedWing: number;
  readonly groundTransports: number;
  readonly airStatuteMiles: number;
  readonly groundStatuteMiles: number;
}

const MOCK: Totals = {
  cash: 1_284_500,
  projectedCash: 1_402_000,
  rotorWing: 96,
  fixedWing: 46,
  groundTransports: 318,
  airStatuteMiles: 18_420,
  groundStatuteMiles: 5_230,
};

const NAUTICAL_PER_STATUTE = 0.868976;

function change(projected: number, actual: number): string {
  const percent = Math.round(((projected - actual) / actual) * 100);
  return `${percent >= 0 ? '+' : ''}${percent}% vs. so far`;
}

/** Mock cards for a Service Line choice. Air carries the rotor and fixed wing split and nautical miles. Ground has neither. */
export function mockMetricCards(selection: ServiceLineSelection): readonly MetricCardModel[] {
  const air = selection !== 'ground';
  const ground = selection !== 'air';
  // Ground figures are illustrative: only the shape of the air cards comes from the mockup.
  const cash = (air ? MOCK.cash : 0) + (ground ? 612_300 : 0);
  const projected = (air ? MOCK.projectedCash : 0) + (ground ? 668_000 : 0);
  const transports = (air ? MOCK.rotorWing + MOCK.fixedWing : 0) + (ground ? MOCK.groundTransports : 0);
  const statuteMiles = (air ? MOCK.airStatuteMiles : 0) + (ground ? MOCK.groundStatuteMiles : 0);

  const transportBreakdown: MetricBreakdownRow[] =
    selection === 'air'
      ? [
          { label: 'Rotor wing', value: wholeNumber.format(MOCK.rotorWing) },
          { label: 'Fixed wing', value: wholeNumber.format(MOCK.fixedWing) },
        ]
      : selection === 'both'
        ? [
            { label: 'Air', value: wholeNumber.format(MOCK.rotorWing + MOCK.fixedWing) },
            { label: 'Ground', value: wholeNumber.format(MOCK.groundTransports) },
          ]
        : [];

  const milesBreakdown: MetricBreakdownRow[] = air
    ? [
        { label: 'Statute', value: wholeNumber.format(statuteMiles) },
        { label: 'Nautical (air only)', value: wholeNumber.format(MOCK.airStatuteMiles * NAUTICAL_PER_STATUTE) },
      ]
    : [];

  return [
    {
      key: 'net_cash_collected',
      title: 'Net cash collected',
      value: dollars.format(cash),
      breakdown: [],
      projection: { label: 'Projected for the period', value: dollars.format(projected), change: change(projected, cash) },
    },
    { key: 'transports', title: 'Transports', value: wholeNumber.format(transports), breakdown: transportBreakdown },
    { key: 'loaded_miles', title: 'Loaded miles', value: wholeNumber.format(statuteMiles), breakdown: milesBreakdown },
  ];
}
