import { describe, expect, it } from 'vitest';
import { WidgetData } from '../saved-views/widget-data';
import { AIRCRAFT, BASES, CONTRIB_AIRCRAFT, CONTRIB_DIVISION, CONTRIB_PAYER, CALL_TYPES, EFFECT, HOSTILE, MILES, PICKUPS, figure } from './new-metrics.testing';
import { fromAircraftPerformance } from './aircraft-performance/aircraft-performance.widget';
import { fromBaseAnalysis } from './base-analysis/base-analysis.widget';
import { fromCashContributionByAircraft } from './cash-contribution-by-aircraft/cash-contribution-by-aircraft.widget';
import { fromCashContributionByDivision } from './cash-contribution-by-division/cash-contribution-by-division.widget';
import { fromCashContributionByPayer } from './cash-contribution-by-payer/cash-contribution-by-payer.widget';
import { fromLoadedMilesByAircraft } from './loaded-miles-by-aircraft/loaded-miles-by-aircraft.widget';
import { fromPayerMixFinancialEffect } from './payer-mix-financial-effect/payer-mix-financial-effect.widget';
import { NET_CASH_PER_TRANSPORT } from './shared/figures';
import { fromTransportMixByCallType } from './transport-mix-by-call-type/transport-mix-by-call-type.widget';
import { fromTransportsByPickupLocation } from './transports-by-pickup-location/transports-by-pickup-location.widget';
import { METRIC_RENDERERS } from './metric-renderers';

const cells = (data: WidgetData): string[] => data.table.rows.flat();
const allText = (data: WidgetData): string => JSON.stringify([data.summary, data.caveat, data.table, data.extraTables, data.categories, data.headline]);

describe('every new Metric mapper', () => {
  it.each([
    ['transport_mix_by_call_type', fromTransportMixByCallType(CALL_TYPES)],
    ['aircraft_performance', fromAircraftPerformance(AIRCRAFT)],
    ['base_analysis', fromBaseAnalysis(BASES)],
    ['loaded_miles_by_aircraft', fromLoadedMilesByAircraft(MILES)],
    ['payer_mix_financial_effect', fromPayerMixFinancialEffect(EFFECT)],
    ['cash_contribution_by_aircraft', fromCashContributionByAircraft(CONTRIB_AIRCRAFT)],
    ['cash_contribution_by_division', fromCashContributionByDivision(CONTRIB_DIVISION)],
    ['cash_contribution_by_payer', fromCashContributionByPayer(CONTRIB_PAYER)],
    ['transports_by_pickup_location', fromTransportsByPickupLocation(PICKUPS)],
  ])('%s has a real table, a short chart description and a caption, and is registered', (key, data) => {
    expect(data.empty).toBe(false);
    expect(data.summary.length).toBeLessThanOrEqual(250);
    expect(data.table.caption).not.toBe('');
    expect(data.table.rows.length).toBeGreaterThan(0);
    expect(data.table.rows.every((r) => r.length === data.table.columns.length)).toBe(true);
    for (const extra of data.extraTables ?? []) {
      expect(extra.rows.every((r) => r.length === extra.columns.length)).toBe(true);
    }
    expect(METRIC_RENDERERS[key]).toBeDefined();
  });

  it('says there is no data, instead of drawing, when nothing was counted', () => {
    const zero = figure(0, 0, 0, 0);
    expect(fromTransportMixByCallType({ ...CALL_TYPES, totalTransports: 0, priorTotalTransports: 0, callTypes: [] }).empty).toBe(true);
    expect(fromAircraftPerformance({ ...AIRCRAFT, totals: zero, aircraft: [] }).empty).toBe(true);
    expect(fromBaseAnalysis({ ...BASES, totals: zero, divisions: [] }).empty).toBe(true);
    expect(fromLoadedMilesByAircraft({ ...MILES, aircraft: [] }).empty).toBe(true);
    expect(fromPayerMixFinancialEffect({ ...EFFECT, transports: 0, priorTransports: 0, groups: [] }).empty).toBe(true);
    expect(fromTransportsByPickupLocation({ ...PICKUPS, totalTransports: 0, priorTotalTransports: 0, sources: [], facilities: [] }).empty).toBe(true);
  });

  it('keeps warehouse text as plain strings and never builds markup from it', () => {
    for (const data of [fromCashContributionByAircraft(CONTRIB_AIRCRAFT), fromCashContributionByDivision(CONTRIB_DIVISION), fromAircraftPerformance(AIRCRAFT), fromBaseAnalysis(BASES), fromLoadedMilesByAircraft(MILES), fromTransportsByPickupLocation(PICKUPS)]) {
      expect(allText(data)).toContain(JSON.stringify(HOSTILE).slice(1, -1));
      expect(data.summary).not.toContain('<img');
    }
  });
});

