import { describe, expect, it } from 'vitest';
import { fromCashPerTransport, cohortLine } from './cash-per-transport-by-payer/cash-per-transport-by-payer.widget';
import { CashPerTransportDto } from './cash-per-transport-by-payer/cash-per-transport-by-payer.dto';
import { NetCashProjectionDto } from './net-cash-projection/net-cash-projection.dto';
import { COMPONENTS_FOOTNOTE, EXISTING_TRIPS_LABEL, FUTURE_TRIPS_LABEL, toProjectionView } from './net-cash-projection/net-cash-projection.model';
import { fromProjection } from './net-cash-projection/net-cash-projection.widget';
import { fromPaymentAge, timingTable } from './payment-age/payment-age.widget';
import { PaymentAgeDto } from './payment-age/payment-age.dto';
import { fromTransports } from './transport-volume/transport-volume.widget';
import { shortMonthYear } from '../shared/data-through';

const METRIC = { key: 'k', name: 'n', description: 'd', unit: 'u' };

/** Components of 130 + 190 against a total of 150: the warehouse's parts do not add up, and the shares add to more than 100%. */
const PROJECTION: NetCashProjectionDto = {
  metric: METRIC, providerId: '1', afterPeriod: '202610', serviceLine: 'air', snapshotDate: '2026-11-05', total: 150, low: 130, high: 190,
  months: [
    { period: '202611', label: 'Nov', year: 2026, value: 150, low: 130, high: 190, changeFromPrevious: null, cumulative: { value: 150, low: 130, high: 190 }, fromExistingTrips: 130, fromFutureTrips: 190, existingTripsShare: 0.8667, futureTripsShare: 1.2667 },
    { period: '202612', label: 'Dec', year: 2026, value: 100, low: 90, high: 120, changeFromPrevious: { amount: -50, percent: -33.3 }, cumulative: { value: 250, low: 220, high: 310 }, fromExistingTrips: null, fromFutureTrips: 90, existingTripsShare: null, futureTripsShare: 0.9 },
  ],
  horizon: { value: 250, low: 220, high: 310, months: 2, fromExistingTrips: 130, fromFutureTrips: 280, existingTripsShare: 0.52, futureTripsShare: 1.12 },
  latestActual: null,
};

describe('projection view', () => {
  it('shows every share as its own figure, so shares above 100% are kept and not rescaled', () => {
    const view = toProjectionView(PROJECTION);

    expect(view.months[0]).toMatchObject({ existingShare: '86.7%', futureShare: '126.7%', existing: '$130.00', future: '$190.00' });
    expect(view.futureShare).toBe('112.0%');
  });

  it('says n/a for a missing component or share, and no change when there is nothing to compare with', () => {
    const view = toProjectionView(PROJECTION);

    expect(view.months[0]!.change).toBe('n/a');
    expect(view.months[0]!.changeBase).toBe('No actual month to compare with');
    expect(view.months[1]).toMatchObject({ existing: 'n/a', existingShare: 'n/a', change: '-$50.00 (-33.3%)', changeBase: 'Nov 2026' });
    expect(view.latestActual).toBe('');
  });

  it('compares the first month with the latest actual month, with the sign in the text', () => {
    const view = toProjectionView({
      ...PROJECTION,
      latestActual: { period: '202610', value: 100 },
      months: [{ ...PROJECTION.months[0]!, changeFromPrevious: { amount: 50, percent: null } }],
    });

    expect(view.months[0]!.change).toBe('+$50.00');
    expect(view.months[0]!.changeBase).toBe(`${shortMonthYear('202610')} actual`);
    expect(view.latestActual).toBe('$100.00');
    expect(view.latestActualMonth).toBe(shortMonthYear('202610'));
  });

  it('uses the database team wording and says the parts may not add up', () => {
    expect(EXISTING_TRIPS_LABEL).toBe('Expected collections on trips that already existed');
    expect(FUTURE_TRIPS_LABEL).toBe('Assumed run rate of trips that do not exist yet');
    expect(COMPONENTS_FOOTNOTE).toBe('The two parts are shown as reported by the warehouse and may not add up to the expected total.');
  });

  it('puts all the figures in the widget table and says the range is not a statistical interval', () => {
    const data = fromProjection(PROJECTION);

    expect(data.table.columns).toContain(EXISTING_TRIPS_LABEL);
    expect(data.table.rows[0]![0]).toBe('Nov 2026');
    expect(data.table.rows[0]).toContain('126.7%');
    expect(data.caveat).toContain('not a statistical interval');
    expect(data.caveat).toContain(COMPONENTS_FOOTNOTE);
    expect(JSON.stringify(data)).not.toMatch(/likely|already billed/i);
  });
});

