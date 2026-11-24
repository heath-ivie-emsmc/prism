import { describe, expect, it } from 'vitest';
import { buildWidgetOption, formatValue } from './widget-chart.options';
import { SUMMARY_MAX, limit } from './widget-data';
import { fromBreakdown } from '../metrics/net-cash-breakdown/net-cash-breakdown.widget';
import { fromByGroup } from '../metrics/net-cash-by-group/net-cash-by-group.widget';
import { fromCashPerTransport } from '../metrics/cash-per-transport-by-payer/cash-per-transport-by-payer.widget';
import { fromCleanClaims } from '../metrics/clean-claim-rate/clean-claim-rate.widget';
import { fromNetCash } from '../metrics/net-collections/net-collections.widget';
import { fromPayerMix } from '../metrics/payer-mix/payer-mix.widget';
import { fromPaymentAge } from '../metrics/payment-age/payment-age.widget';
import { fromProjection } from '../metrics/net-cash-projection/net-cash-projection.widget';
import { fromTransports } from '../metrics/transport-volume/transport-volume.widget';
import type { ChartColors } from '../charts/chart.component';

const METRIC = { key: 'k', name: 'n', description: 'd', unit: 'u' };
const COLORS: ChartColors = { text: '#000', mutedText: '#555', grid: '#ccc', actual: '#111', highlight: '#a00', projected: '#0a0', narrow: false };