describe('transport mix by call type', () => {
  const data = fromTransportMixByCallType(CALL_TYPES);

  it('shows counts, shares and the change in share with its sign, and never invents a share', () => {
    expect(data.table.rows[0]).toEqual(['Scene', '10', '5', '+5', '71.4%', '50.0%', '+21.4 pts', '900', '$5,000.00', '$4,000.00']);
    const none = fromTransportMixByCallType({ ...CALL_TYPES, callTypes: [{ ...CALL_TYPES.callTypes[0]!, share: null, priorShare: null, changePoints: null }] });
    expect(none.table.rows[0]!.slice(4, 7)).toEqual(['n/a', 'n/a', 'n/a']);
  });

  it('draws the month before and the month asked for by call type, and the months in the trend', () => {
    expect(data.categories).toEqual(['Scene', 'Interfacility']);
    expect(data.series.map((s) => s.name)).toEqual(['September 2026', 'October 2026']);
    expect(data.trend!.categories).toEqual(['Sep 2026', 'Oct 2026']);
    expect(data.trend!.series.map((s) => [s.name, s.values])).toEqual([['Scene', [5, 10]], ['Interfacility', [5, 4]]]);
  });

  it('keeps net cash as its own posting-month figure and says expected payment is not available', () => {
    expect(data.table.columns).toContain('Net cash posted in the month');
    expect(data.caveat).toContain('date of service');
    expect(data.caveat).toContain('Expected payment is not available');
  });

  it('adds the payer mix within each call type and a month-by-month table', () => {
    expect(data.extraTables!.map((t) => t.caption)).toEqual(['Payer mix within each call type, October 2026', 'Transports by call type, month by month (6 months)']);
    expect(data.extraTables![0]!.rows[0]).toEqual(['Scene', 'Commercial', '6', '60.0%', '50.0%']);
  });
});

describe('aircraft performance', () => {
  const data = fromAircraftPerformance(AIRCRAFT);

  it('labels cash per transport honestly everywhere it appears', () => {
    expect(data.table.columns).toContain(NET_CASH_PER_TRANSPORT);
    expect(data.table.columns.filter((c) => c.includes('per transport')).every((c) => c.startsWith('Net cash per transport (cash posted in the month ÷ transports)'))).toBe(true);
    expect(data.caveat).toContain(NET_CASH_PER_TRANSPORT);
  });

  it('heads the first column Tail number, ends with an All aircraft row and says n/a for a missing figure', () => {
    expect(data.table.columns[0]).toBe('Tail number');
    expect(data.table.rows.at(-1)![0]).toBe('All aircraft');
    expect(data.table.rows[1]![10]).toBe('n/a');
    expect(data.table.rows[2]![0]).toBe('Other');
  });

  it('charts the busiest tails and keeps the Other row', () => {
    expect(data.categories).toEqual([HOSTILE, 'N611CV', 'Other']);
  });
});

