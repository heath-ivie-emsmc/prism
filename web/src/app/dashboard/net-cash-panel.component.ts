import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { EmsButtonDirective, EmsChipComponent, EmsIconComponent, EmsMessageComponent, EmsPickerOption, EmsSelectComponent } from '@ems-management-consultants/ui';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ChartComponent, ChartOptionBuilder } from '../charts/chart.component';
import { NetCashMonth, boundedMonths, changeFromPrior, formatExact, formatLabel, summarizeNetCash } from '../charts/net-cash';
import { NetCashProjectionDto, NetCollectionsDto, NetCollectionsService } from '../metrics/net-collections.service';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { apPeriodLabel } from './dashboard-format';
import { SwitchComponent } from './switch.component';
import { buildNetCashOption } from '../charts/net-cash-chart.options';
import { SegmentOption, SegmentedControlComponent } from './segmented-control.component';

type ChartKind = 'line' | 'bar' | 'table';
type Range = '6' | '12';
type CallType = 'all' | 'interfacility' | 'scene';

/** The warehouse view behind this panel has no call type or pickup location, so those filters are shown but off until it does. */
const FILTER_NOT_AVAILABLE = 'Not available for this data yet';

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

interface TableRow {
  readonly month: string;
  readonly value: string;
  readonly change: string;
  readonly direction: 'up' | 'down' | null;
  readonly latest: boolean;
}