describe('transport volume split', () => {
  it('adds a latest-month table with the prior month, the change and the percent, n/a when the prior month had none', () => {
    const data = fromTransports({
      metric: METRIC, providerId: '1', endPeriod: '202610', months: 2, serviceLine: 'air', periodOpen: false,
      points: [{ period: '202609', label: 'Sep', year: 2026, transports: 10, loadedMiles: 1 }, { period: '202610', label: 'Oct', year: 2026, transports: 12, loadedMiles: 1 }],
      groups: [
        { key: 'a', name: 'Rotor wing', serviceLine: 'air', transports: 8, priorTransports: 10, changeTransports: -2, changePercent: -20 },
        { key: 'b', name: 'Fixed wing', serviceLine: 'air', transports: 4, priorTransports: 0, changeTransports: 4, changePercent: null },
      ],
    });

    const split = data.extraTables![0]!;
    expect(split.columns).toEqual(['Aircraft type or level of service', 'Oct 2026', 'Sep 2026', 'Change', 'Change %']);
    expect(split.rows).toEqual([['Rotor wing', '8', '10', '-2', '-20.0%'], ['Fixed wing', '4', '0', '+4', 'n/a']]);
  });
});

const CPT: CashPerTransportDto = {
  metric: METRIC, providerId: '1', period: '202611', cohortFrom: '202501', cohortThrough: '202512', serviceLine: 'air', group: null,
  overall: { transports: 100, cash: 50_000, cashPerTransport: 500 },
  cohorts: { firstServiceMonth: '202501', lastServiceMonth: '202512', cashThroughPeriod: '202611' },
  payers: [
    { key: 'commercial', name: 'Commercial', transports: 60, cash: 36_000, cashPerTransport: 600, differenceFromOverall: 100, percentFromOverall: 20 },
    { key: 'va', name: 'VA', transports: 0, cash: 0, cashPerTransport: null, differenceFromOverall: null, percentFromOverall: null },
  ],
};

describe('cash per transport', () => {
  it('shows each payer group against the overall average, with n/a where there is no figure', () => {
    const data = fromCashPerTransport(CPT);

    expect(data.table.columns).toContain('Difference from overall');
    expect(data.table.rows[0]).toEqual(['Commercial', '$600.00', '+$100.00', '+20.0%', '60', '$36,000.00']);
    expect(data.table.rows[1]).toEqual(['VA', 'n/a', 'n/a', 'n/a', '0', '$0.00']);
    expect(data.table.rows[2]![0]).toBe('All payer groups (overall)');
  });

  it('says which service months and which payment cut-off were used, in the user locale', () => {
    const line = `Transports from ${shortMonthYear('202501')} to ${shortMonthYear('202512')}, cash posted through ${shortMonthYear('202611')}`;

    expect(cohortLine(CPT)).toBe(line);
    expect(fromCashPerTransport(CPT).caveat).toBe(line);
    expect(cohortLine({ cohorts: null })).toBe('');
  });
});

describe('payment timing', () => {
  const PAYERS = [
    { payerKey: 'c', payerName: 'Commercial', typicalAgeMonths: 2, bands: [{ key: '0-1', fromMonths: 0, toMonths: 1, share: 0.4 }, { key: '2-3', fromMonths: 2, toMonths: 3, share: 0.35 }, { key: '4-6', fromMonths: 4, toMonths: 6, share: 0.15 }, { key: '7-12', fromMonths: 7, toMonths: 12, share: 0.07 }, { key: '13+', fromMonths: 13, toMonths: null, share: 0.03 }] },
    { payerKey: 'v', payerName: 'VA', typicalAgeMonths: null, bands: [] },
  ] as const;

  it('lists typical months and the share of cash by age band, and n/a for a payer group with no cash', () => {
    const table = timingTable(PAYERS as never);

    expect(table.columns).toEqual(['Payer group', 'Typical months to payment', '0-1 months', '2-3 months', '4-6 months', '7-12 months', '13+ months']);
    expect(table.rows[0]).toEqual(['Commercial', '2 months', '40.0%', '35.0%', '15.0%', '7.0%', '3.0%']);
    expect(table.rows[1]).toEqual(['VA', 'n/a', 'n/a', 'n/a', 'n/a', 'n/a', 'n/a']);
  });

  it('adds the table to the widget, says age is whole months, and copes with an empty bands list', () => {
    const dto: PaymentAgeDto = {
      metric: METRIC, providerId: '1', period: '202610', serviceLine: 'air', group: null, payers: PAYERS as never,
      cells: [{ payerKey: 'c', payerName: 'Commercial', postingPeriod: '202610', ageMonths: 2, cash: 100, transports: 1 }],
    };
    const data = fromPaymentAge(dto);

    expect(data.extraTables).toHaveLength(1);
    expect(data.caveat).toContain('whole months');
    expect(fromPaymentAge({ ...dto, payers: [] }).extraTables).toEqual([]);
  });
});