describe('base analysis', () => {
  it('names the term from the Label of the line in effect, never a literal', () => {
    expect(fromBaseAnalysis(BASES).table.columns[0]).toBe('Base location');
    const both = fromBaseAnalysis({ ...BASES, serviceLine: 'all' });
    expect(both.table.columns[0]).toBe('Reporting division');
    expect(both.table.caption).toBe('Transports, loaded miles and net cash by reporting division');
  });

  it('lists the aircraft that flew from each one and labels cash per transport honestly', () => {
    const data = fromBaseAnalysis(BASES);
    expect(data.extraTables![0]!.rows).toEqual([['North base', 'N611CV', '12'], ['North base', 'Other', '8']]);
    expect(data.table.columns).toContain(NET_CASH_PER_TRANSPORT);
  });
});

describe('loaded miles by aircraft', () => {
  const data = fromLoadedMilesByAircraft(MILES);

  it('is statute miles, with an average that is n/a when there were no transports', () => {
    expect(data.table.columns).toEqual(['Tail number', 'Transports', 'Loaded miles (statute)', 'Average loaded miles per transport']);
    expect(data.table.rows[0]).toEqual([HOSTILE, '8', '1,200', '150.0']);
    expect(data.table.rows[1]![3]).toBe('n/a');
  });

  it('draws miles by tail as columns and each tail over the months as lines', () => {
    expect(data.series).toEqual([{ name: 'Loaded miles (statute)', values: [1200, 300] }]);
    expect(data.trend!.categories).toEqual(['Sep 2026', 'Oct 2026']);
    expect(data.trend!.series[1]!.values).toEqual([300, null]);
  });

  it('adds the month-by-month table', () => {
    expect(data.extraTables![0]!.columns).toEqual(['Tail number', 'Sep 2026', 'Oct 2026']);
    expect(data.extraTables![0]!.rows[1]).toEqual(['N611CV', '300', 'n/a']);
  });
});

describe('payer mix financial effect', () => {
  const data = fromPayerMixFinancialEffect(EFFECT);

  it('says the effect is expected and not posted cash, and that the recurring figure assumes the shift lasts', () => {
    expect(data.summary).toContain('expected effect, not posted cash');
    expect(data.caveat).toContain('not posted cash');
    expect(data.caveat).toContain('If the shift lasts, the same amount repeats each month');
    expect(data.headline).toEqual({ value: '-$3,600.00', note: expect.stringContaining('not posted cash') });
  });

  it('shows the service period and payment cut-off from the cohorts', () => {
    expect(data.caveat).toContain('Transports from');
    expect(data.caveat).toContain('cash posted through');
  });

  it('shows the effect with its sign, n/a for a group with none, and both amounts in a small table', () => {
    expect(data.table.rows[0]).toEqual(['Commercial', '26', '30', '26.0%', '30.0%', '-4.0 pts', '$900.00', '-$3,600.00']);
    expect(data.table.rows[1]!.slice(6)).toEqual(['n/a', 'n/a']);
    expect(data.extraTables![0]!.rows).toEqual([['Expected effect of the shift in payer mix', '-$3,600.00'], ['Recurring monthly effect, if the shift lasts', '-$3,600.00']]);
  });

  it('says n/a, not zero, when no effect can be worked out', () => {
    const none = fromPayerMixFinancialEffect({ ...EFFECT, estimatedEffect: null, recurringMonthlyEffect: null, cohorts: null });
    expect(none.headline!.value).toBe('n/a');
    expect(none.summary).toContain('No expected effect');
    expect(cells(none)).toContain('n/a');
  });
});

