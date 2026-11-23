import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { EmsChipComponent } from '@ems-management-consultants/ui';
import { payerColor } from '../payer-mix/payer-mix';
import { SegmentOption, SegmentedControlComponent } from './segmented-control.component';

type Month = 'nov' | 'dec' | 'jan';

const MONTHS: readonly SegmentOption<Month>[] = [
  { value: 'nov', label: 'Nov 2026' },
  { value: 'dec', label: 'Dec 2026' },
  { value: 'jan', label: 'Jan 2027' },
];

interface PayerShare {
  readonly name: string;
  readonly amount: string;
  readonly percent: number;
  /** Token color for the bar segment and its legend swatch. */
  readonly color: string;
}

/** Layout only: placeholder shares until the projection endpoint exists. */
const PAYERS: readonly PayerShare[] = [
  { name: 'Commercial', amount: '$859K', percent: 70.7, color: payerColor('commercial').fill },
  { name: 'Medicare', amount: '$209K', percent: 17.2, color: payerColor('medicare').fill },
  { name: 'Medicaid', amount: '$83K', percent: 6.8, color: payerColor('medicaid').fill },
  { name: 'Other gov', amount: '$30K', percent: 2.5, color: payerColor('other-gov').fill },
  { name: 'VA', amount: '$28K', percent: 2.3, color: payerColor('va').fill },
  { name: 'Patient resp.', amount: '$6K', percent: 0.5, color: payerColor('patient').fill },
];

/** The projection for one future month: expected cash, range, how much is already billed, and the payer mix behind it. */
@Component({
  selector: 'app-projection-panel',
  imports: [EmsChipComponent, SegmentedControlComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="projection-title">
      <header class="panel__header">
        <h2 id="projection-title">Nov 2026 projection</h2>
        <ems-chip state="info" icon="query_stats">Projected</ems-chip>
        <app-segmented-control class="panel__months" label="Projected month" [options]="months" [(value)]="month" />
      </header>

      <dl class="panel__stats">
        <div>
          <dt>Expected net cash</dt>
          <dd class="panel__stat">$1.21M</dd>
          <dd class="panel__sub">$1,214,774.00</dd>
        </div>
        <div>
          <dt>Likely range</dt>
          <dd class="panel__stat">$1.15M to $1.28M</dd>
          <dd class="panel__sub">Wider further out</dd>
        </div>
        <div>
          <dt>From claims already billed</dt>
          <dd class="panel__stat">100%</dd>
          <dd class="panel__sub">$1,214,774.00</dd>
        </div>
        <div>
          <dt>Expected per transport</dt>
          <dd class="panel__stat">$8,477.00</dd>
          <dd class="panel__sub">After contractual adjustments</dd>
        </div>
      </dl>

      <div class="panel__mix" role="img" aria-label="Payer mix of the expected net cash">
        @for (payer of payers; track payer.name) {
          <span class="panel__segment" [style.flex-grow]="payer.percent" [style.background]="payer.color"></span>
        }
      </div>
      <ul class="panel__legend" aria-label="Payer mix">
        @for (payer of payers; track payer.name) {
          <li>
            <span class="panel__swatch" [style.background]="payer.color" aria-hidden="true"></span>
            {{ payer.name }} <strong>{{ payer.amount }}</strong>
          </li>
        }
      </ul>

      <p class="panel__note">
        Based on what each payer class paid on similar air claims over the last 18 months and how long it took, with new transports at October's volume
        and payer mix. Projections aren't guarantees.
      </p>
    </section>
  `,
  styles: `
    :host {
      display: block;
    }
    .panel {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.25rem;
      /* The same surface, ring and shadow as the library card, matching the other panels. */
      border-radius: var(--ems-corner-md);
      background: var(--ems-surface-neutral-default);
      box-shadow:
        inset 0 0 0 var(--ems-border-width-default) var(--ems-border-neutral-default),
        var(--ems-fx-neutral-default-frosted-raised-glowing);
      -webkit-backdrop-filter: var(--ems-fx-frosted);
      backdrop-filter: var(--ems-fx-frosted);
    }
    .panel__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }
    h2 {
      margin: 0;
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
      font-weight: var(--ems-font-weight-heavy);
    }
    .panel__months {
      margin-left: auto;
    }
    .panel__stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: 1rem;
      margin: 0;
    }
    dt,
    .panel__sub,
    .panel__note,
    .panel__legend {
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
    dd {
      margin: 0;
    }
    .panel__stat {
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
      color: var(--ems-text-default);
    }
    .panel__mix {
      display: flex;
      height: 1rem;
      overflow: hidden;
      border-radius: var(--ems-corner-xs, 0.25rem);
    }
    .panel__segment {
      flex-basis: 0;
    }
    .panel__legend {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
      gap: 0.5rem 1rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .panel__legend li {
      display: flex;
      align-items: center;
      gap: 0.375rem;
    }
    .panel__legend strong {
      color: var(--ems-text-default);
    }
    .panel__swatch {
      width: 0.75rem;
      height: 0.75rem;
      border-radius: 2px;
    }
    .panel__note {
      margin: 0;
    }
  `,
})
export class ProjectionPanelComponent {
  protected readonly months = MONTHS;
  protected readonly payers = PAYERS;
  protected readonly month = signal<Month>('nov');
}
