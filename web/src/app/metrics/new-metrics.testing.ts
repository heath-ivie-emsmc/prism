import type { AircraftPerformanceDto } from './aircraft-performance/aircraft-performance.dto';
import type { BaseAnalysisDto } from './base-analysis/base-analysis.dto';
import type { LoadedMilesByAircraftDto } from './loaded-miles-by-aircraft/loaded-miles-by-aircraft.dto';
import type { PayerMixFinancialEffectDto } from './payer-mix-financial-effect/payer-mix-financial-effect.dto';
import type { CashContributionDto } from './shared/contribution.dto';
import type { FigureDto, PayerShareDto } from './shared/figures.dto';
import type { TransportMixByCallTypeDto } from './transport-mix-by-call-type/transport-mix-by-call-type.dto';
import type { TransportsByPickupLocationDto } from './transports-by-pickup-location/transports-by-pickup-location.dto';

/** Warehouse text that holds markup. It must reach the page as literal text. */
export const HOSTILE = '<img src=x onerror=alert(1)>';

const envelope = (key: string, name: string, over: Record<string, unknown> = {}) => ({
  metric: { key, name, description: 'd', unit: 'transports' },
  providerId: '526',
  period: '202610',
  priorPeriod: '202609',
  serviceLine: 'air' as const,
  group: null,
  division: null,
  divisionName: null,
  dataThrough: '2026-11-05',
  ...over,
});

export const figure = (transports: number, prior: number, miles: number, cash: number, over: Partial<FigureDto> = {}): FigureDto => ({
  transports,
  priorTransports: prior,
  changeTransports: transports - prior,
  changeTransportsPercent: prior === 0 ? null : Math.round(((transports - prior) / prior) * 1000) / 10,
  loadedMiles: miles,
  priorLoadedMiles: miles - 50,
  changeLoadedMiles: 50,
  changeLoadedMilesPercent: 2.5,
  netCash: cash,
  priorNetCash: cash - 1000,
  changeNetCash: 1000,
  changeNetCashPercent: 4.2,
  netCashPerTransport: transports === 0 ? null : Math.round((cash / transports) * 100) / 100,
  priorNetCashPerTransport: prior === 0 ? null : Math.round(((cash - 1000) / prior) * 100) / 100,
  ...over,
});

const payerMix = (): PayerShareDto[] => [
  { key: 'commercial', name: 'Commercial', transports: 6, priorTransports: 5, share: 0.6, priorShare: 0.5 },
  { key: 'medicare', name: 'Medicare', transports: 4, priorTransports: 5, share: 0.4, priorShare: 0.5 },
];

export const CALL_TYPES: TransportMixByCallTypeDto = {
  ...envelope('transport_mix_by_call_type', 'Transport mix by call type'),
  months: 6,
  totalTransports: 14,
  priorTotalTransports: 10,
  callTypes: [
    { key: 'scene', name: 'Scene', transports: 10, priorTransports: 5, changeTransports: 5, share: 10 / 14, priorShare: 0.5, changePoints: 21.4, loadedMiles: 900.4, priorLoadedMiles: 400, netCash: 5000, priorNetCash: 4000, payerMix: payerMix() },
    { key: 'interfacility', name: 'Interfacility', transports: 4, priorTransports: 5, changeTransports: -1, share: 4 / 14, priorShare: 0.5, changePoints: -21.4, loadedMiles: 500, priorLoadedMiles: 600, netCash: 3000, priorNetCash: 3500, payerMix: [] },
  ],
  trend: [
    { period: '202609', label: 'Sep', year: 2026, transports: 10, callTypes: [{ key: 'scene', name: 'Scene', transports: 5, share: 0.5 }, { key: 'interfacility', name: 'Interfacility', transports: 5, share: 0.5 }] },
    { period: '202610', label: 'Oct', year: 2026, transports: 14, callTypes: [{ key: 'scene', name: 'Scene', transports: 10, share: 10 / 14 }, { key: 'interfacility', name: 'Interfacility', transports: 4, share: 4 / 14 }] },
  ],
};