describe('transports by pickup location', () => {
  const data = fromTransportsByPickupLocation(PICKUPS);

  it('says the grouping follows the origin modifier on the claim', () => {
    expect(data.caveat).toContain('origin modifier on the claim');
  });

  it('lists facilities with the pickup type each belongs to, as plain text', () => {
    expect(data.extraTables![0]!.rows[0]).toEqual([HOSTILE, 'Hospital', '4', '3', '+1', '28.6%', '400']);
    expect(data.extraTables![0]!.rows[1]![1]).toBe('Not grouped');
  });

  it('draws the month before and the month asked for by pickup type, and leaves facilities out when there are none', () => {
    expect(data.categories).toEqual(['Scene', 'Hospital']);
    expect(fromTransportsByPickupLocation({ ...PICKUPS, facilities: [] }).extraTables!.map((t) => t.caption)).toEqual(['Payer mix within each pickup type, October 2026']);
  });
});

describe('cash contribution Metrics', () => {
  it('lists every row and a total that equals the rows, with the sign in the text', () => {
    const data = fromCashContributionByAircraft(CONTRIB_AIRCRAFT);
    expect(data.table.columns).toEqual(['Tail number', 'Change', 'Share of change', 'This month']);
    expect(data.table.caption).toBe('Change in net cash by tail number, October 2026 against September 2026. This month is the net cash of October 2026');
    expect(data.table.rows[0]).toEqual([HOSTILE, '+$4,500.00', '150.0%', '$9,000.00']);
    expect(data.table.rows[1]![1]).toBe('-$1,500.00');
    expect(data.table.rows.at(-1)).toEqual(['All aircraft', '+$3,000.00 (+12.5%)', '100.0%', '$14,000.00']);
    const change = data.table.rows.slice(0, -1).reduce((t, r) => t + Number(r[1]!.replace(/[^0-9.-]/g, '')), 0);
    expect(change).toBe(3000);
    expect(data.series[0]!.values).toEqual([4500, -1500, 0]);
    expect(data.unit).toBe('usd');
  });

  it('names the Reporting Division from the Label, and says n/a instead of a share when the total did not change', () => {
    expect(fromCashContributionByDivision(CONTRIB_DIVISION).table.columns[0]).toBe('Base location');
    expect(fromCashContributionByDivision({ ...CONTRIB_DIVISION, serviceLine: 'all' }).table.columns[0]).toBe('Reporting division');
    const flat = { ...CONTRIB_DIVISION, total: { ...CONTRIB_DIVISION.total, change: 0, changePercent: null }, rows: CONTRIB_DIVISION.rows.map((r) => ({ ...r, shareOfChange: null })) };
    const data = fromCashContributionByDivision(flat);
    expect(data.table.rows[0]![2]).toBe('n/a');
    expect(data.table.rows.at(-1)![2]).toBe('n/a');
  });

  it('says there is no data when nothing was posted in either month', () => {
    expect(fromCashContributionByPayer({ ...CONTRIB_PAYER, rows: [] }).empty).toBe(true);
    expect(fromCashContributionByAircraft({ ...CONTRIB_AIRCRAFT, total: { netCash: 0, priorNetCash: 0, change: 0, changePercent: null } }).empty).toBe(true);
  });

  it('follows only the server-issued payer drill that the strict parser knows, and colours payers by key', () => {
    const data = fromCashContributionByPayer(CONTRIB_PAYER);
    expect(data.categoryKeys).toEqual(['commercial', 'medicare', 'patient']);
    expect(data.drills).toEqual([{ commands: ['/dashboard/metrics/payer-mix', 'fixed-wing', 'commercial'], queryParams: { period: '202610', line: 'air' } }, null, null]);
    // Without a group in effect the server cannot name the breakdown, so no drill is drawn.
    const noGroup = { ...CONTRIB_PAYER, group: null, rows: CONTRIB_PAYER.rows.map((r) => ({ ...r, links: r.links?.map((l) => ({ ...l, href: l.href.replace('group=fixed-wing&', '') })) })) };
    expect(fromCashContributionByPayer(noGroup).drills).toBeUndefined();
    expect(fromCashContributionByAircraft(CONTRIB_AIRCRAFT).drills).toBeUndefined();
  });
});
