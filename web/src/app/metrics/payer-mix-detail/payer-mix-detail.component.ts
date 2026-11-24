import { DivisionSelectComponent } from '../../shared/division-select.component';
import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map, of } from 'rxjs';
import { EmsBreadcrumb, EmsBreadcrumbsComponent, EmsMessageComponent, EmsPickerOption, EmsSelectComponent } from '@ems-management-consultants/ui';
import { AuthService } from '../../auth/auth.service';
import { ChartComponent } from '../../charts/chart.component';
import { apPeriodEnd, apPeriodLabel } from '../../dashboard/dashboard-format';
import { SegmentOption, SegmentedControlComponent } from '../../dashboard/segmented-control.component';
import { ContextBadgeComponent } from '../../shared/context-badge.component';
import { DataFreshnessService } from '../../shared/data-freshness.service';
import { DataFrameComponent } from '../../shared/data-frame.component';
import { FreshnessStampComponent } from '../../shared/freshness-stamp.component';
import { frameContext } from '../../shared/fullscreen';
import { LoadProblemComponent } from '../../shared/load-problem.component';
import { MetricHelpComponent } from '../../shared/metric-help.component';
import { SkeletonComponent } from '../../shared/skeleton.component';
import { defaultLabels } from '../../dashboard/service-line';
import { DashboardFilterContext } from '../../dashboard/dashboard-filter-context';
import { emptyMessage } from '../../shared/empty-scope';
import { MetricCatalogService } from '../metric-catalog.service';
import { NetCollectionsService } from '../net-collections.service';
import { NetCollectionsPeriodsDto } from '../net-collections/net-collections.dto';
import { monthName, priorMonthName, priorPeriod } from '../net-cash-breakdown/net-cash-breakdown';
import { BreakdownLine } from '../net-cash-breakdown/net-cash-breakdown.model';
import { PayerDonutsComponent } from '../payer-mix/payer-donuts.component';
import { PayerLegendComponent } from '../payer-mix/payer-legend.component';
import { buildPayerMixOption, describePayerMix, formatPoints, formatShare, highlightedPayer, summarizePayerMix } from '../payer-mix/payer-mix';
import { PayerMixDetailDto } from './payer-mix-detail.dto';
import { toPayerDetailMix } from './payer-mix-detail';

type DetailView = 'bar' | 'donut' | 'table';

