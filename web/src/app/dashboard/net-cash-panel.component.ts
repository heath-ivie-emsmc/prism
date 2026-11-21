import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { EmsChipComponent, EmsIconComponent, EmsMessageComponent, EmsPickerOption, EmsSelectComponent } from '@ems-management-consultants/ui';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ChartComponent, ChartOptionBuilder } from '../charts/chart.component';
import { NetCashMonth, boundedMonths, changeFromPrior, formatExact, summarizeNetCash, formatCompact } from '../charts/net-cash';
import { buildNetCashOption } from '../charts/net-cash-chart.options';
import { SegmentOption, SegmentedControlComponent } from './segmented-control.component';

type ChartKind = 'line' | 'bar' | 'table';
type Range = '6' | '12';
type CallType = 'all' | 'interfacility' | 'scene';

const CHART_KINDS: readonly SegmentOption<ChartKind>[] = [
  { value: 'line', label: 'Line chart', icon: 'show_chart' },
  { value: 'bar', label: 'Bar chart', icon: 'bar_chart' },
  { value: 'table', label: 'Table', icon: 'table_rows' },
];
const RANGES: readonly SegmentOption<Range>[] = [
  { value: '6', label: 'Last 6 months' },
  { value: '12', label: 'Last 12 months' },
];
const CALL_TYPES: readonly SegmentOption<CallType>[] = [
  { value: 'all', label: 'All call types' },
  { value: 'interfacility', label: 'Interfacility' },
  { value: 'scene', label: 'Scene' },
];
const PICKUP_LOCATIONS: EmsPickerOption<string>[] = [{ value: 'all', label: 'All pickup locations' }];

/** Layout only: placeholder figures until the Metric Catalog serves the net cash series. */
const PLACEHOLDER_MONTHS: readonly NetCashMonth[] = [
  { label: 'May', year: 2026, value: 1_180_000, kind: 'actual' },
  { label: 'Jun', year: 2026, value: 1_220_000, kind: 'actual' },
  { label: 'Jul', year: 2026, value: 1_200_000, kind: 'actual' },
  { label: 'Aug', year: 2026, value: 1_240_000, kind: 'actual' },
  { label: 'Sep', year: 2026, value: 1_250_460, kind: 'actual' },
  { label: 'Oct', year: 2026, value: 1_100_240, kind: 'actual' },
  { label: 'Nov', year: 2026, value: 1_214_774, kind: 'projected', low: 1_150_000, high: 1_280_000 },
  { label: 'Dec', year: 2026, value: 1_204_722, kind: 'projected', low: 1_120_000, high: 1_290_000 },
  { label: 'Jan', year: 2027, value: 1_197_967, kind: 'projected', low: 1_080_000, high: 1_320_000 },
];

interface TableRow {
  readonly month: string;
  readonly value: string;
  readonly range: string;
  readonly change: string;
  readonly direction: 'up' | 'down' | null;
  readonly projected: boolean;
  readonly latest: boolean;
}

