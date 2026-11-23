import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { PayerShare, formatShare, shareColor } from './payer-mix';

/** The ring is 16 wide, so the hole has a radius of 40 (in a 120-wide drawing): the middle text has to fit inside that. */
const RADIUS = 48;
const STROKE = 16;
/** A name longer than this is split over two lines in the middle. */
const LINE_CHARS = 12;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
/** A small gap between neighbours, in circumference units, so adjacent colors never run together. */
const GAP = 1.2;

/** One line if the name is short, else two lines split at the space nearest the middle, so a long name fits inside the hole. */
export function splitName(name: string): string[] {
  const words = name.split(' ').filter(Boolean);
  if (name.length <= LINE_CHARS || words.length < 2) return name ? [name] : [];
  let best = 1;
  for (let i = 1; i < words.length; i++) {
    const gap = (n: number): number => Math.abs(words.slice(0, n).join(' ').length - words.slice(n).join(' ').length);
    if (gap(i) < gap(best)) best = i;
  }
  return [words.slice(0, best).join(' '), words.slice(best).join(' ')];
}

interface Arc {
  readonly key: string;
  readonly fill: string;
  readonly dash: string;
  readonly offset: number;
  readonly title: string;
  readonly drill: boolean;
}

interface Donut {
  readonly month: string;
  readonly label: string;
  readonly arcs: readonly Arc[];
  readonly centre: string;
}

/** One donut per month, each adding up to 100%, with the called-out payer's share in the middle. Drawn as SVG, so it scales and reads aloud. */
@Component({
  selector: 'app-payer-donuts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (donut of donuts(); track donut.month) {
      <figure class="donut">
        <svg viewBox="0 0 120 120" role="img" [attr.aria-label]="donut.label">
          <g transform="rotate(-90 60 60)">
            @for (arc of donut.arcs; track arc.key) {
 <!-- Pointer shortcut only: the key under the donuts has a real link for every group that has one. -->
              <circle class="arc" [class.arc--drill]="arc.drill" (click)="arc.drill && drill.emit(arc.key)" cx="60" cy="60" [attr.r]="radius" fill="none" [attr.stroke-width]="stroke" [attr.stroke]="arc.fill" [attr.stroke-dasharray]="arc.dash" [attr.stroke-dashoffset]="arc.offset">
                <title>{{ arc.title }}</title>
              </circle>
            }
          </g>
          <text x="60" y="49" class="donut__value" text-anchor="middle" dominant-baseline="central">{{ donut.centre }}</text>
          <text class="donut__name" text-anchor="middle">
            @for (line of calloutLines(); track $index) {
              <tspan x="60" [attr.y]="66 + $index * 10">{{ line }}</tspan>
            }
          </text>
        </svg>
        <figcaption>{{ donut.month }}</figcaption>
      </figure>
    }
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 14rem), 1fr));
      gap: 1rem;
    }
    .donut {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.5rem;
      margin: 0;
    }
    svg {
      width: min(100%, 15rem);
      height: auto;
    }
    .arc--drill {
      cursor: pointer;
    }
    .arc--drill:hover {
      stroke-width: 20;
    }
    .donut__value {
      fill: var(--ems-text-default);
      font-size: 22px;
      font-weight: 800;
    }
    .donut__name {
      fill: var(--ems-text-default);
      font-size: 8.5px;
      font-weight: 600;
    }
    figcaption {
      font-weight: var(--ems-font-weight-bold);
    }
  `,
})
export class PayerDonutsComponent {
  readonly shares = input.required<readonly PayerShare[]>();
  readonly callout = input<PayerShare | null>(null);
  readonly currentName = input.required<string>();
  readonly priorName = input.required<string>();
  readonly currentLong = input.required<string>();
  readonly priorLong = input.required<string>();
  /** A group with a drill link was clicked. The keyboard path is the key's links. */
  readonly drill = output<string>();

  protected readonly radius = RADIUS;
  protected readonly stroke = STROKE;
  protected readonly calloutLines = computed(() => splitName(this.callout()?.name ?? ''));

  protected readonly donuts = computed<readonly Donut[]>(() => {
    const shares = this.shares();
    const callout = this.callout();
    const build = (month: string, short: string, pick: (s: PayerShare) => number): Donut => {
      let used = 0;
      const arcs = shares
        .filter((s) => pick(s) > 0)
        .map((s): Arc => {
          const length = (pick(s) / 100) * CIRCUMFERENCE;
          const arc: Arc = {
            key: s.key,
            fill: shareColor(s).fill,
            dash: `${Math.max(0.5, length - GAP)} ${CIRCUMFERENCE}`,
            offset: -used,
            title: `${s.name}, ${short}: ${formatShare(pick(s))}`,
            drill: !!s.drill,
          };
          used += length;
          return arc;
        });
      return {
        month,
        label: `${month}: ${shares.map((s) => `${s.name} ${formatShare(pick(s))}`).join(', ')}`,
        arcs,
        centre: callout ? formatShare(pick(callout)) : '',
      };
    };
    return [build(this.priorLong(), this.priorName(), (s) => s.priorPercent), build(this.currentLong(), this.currentName(), (s) => s.percent)];
  });
}
