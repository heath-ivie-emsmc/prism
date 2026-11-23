import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { PayerShare, formatShare, shareColor } from './payer-mix';

interface Segment {
  readonly key: string;
  readonly width: number;
  readonly fill: string;
  readonly text: string;
  readonly label: string;
  readonly title: string;
  readonly drill: boolean;
}

interface BarRow {
  readonly month: string;
  readonly label: string;
  readonly segments: readonly Segment[];
}

/** Two bars that each add up to 100%: the share of transports by payer group in the earlier month, then the later one. */
@Component({
  selector: 'app-payer-stacked',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (row of rows(); track row.month) {
      <div class="row">
        <span class="row__month" aria-hidden="true">{{ row.month }}</span>
        <div class="bar" role="img" [attr.aria-label]="row.label">
          @for (segment of row.segments; track segment.key) {
            <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -- pointer shortcut only: the key under the bars has a real link for every group that has one, and a segment can be too narrow for a 24px target -->
            <span class="bar__segment" [class.bar__segment--drill]="segment.drill" (click)="segment.drill && drill.emit(segment.key)" [style.width.%]="segment.width" [style.background]="segment.fill" [style.color]="segment.text" [title]="segment.title">{{ segment.width >= 8 ? segment.label : '' }}</span>
          }
        </div>
      </div>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .row {
      display: grid;
      grid-template-columns: 5.5rem minmax(0, 1fr);
      align-items: center;
      gap: 0.75rem;
    }
    @media (max-width: 479.98px) {
      .row {
        grid-template-columns: minmax(0, 1fr);
        gap: 0.25rem;
      }
    }
    .row__month {
      font-weight: var(--ems-font-weight-bold);
    }
    .bar {
      display: flex;
      height: 2.25rem;
      overflow: hidden;
      border-radius: 6px;
      box-shadow: inset 0 0 0 1px var(--ems-border-neutral-default);
    }
    .bar__segment {
      display: flex;
      align-items: center;
      justify-content: center;
      min-width: 0;
      font-size: var(--ems-font-size-small);
      font-weight: var(--ems-font-weight-bold);
      /* A gap between neighbours so adjacent colors never run together. */
      border-inline-end: 2px solid var(--ems-surface-neutral-default);
      overflow: hidden;
    }
    .bar__segment--drill {
      cursor: pointer;
    }
    .bar__segment--drill:hover {
      filter: brightness(1.15);
    }
    .bar__segment:last-child {
      border-inline-end: 0;
    }
  `,
})
export class PayerStackedComponent {
  readonly shares = input.required<readonly PayerShare[]>();
  readonly currentName = input.required<string>();
  readonly priorName = input.required<string>();
  /** Full month names for the row labels, such as "September". */
  readonly currentLong = input.required<string>();
  readonly priorLong = input.required<string>();
  /** A group with a drill link was clicked. The keyboard path is the key's links. */
  readonly drill = output<string>();

  protected readonly rows = computed<readonly BarRow[]>(() => {
    const shares = this.shares();
    const build = (month: string, short: string, pick: (s: PayerShare) => number): BarRow => ({
      month,
      label: `${month}: ${shares.map((s) => `${s.name} ${formatShare(pick(s))}`).join(', ')}`,
      segments: shares
        .filter((s) => pick(s) > 0)
        .map((s): Segment => ({
          key: s.key,
          width: pick(s),
          fill: shareColor(s).fill,
          text: shareColor(s).text,
          label: formatShare(pick(s)),
          title: `${s.name}, ${short}: ${formatShare(pick(s))}`,
          drill: !!s.drill,
        })),
    });
    return [build(this.priorLong(), this.priorName(), (s) => s.priorPercent), build(this.currentLong(), this.currentName(), (s) => s.percent)];
  });
}