/** The large Air net cash panel: filters, headline, narrative, trend and projection. */
@Component({
  selector: 'app-net-cash-panel',
  imports: [ChartComponent, EmsChipComponent, EmsIconComponent, EmsMessageComponent, EmsSelectComponent, ReactiveFormsModule, SegmentedControlComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="net-cash-title">
      <header class="panel__header">
        <h2 id="net-cash-title">Air net cash collected</h2>
        <app-segmented-control label="Chart type" [options]="chartKinds" [(value)]="chartKind" [iconOnly]="true" />
      </header>

      <div class="panel__filters">
        <app-segmented-control label="Date range" [options]="ranges" [(value)]="range" />
        <app-segmented-control label="Call type" [options]="callTypes" [(value)]="callType" />
        <ems-select class="panel__pickup" label="Pickup location" [options]="pickupLocations" [formControl]="pickup" [clearable]="false" />
        <label class="panel__switch">
          <input type="checkbox" role="switch" [checked]="showProjection()" (change)="showProjection.set(!showProjection())" />
          Show 3-month projection
        </label>
      </div>

      <div class="panel__headline">
        <div class="panel__total">
          <span class="panel__amount">$1.10M</span>
          <ems-chip state="error" icon="trending_down">-12% vs Sep</ems-chip>
          <span class="panel__detail">$1,100,240.00 in October · $1,250,460.00 in September</span>
        </div>
        @if (showProjection()) {
          <div class="panel__expected">
            <span class="panel__expected-label">Expected, next 3 months</span>
            <span class="panel__expected-amount">$3.62M</span>
            <span class="panel__expected-range">Likely $3.35M to $3.88M</span>
          </div>
        }
      </div>

      <ems-message state="brand" icon="auto_awesome">
        Most of the drop is in fixed wing. A one-time catch-up payment inflated September, and more October patients were on Medicaid.
        2 denied claims need action. November should recover to about $1.21M as Medicare pays October transports.
        <a href="#what-changed" class="panel__link">See what changed</a>
      </ems-message>

      @if (chartKind() === 'table') {
        <div class="panel__table-wrap" role="region" aria-label="Net cash by month" tabindex="0">
          <table class="panel__table">
            <caption class="panel__sr">Net cash collected by month</caption>
            <thead>
              <tr>
                <th scope="col">Month</th>
                <th scope="col" class="panel__num">Net cash</th>
                <th scope="col" class="panel__num">Change vs prior month</th>
                <th scope="col">Type</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.month) {
                <tr [class.panel__row--projected]="row.projected">
                  <th scope="row">{{ row.month }}</th>
                  <td class="panel__num">
                    <span [class.panel__strong]="row.latest">{{ row.value }}</span>
                    @if (row.range) {
                      <span class="panel__range">{{ row.range }}</span>
                    }
                  </td>
                  <td class="panel__num">
                    @if (row.direction) {
                      <ems-icon [name]="row.direction === 'up' ? 'arrow_upward' : 'arrow_downward'" size="sm" />
                    }
                    {{ row.change }}
                  </td>
                  <td>
                    @if (row.projected) {
                      <ems-chip state="info">Projected</ems-chip>
                    } @else {
                      Actual
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <app-chart [option]="option()" [label]="summary()" />
      }

      <p class="panel__note">
        Select a projected month to see its detail below, or an actual month to see what drove it. Cash is by payment date.
        Air claims can take 180 days or more to collect.
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
      border: 1px solid var(--ems-border-neutral-subtle);
      border-radius: var(--ems-corner-xl, 1rem);
      background: var(--ems-surface-neutral-subtle);
    }
    .panel__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
    }
    h2 {
      margin: 0;
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
      font-weight: var(--ems-font-weight-heavy);
    }
    .panel__filters {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 1rem;
    }
    .panel__pickup {
      min-width: 14rem;
    }
    .panel__switch {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      margin-left: auto;
      font-weight: var(--ems-font-weight-bold);
    }
    .panel__headline {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
    }
    .panel__total {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }
    .panel__amount {
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .panel__detail,
    .panel__expected-label,
    .panel__expected-range,
    .panel__note {
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
    .panel__expected {
      display: flex;
      flex-direction: column;
      padding-left: 1rem;
      border-left: 1px dashed var(--ems-border-neutral-default);
    }
    .panel__expected-amount {
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .panel__link {
      display: block;
      margin-top: 0.25rem;
      font-weight: var(--ems-font-weight-bold);
    }
    .panel__note {
      margin: 0;
    }
    .panel__table-wrap {
      overflow-x: auto;
      border: 1px solid var(--ems-border-neutral-default);
      border-radius: var(--ems-corner-md, 0.5rem);
      background: var(--ems-surface-neutral-elevated);
    }
    .panel__table {
      width: 100%;
      border-collapse: collapse;
    }
    .panel__table th,
    .panel__table td {
      padding: 0.75rem 1rem;
      text-align: left;
      vertical-align: top;
      border-bottom: 1px solid var(--ems-border-neutral-subtle);
      line-height: var(--ems-line-height-body);
    }
    .panel__table thead th {
      background: var(--ems-surface-neutral-subtle);
      font-weight: var(--ems-font-weight-bold);
    }
    .panel__table tbody th {
      color: var(--ems-text-primary);
      font-weight: var(--ems-font-weight-bold);
    }
    .panel__table tr:last-child th,
    .panel__table tr:last-child td {
      border-bottom: 0;
    }
    .panel__table .panel__num {
      text-align: right;
    }
    .panel__row--projected {
      background: var(--ems-surface-primary-subtlest);
    }
    .panel__range {
      display: block;
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
    .panel__strong {
      font-weight: var(--ems-font-weight-heavy);
    }
    .panel__sr {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class NetCashPanelComponent {
  protected readonly chartKinds = CHART_KINDS;
  protected readonly ranges = RANGES;
  protected readonly callTypes = CALL_TYPES;
  protected readonly pickupLocations = PICKUP_LOCATIONS;

  protected readonly chartKind = signal<ChartKind>('line');
  protected readonly range = signal<Range>('6');
  protected readonly callType = signal<CallType>('all');
  protected readonly showProjection = signal(true);
  private readonly months = computed(() => {
    const all = boundedMonths(PLACEHOLDER_MONTHS);
    return this.showProjection() ? all : all.filter((m) => m.kind === 'actual');
  });

  protected readonly summary = computed(() => summarizeNetCash(this.months()));
  protected readonly option = computed<ChartOptionBuilder>(() => {
    const months = this.months();
    const kind = this.chartKind() === 'bar' ? 'bar' : 'line';
    return (colors) => buildNetCashOption(months, kind, colors);
  });
  protected readonly rows = computed<readonly TableRow[]>(() => {
    const months = this.months();
    const lastActual = months.reduce((last, m, i) => (m.kind === 'actual' ? i : last), -1);
    return months.map((m, i) => {
      const change = changeFromPrior(months, i);
      return {
        month: `${m.label} ${m.year}`,
        value: formatExact(m.value),
        range: m.low !== undefined && m.high !== undefined ? `${formatCompact(m.low)} to ${formatCompact(m.high)}` : '',
        change: change === null ? '' : `${Math.abs(change).toFixed(1)}%`,
        direction: change === null ? null : change >= 0 ? 'up' : 'down',
        projected: m.kind === 'projected',
        latest: i === lastActual,
      };
    });
  });
  protected readonly pickup = new FormControl<string>('all', { nonNullable: true });
}
