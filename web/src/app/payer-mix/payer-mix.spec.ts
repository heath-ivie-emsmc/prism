import { describe, expect, it } from 'vitest';
import { PayerMixDto } from '../metrics/net-collections.service';
import { priorPeriod } from '../breakdown/net-cash-breakdown';
import { biggestRise, buildPayerMixOption, describePayerMix, formatPoints, highlightedPayer, payerColor, summarizePayerMix, toPayerMix } from './payer-mix';

const dto = (shares: [string, string, number, number][]): PayerMixDto => ({
  metric: { key: 'payer_mix', name: 'Payer mix', description: '', unit: 'transports' },
  providerId: '1',
  period: '202610',
  priorPeriod: '202609',
  serviceLine: 'air',
  group: 'fixed-wing',
  groupName: 'Fixed wing',
  shares: shares.map(([key, name, current, prior]) => ({ key, name, current, prior })),
});

const mix = toPayerMix(dto([['commercial', 'Commercial', 26, 30], ['medicare', 'Medicare', 38, 40], ['medicaid', 'Medicaid', 23, 18], ['va', 'VA', 13, 12]]));
const colors = { text: '#000', mutedText: '#555', grid: '#ddd', actual: '#666', highlight: '#a00', projected: '#070', narrow: false };

describe('payer mix', () => {
  it('turns counts into shares of each month and the change in points', () => {
    const medicaid = mix.shares.find((s) => s.key === 'medicaid')!;
    expect(mix.total).toBe(100);
    expect(mix.priorTotal).toBe(100);
    expect([medicaid.percent, medicaid.priorPercent, medicaid.points]).toEqual([23, 18, 5]);
  });

  it('has no shares and no division by zero for an empty month', () => {
    const empty = toPayerMix(dto([['commercial', 'Commercial', 0, 0]]));
    expect(empty.shares[0]!.percent).toBe(0);
    expect(toPayerMix(null).shares).toEqual([]);
    expect(summarizePayerMix(empty, 'Fixed wing', 'Oct', 'Sep')).toBe('');
  });

  it('names the biggest rise only when it is notable', () => {
    expect(biggestRise(mix)?.key).toBe('medicaid');
    const flat = toPayerMix(dto([['a', 'A', 51, 50], ['b', 'B', 49, 50]]));
    expect(biggestRise(flat)).toBeNull();
    expect(biggestRise(toPayerMix(dto([['a', 'A', 5, 0]])))).toBeNull();
  });

  it('writes points with a real minus sign and the right plural', () => {
    expect(formatPoints(5.2)).toBe('+5 pts');
    expect(formatPoints(-3)).toBe('−3 pts');
    expect(formatPoints(1)).toBe('+1 pt');
    expect(formatPoints(0.2)).toBe('0 pts');
  });

  it('summarizes the largest payer and what moved, biggest first', () => {
    expect(summarizePayerMix(mix, 'Fixed wing', 'Oct', 'Sep')).toBe(
      'Medicare was 38% of fixed wing transports in Oct. Medicaid rose 5 points, from 18% in Sep to 23%. Commercial fell 4 points, from 30% in Sep to 26%.',
    );
  });

  it('describes every share for a screen reader', () => {
    expect(describePayerMix(mix, 'Fixed wing', 'Oct', 'Sep')).toContain('VA 13% in Oct, 12% in Sep');
  });

  it('keeps the hover highlight behind the bars and light, so it never hides one', () => {
    const option = buildPayerMixOption(mix, 'Oct', 'Sep', colors) as { tooltip: { axisPointer: { z: number; shadowStyle: { opacity: number } } } };
    expect(option.tooltip.axisPointer.z).toBeLessThan(2);
    expect(option.tooltip.axisPointer.shadowStyle.opacity).toBeLessThanOrEqual(0.2);
  });

  it('gives the bars no hover state of their own, which would dim the one under the pointer', () => {
    const option = buildPayerMixOption(mix, 'Oct', 'Sep', colors) as { series: { emphasis?: { disabled?: boolean } }[] };
    expect(option.series.map((s) => s.emphasis?.disabled)).toEqual([true, true]);
  });

  it('draws two bars per payer, marks the biggest rise, and goes horizontal when narrow', () => {
    const wide = buildPayerMixOption(mix, 'Oct', 'Sep', colors) as { series: { name: string; data: unknown[] }[]; xAxis: { type: string } };
    const narrow = buildPayerMixOption(mix, 'Oct', 'Sep', { ...colors, narrow: true }) as { xAxis: { type: string } };
    expect(wide.series.map((s) => s.name)).toEqual(['Sep', 'Oct']);
    expect(wide.series[1]!.data[2]).toMatchObject({ itemStyle: { color: '#a00' }, label: { formatter: '+5 pts' } });
    expect(wide.xAxis.type).toBe('category');
    expect(narrow.xAxis.type).toBe('value');
  });

  it('calls out the payer that rose, else the largest', () => {
    expect(highlightedPayer(mix)?.key).toBe('medicaid');
    const flat = toPayerMix(dto([['a', 'A', 51, 50], ['b', 'B', 49, 50]]));
    expect(highlightedPayer(flat)?.key).toBe('a');
    expect(highlightedPayer(toPayerMix(null))).toBeNull();
  });

  it('gives every payer group its own color and falls back for an unknown one', () => {
    const keys = ['commercial', 'medicare', 'medicaid', 'other-gov', 'va', 'facility', 'patient', 'not-recorded'];
    expect(new Set(keys.map((k) => payerColor(k).fill)).size).toBe(keys.length);
    expect(payerColor('something-new')).toEqual(payerColor('not-recorded'));
  });

  it('finds the month before, across a year end', () => {
    expect(priorPeriod('202610')).toBe('202609');
    expect(priorPeriod('202601')).toBe('202512');
    expect(priorPeriod('nope')).toBe('');
  });
});
