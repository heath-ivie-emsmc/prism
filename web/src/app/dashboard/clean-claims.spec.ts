import { describe, expect, it } from 'vitest';
import { CleanClaimRateDto } from '../metrics/net-collections.service';
import { formatRate, toCleanClaimCard } from './clean-claims';

const dto = (points: [string, number | null][]): CleanClaimRateDto => ({
  metric: { key: 'clean_claim_rate', name: 'c', description: 'd', unit: 'percent' },
  providerId: '1',
  endPeriod: '202610',
  months: 6,
  snapshotDate: '2026-04-07',
  points: points.map(([period, rate]) => ({ period, label: '', year: 2026, cleanClaims: 0, rejectedClaims: 0, totalClaims: rate === null ? 0 : 100, rate })),
});

describe('clean claim rate tile', () => {
  it('writes the rate with one decimal, and 100% without', () => {
    expect(formatRate(96.91)).toBe('96.9%');
    expect(formatRate(100)).toBe('100%');
    expect(formatRate(99.96)).toBe('100%');
    expect(formatRate(0)).toBe('0%');
  });

  it('shows the month on screen and the change from the month before, in points', () => {
    const card = toCleanClaimCard(dto([['202602', 0.9996], ['202603', 0.9691]]), '202603');
    expect(card.value).toBe('96.9%');
    expect(card.delta).toEqual({ direction: 'down', text: '3.1 pts lower than Feb' });
    expect(card.latestNote).toBe('');
    expect(card.percent).toBeCloseTo(96.91);
  });

  it('says higher when the rate rose', () => {
    expect(toCleanClaimCard(dto([['202602', 0.9], ['202603', 0.95]]), '202603').delta).toEqual({ direction: 'up', text: '5.0 pts higher than Feb' });
  });

  it('says which month it is when the data is behind the month on screen', () => {
    const card = toCleanClaimCard(dto([['202602', 0.9996], ['202603', 0.9691]]), '202610');
    expect(card.value).toBe('96.9%');
    expect(card.latestNote).toBe('Latest in the data: March 2026.');
  });

  it('shows no change when the month before is not in the data, or the rate did not move', () => {
    expect(toCleanClaimCard(dto([['202601', 0.9], ['202603', 0.95]]), '202603').delta).toBeNull();
    expect(toCleanClaimCard(dto([['202602', 0.95], ['202603', 0.95]]), '202603').delta).toBeNull();
  });

  it('skips months with no claims and shows a dash with a note when there is nothing', () => {
    expect(toCleanClaimCard(dto([['202602', 0.9], ['202603', null]]), '202603').value).toBe('90%');
    const none = toCleanClaimCard(dto([]), '202610');
    expect(none.value).toBe('—');
    expect(none.percent).toBeNull();
    expect(none.latestNote).toBe('No clean claim data for October 2026 yet.');
    expect(toCleanClaimCard(null, '202610')).toMatchObject({ value: '—', latestNote: '' });
  });
});