export const AIRCRAFT: AircraftPerformanceDto = {
  ...envelope('aircraft_performance', 'Aircraft performance'),
  totals: figure(30, 25, 4000, 20000),
  aircraft: [
    { tail: HOSTILE, isOther: false, figures: figure(20, 15, 2500, 12000) },
    { tail: 'N611CV', isOther: false, figures: figure(8, 10, 1200, 6000, { priorNetCashPerTransport: null }) },
    { tail: 'Other', isOther: true, figures: figure(2, 0, 300, 2000) },
  ],
};

export const BASES: BaseAnalysisDto = {
  ...envelope('base_analysis', 'Base analysis'),
  totals: figure(30, 25, 4000, 20000),
  divisions: [
    { key: 'north', name: 'North base', isOther: false, figures: figure(20, 15, 2500, 12000), aircraft: [{ tail: 'N611CV', isOther: false, transports: 12 }, { tail: 'Other', isOther: true, transports: 8 }] },
    { key: null, name: HOSTILE, isOther: false, figures: figure(10, 10, 1500, 8000), aircraft: [] },
  ],
};

const point = (period: string, label: string, miles: number) => ({ period, label, year: 2026, transports: 4, loadedMiles: miles, averageLoadedMiles: miles / 4 });
export const MILES: LoadedMilesByAircraftDto = {
  ...envelope('loaded_miles_by_aircraft', 'Loaded miles by aircraft', { priorPeriod: undefined }),
  months: 6,
  aircraft: [
    { tail: HOSTILE, isOther: false, transports: 8, loadedMiles: 1200, averageLoadedMiles: 150, trend: [point('202609', 'Sep', 400), point('202610', 'Oct', 800)] },
    { tail: 'N611CV', isOther: false, transports: 0, loadedMiles: 300, averageLoadedMiles: null, trend: [point('202609', 'Sep', 300)] },
  ],
};

export const EFFECT: PayerMixFinancialEffectDto = {
  ...envelope('payer_mix_financial_effect', 'Payer mix financial effect'),
  basis: 'expected',
  transports: 100,
  priorTransports: 100,
  cohorts: { firstServiceMonth: '202501', lastServiceMonth: '202512', cashThroughPeriod: '202611' },
  groups: [
    { key: 'commercial', name: 'Commercial', transports: 26, priorTransports: 30, share: 0.26, priorShare: 0.3, changePoints: -4, cashPerTransport: 900, effect: -3600 },
    { key: 'medicaid', name: 'Medicaid', transports: 23, priorTransports: 18, share: 0.23, priorShare: 0.18, changePoints: 5, cashPerTransport: null, effect: null },
  ],
  estimatedEffect: -3600,
  recurringMonthlyEffect: -3600,
};

export const PICKUPS: TransportsByPickupLocationDto = {
  ...envelope('transports_by_pickup_location', 'Transports by pickup location'),
  totalTransports: 14,
  priorTotalTransports: 10,
  sources: [
    { key: 'scene', name: 'Scene', facilityBased: false, transports: 8, priorTransports: 5, changeTransports: 3, share: 8 / 14, priorShare: 0.5, changePoints: 7.1, loadedMiles: 700, priorLoadedMiles: 500, netCash: 4000, priorNetCash: 3000, payerMix: payerMix() },
    { key: 'hospital', name: 'Hospital', facilityBased: true, transports: 6, priorTransports: 5, changeTransports: 1, share: 6 / 14, priorShare: 0.5, changePoints: -7.1, loadedMiles: 600, priorLoadedMiles: 500, netCash: 3000, priorNetCash: 2500, payerMix: [] },
  ],
  facilities: [
    { sourceKey: 'hospital', name: HOSTILE, isOther: false, transports: 4, priorTransports: 3, changeTransports: 1, share: 4 / 14, loadedMiles: 400 },
    { sourceKey: null, name: 'Other', isOther: true, transports: 2, priorTransports: 2, changeTransports: 0, share: 2 / 14, loadedMiles: 200 },
  ],
};

