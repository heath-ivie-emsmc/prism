import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { map, of } from 'rxjs';
import { EmsBreadcrumb, EmsBreadcrumbsComponent, EmsMessageComponent, EmsPickerOption, EmsSelectComponent } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { DataFrameComponent } from '../shared/data-frame.component';
import { frameContext } from '../shared/fullscreen';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { ChartComponent } from '../charts/chart.component';
import { monthName, priorMonthName, priorPeriod } from '../breakdown/net-cash-breakdown';
import { BreakdownLine } from '../breakdown/net-cash-breakdown.model';
import { apPeriodEnd, apPeriodLabel, formatDate } from '../dashboard/dashboard-format';
import { SegmentOption, SegmentedControlComponent } from '../dashboard/segmented-control.component';
import { NetCollectionsPeriodsDto, NetCollectionsService, PayerMixDto } from '../metrics/net-collections.service';
import { WhatChangedComponent } from '../insights/what-changed.component';
import { PayerDonutsComponent } from './payer-donuts.component';
import { PayerLegendComponent } from './payer-legend.component';
import { PayerStackedComponent } from './payer-stacked.component';
import { biggestRise, buildPayerMixOption, describePayerMix, formatPoints, formatShare, highlightedPayer, summarizePayerMix, toPayerMix } from './payer-mix';

type PayerView = 'bar' | 'stacked' | 'donut' | 'table';

const VIEWS: readonly SegmentOption<PayerView>[] = [
  { value: 'bar', label: 'Bar chart', icon: 'bar_chart' },
  { value: 'stacked', label: 'Stacked bars', icon: 'stacked_bar_chart' },
  { value: 'donut', label: 'Donut charts', icon: 'donut_large' },
  { value: 'table', label: 'Table', icon: 'table_rows' },
];

const KEY_PATTERN = /^[a-z0-9-]{1,40}$/;
const count = new Intl.NumberFormat('en-US');

interface TableRow {
  readonly key: string;
  readonly name: string;
  readonly priorCount: string;
  readonly priorShare: string;
  readonly count: string;
  readonly share: string;
  readonly change: string;
}

/**
 * Payer mix for one aircraft type or level of service: the share of its transports by primary payer, in the chosen month and the month before.
 * Transports belong to the month they happened (date of service). The group, month and Service Line are in the address, so the page can be shared.
 */