describe('widget data', () => {
  it('turns a net cash series into a headline, a table and a chart with the same numbers', () => {
    const data = fromNetCash({
      metric: METRIC, providerId: '1', endPeriod: '202610', months: 2, snapshotDate: '2026-11-05',
      points: [{ period: '202609', label: 'Sep', year: 2026, value: 1000 }, { period: '202610', label: 'Oct', year: 2026, value: 1100 }],
    });

    expect(data.empty).toBe(false);
    expect(data.headline).toEqual({ value: '$1,100', note: '+10.0% on Sep' });
    expect(data.table.rows).toEqual([['Sep 2026', '$1,000.00', 'n/a'], ['Oct 2026', '$1,100.00', '+10.0%']]);
    expect(data.series[0]!.values).toEqual([1000, 1100]);
    expect(data.categories).toEqual(['Sep 2026', 'Oct 2026']);
    expect(data.dataThrough).toBe('2026-11-05');
    expect(data.summary.length).toBeLessThanOrEqual(SUMMARY_MAX);
  });

  it('says there is nothing to show when a Metric has no data', () => {
    const empties = [
      fromNetCash({ metric: METRIC, providerId: '1', endPeriod: null, months: 6, snapshotDate: null, points: [] }),
      fromProjection({ metric: METRIC, providerId: '1', afterPeriod: null, serviceLine: 'all', snapshotDate: null, total: 0, low: 0, high: 0, months: [], horizon: { value: 0, low: 0, high: 0, months: 0, fromExistingTrips: null, fromFutureTrips: null, existingTripsShare: null, futureTripsShare: null }, latestActual: null }),
      fromTransports({ metric: METRIC, providerId: '1', endPeriod: null, months: 6, serviceLine: 'all', periodOpen: false, points: [], groups: [] }),
      fromCleanClaims({ metric: METRIC, providerId: '1', endPeriod: null, months: 6, snapshotDate: null, points: [] }),
      fromPayerMix({ metric: METRIC, providerId: '1', period: null, priorPeriod: null, serviceLine: 'all', group: null, groupName: null, shares: [] }),
      fromBreakdown({ metric: METRIC, providerId: '1', period: null, priorPeriod: null, serviceLine: 'all', periodOpen: false, groups: [], rows: [] }),
      fromByGroup({ metric: METRIC, providerId: '1', period: null, serviceLine: 'all', points: [] }),
      fromCashPerTransport({ metric: METRIC, providerId: '1', period: null, cohortFrom: null, cohortThrough: null, serviceLine: 'all', group: null, overall: null, cohorts: null, payers: [] }),
      fromPaymentAge({ metric: METRIC, providerId: '1', period: null, serviceLine: 'all', group: null, cells: [], payers: [] }),
    ];

    expect(empties.every((d) => d.empty && d.table.rows.length === 0)).toBe(true);
  });

  it('keeps the open-month caveat for transports and skips months with no clean claim figure', () => {
    const transports = fromTransports({
      metric: METRIC, providerId: '1', endPeriod: '202610', months: 1, serviceLine: 'air', periodOpen: true,
      points: [{ period: '202610', label: 'Oct', year: 2026, transports: 1234, loadedMiles: 50000.4 }], groups: [],
    });
    const clean = fromCleanClaims({
      metric: METRIC, providerId: '1', endPeriod: '202610', months: 2, snapshotDate: null,
      points: [{ period: '202609', label: 'Sep', year: 2026, cleanClaims: 0, rejectedClaims: 0, totalClaims: 0, rate: null }, { period: '202610', label: 'Oct', year: 2026, cleanClaims: 95, rejectedClaims: 5, totalClaims: 100, rate: 0.95 }],
    });

    expect(transports.caveat).toContain('still open');
    expect(transports.table.rows[0]).toEqual(['Oct 2026', '1,234', '50,000']);
    expect(clean.headline?.value).toBe('95.0%');
    expect(clean.series[0]!.values).toEqual([null, 95]);
  });

  it('works out payer shares and names the largest group', () => {
    const data = fromPayerMix({
      metric: METRIC, providerId: '1', period: '202610', priorPeriod: '202609', serviceLine: 'air', group: null, groupName: null,
      shares: [{ key: 'a', name: 'Commercial', current: 30, prior: 40 }, { key: 'b', name: 'Medicaid', current: 70, prior: 60 }],
    });

    expect(data.headline).toEqual({ value: '70.0%', note: 'Medicaid, the largest group' });
    expect(data.table.rows[1]).toEqual(['Medicaid', '70', '70.0%', '60.0%']);
  });

  it('builds a cross table for net cash by month and group, and leaves a missing cell as n/a', () => {
    const data = fromByGroup({
      metric: METRIC, providerId: '1', period: '202610', serviceLine: 'air',
      points: [
        { groupKey: 'r', groupName: 'Rotor', serviceLine: 'air', period: '202609', cash: 10, transports: 1 },
        { groupKey: 'f', groupName: 'Fixed', serviceLine: 'air', period: '202610', cash: 20, transports: 1 },
      ],
    });

    expect(data.table.columns).toEqual(['Month', 'Rotor', 'Fixed']);
    expect(data.table.rows[0]![2]).toBe('n/a');
    expect(data.series.map((s) => s.values)).toEqual([[10, null], [null, 20]]);
  });

  it('averages the age cash was paid at, weighted by cash', () => {
    const data = fromPaymentAge({
      metric: METRIC, providerId: '1', period: '202610', serviceLine: 'air', group: null, payers: [],
      cells: [
        { payerKey: 'c', payerName: 'Commercial', postingPeriod: '202610', ageMonths: 2, cash: 100, transports: 1 },
        { payerKey: 'c', payerName: 'Commercial', postingPeriod: '202610', ageMonths: 4, cash: 300, transports: 1 },
      ],
    });

    expect(data.table.rows[0]).toEqual(['Commercial', '$400.00', '3.5 months']);
    expect(data.categoryKeys).toEqual(['c']);
  });

  it('keeps every summary under the accessible-name limit however long the text', () => {
    expect(limit('x'.repeat(1000)).length).toBeLessThanOrEqual(SUMMARY_MAX);
    expect(limit('short')).toBe('short');
  });

  it('never puts a patient or claim field in a table: columns are only figures and group names', () => {
    const data = fromBreakdown({
      metric: METRIC, providerId: '1', period: '202610', priorPeriod: '202609', serviceLine: 'air', periodOpen: true,
      groups: [{ key: 'r', name: 'Rotor wing', serviceLine: 'air', current: { cash: 5, transports: 1, loadedMiles: 2, primaryMix: 0, secondaryMix: 0 }, prior: { cash: 4, transports: 1, loadedMiles: 2, primaryMix: 0, secondaryMix: 0 } }],
      rows: [{ groupKey: 'r', groupName: 'Rotor wing', serviceLine: 'air', base: 'Base', vehicle: 'N123', current: { cash: 5, transports: 1, loadedMiles: 2, primaryMix: 0, secondaryMix: 0 }, prior: { cash: 4, transports: 1, loadedMiles: 2, primaryMix: 0, secondaryMix: 0 } }],
    });

    expect(data.table.columns).toEqual(['Group', 'Net cash', 'Change on month before', 'Transports', 'Loaded miles']);
    expect(JSON.stringify(data.table)).not.toContain('N123');
    expect(data.caveat).toContain('still open');
  });
});