const VIEWS: readonly SegmentOption<DetailView>[] = [
  { value: 'bar', label: 'Bar chart', icon: 'bar_chart' },
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

/** "fixed-wing" as "Fixed wing", while the real name loads. */
const words = (key: string): string => {
  const text = key.replace(/-/g, ' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
};

/**
 * The payers inside one payer group of a group's payer mix, for example the commercial payers on rotor wing transports: the drill target of the payer mix.
 * The group, payer group, month, Service Line and Reporting Division are all in the address, so the page can be shared and Back returns to the payer mix.
 */
@Component({
  selector: 'app-payer-mix-detail',
  imports: [DivisionSelectComponent, RouterLink, ContextBadgeComponent, FreshnessStampComponent, MetricHelpComponent, DataFrameComponent, SkeletonComponent, LoadProblemComponent, PayerDonutsComponent, PayerLegendComponent, ChartComponent, EmsBreadcrumbsComponent, EmsMessageComponent, EmsSelectComponent, ReactiveFormsModule, SegmentedControlComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page">
      <ems-breadcrumbs class="page__crumbs" [items]="crumbs()" />

      <header class="page__header">
        <div class="page__title">
          <h1 #heading tabindex="-1">{{ pageHeading() }}</h1>
          <p class="page__subtitle">{{ providerName() }}</p>
          <app-freshness-stamp [dataThrough]="dataThroughIso()" />
          <app-context-badge [serviceLine]="line()" [divisionApplied]="divisionApplied()" [divisionKey]="division()" />
        </div>
        <form class="page__filters" aria-label="Payer breakdown filters" (submit)="$event.preventDefault()">
          <ems-select class="page__select" label="Period" [options]="periods()" [formControl]="period" [clearable]="false" />
          <app-division-select class="page__select" mode="follow" [selectedKey]="division()" [line]="line()" />
        </form>
      </header>

      <p class="page__back"><a [routerLink]="backLink()" [queryParams]="backParams()">Back to {{ groupName() }} payer mix</a></p>

      <app-data-frame [heading]="payerGroupName() + ' payers'" [context]="context()">
        <div frameActions class="mix__controls">
          <span class="mix__unit">Share of {{ groupLower() }} transports billed to {{ payerGroupName() }}</span>
          <app-segmented-control label="Chart type" [options]="views" [(value)]="view" [iconOnly]="true" />
        </div>
        <div class="mix__body">
          @if (loadError() !== undefined) {
            <app-load-problem [error]="loadError()" subject="the payer breakdown" (retry)="retry()" />
          } @else if (loading()) {
            <p class="cdk-visually-hidden" role="status">Loading the payer breakdown</p>
            <div class="mix__skeleton" aria-hidden="true">
              <app-skeleton width="70%" height="1rem" />
              <app-skeleton height="16rem" />
            </div>
          } @else if (empty()) {
            <ems-message state="neutral">{{ emptyText() }}</ems-message>
          } @else if (loaded()) {
            <p class="mix__summary">{{ summary() }}</p>
            @if (view() === 'bar') {
              <ul class="legend" aria-label="Chart key">
                <li><span class="legend__swatch legend__swatch--prior"></span>{{ priorName() }}</li>
                <li><span class="legend__swatch legend__swatch--current"></span>{{ currentName() }}</li>
              </ul>
              <app-chart [option]="option()" [label]="chartLabel()" />
            } @else if (view() === 'donut') {
              <app-payer-donuts [shares]="shares()" [callout]="callout()" [currentName]="currentName()" [priorName]="priorName()" [currentLong]="currentLong()" [priorLong]="priorLong()" />
              <app-payer-legend [shares]="shares()" [currentName]="currentName()" [priorName]="priorName()" />
            } @else {
              <div class="table-wrap" role="region" [attr.aria-label]="payerGroupName() + ' payers'" tabindex="0">
                <table class="table">
                  <caption class="cdk-visually-hidden">{{ groupName() }} {{ payerGroupName() }} transports by payer, {{ priorName() }} and {{ currentName() }}</caption>
                  <thead>
                    <tr>
                      <th scope="col">Payer</th>
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
          }
          <app-metric-help metric="payer_mix_detail" [serviceLine]="line()" />
        </div>
      </app-data-frame>
    </main>
  `,
  styles: `
    .page {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
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
    /* Focus moves here when the page opens. It is not a control, so no ring is drawn. */
    h1:focus {
      outline: none;
    }
    .page__subtitle {
      margin: 0.25rem 0 0;
      color: var(--ems-text-muted);
      line-height: var(--ems-line-height-body);
    }
    .page__back {
      margin: 0;
    }
    .page__back a {
      display: inline-block;
      min-height: 1.5rem;
      color: var(--ems-text-link, inherit);
    }
    .page__filters {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem;
    }
    .page__select {
      min-width: 12rem;
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
export class PayerMixDetailComponent {
  private readonly auth = inject(AuthService);
  private readonly netCollections = inject(NetCollectionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly freshness = inject(DataFreshnessService);
  private readonly catalog = inject(MetricCatalogService);
  private readonly filters = inject(DashboardFilterContext);
  private readonly injector = inject(Injector);
  private readonly headingRef = viewChild.required<ElementRef<HTMLElement>>('heading');

  protected readonly views = VIEWS;
  protected readonly view = signal<DetailView>('bar');
  protected readonly period = new FormControl<string>('', { nonNullable: true });

  private readonly params = toSignal(this.route.paramMap.pipe(map((p) => ({ group: p.get('group') ?? '', payerKey: p.get('payerKey') ?? '' }))), {
    initialValue: { group: '', payerKey: '' },
  });
  private readonly query = toSignal(this.route.queryParamMap.pipe(map((q) => ({ period: q.get('period') ?? '', line: q.get('line'), division: q.get('division') }))), {
    initialValue: { period: '', line: null as string | null, division: null as string | null },
  });

  protected readonly group = computed(() => (KEY_PATTERN.test(this.params().group) ? this.params().group : ''));
  protected readonly payerKey = computed(() => (KEY_PATTERN.test(this.params().payerKey) ? this.params().payerKey : ''));
  protected readonly line = computed<BreakdownLine>(() => (this.query().line === 'ground' ? 'ground' : 'air'));
  protected readonly division = computed(() => (/^[A-Za-z0-9_-]{1,64}$/.test(this.query().division ?? '') ? this.query().division! : undefined));
  protected readonly divisionApplied = computed(() => this.division() !== undefined && this.catalog.honorsDivision('payer_mix_detail'));
  protected readonly providerName = computed(() => this.auth.activeProvider()?.name ?? '');
  private readonly providerId = computed(() => this.auth.activeProviderId());

  private readonly periodsResource = rxResource<NetCollectionsPeriodsDto | null, { providerId: string; rev: number } | null>({
    params: () => {
      const providerId = this.providerId();
      return providerId ? { providerId, rev: this.freshness.revision() } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.periods(params.providerId) : of(null)),
  });
  private readonly periodsValue = computed(() => (this.periodsResource.hasValue() ? this.periodsResource.value() : undefined));
  protected readonly periods = computed<EmsPickerOption<string>[]>(() => (this.periodsValue()?.periods ?? []).map((p) => ({ value: p.period, label: apPeriodLabel(p.period) })));

  protected readonly month = computed(() => (/^\d{6}$/.test(this.query().period) ? this.query().period : (this.periodsValue()?.periods[0]?.period ?? '')));
  protected readonly periodLabel = computed(() => apPeriodLabel(this.month()));
  protected readonly currentName = computed(() => monthName(this.month()));
  protected readonly priorName = computed(() => priorMonthName(this.month()));
  protected readonly currentLong = computed(() => apPeriodLabel(this.month()).split(' ')[0] ?? '');
  protected readonly priorLong = computed(() => apPeriodLabel(priorPeriod(this.month())).split(' ')[0] ?? '');

  private readonly detail = rxResource<PayerMixDetailDto | null, { providerId: string; month: string; line: BreakdownLine; group: string; payerKey: string; division: string | undefined; rev: number } | null>({
    params: () => {
      const providerId = this.providerId();
      const month = this.month();
      const group = this.group();
      const payerKey = this.payerKey();
      return providerId && month && group && payerKey ? { providerId, month, line: this.line(), group, payerKey, division: this.division(), rev: this.freshness.revision() } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.payerMixDetail(params.providerId, params.month, params.line, params.group, params.payerKey, params.division) : of(null)),
  });
  private readonly detailValue = computed(() => (this.detail.hasValue() ? this.detail.value() : null));

  /** The response's own Data Through, else the end of the newest period. */
  protected readonly dataThroughIso = computed(() => this.detailValue()?.dataThrough ?? this.periodsValue()?.dataThrough ?? (apPeriodEnd(this.periodsValue()?.periods[0]?.period ?? '') || null));

  protected readonly loadError = computed<unknown>(() => this.periodsResource.error() ?? this.detail.error());
  protected readonly loading = computed(() => this.periodsResource.isLoading() || this.detail.isLoading());
  protected readonly loaded = computed(() => this.detailValue() !== null);
  private readonly figures = computed(() => toPayerDetailMix(this.detailValue()));
  protected readonly empty = computed(() => this.loaded() && this.loadError() === undefined && this.figures().total + this.figures().priorTotal === 0);

  protected retry(): void {
    if (this.periodsResource.error() !== undefined) {
      this.periodsResource.reload();
    } else {
      this.detail.reload();
    }
  }

  /** Names as the warehouse gave them, else the keys written as words while the data loads. */
  protected readonly groupName = computed(() => this.detailValue()?.groupName?.trim() || words(this.group()));
  protected readonly payerGroupName = computed(() => this.detailValue()?.payerGroup.name?.trim() || words(this.payerKey()));
  /** The empty-state sentence. This page is already on one Service Line, so only the Reporting Division is named when it narrowed the answer. */
  protected readonly emptyText = computed(() => {
    const key = this.division();
    return emptyMessage(`${this.payerGroupName().toLowerCase()} ${this.groupLower()} transports`, this.periodLabel(), `There are no ${this.payerGroupName().toLowerCase()} ${this.groupLower()} transports for ${this.periodLabel()}.`, { line: null, divisionApplied: this.divisionApplied(), divisionName: key ? this.filters.divisionName(key) : undefined, labels: defaultLabels(this.line()) });
  });
  protected readonly groupLower = computed(() => this.groupName().toLowerCase());
  protected readonly pageHeading = computed(() => `${this.payerGroupName()} payers, ${this.groupLower()}`);
  protected readonly context = computed(() => frameContext(this.periodLabel(), this.line() === 'air' ? 'Air' : 'Ground'));

  protected readonly shares = computed(() => this.figures().shares);
  protected readonly callout = computed(() => highlightedPayer(this.figures()));
  protected readonly summary = computed(() => summarizePayerMix(this.figures(), `${this.groupName()} ${this.payerGroupName()}`, this.currentName(), this.priorName()));
  protected readonly chartLabel = computed(() => describePayerMix(this.figures(), `${this.groupName()} ${this.payerGroupName()}`, this.currentName(), this.priorName()));
  protected readonly option = computed(() => {
    const figures = this.figures();
    const current = this.currentName();
    const prior = this.priorName();
    return (colors: Parameters<typeof buildPayerMixOption>[3]) => buildPayerMixOption(figures, current, prior, colors);
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

  protected readonly backLink = computed(() => ['/dashboard/net-cash', this.group()]);
  protected readonly backParams = computed<Record<string, string>>(() => ({ period: this.month(), line: this.line(), ...(this.division() ? { division: this.division()! } : {}) }));

  protected readonly crumbs = computed<EmsBreadcrumb[]>(() => {
    // The library's crumb link is a router link; a UrlTree carries the month, Service Line and Division back to the page this one came from.
    const net = this.router.createUrlTree(['/dashboard/net-cash'], { queryParams: { period: this.month() || null, line: this.line(), division: this.division() ?? null } });
    const mix = this.router.createUrlTree(['/dashboard/net-cash', this.group()], { queryParams: { period: this.month() || null, line: this.line(), division: this.division() ?? null } });
    return [
      { label: 'Dashboard', link: '/dashboard', icon: 'home' },
      { label: `${this.line() === 'air' ? 'Air' : 'Ground'} net cash${this.periodLabel() ? ', ' + this.periodLabel() : ''}`, link: net as unknown as string },
      { label: `${this.groupName()} payer mix`, link: mix as unknown as string },
      { label: this.payerGroupName() },
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
    // Following a link moves focus to this page's heading, so a screen reader starts at the new page and not where the link was. Only the address is tracked.
    effect(() => {
      this.payerKey();
      this.group();
      untracked(() => afterNextRender(() => this.headingRef().nativeElement.focus({ preventScroll: true }), { injector: this.injector }));
    });
  }
}