@Component({
  selector: 'app-payer-mix',
  imports: [DataFrameComponent, WhatChangedComponent, SkeletonComponent, LoadProblemComponent, PayerDonutsComponent, PayerLegendComponent, PayerStackedComponent, ChartComponent, EmsBreadcrumbsComponent, EmsMessageComponent, EmsSelectComponent, ReactiveFormsModule, SegmentedControlComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page">
      <ems-breadcrumbs class="page__crumbs" [items]="crumbs()" />

      <header class="page__header">
        <div class="page__title">
          <h1>{{ providerName() }}</h1>
          <p class="page__subtitle">Revenue overview@if (dataThrough()) { · Data through {{ dataThrough() }} }</p>
        </div>
        <form class="page__filters" aria-label="Payer mix filters" (submit)="$event.preventDefault()">
          <ems-select class="page__select" label="Period" [options]="periods()" [formControl]="period" [clearable]="false" />
          <ems-select class="page__select" label="Base location" [options]="allLocations" [formControl]="location" [clearable]="false" [title]="notAvailable" />
        </form>
      </header>

      <div class="page__panels">
        <app-what-changed [providerId]="providerId()" [period]="month()" [line]="line()" [group]="group()" [groupName]="groupName()" />

        <app-data-frame [heading]="groupName() + ' payer mix'" [context]="context()">
          <div frameActions class="mix__controls">
            <span class="mix__unit">Share of transports</span>
            <app-segmented-control label="Chart type" [options]="views" [(value)]="view" [iconOnly]="true" />
          </div>
          <div class="mix__body">

          @if (loadError() !== undefined) {
            <app-load-problem [error]="loadError()" subject="the payer mix" (retry)="retry()" />
          } @else if (loading()) {
            <p class="cdk-visually-hidden" role="status">Loading the payer mix</p>
            <div class="mix__skeleton" aria-hidden="true">
              <app-skeleton width="70%" height="1rem" />
              <app-skeleton height="16rem" />
            </div>
          } @else if (empty()) {
            <ems-message state="neutral">There are no {{ groupLower() }} transports for {{ periodLabel() }}.</ems-message>
          } @else if (loaded()) {
            <p class="mix__summary">{{ summary() }}</p>
            @if (view() === 'bar') {
              <ul class="legend" aria-label="Chart key">
                <li><span class="legend__swatch legend__swatch--prior"></span>{{ priorName() }}</li>
                <li><span class="legend__swatch legend__swatch--current"></span>{{ currentName() }}</li>
              </ul>
              <app-chart [option]="option()" [label]="chartLabel()" />
            } @else if (view() === 'stacked') {
              <app-payer-stacked [shares]="shares()" [currentName]="currentName()" [priorName]="priorName()" [currentLong]="currentLong()" [priorLong]="priorLong()" />
              <app-payer-legend [shares]="shares()" [currentName]="currentName()" [priorName]="priorName()" />
            } @else if (view() === 'donut') {
              <app-payer-donuts [shares]="shares()" [callout]="callout()" [currentName]="currentName()" [priorName]="priorName()" [currentLong]="currentLong()" [priorLong]="priorLong()" />
              <app-payer-legend [shares]="shares()" [currentName]="currentName()" [priorName]="priorName()" />
            } @else {
              <div class="table-wrap" role="region" [attr.aria-label]="groupName() + ' payer mix'" tabindex="0">
                <table class="table">
                  <caption class="cdk-visually-hidden">{{ groupName() }} transports by primary payer, {{ priorName() }} and {{ currentName() }}</caption>
                  <thead>
                    <tr>
                      <th scope="col">Payer group</th>
                      <th scope="col" class="num">{{ priorName() }} transports</th>
                      <th scope="col" class="num">{{ priorName() }} share</th>
                      <th scope="col" class="num">{{ currentName() }} transports</th>
                      <th scope="col" class="num">{{ currentName() }} share</th>
                      <th scope="col" class="num">Change</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of rows(); track row.key) {
                      <tr>
                        <th scope="row">{{ row.name }}</th>
                        <td class="num">{{ row.priorCount }}</td>
                        <td class="num">{{ row.priorShare }}</td>
                        <td class="num">{{ row.count }}</td>
                        <td class="num">{{ row.share }}</td>
                        <td class="num">{{ row.change }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
            @if (riseNote()) {
              <ems-message state="warn">{{ riseNote() }}</ems-message>
            }
          }
          <p class="page__note">
            Each transport is counted in the month it happened, under its primary payer. Commercial is insurance and third-party liability, Medicare includes
            Medicare Advantage, Medicaid includes Medicaid managed care, and VA includes CHAMPVA and TRICARE.
          </p>
          </div>
        </app-data-frame>
      </div>
    </main>
  `,
  styles: `
    .page {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
    }
    .page:focus {
      outline: none;
    }
    .page__crumbs {
      margin-top: 0.5rem;
    }
    @media (max-width: 899.98px) {
      .page {
        padding-block-start: 0.25rem;
      }
      .page__crumbs {
        margin-top: 0;
      }
    }
    .page__header {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: 1.5rem;
    }
    h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .page__subtitle,
    .page__note {
      margin: 0.25rem 0 0;
      color: var(--ems-text-muted);
      line-height: var(--ems-line-height-body);
    }
    .page__filters {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem;
    }
    .page__select {
      min-width: 12rem;
    }
    .page__panels {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: 1rem;
      align-items: start;
    }
    @media (min-width: 1100px) {
      .page__panels {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      }
    }
    .mix__body {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .mix__controls {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }
    .mix__unit {
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
    }
    .mix__skeleton {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .legend {
      display: flex;
      gap: 1.25rem;
      margin: 0;
      padding: 0;
      list-style: none;
      color: var(--ems-text-muted);
    }
    .legend li {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
    }
    .legend__swatch {
      width: 0.875rem;
      height: 0.875rem;
      border-radius: 3px;
    }
    .legend__swatch--prior {
      background: var(--ems-surface-neutral-strong);
    }
    .legend__swatch--current {
      background: var(--ems-surface-accent-emerald);
    }
    .mix__summary {
      margin: 0;
      line-height: var(--ems-line-height-body);
    }
    .table-wrap {
      overflow-x: auto;
    }
    .table {
      width: 100%;
      border-collapse: collapse;
    }
    .table th,
    .table td {
      padding: 0.75rem 1rem;
      text-align: left;
      border-bottom: 1px solid var(--ems-border-neutral-subtle);
      line-height: var(--ems-line-height-body);
    }
    .table thead th {
      font-weight: var(--ems-font-weight-bold);
    }
    .table tbody th {
      font-weight: var(--ems-font-weight-regular, normal);
    }
    .table tr:last-child th,
    .table tr:last-child td {
      border-bottom: 0;
    }
    .table .num {
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
  `,
})
export class PayerMixComponent {
  private readonly auth = inject(AuthService);
  private readonly netCollections = inject(NetCollectionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly views = VIEWS;
  protected readonly view = signal<PayerView>('bar');
  protected readonly notAvailable = 'Not available for this data yet';
  protected readonly allLocations: EmsPickerOption<string>[] = [{ value: 'all', label: 'All locations' }];
  protected readonly period = new FormControl<string>('', { nonNullable: true });
  protected readonly location = new FormControl<string>({ value: 'all', disabled: true }, { nonNullable: true });

  private readonly params = toSignal(this.route.paramMap.pipe(map((p) => p.get('group') ?? '')), { initialValue: '' });
  private readonly query = toSignal(this.route.queryParamMap.pipe(map((q) => ({ period: q.get('period') ?? '', line: q.get('line') }))), {
    initialValue: { period: '', line: null as string | null },
  });

  protected readonly group = computed(() => (KEY_PATTERN.test(this.params()) ? this.params() : ''));
  protected readonly line = computed<BreakdownLine>(() => (this.query().line === 'ground' ? 'ground' : 'air'));
  protected readonly providerName = computed(() => this.auth.me()?.providers[0]?.name ?? '');
  private readonly providerId = computed(() => this.auth.me()?.providers[0]?.id ?? null);

  private readonly periodsResource = rxResource<NetCollectionsPeriodsDto | null, string | null>({
    params: () => this.providerId(),
    stream: ({ params }) => (params ? this.netCollections.periods(params) : of(null)),
  });
  protected readonly periods = computed<EmsPickerOption<string>[]>(() =>
    ((this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods ?? []).map((p) => ({ value: p.period, label: apPeriodLabel(p.period) })),
  );
  protected readonly dataThrough = computed(() => {
    const end = apPeriodEnd((this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods[0]?.period ?? '');
    return end ? formatDate(end) : '';
  });

  protected readonly month = computed(() => (/^\d{6}$/.test(this.query().period) ? this.query().period : ((this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods[0]?.period ?? '')));
  protected readonly periodLabel = computed(() => apPeriodLabel(this.month()));
  /** What the figures cover, said when the panel is full screen and the page filters are out of sight. */
  protected readonly context = computed(() => frameContext(this.periodLabel(), this.line() === 'air' ? 'Air' : 'Ground'));
  protected readonly currentName = computed(() => monthName(this.month()));
  protected readonly priorName = computed(() => priorMonthName(this.month()));

  private readonly mix = rxResource<PayerMixDto | null, { providerId: string; month: string; line: BreakdownLine; group: string } | null>({
    params: () => {
      const providerId = this.providerId();
      const month = this.month();
      const group = this.group();
      return providerId && month && group ? { providerId, month, line: this.line(), group } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.payerMix(params.providerId, params.month, params.line, params.group) : of(null)),
  });

  protected readonly loadError = computed<unknown>(() => this.periodsResource.error() ?? this.mix.error());

  protected retry(): void {
    if (this.periodsResource.error() !== undefined) {
      this.periodsResource.reload();
    } else {
      this.mix.reload();
    }
  }
  /** Waiting for the months, then for the mix. */
  protected readonly loading = computed(() => this.periodsResource.isLoading() || this.mix.isLoading());
  protected readonly loaded = computed(() => this.mix.hasValue() && this.mix.value() !== null);
  private readonly figures = computed(() => toPayerMix(this.mix.hasValue() ? this.mix.value() : null));
  protected readonly empty = computed(() => this.loaded() && this.loadError() === undefined && this.figures().total + this.figures().priorTotal === 0);

  /** The name the warehouse gave the group, else the key written as words while it loads. */
  protected readonly groupName = computed(() => {
    const named = this.mix.hasValue() ? this.mix.value()?.groupName : null;
    return named ? this.display(named) : this.display(this.group().replace(/-/g, ' '));
  });
  protected readonly groupLower = computed(() => this.groupName().toLowerCase());

  protected readonly shares = computed(() => this.figures().shares);
  protected readonly callout = computed(() => highlightedPayer(this.figures()));
  protected readonly currentLong = computed(() => apPeriodLabel(this.month()).split(' ')[0] ?? '');
  protected readonly priorLong = computed(() => apPeriodLabel(priorPeriod(this.month())).split(' ')[0] ?? '');

  protected readonly summary = computed(() => summarizePayerMix(this.figures(), this.groupName(), this.currentName(), this.priorName()));
  protected readonly chartLabel = computed(() => describePayerMix(this.figures(), this.groupName(), this.currentName(), this.priorName()));
  protected readonly option = computed(() => {
    const figures = this.figures();
    const current = this.currentName();
    const prior = this.priorName();
    return (colors: Parameters<typeof buildPayerMixOption>[3]) => buildPayerMixOption(figures, current, prior, colors);
  });
  protected readonly riseNote = computed(() => {
    const rise = biggestRise(this.figures());
    return rise ? `${rise.name} up ${Math.round(rise.points)} ${Math.round(rise.points) === 1 ? 'point' : 'points'}, to ${formatShare(rise.percent)} of transports.` : '';
  });
  protected readonly rows = computed<readonly TableRow[]>(() =>
    this.figures().shares.map((s) => ({
      key: s.key,
      name: s.name,
      priorCount: count.format(s.priorCount),
      priorShare: formatShare(s.priorPercent),
      count: count.format(s.count),
      share: formatShare(s.percent),
      change: formatPoints(s.points),
    })),
  );

  protected readonly crumbs = computed<EmsBreadcrumb[]>(() => {
    // The library's crumb link is a router link; a UrlTree carries the month and Service Line back to the page this one came from.
    const parent = this.router.createUrlTree(['/dashboard/net-cash'], { queryParams: { period: this.month() || null, line: this.line() } });
    return [
      { label: 'Dashboard', link: '/dashboard', icon: 'home' },
      { label: `${this.line() === 'air' ? 'Air' : 'Ground'} net cash${this.periodLabel() ? ', ' + this.periodLabel() : ''}`, link: parent as unknown as string },
      { label: this.groupName() },
    ];
  });

  constructor() {
    effect(() => {
      const month = this.month();
      if (month && this.period.value !== month) untracked(() => this.period.setValue(month, { emitEvent: false }));
    });
    this.period.valueChanges.subscribe((value) => {
      void this.router.navigate([], { relativeTo: this.route, queryParams: { period: value }, queryParamsHandling: 'merge', replaceUrl: true });
    });
  }

  /** "Fixed Wing" and "fixed wing" both read as "Fixed wing"; level-of-service codes such as ALS stay as they are. */
  private display(name: string): string {
    const trimmed = name.trim();
    if (trimmed === '' || trimmed === trimmed.toUpperCase()) return trimmed.toUpperCase();
    return trimmed.charAt(0).toUpperCase() + trimmed.slice(1).toLowerCase();
  }
}
