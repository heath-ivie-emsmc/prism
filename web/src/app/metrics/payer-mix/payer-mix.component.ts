import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map, of } from 'rxjs';
import { EmsBreadcrumb, EmsBreadcrumbsComponent, EmsMessageComponent, EmsPickerOption, EmsSelectComponent } from '@ems-management-consultants/ui';
import { AuthService } from '../../auth/auth.service';
import { DataFreshnessService } from '../../shared/data-freshness.service';
import { MetricCatalogService } from '../metric-catalog.service';
import { ChartElementEvent } from '../../charts/chart.component';
import { DataFrameComponent } from '../../shared/data-frame.component';
import { frameContext } from '../../shared/fullscreen';
import { ContextBadgeComponent } from '../../shared/context-badge.component';
import { FreshnessStampComponent } from '../../shared/freshness-stamp.component';
import { MetricHelpComponent } from '../../shared/metric-help.component';
import { LoadProblemComponent } from '../../shared/load-problem.component';
import { SkeletonComponent } from '../../shared/skeleton.component';
import { ChartComponent } from '../../charts/chart.component';
import { monthName, priorMonthName, priorPeriod } from '../net-cash-breakdown/net-cash-breakdown';
import { BreakdownLine } from '../net-cash-breakdown/net-cash-breakdown.model';
import { defaultLabels } from '../../dashboard/service-line';
import { apPeriodEnd, apPeriodLabel, formatDate } from '../../dashboard/dashboard-format';
import { SegmentOption, SegmentedControlComponent } from '../../dashboard/segmented-control.component';
import { NetCollectionsService } from '../net-collections.service';
import { NetCollectionsPeriodsDto } from '../net-collections/net-collections.dto';
import { PayerMixDto } from './payer-mix.dto';
import { WhatChangedComponent } from '../../insights/what-changed.component';
import { PayerDonutsComponent } from './payer-donuts.component';
import { PayerLegendComponent } from './payer-legend.component';
import { PayerStackedComponent } from './payer-stacked.component';
import { biggestRise, displayName, buildPayerMixOption, describePayerMix, drillName, formatPoints, formatShare, highlightedPayer, summarizePayerMix, toPayerMix } from './payer-mix';

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
  /** Router commands for the group's breakdown, or null when the API gave no link. */
  readonly link: readonly string[] | null;
  readonly drillName: string;
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
  imports: [RouterLink, ContextBadgeComponent, FreshnessStampComponent, MetricHelpComponent, DataFrameComponent, WhatChangedComponent, SkeletonComponent, LoadProblemComponent, PayerDonutsComponent, PayerLegendComponent, PayerStackedComponent, ChartComponent, EmsBreadcrumbsComponent, EmsMessageComponent, EmsSelectComponent, ReactiveFormsModule, SegmentedControlComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page">
      <ems-breadcrumbs class="page__crumbs" [items]="crumbs()" />

      <header class="page__header">
        <div class="page__title">
          <h1>{{ providerName() }}</h1>
          <p class="page__subtitle">Revenue overview</p>
          <app-freshness-stamp [dataThrough]="dataThroughIso()" />
          <app-context-badge [serviceLine]="line()" [divisionApplied]="divisionApplied()" />
        </div>
        <form class="page__filters" aria-label="Payer mix filters" (submit)="$event.preventDefault()">
          <ems-select class="page__select" label="Period" [options]="periods()" [formControl]="period" [clearable]="false" />
          <ems-select class="page__select" [label]="labels().reportingDivision" [options]="allLocations()" [formControl]="location" [clearable]="false" [title]="notAvailable" />
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
              <app-chart [option]="option()" [label]="chartLabel()" (elementActivate)="openBar($event)" />
              @if (drillLinks().length > 0) {
                <!-- The chart's bars take a click; these are the same destinations for the keyboard and for screen readers. -->
                <ul class="drill" aria-label="Breakdown by payer group">
                  @for (link of drillLinks(); track link.key) {
                    <li><a [routerLink]="link.commands" [queryParams]="drillParams()">{{ link.name }}</a></li>
                  }
                </ul>
              }
            } @else if (view() === 'stacked') {
              <app-payer-stacked [shares]="shares()" [currentName]="currentName()" [priorName]="priorName()" [currentLong]="currentLong()" [priorLong]="priorLong()" (drill)="openKey($event)" />
              <app-payer-legend [shares]="shares()" [currentName]="currentName()" [priorName]="priorName()" [drillParams]="drillParams()" />
            } @else if (view() === 'donut') {
              <app-payer-donuts [shares]="shares()" [callout]="callout()" [currentName]="currentName()" [priorName]="priorName()" [currentLong]="currentLong()" [priorLong]="priorLong()" (drill)="openKey($event)" />
              <app-payer-legend [shares]="shares()" [currentName]="currentName()" [priorName]="priorName()" [drillParams]="drillParams()" />
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
                        <th scope="row">
                          @if (row.link) {
                            <a [routerLink]="row.link" [queryParams]="drillParams()" [attr.aria-label]="row.drillName">{{ row.name }}</a>
                          } @else {
                            {{ row.name }}
                          }
                        </th>
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
          <app-metric-help metric="payer_mix" [serviceLine]="line()" />
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
      flex: 1 1 auto;
      flex-wrap: wrap;
      align-items: center;
      justify-content: flex-end;
      gap: 0.5rem 0.75rem;
      min-width: 0;
    }
    /* Under 400px the subtitle and the switcher each get a full-width row. */
    @media (max-width: 400px) {
      .mix__controls {
        flex-direction: column;
        align-items: flex-start;
        justify-content: flex-start;
      }
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
    .drill {
      display: flex;
      flex-wrap: wrap;
      gap: 0.25rem 1.25rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .drill a,
    .table a {
      display: inline-block;
      min-height: 1.5rem;
      color: var(--ems-text-link, inherit);
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
  private readonly freshness = inject(DataFreshnessService);
  private readonly catalog = inject(MetricCatalogService);

  protected readonly views = VIEWS;
  protected readonly view = signal<PayerView>('bar');
  protected readonly notAvailable = 'Not available for this data yet';
  protected readonly period = new FormControl<string>('', { nonNullable: true });
  protected readonly location = new FormControl<string>({ value: 'all', disabled: true }, { nonNullable: true });

  private readonly params = toSignal(this.route.paramMap.pipe(map((p) => p.get('group') ?? '')), { initialValue: '' });
  private readonly query = toSignal(this.route.queryParamMap.pipe(map((q) => ({ period: q.get('period') ?? '', line: q.get('line'), division: q.get('division') }))), {
    initialValue: { period: '', line: null as string | null, division: null as string | null },
  });
  /** The Reporting Division key from the address, sent to the Metric and carried to the pages this one links to. */
  protected readonly division = computed(() => (/^[A-Za-z0-9_-]{1,64}$/.test(this.query().division ?? '') ? this.query().division! : undefined));
  protected readonly divisionApplied = computed(() => this.division() !== undefined && this.catalog.honorsDivision('payer_mix'));

  protected readonly group = computed(() => (KEY_PATTERN.test(this.params()) ? this.params() : ''));
  protected readonly line = computed<BreakdownLine>(() => (this.query().line === 'ground' ? 'ground' : 'air'));
  /** The Provider's term for a Reporting Division (a Label, by Service Line), never a hard-coded one. */
  protected readonly labels = computed(() => defaultLabels(this.line()));
  protected readonly allLocations = computed<EmsPickerOption<string>[]>(() => [{ value: 'all', label: this.labels().allReportingDivisions }]);
  protected readonly providerName = computed(() => this.auth.activeProvider()?.name ?? '');
  private readonly providerId = computed(() => this.auth.activeProviderId());

  private readonly periodsResource = rxResource<NetCollectionsPeriodsDto | null, { providerId: string; rev: number } | null>({
    params: () => {
      const providerId = this.providerId();
      return providerId ? { providerId, rev: this.freshness.revision() } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.periods(params.providerId) : of(null)),
  });
  protected readonly periods = computed<EmsPickerOption<string>[]>(() =>
    ((this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods ?? []).map((p) => ({ value: p.period, label: apPeriodLabel(p.period) })),
  );
  protected readonly dataThroughIso = computed(() => apPeriodEnd((this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods[0]?.period ?? '') || null);
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

  private readonly mix = rxResource<PayerMixDto | null, { providerId: string; month: string; line: BreakdownLine; group: string; division: string | undefined; rev: number } | null>({
    params: () => {
      const providerId = this.providerId();
      const month = this.month();
      const group = this.group();
      return providerId && month && group ? { providerId, month, line: this.line(), group, division: this.division(), rev: this.freshness.revision() } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.payerMix(params.providerId, params.month, params.line, params.group, params.division) : of(null)),
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
    return named ? displayName(named) : displayName(this.group().replace(/-/g, ' '));
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
    const drillable = new Set(figures.shares.filter((s) => s.drill).map((s) => s.key));
    return (colors: Parameters<typeof buildPayerMixOption>[3]) => buildPayerMixOption(figures, current, prior, colors, drillable);
  });
  protected readonly riseNote = computed(() => {
    const rise = biggestRise(this.figures());
    return rise ? `${rise.name} up ${Math.round(rise.points)} ${Math.round(rise.points) === 1 ? 'point' : 'points'}, to ${formatShare(rise.percent)} of transports.` : '';
  });
  protected readonly rows = computed<readonly TableRow[]>(() =>
    this.figures().shares.map((s) => ({
      key: s.key,
      link: s.drill ? ['/dashboard/metrics/payer-mix', s.drill.group, s.drill.payerKey] : null,
      drillName: drillName(s.name),
      name: s.name,
      priorCount: count.format(s.priorCount),
      priorShare: formatShare(s.priorPercent),
      count: count.format(s.count),
      share: formatShare(s.percent),
      change: formatPoints(s.points),
    })),
  );

  /** The month, Service Line and Division the breakdown pages open on. */
  protected readonly drillParams = computed<Record<string, string>>(() => ({ period: this.month(), line: this.line(), ...(this.division() ? { division: this.division()! } : {}) }));
  protected readonly drillLinks = computed(() =>
    this.figures().shares.flatMap((s) => (s.drill ? [{ key: s.key, name: drillName(s.name), commands: ['/dashboard/metrics/payer-mix', s.drill.group, s.drill.payerKey] }] : [])),
  );

  /** A click on a bar. Bars follow the order of the shares. */
  protected openBar(event: ChartElementEvent): void {
    const share = this.figures().shares[event.dataIndex];
    if (share) this.openKey(share.key);
  }

  protected openKey(key: string): void {
    const drill = this.figures().shares.find((s) => s.key === key)?.drill;
    if (drill) void this.router.navigate(['/dashboard/metrics/payer-mix', drill.group, drill.payerKey], { queryParams: this.drillParams() });
  }

  protected readonly crumbs = computed<EmsBreadcrumb[]>(() => {
    // The library's crumb link is a router link; a UrlTree carries the month and Service Line back to the page this one came from.
    const parent = this.router.createUrlTree(['/dashboard/net-cash'], { queryParams: { period: this.month() || null, line: this.line(), division: this.division() ?? null } });
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
}
