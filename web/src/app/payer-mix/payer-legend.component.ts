import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { PayerShare, formatShare, payerColor } from './payer-mix';

/** The key under the stacked bars and donuts: a swatch, the payer group, and its share in both months. The numbers carry the meaning, the swatch only matches the colors. */
@Component({
  selector: 'app-payer-legend',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="legend" aria-label="Payer groups">
      @for (item of items(); track item.key) {
        <li class="legend__item">
          <span class="legend__swatch" [style.background]="item.color" aria-hidden="true"></span>
          <span class="legend__name">{{ item.name }}</span>
          <strong class="legend__current">{{ item.current }}</strong>
          <span class="legend__prior">{{ currentName() }} · {{ item.prior }} {{ priorName() }}</span>
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      display: block;
      container-type: inline-size;
    }
    .legend {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 19rem), 1fr));
      gap: 0.5rem 2rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    /* A row per payer group with the same columns in every row, so the swatches, names and figures line up whatever the name's length. */
    .legend__item {
      display: grid;
      grid-template-columns: 0.875rem minmax(0, 1fr) 2.75rem 7.5rem;
      align-items: baseline;
      gap: 0 0.5rem;
    }
    .legend__swatch {
      align-self: center;
      width: 0.875rem;
      height: 0.875rem;
      border-radius: 3px;
      box-shadow: inset 0 0 0 1px var(--ems-border-neutral-default);
    }
    .legend__current {
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
    .legend__prior {
      color: var(--ems-text-muted);
      white-space: nowrap;
      font-variant-numeric: tabular-nums;
    }
    /* Too narrow for all four columns: the earlier month's share goes under the name. */
    @container (max-width: 24rem) {
      .legend__item {
        grid-template-columns: 0.875rem minmax(0, 1fr) auto;
      }
      .legend__prior {
        grid-column: 2 / -1;
      }
    }
  `,
})
export class PayerLegendComponent {
  readonly shares = input.required<readonly PayerShare[]>();
  readonly currentName = input.required<string>();
  readonly priorName = input.required<string>();

  protected items(): { key: string; name: string; color: string; current: string; prior: string }[] {
    return this.shares().map((s) => ({ key: s.key, name: s.name, color: payerColor(s.key).fill, current: formatShare(s.percent), prior: formatShare(s.priorPercent) }));
  }
}
