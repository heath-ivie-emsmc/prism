import { InsightCardDto, InsightLabelDto, WhatChangedDto } from './insights.service';

/** What a card looks like on screen. Everything is worked out here, so the template only prints it. */
export interface CardView {
  readonly key: string;
  readonly kind: string;
  readonly title: string;
  readonly body: string;
  readonly label: InsightLabelDto;
  /** The label as words, so its meaning never depends on its color. */
  readonly labelText: string;
  readonly icon: string;
  /** The amount beside the card, with its sign as a minus sign or a plus. */
  readonly amount: string;
  /** What the amount is, for the line under it. */
  readonly amountNote: string;
  /** The amount and its note as one phrase, for a screen reader. */
  readonly amountSpoken: string;
}

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

const LABELS: Record<InsightLabelDto, { text: string; icon: string }> = {
  temporary: { text: 'Temporary', icon: 'schedule' },
  structural: { text: 'Lasting', icon: 'trending_down' },
  watch: { text: 'Watch', icon: 'visibility' },
  info: { text: 'For information', icon: 'info' },
};

/** A signed dollar amount with a real minus sign. Zero has no sign. */
export function signedMoney(amount: number): string {
  if (Math.round(amount * 100) === 0) return money.format(0);
  return `${amount < 0 ? '−' : '+'}${money.format(Math.abs(amount))}`;
}

/** The dollars with no sign, for the sentence that already says whether cash rose or fell. */
export function plainMoney(amount: number): string {
  return money.format(Math.abs(amount));
}

export function toCardView(card: InsightCardDto, expected: boolean, index: number): CardView {
  const label = LABELS[card.label];
  const amount = signedMoney(card.impact);
  const amountNote = expected ? 'expected per month' : 'this month';
  return {
    key: `${expected ? 'expected' : 'driver'}-${card.kind}-${index}`,
    kind: card.kind,
    title: card.title,
    body: card.body,
    label: card.label,
    labelText: label.text,
    icon: label.icon,
    amount,
    amountNote,
    amountSpoken: `${amount} ${amountNote}`,
  };
}

export function toCardViews(dto: WhatChangedDto | null): readonly CardView[] {
  if (!dto) return [];
  return [...dto.drivers.map((c, i) => toCardView(c, false, i)), ...dto.expected.map((c, i) => toCardView(c, true, i))];
}

/** "10/31/2026" from "2026-10-31", or empty when the date is missing or not a date. */
export function formatDataThrough(date: string | null): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date ?? '');
  return match ? `${match[2]}/${match[3]}/${match[1]}` : '';
}

/** The line under the cards: what the figures are based on. */
export function basisLine(dto: WhatChangedDto): string {
  const through = formatDataThrough(dto.dataThrough);
  return through ? `Based on payments, payer mix and claim data through ${through}.` : 'Based on payments, payer mix and claim data.';
}