/** The large net cash panel: filters, the headline for the latest month, and the line, bar or table view of the months. */
@Component({
  selector: 'app-net-cash-panel',
  imports: [SkeletonComponent, ChartComponent, EmsButtonDirective, LoadProblemComponent, EmsChipComponent, EmsIconComponent, EmsMessageComponent, EmsSelectComponent, ReactiveFormsModule, RouterLink, SegmentedControlComponent, SwitchComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="net-cash-title" [attr.aria-busy]="series.isLoading()">
      <header class="panel__header">
        <h2 id="net-cash-title">Net cash collected</h2>
        <app-segmented-control label="Chart type" [options]="chartKinds" [(value)]="chartKind" [iconOnly]="true" />
      </header>

      <div class="panel__filters">
        <app-segmented-control label="Date range" [options]="ranges" [(value)]="range" />
        <app-segmented-control label="Call type" [options]="callTypes" [(value)]="callType" [disabled]="true" [title]="notAvailable" />
        <ems-select class="panel__pickup" label="Pickup location" [options]="pickupLocations" [formControl]="pickup" [clearable]="false" [title]="notAvailable" />
        <app-switch class="panel__projection" label="Show 3-month projection" [(checked)]="showProjection" [disabled]="chartKind() === 'table'" />
      </div>

      @if (projectionMissing()) {
        <ems-message state="warn">{{ projectionMessage() }}</ems-message>
      }

      @if (loadError() !== undefined) {
        <app-load-problem [error]="loadError()" subject="net cash" (retry)="retry()" />
      } @else if (waiting()) {
        <p class="cdk-visually-hidden" role="status">Loading net cash</p>
        <div class="panel__skeleton" aria-hidden="true">
          <div class="panel__skeleton-headline">
            <app-skeleton width="9rem" height="2.5rem" />
            <app-skeleton width="5rem" height="1.75rem" radius="999px" />
            <app-skeleton width="12rem" height="0.875rem" />
          </div>
          <app-skeleton class="panel__skeleton-chart" width="100%" height="100%" />
        </div>
      } @else if (months().length === 0) {
        <p class="panel__state" role="status">No net cash data for this period.</p>
      } @else {
        @if (headline(); as headline) {
          <div class="panel__headline">
            <div class="panel__total">
              <a class="panel__amount panel__amount-link" routerLink="/dashboard/net-cash" [queryParams]="breakdownParams()" [attr.aria-label]="headline.amount + ', see what drove ' + breakdownLabel()">
                {{ headline.amount }}
                <ems-icon name="arrow_forward" size="sm" />
              </a>
              @if (headline.change) {
                <ems-chip [state]="headline.up ? 'success' : 'error'" [icon]="headline.up ? 'trending_up' : 'trending_down'">{{ headline.change }}</ems-chip>
              }
              <span class="panel__detail">{{ headline.detail }}</span>
            </div>
            <!-- Always laid out, only hidden while the switch is off, so turning it on cannot move anything else on the page. -->
            <div class="panel__expected" [class.panel__expected--off]="!projected()" [attr.aria-hidden]="!projected()" [attr.inert]="projected() ? null : ''">
              <span class="panel__detail">Expected, next 3 months</span>
              <span class="panel__expected-amount">{{ expectedTotal() }}</span>
              <span class="panel__detail">Likely {{ expectedRange() }}</span>
            </div>
          </div>
        }

        @if (chartKind() === 'table') {
          <div class="panel__table-wrap" role="region" aria-label="Net cash by month" tabindex="0">
            <table class="panel__table">
              <caption class="panel__sr">Net cash collected by month</caption>
              <thead>
                <tr>
                  <th scope="col">Month</th>
                  <th scope="col" class="panel__num">Net cash</th>
                  <th scope="col" class="panel__num">Change vs prior month</th>
                </tr>
              </thead>
              <tbody>
                @for (row of rows(); track row.month) {
                  <tr>
                    <th scope="row">{{ row.month }}</th>
                    <td class="panel__num">
                      <span [class.panel__strong]="row.latest">{{ row.value }}</span>
                    </td>
                    <td class="panel__num">
                      @if (row.direction) {
                        <ems-icon [name]="row.direction === 'up' ? 'arrow_upward' : 'arrow_downward'" size="sm" />
                      }
                      {{ row.change }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <app-chart [option]="option()" [label]="summary()" />
        }
      }

      <p class="panel__note">Cash is by the accounting period the payment was posted in, from the latest data load.</p>
    </section>
  `,
  styles: `
    :host {
      display: block;
    }
    .panel {
      height: 100%;
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.25rem;
      /* The same surface, ring and shadow as the library card the other panels use, so they read as one set. */
      border-radius: var(--ems-corner-md);
      background: var(--ems-surface-neutral-default);
      box-shadow:
        inset 0 0 0 var(--ems-border-width-default) var(--ems-border-neutral-default),
        var(--ems-fx-neutral-default-frosted-raised-glowing);
      -webkit-backdrop-filter: var(--ems-fx-frosted);
      backdrop-filter: var(--ems-fx-frosted);
    }
    /* The chart takes whatever height the panel has left, never less than a readable chart. */
    app-chart {
      flex: 1 1 0;
      min-height: 12rem;
      --chart-height: 100%;
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
    .panel__projection {
      margin-left: auto;
    }
    .panel__expected {
      display: flex;
      flex-direction: column;
      padding-left: 1rem;
      border-left: 2px dotted var(--ems-border-neutral-default);
    }
    .panel__expected--off {
      visibility: hidden;
    }
    .panel__expected-amount {
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
      font-weight: var(--ems-font-weight-heavy);
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
    .panel__amount-link {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      min-height: 2.75rem;
      color: inherit;
      text-decoration: none;
    }
    .panel__amount-link:hover .panel__amount-arrow,
    .panel__amount-link:hover ems-icon {
      color: var(--ems-text-primary);
    }
    .panel__amount-link:focus-visible {
      outline: 2px solid var(--ems-border-focus-default, currentColor);
      outline-offset: 2px;
      border-radius: var(--ems-corner-sm, 0.5rem);
    }
    .panel__amount {
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .panel__detail,
    .panel__note {
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
    .panel__note {
      margin: 0;
    }
    .panel__skeleton {
      display: flex;
      flex: 1 1 0;
      flex-direction: column;
      gap: 1rem;
      min-height: 14rem;
    }
    .panel__skeleton-headline {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem 1rem;
    }
    .panel__skeleton-chart {
      flex: 1 1 0;
      min-height: 12rem;
    }
    .panel__state {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding: 2rem 0;
      color: var(--ems-text-muted);
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
  private readonly netCollections = inject(NetCollectionsService);

  /** The Provider whose figures to show, from the signed-in user's own Providers. The API checks it again. */
  readonly providerId = input.required<string | null>();

  /** The newest accounting period to show, as yyyyMM. Empty until the periods have loaded. */
  readonly endPeriod = input.required<string>();

  /** Which Service Line the projection covers, from the dashboard toggle. */
  readonly serviceLine = input<'air' | 'ground' | 'both'>('both');

  /** The error from loading the list of periods, when it failed: without it there is no month to ask for, so the panel has nothing to show. */
  readonly periodsError = input<unknown>(undefined);

  /** The list of months is still loading, so there is no month to ask for yet. */
  readonly periodsLoading = input(false);

  /** The person asked to try again after the periods failed to load. */
  readonly retryPeriods = output<void>();

  protected readonly chartKinds = CHART_KINDS;
  protected readonly ranges = RANGES;
  protected readonly callTypes = CALL_TYPES;
  protected readonly pickupLocations = PICKUP_LOCATIONS;
  protected readonly notAvailable = FILTER_NOT_AVAILABLE;

  protected readonly chartKind = signal<ChartKind>('line');
  protected readonly range = signal<Range>('6');
  protected readonly callType = signal<CallType>('all');
  protected readonly pickup = new FormControl<string>({ value: 'all', disabled: true }, { nonNullable: true });

  private readonly apiServiceLine = computed<'all' | 'air' | 'ground'>(() => {
    const line = this.serviceLine();
    return line === 'both' ? 'all' : line;
  });

  /** The figures follow the Service Line toggle: air or ground alone, or both added together. */
  protected readonly series = rxResource<NetCollectionsDto | null, { providerId: string; endPeriod: string; months: number; serviceLine: 'all' | 'air' | 'ground' } | null>({
    params: () =>
      this.providerId() && this.endPeriod()
        ? { providerId: this.providerId()!, endPeriod: this.endPeriod(), months: Number(this.range()), serviceLine: this.apiServiceLine() }
        : null,
    stream: ({ params }) => (params ? this.netCollections.series(params.providerId, params.endPeriod, params.months, params.serviceLine) : of(null)),
  });

  /** Why nothing can be shown, if something failed: the periods first, since the figures cannot be asked for without one. */
  protected readonly loadError = computed<unknown>(() => this.periodsError() ?? this.series.error());

  /** Waiting for figures: the months first, then the series. Without this the panel would say "no data" in the moment before it asks. */
  protected readonly waiting = computed(() => this.periodsLoading() || (this.series.isLoading() && !this.series.hasValue()));

  protected retry(): void {
    if (this.periodsError() !== undefined) {
      this.retryPeriods.emit();
    } else {
      this.series.reload();
    }
  }

  protected readonly months = computed<readonly NetCashMonth[]>(() =>
    boundedMonths(((this.series.hasValue() ? this.series.value() : undefined)?.points ?? []).map((p) => ({ label: p.label, year: p.year, value: p.value, kind: 'actual' as const }))),
  );

  /** The month and Service Line the breakdown page opens on: the same ones this panel shows. `both` opens on air. */
  protected readonly breakdownParams = computed(() => ({ period: this.endPeriod(), line: this.serviceLine() === 'ground' ? 'ground' : 'air' }));
  protected readonly breakdownLabel = computed(() => (this.serviceLine() === 'ground' ? 'level of service' : 'aircraft type'));

  protected readonly showProjection = signal(false);

  /** Asked for only while the switch is on and a chart (not the table) is showing. */
  protected readonly projection = rxResource<NetCashProjectionDto | null, { providerId: string; endPeriod: string; serviceLine: 'all' | 'air' | 'ground' } | null>({
    params: () =>
      this.showProjection() && this.chartKind() !== 'table' && this.providerId() && this.endPeriod()
        ? { providerId: this.providerId()!, endPeriod: this.endPeriod(), serviceLine: this.apiServiceLine() }
        : null,
    stream: ({ params }) => (params ? this.netCollections.projection(params.providerId, params.endPeriod, params.serviceLine) : of(null)),
  });

  /** The forecast months and their sums, or null while off, loading, failed, or when the warehouse has no forecast. */
  protected readonly projected = computed(() => {
    const value = this.projection.hasValue() ? this.projection.value() : null;
    if (!value || value.months.length === 0 || this.chartKind() === 'table' || !this.showProjection()) return null;
    return {
      total: value.total,
      low: value.low,
      high: value.high,
      months: value.months.map((m): NetCashMonth => ({ label: m.label, year: m.year, value: m.value, kind: 'projected', low: m.low, high: m.high })),
    };
  });
  /** The switch is on but there is nothing to draw. */
  /** Why the projection is not drawn: it failed to load, or the forecast does not reach the months after this period. */
  protected readonly projectionMessage = computed(() =>
    this.projection.error() ? "We can't load the projection right now. Try again in a moment." : 'There is no forecast for the months after this period.',
  );

  protected readonly projectionMissing = computed(() => this.showProjection() && this.chartKind() !== 'table' && !this.projection.isLoading() && (this.projection.error() !== undefined || (this.projection.hasValue() && this.projected() === null)));
  protected readonly expectedTotal = computed(() => formatLabel(this.projected()?.total ?? 3_000_000));
  protected readonly expectedRange = computed(() => `${formatLabel(this.projected()?.low ?? 0)} to ${formatLabel(this.projected()?.high ?? 0)}`);

  protected readonly chartMonths = computed<readonly NetCashMonth[]>(() => [...this.months(), ...(this.projected()?.months ?? [])]);
  protected readonly summary = computed(() => summarizeNetCash(this.chartMonths()));
  protected readonly option = computed<ChartOptionBuilder>(() => {
    const months = this.chartMonths();
    const kind = this.chartKind() === 'bar' ? 'bar' : 'line';
    return (colors) => buildNetCashOption(months, kind, colors);
  });

  /** The latest month's figure, how it moved from the month before, and both amounts written out. */
  protected readonly headline = computed(() => {
    const points = (this.series.hasValue() ? this.series.value() : undefined)?.points ?? [];
    const latest = points.at(-1);
    if (!latest) return null;
    const prior = points.at(-2);
    const change = prior && prior.value !== 0 ? Math.round(((latest.value - prior.value) / prior.value) * 100) : null;
    return {
      amount: formatLabel(latest.value),
      up: (change ?? 0) >= 0,
      change: change === null || !prior ? '' : `${change > 0 ? '+' : ''}${change}% vs ${prior.label}`,
      detail: prior
        ? `${formatExact(latest.value)} in ${apPeriodLabel(latest.period).split(' ')[0]} · ${formatExact(prior.value)} in ${apPeriodLabel(prior.period).split(' ')[0]}`
        : `${formatExact(latest.value)} in ${apPeriodLabel(latest.period).split(' ')[0]}`,
    };
  });

  protected readonly rows = computed<readonly TableRow[]>(() => {
    const months = this.months();
    return months.map((m, i) => {
      const change = changeFromPrior(months, i);
      return {
        month: `${m.label} ${m.year}`,
        value: formatExact(m.value),
        change: change === null ? '' : `${Math.abs(change).toFixed(1)}%`,
        direction: change === null ? null : change >= 0 ? 'up' : 'down',
        latest: i === months.length - 1,
      };
    });
  });
}
