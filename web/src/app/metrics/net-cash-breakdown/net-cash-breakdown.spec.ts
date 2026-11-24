import { NetCashBreakdownDto } from './net-cash-breakdown.dto';
import { monthName, percentChange, priorMonthName, summarizeBreakdown } from './net-cash-breakdown';
import { emptyBreakdown, mixText, toBreakdown } from './net-cash-breakdown.model';

const figures = (cash: number, transports = 10, loadedMiles = 100.4, primaryMix = 7, secondaryMix = 3) => ({ cash, transports, loadedMiles, primaryMix, secondaryMix });

function dto(groups: [string, string, number, number][]): NetCashBreakdownDto {
  return {
    metric: { key: 'net_cash_breakdown', name: 'n', description: 'd', unit: 'USD' },
    providerId: '1',
    period: '202610',
    priorPeriod: '202609',
    serviceLine: 'air',
    periodOpen: true,
    groups: groups.map(([key, name, cash, prior]) => ({ key, name, serviceLine: 'air', current: figures(cash), prior: figures(prior, 8) })),
    rows: [{ groupKey: 'rotor-wing', groupName: 'Rotor wing', serviceLine: 'air', base: null, vehicle: 'N119MF', current: figures(500, 12, 55.6), prior: figures(400, 9) }],
  };
}

const AIR = dto([
  ['rotor-wing', 'Rotor wing', 607_860, 620_310],
  ['fixed-wing', 'Fixed wing', 492_380, 630_150],
]);

describe('net cash breakdown helpers', () => {
  it('names the month and the month before, across a year end', () => {
    expect([monthName('202610'), priorMonthName('202610'), priorMonthName('202601')]).toEqual(['Oct', 'Sep', 'Dec']);
    expect(priorMonthName('bad')).toBe('');
  });

  it('calculates percent change and has none from zero', () => {
    expect(percentChange(90, 100)).toBeCloseTo(-10);
    expect(percentChange(5, 0)).toBeNull();
  });

  it('says which group explains most of the drop', () => {
    const text = summarizeBreakdown(toBreakdown(AIR, 'air').groups);

    expect(text).toContain('Fixed wing accounts for $137,770.00 of the $150,220.00 drop (92%).');
    expect(text).toContain('Rotor wing held steady.');
  });

  it('does not say a group held steady when it moved a lot, and says how much', () => {
    const text = summarizeBreakdown(toBreakdown(dto([['rotor-wing', 'Rotor wing', 124_562, 651_408], ['fixed-wing', 'Fixed wing', 87_983, 779_012]]), 'air').groups);

    expect(text).toContain('Fixed wing accounts for $691,029.00');
    expect(text).toContain('Rotor wing fell $526,846.00.');
    expect(text).not.toContain('held steady');
  });

  it('says rise when the total went up and nothing when there are no groups', () => {
    const groups = toBreakdown(AIR, 'air').groups.map((g) => ({ ...g, priorCash: g.cash - 100 }));

    expect(summarizeBreakdown(groups)).toContain('rise');
    expect(summarizeBreakdown([])).toBe('');
  });
});

describe('net cash breakdown model', () => {
  it('writes two shares that add up to 100, and a dash when there is nothing to share', () => {
    expect([mixText(1, 2), mixText(7, 3), mixText(0, 0)]).toEqual(['33% / 67%', '70% / 30%', '—']);
  });

  it('maps the API answer to what the page shows', () => {
    const view = toBreakdown(AIR, 'air');

    expect(view.groups[0]).toMatchObject({ name: 'Rotor wing', icon: 'helicopter', cash: 607_860, priorCash: 620_310, transports: 10, priorTransports: 8, mix: '70% / 30%', loadedMiles: 100 });
    expect(view.groups[1]!.icon).toBe('flight');
    expect(view.rows[0]).toMatchObject({ id: 'N119MF', location: '—', transports: 12, transportsChange: 3, loadedMiles: 56, cash: 500, priorCash: 400 });
    expect(view.terms.groupHeading).toBe('Aircraft type');
    expect(view.stillPosting).toBe(true);
  });

  it('gives a level of service without an icon of its own a default, and the Service Line its words', () => {
    const view = toBreakdown(dto([['wheelchair', 'Wheelchair', 1, 1]]), 'ground');

    expect(view.groups[0]!.icon).toBe('medical_services');
    expect(view.terms.groupHeading).toBe('Level of service');
  });

  it('has an empty page for each Service Line', () => {
    expect(emptyBreakdown('ground')).toMatchObject({ groups: [], rows: [], stillPosting: false, terms: { idHeading: 'Tail number' } });
  });

  it('reads each group card link from the API links and has none where the API gave none', () => {
    const withLink = dto([['rotor-wing', 'Rotor wing', 1, 1], ['fixed-wing', 'Fixed wing', 1, 1]]);
    const href = '/api/v1/providers/526/metrics/net-collections/payer-mix?group=rotor-wing&period=202610&serviceLine=air';
    const view = toBreakdown({ ...withLink, groups: [{ ...withLink.groups[0]!, links: [{ rel: 'drill', href, title: 'Rotor wing payer mix' }] }, { ...withLink.groups[1]!, links: [] }] }, 'air');

    expect(view.groups[0]!.drill).toEqual({ commands: ['/dashboard/net-cash', 'rotor-wing'], queryParams: { period: '202610', line: 'air' } });
    expect(view.groups[1]!.drill).toBeNull();
    expect(toBreakdown(withLink, 'air').groups[0]!.drill).toBeNull();
  });

  it('ignores a group link that is not a known drill target', () => {
    const base = dto([['rotor-wing', 'Rotor wing', 1, 1]]);
    const bad = { ...base, groups: [{ ...base.groups[0]!, links: [{ rel: 'drill', href: 'https://evil.example/x', title: 'x' }] }] };

    expect(toBreakdown(bad, 'air').groups[0]!.drill).toBeNull();
  });
});
