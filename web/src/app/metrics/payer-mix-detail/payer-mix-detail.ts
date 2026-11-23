import { PayerMixFigures, PayerShare, paletteColor, toPayerMix } from '../payer-mix/payer-mix';
import { PayerMixDetailDto } from './payer-mix-detail.dto';

/**
 * The payers inside one payer group as the same shares the payer mix draws, so the bar chart, donuts and summary are reused as they are.
 * A payer has no fixed color, so each takes the next one in the palette, in the order the API listed them (largest first, "Other" last).
 * The key is the position, not the name: names come from the warehouse and are only ever displayed.
 */
export function toPayerDetailMix(dto: PayerMixDetailDto | null): PayerMixFigures {
  const rows = dto?.payers ?? [];
  const figures = toPayerMix({
    metric: dto?.metric ?? { key: 'payer_mix_detail', name: '', description: '', unit: 'transports' },
    providerId: dto?.providerId ?? '',
    period: dto?.period ?? null,
    priorPeriod: dto?.priorPeriod ?? null,
    serviceLine: 'all',
    group: null,
    groupName: null,
    shares: rows.map((p, i) => ({ key: `payer-${i}`, name: p.name, current: p.current, prior: p.prior })),
  });
  return { ...figures, shares: figures.shares.map((s, i): PayerShare => ({ ...s, ...paletteColor(i) })) };
}