describe('widget chart options', () => {
  const data = fromNetCash({
    metric: METRIC, providerId: '1', endPeriod: '202610', months: 2, snapshotDate: null,
    points: [{ period: '202609', label: 'Sep', year: 2026, value: 1000 }, { period: '202610', label: 'Oct', year: 2026, value: 1100 }],
  });

  it('draws a line, a column, a stack and a donut from the same data', () => {
    const line = buildWidgetOption(data, 'line', COLORS) as { series: { type: string; symbol?: string }[] };
    const column = buildWidgetOption(data, 'column', COLORS) as { series: { type: string }[] };
    const stacked = buildWidgetOption(data, 'stacked', COLORS) as { series: { stack?: string }[] };
    const donut = buildWidgetOption(data, 'donut', COLORS) as { series: { type: string; data: { value: number }[] }[] };

    expect(line.series[0]).toMatchObject({ type: 'line', symbol: 'circle' });
    expect(column.series[0]!.type).toBe('bar');
    expect(stacked.series[0]!.stack).toBe('total');
    expect(donut.series[0]).toMatchObject({ type: 'pie' });
    expect(donut.series[0]!.data.map((d) => d.value)).toEqual([1000, 1100]);
  });

  it('turns ECharts own description off and motion off', () => {
    const option = buildWidgetOption(data, 'line', COLORS) as { aria: { enabled: boolean; label: { enabled: boolean } }; animation: boolean };

    expect(option.aria.enabled).toBe(true);
    expect(option.aria.label.enabled).toBe(false);
    expect(option.animation).toBe(false);
  });

  it('tells series apart by shape and dash, not colour alone, and adds decals from four series', () => {
    const many = { ...data, series: ['a', 'b', 'c', 'd'].map((name) => ({ name, values: [1, 2] })) };

    const option = buildWidgetOption(many, 'line', COLORS) as { aria: { decal: { show: boolean } }; series: { symbol: string }[] };

    expect(new Set(option.series.map((s) => s.symbol)).size).toBe(4);
    expect(option.aria.decal.show).toBe(true);
  });

  it('formats axes by unit', () => {
    expect(formatValue(1500000, 'usd', true)).toBe('$1.5M');
    expect(formatValue(1500000, 'usd', false)).toBe('$1,500,000');
    expect(formatValue(95.25, 'percent', true)).toBe('95.3%');
    expect(formatValue(12345, 'count', false)).toBe('12,345');
  });

  it('draws with solid colors and no hover recolouring, so a bar never disappears under the pointer', () => {
    const solid = { ...COLORS, categorical: ['#38469b', '#00724c', '#752e58'] };
    const data = { unit: 'usd', categories: ['a', 'b'], series: [{ name: 'One', values: [1, 2] }, { name: 'Two', values: [3, 4] }] } as unknown as Parameters<typeof buildWidgetOption>[0];
    for (const kind of ['column', 'line', 'stacked', 'donut'] as const) {
      const option = buildWidgetOption(data, kind, solid) as { color: string[]; series: { emphasis?: { disabled?: boolean } }[] };
      expect(option.color).toEqual(['#38469b', '#00724c', '#752e58']);
      expect(option.series.every((series) => series.emphasis?.disabled === true)).toBe(true);
      expect(JSON.stringify(option)).not.toContain('color-mix');
    }
  });
});