const row = (key: string | null, name: string, netCash: number, priorNetCash: number, total: number, over: Record<string, unknown> = {}) => ({
  key, name, isOther: false, netCash, priorNetCash, change: netCash - priorNetCash, shareOfChange: total === 0 ? null : (netCash - priorNetCash) / total, links: [], ...over,
});
const contribution = (key: string, name: string, rows: (total: number) => CashContributionDto['rows'], over: Record<string, unknown> = {}): CashContributionDto => {
  const total = 3000;
  const list = rows(total);
  return {
    ...envelope(key, name, over),
    total: { netCash: list.reduce((t, r) => t + r.netCash, 0), priorNetCash: list.reduce((t, r) => t + r.priorNetCash, 0), change: total, changePercent: 12.5 },
    rows: list,
  };
};

/** Rows add up exactly to a change of 3,000: 4,500 - 1,500 + 0. */
export const CONTRIB_AIRCRAFT = contribution('cash_contribution_by_aircraft', 'Cash contribution by aircraft', (t) => [
  row(null, HOSTILE, 9000, 4500, t), row(null, 'N611CV', 4000, 5500, t), row(null, 'Other', 1000, 1000, t, { isOther: true }),
]);
export const CONTRIB_DIVISION = contribution('cash_contribution_by_division', 'Cash contribution by reporting division', (t) => [
  row('north', 'North base', 9000, 4500, t), row(null, HOSTILE, 4000, 5500, t), row(null, 'Other', 1000, 1000, t, { isOther: true }),
]);
const payerLink = (key: string) => ({ rel: 'drill', href: `/api/v1/providers/526/metrics/net-collections/payer-mix/${key}?group=fixed-wing&period=202610&serviceLine=air`, title: `Payer mix detail: ${key}` });
export const CONTRIB_PAYER = contribution(
  'cash_contribution_by_payer',
  'Cash contribution by payer',
  (t) => [
    row('commercial', 'Commercial', 9000, 4500, t, { links: [payerLink('commercial')] }),
    row('medicare', 'Medicare', 4000, 5500, t, { links: [{ rel: 'drill', href: 'https://evil.example/payer-mix/medicare?group=fixed-wing', title: 'x' }] }),
    row('patient', 'Patient responsibility', 1000, 1000, t),
  ],
  { group: 'fixed-wing' },
);

/** The reply each new route gives, by route suffix, for the widget and metric view specs. */
export const NEW_REPLIES: Readonly<Record<string, { readonly key: string; readonly route: string; readonly reply: object; readonly airOnly: boolean }>> = {
  transport_mix_by_call_type: { key: 'transport_mix_by_call_type', route: 'transport-mix-by-call-type', reply: CALL_TYPES, airOnly: false },
  aircraft_performance: { key: 'aircraft_performance', route: 'aircraft-performance', reply: AIRCRAFT, airOnly: true },
  base_analysis: { key: 'base_analysis', route: 'base-analysis', reply: BASES, airOnly: false },
  loaded_miles_by_aircraft: { key: 'loaded_miles_by_aircraft', route: 'loaded-miles-by-aircraft', reply: MILES, airOnly: true },
  cash_contribution_by_aircraft: { key: 'cash_contribution_by_aircraft', route: 'cash-contribution/aircraft', reply: CONTRIB_AIRCRAFT, airOnly: true },
  cash_contribution_by_division: { key: 'cash_contribution_by_division', route: 'cash-contribution/division', reply: CONTRIB_DIVISION, airOnly: false },
  cash_contribution_by_payer: { key: 'cash_contribution_by_payer', route: 'cash-contribution/payer', reply: CONTRIB_PAYER, airOnly: false },
  payer_mix_financial_effect: { key: 'payer_mix_financial_effect', route: 'payer-mix-financial-effect', reply: EFFECT, airOnly: false },
  transports_by_pickup_location: { key: 'transports_by_pickup_location', route: 'transports-by-pickup-location', reply: PICKUPS, airOnly: false },
};
