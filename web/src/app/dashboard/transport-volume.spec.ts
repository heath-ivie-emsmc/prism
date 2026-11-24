import { describe, expect, it } from 'vitest';
import { TransportVolumeDto } from '../metrics/transport-volume/transport-volume.dto';
import { cardMonths, groupsText, milesIn, toMilesCard, toTransportsCard } from './transport-volume';

const volume = (end: string, points: [string, number, number][], groups: [string, string, number][] = []): TransportVolumeDto => ({
  metric: { key: 'transport_volume', name: 't', description: 'd', unit: 'transports' },
  providerId: '1',
  endPeriod: end,
  months: 6,
  serviceLine: 'air',
  periodOpen: false,
  points: points.map(([period, transports, loadedMiles]) => ({ period, label: '', year: 2026, transports, loadedMiles })),
  groups: groups.map(([key, name, transports]) => ({ key, name, serviceLine: 'air', transports, priorTransports: transports + 2, changeTransports: -2, changePercent: -1.5 })),
});

const sample = volume(
  '202610',
  [['202605', 100, 9000], ['202606', 120, 10000], ['202608', 150, 12000], ['202609', 146, 19000], ['202610', 140, 18420]],
  [['rotor-wing', 'Rotor wing', 92], ['fixed-wing', 'Fixed wing', 48]],
);

describe('dashboard cards', () => {
  it('covers six months ending at the chosen one, across a year end', () => {
    expect(cardMonths('202610')).toEqual(['202605', '202606', '202607', '202608', '202609', '202610']);
    expect(cardMonths('202602')).toEqual(['202509', '202510', '202511', '202512', '202601', '202602']);
  });

  it('shows the latest month, the change from the month before and the split of the last month', () => {
    const card = toTransportsCard(sample);
    expect(card.value).toBe('140');
    expect(card.delta).toEqual({ direction: 'down', text: '6 fewer than Sep' });
    expect(card.footnote).toBe('Rotor wing 92 · Fixed wing 48');
  });

  it('draws six bars, a stub for a month with none, and the tallest at full height', () => {
    const card = toTransportsCard(sample);
    expect(card.bars).toHaveLength(6);
    expect(card.bars[2]).toBe(6);
    expect(Math.max(...card.bars)).toBe(100);
    expect(card.labels).toHaveLength(6);
    expect(card.labels[2]).toBe('0');
    expect(card.description).toBe('Transports by month: May 100, Jun 120, Jul 0, Aug 150, Sep 146, Oct 140.');
  });

  it('says more, and nothing when nothing changed', () => {
    expect(toTransportsCard(volume('202610', [['202609', 10, 0], ['202610', 25, 0]])).delta).toEqual({ direction: 'up', text: '15 more than Sep' });
    expect(toTransportsCard(volume('202610', [['202609', 10, 0], ['202610', 10, 0]])).delta).toBeNull();
  });

  it('shows a dash, not a zero, when there is no data to show', () => {
    expect(toTransportsCard(null).value).toBe('—');
    expect(toMilesCard(null, 'statute').value).toBe('—');
    expect(toTransportsCard({ ...volume('202610', []), endPeriod: null }).value).toBe('—');
  });

  it('folds long group lists into "+n more"', () => {
    const groups = [1, 2, 3, 4, 5].map((n) => ({ key: `g${n}`, name: `G${n}`, serviceLine: 'ground' as const, transports: 10 - n, priorTransports: 10 - n, changeTransports: 0, changePercent: 0 }));
    expect(groupsText(groups)).toBe('G1 9 · G2 8 · G3 7 · +2 more');
  });

  it('shows miles in statute, and converts to nautical', () => {
    const statute = toMilesCard(sample, 'statute');
    expect(statute.value).toBe('18,420');
    expect(statute.unit).toBe('statute mi');
    expect(statute.delta).toEqual({ direction: 'down', text: '3% fewer than Sep' });
    const nautical = toMilesCard(sample, 'nautical');
    expect(nautical.value).toBe('16,007');
    expect(nautical.unit).toBe('nautical mi');
    expect(milesIn(1000, 'nautical')).toBe(869);
  });

  it('labels each point on the line with its figure in the chosen unit', () => {
    const marks = toMilesCard(volume('202610', [['202605', 1, 100], ['202610', 1, 900]]), 'nautical').marks;
    expect(marks).toHaveLength(6);
    expect(marks[5]!.label).toBe('782');
    expect(marks[0]!.x).toBeGreaterThan(0);
    expect(marks[5]!.x).toBeLessThan(100);
  });

  it('draws the line up for more miles and across the middle when flat', () => {
    const rising = toMilesCard(volume('202610', [['202605', 1, 100], ['202610', 1, 900]]), 'statute').line.split(' ');
    expect(Number(rising.at(-1)!.split(',')[1])).toBeLessThan(Number(rising[0]!.split(',')[1]));
    const flat = toMilesCard(volume('202610', cardMonths('202610').map((m): [string, number, number] => [m, 5, 100])), 'statute');
    expect(flat.line.split(' ').every((p) => p.endsWith(',15.0'))).toBe(true);
    expect(flat.delta).toBeNull();
  });
});
