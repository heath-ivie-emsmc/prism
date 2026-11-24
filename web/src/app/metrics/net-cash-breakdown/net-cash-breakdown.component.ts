import { ChangeDetectionStrategy, Component, computed, effect, inject, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map, of } from 'rxjs';
import { EmsBreadcrumb, EmsBreadcrumbsComponent, EmsChipComponent, EmsIconComponent, EmsMessageComponent, EmsPickerOption, EmsSelectComponent } from '@ems-management-consultants/ui';
import { DataFreshnessService } from '../../shared/data-freshness.service';
import { AuthService } from '../../auth/auth.service';
import { MetricHelpComponent } from '../../shared/metric-help.component';
import { LoadProblemComponent } from '../../shared/load-problem.component';
import { SkeletonComponent } from '../../shared/skeleton.component';
import { defaultLabels } from '../../dashboard/service-line';
import { formatExact } from '../../charts/net-cash';
import { NetCollectionsService } from '../net-collections.service';
import { NetCashBreakdownDto } from './net-cash-breakdown.dto';
import { NetCollectionsPeriodsDto } from '../net-collections/net-collections.dto';
import { apPeriodEnd, apPeriodLabel, formatDate } from '../../dashboard/dashboard-format';
import { monthName, percentChange, priorMonthName, summarizeBreakdown } from './net-cash-breakdown';
import { BreakdownLine, breakdownTableTitle, emptyBreakdown, toBreakdown } from './net-cash-breakdown.model';

/** The page has no picker of its own: the dashboard's choice arrives in the address and is applied. The control says so instead of offering a choice it cannot take. */
const NOT_AVAILABLE = 'Set on the dashboard. This page follows that choice.';
/** A change smaller than this is "steady": shown in neutral, not as good or bad news. */
const STEADY_PERCENT = 5;

interface GroupView {
  readonly key: string;
  readonly name: string;
  readonly icon: string;
  readonly change: string;
  readonly changeIcon: string;
  readonly changeState: 'success' | 'error' | 'neutral';
  readonly priorBar: number;
  readonly currentBar: number;
  readonly currentBad: boolean;
  readonly priorCash: string;
  readonly cash: string;
  readonly transports: string;
  readonly mix: string;
  readonly miles: string;
  /** Router link from the API's drill link, or null for no link. */
  readonly drill: { readonly commands: readonly string[]; readonly queryParams: Record<string, string> } | null;
}

interface RowView {
  readonly key: string;
  readonly id: string;
  readonly groupName: string;
  readonly icon: string;
  readonly location: string;
  readonly transports: string;
  readonly transportsChange: string;
  readonly miles: string;
  readonly cash: string;
  readonly cashChange: string;
  readonly cashDirection: 'up' | 'down' | null;
  readonly cashBad: boolean;
}

const number = new Intl.NumberFormat('en-US');
const signed = (n: number): string => (n > 0 ? `+${n}` : n < 0 ? `−${Math.abs(n)}` : '0');
const percent = (value: number | null): string => (value === null ? '' : `${Math.round(Math.abs(value))}%`);

/**
 * What drove net cash in one month, by aircraft type (air) or level of service (ground), then by base and vehicle.
 * Figures come from the net cash breakdown route: cash by the month it posted, transports as the encounters with cash posted in the month.
 * The chosen month and Service Line come in the address, so the page can be shared and the back button works.
 */
@Component({
  selector: 'app-net-cash-breakdown',
  imports: [MetricHelpComponent, SkeletonComponent, LoadProblemComponent, EmsBreadcrumbsComponent, EmsChipComponent, EmsIconComponent, EmsMessageComponent, EmsSelectComponent, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page">
      <ems-breadcrumbs class="page__crumbs" [items]="crumbs()" />

      <header class="page__header">
        <div class="page__title">
          <h1>{{ providerName() }}</h1>
          <p class="page__subtitle">Revenue overview@if (dataThrough()) { · Data through {{ dataThrough() }} }</p>
        </div>
        <form class="page__filters" aria-label="Breakdown filters" (submit)="$event.preventDefault()">
          <ems-select class="page__select" label="Period" [options]="periods()" [formControl]="period" [clearable]="false" />
          <ems-select class="page__select" [label]="locationLabel()" [options]="allLocations()" [formControl]="location" [clearable]="false" [title]="notAvailable" />
        </form>
      </header>

      <section class="app-panel" aria-labelledby="groups-title">
        <h2 id="groups-title">{{ data().terms.title }}</h2>
        @if (loadError() !== undefined) {
          <app-load-problem [error]="loadError()" subject="the breakdown" (retry)="retry()" />
        } @else if (loading()) {
          <p class="cdk-visually-hidden" role="status">Loading the breakdown</p>
          <div class="groups" aria-hidden="true">
            @for (n of placeholders; track n) {
              <div class="group group--loading">
                <app-skeleton width="55%" height="1.5rem" />
                <app-skeleton height="1rem" />
                <app-skeleton height="1rem" />
                <app-skeleton width="80%" height="2.5rem" />
              </div>
            }
          </div>
        } @else if (empty()) {
          <ems-message state="neutral">There is no net cash for {{ periodLabel() }}.</ems-message>
        }
        @if (data().stillPosting && loadError() === undefined && !empty()) {
          <ems-message state="warn">
            {{ periodLabel() }} is still open, so cash is still posting. These figures are not final, and the change from {{ priorName() }} will shrink as it fills in.
          </ems-message>
        }
        <p class="page__summary">{{ summary() }}</p>
        <div class="groups">
          @for (group of groups(); track group.key) {
            <article class="group" [attr.aria-labelledby]="group.key + '-name'">
              <header class="group__header">
                <h3 class="group__name" [id]="group.key + '-name'">
                  <ems-icon [name]="group.icon" />
                  {{ group.name }}
                </h3>
                <ems-chip [state]="group.changeState" [icon]="group.changeIcon">{{ group.change }} vs {{ priorName() }}</ems-chip>
              </header>
              <dl class="bars">
                <div class="bars__row">
                  <dt>{{ priorName() }}</dt>
                  <dd class="bars__track"><span class="bars__fill" [style.width.%]="group.priorBar"></span></dd>
                  <dd class="bars__value">{{ group.priorCash }}</dd>
                </div>
                <div class="bars__row">
                  <dt>{{ currentName() }}</dt>
                  <dd class="bars__track"><span class="bars__fill bars__fill--current" [class.bars__fill--bad]="group.currentBad" [style.width.%]="group.currentBar"></span></dd>
                  <dd class="bars__value">{{ group.cash }}</dd>
                </div>
              </dl>
              <dl class="stats">
                <div>
                  <dt>Transports</dt>
                  <dd>{{ group.transports }}</dd>
                </div>
                <div>
                  <dt>{{ data().terms.mixHeading }}</dt>
                  <dd>{{ group.mix }}</dd>
                </div>
                <div>
                  <dt>Loaded miles</dt>
                  <dd>{{ group.miles }}</dd>
                </div>
              </dl>
              @if (group.drill; as drill) {
                <a class="group__link" [routerLink]="drill.commands" [queryParams]="drill.queryParams">
                  See {{ group.name.toLowerCase() }} detail
                  <ems-icon name="arrow_forward" size="sm" />
                </a>
              }
            </article>
          }
        </div>
      </section>

      <section class="app-panel" aria-labelledby="rows-title">
        <h2 id="rows-title">{{ tableTitle() }}, {{ periodLabel() }}</h2>
        <div class="table-wrap" role="region" [attr.aria-labelledby]="'rows-title'" tabindex="0">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">{{ data().terms.idHeading }}</th>
                <th scope="col">{{ data().terms.groupHeading }}</th>
                <th scope="col">{{ locationLabel() }}</th>
                <th scope="col" class="num">Transports</th>
                <th scope="col" class="num">Change vs {{ priorName() }}</th>
                <th scope="col" class="num">Loaded miles</th>
                <th scope="col" class="num">Net cash</th>
                <th scope="col" class="num">Cash vs {{ priorName() }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.key) {
                <tr>
                  <th scope="row">
                    <span class="table__id"><ems-icon [name]="row.icon" size="sm" />{{ row.id }}</span>
                  </th>
                  <td>{{ row.groupName }}</td>
                  <td>{{ row.location }}</td>
                  <td class="num">{{ row.transports }}</td>
                  <td class="num">{{ row.transportsChange }}</td>
                  <td class="num">{{ row.miles }}</td>
                  <td class="num">{{ row.cash }}</td>
                  <td class="num" [class.bad]="row.cashBad">
                    @if (row.cashDirection) {
                      <ems-icon [name]="row.cashDirection === 'up' ? 'arrow_upward' : 'arrow_downward'" size="sm" />
                    }
                    {{ row.cashChange }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <app-metric-help metric="net_cash_breakdown" [serviceLine]="line()" />
      </section>
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
    /* The menu wraps under the logo on small screens and brings its own space, so the page starts closer to it. */
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
    h2 {
      margin: 0;
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
      font-weight: var(--ems-font-weight-heavy);
    }
    .page__subtitle {
      margin: 0.25rem 0 0;
      color: var(--ems-text-muted);
      line-height: var(--ems-line-height-body);
    }
    .page__summary {
      margin: 0;
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
    .groups {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 22rem), 1fr));
      gap: 1rem;
    }
    .group {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.25rem;
      border-radius: var(--ems-corner-md);
      background: var(--ems-surface-neutral-default);
      box-shadow:
        inset 0 0 0 var(--ems-border-width-default) var(--ems-border-neutral-default),
        var(--ems-fx-neutral-default-frosted-raised-glowing);
    }
    .group--loading {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .group__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
    }
    .group__name {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin: 0;
      font-size: 1.125rem;
      line-height: 1.5rem;
      font-weight: var(--ems-font-weight-heavy);
    }
    .bars {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      margin: 0;
    }
    .bars__row {
      display: grid;
      grid-template-columns: 2.5rem minmax(0, 1fr) auto;
      align-items: center;
      gap: 0.75rem;
    }
    .bars__row dt {
      color: var(--ems-text-muted);
    }
    .bars__row dd {
      margin: 0;
    }
    .bars__track {
      height: 1rem;
      border-radius: 999px;
      background: var(--ems-surface-neutral-subtle);
      overflow: hidden;
    }
    .bars__fill {
      display: block;
      height: 100%;
      border-radius: 999px;
      background: var(--ems-surface-neutral-strong);
    }
    .bars__fill--current {
      background: var(--ems-surface-accent-emerald);
    }
    .bars__fill--bad {
      background: var(--ems-surface-error-stronger);
    }
    .bars__value {
      min-width: 6.5rem;
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(7rem, 1fr));
      gap: 0.75rem 1.5rem;
      margin: 0;
    }
    .stats dt {
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
    .stats dd {
      margin: 0.125rem 0 0;
    }
    .group__link {
      align-self: flex-start;
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      min-height: 2.75rem;
      padding: 0;
      border: 0;
      background: none;
      color: var(--ems-text-primary);
      font: inherit;
      font-weight: var(--ems-font-weight-bold);
      text-decoration: none;
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
      vertical-align: middle;
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
    .table__id {
      white-space: nowrap;
      display: inline-flex;
      align-items: center;
      gap: 0.75rem;
    }
    .bad {
      color: var(--ems-text-error, #a01f1f);
    }
  `,
})
export class NetCashBreakdownComponent {
  private readonly auth = inject(AuthService);
  private readonly freshness = inject(DataFreshnessService);
  private readonly netCollections = inject(NetCollectionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly notAvailable = NOT_AVAILABLE;
  protected readonly allLocations = computed<EmsPickerOption<string>[]>(() => [{ value: 'all', label: this.division() ? 'Chosen on the dashboard' : defaultLabels(this.line()).allReportingDivisions }]);

  protected readonly period = new FormControl<string>('', { nonNullable: true });
  protected readonly location = new FormControl<string>({ value: 'all', disabled: true }, { nonNullable: true });

  private readonly query = toSignal(this.route.queryParamMap.pipe(map((q) => ({ period: q.get('period') ?? '', line: q.get('line'), division: q.get('division') }))), {
    initialValue: { period: '', line: null as string | null, division: null as string | null },
  });
  /** The Reporting Division key from the address, passed on to the Metric and to the pages this one links to. */
  protected readonly division = computed(() => (/^[A-Za-z0-9_-]{1,64}$/.test(this.query().division ?? '') ? this.query().division! : undefined));
  protected readonly groupParams = computed(() => ({ period: this.month(), line: this.line(), ...(this.division() ? { division: this.division() } : {}) }));

  /** Air unless the address says ground: the dashboard's "both" opens on air, the first Service Line. */
  protected readonly line = computed<BreakdownLine>(() => (this.query().line === 'ground' ? 'ground' : 'air'));
  protected readonly selectedPeriod = computed(() => (/^\d{6}$/.test(this.query().period) ? this.query().period : ''));

  protected readonly providerName = computed(() => this.auth.activeProvider()?.name ?? '');
  private readonly providerId = computed(() => this.auth.activeProviderId());

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

  /** The month being explained: the one in the address, else the newest with data. */
  protected readonly month = computed(() => this.selectedPeriod() || ((this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods[0]?.period ?? ''));
  protected readonly periodLabel = computed(() => apPeriodLabel(this.month()));
  protected readonly currentName = computed(() => monthName(this.month()));
  protected readonly priorName = computed(() => priorMonthName(this.month()));

  private readonly breakdown = rxResource<NetCashBreakdownDto | null, { providerId: string; month: string; line: BreakdownLine; division: string | undefined; rev: number } | null>({
    params: () => {
      const providerId = this.providerId();
      const month = this.month();
      return providerId && month ? { providerId, month, line: this.line(), division: this.division(), rev: this.freshness.revision() } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.breakdown(params.providerId, params.month, params.line, params.division) : of(null)),
  });
  protected readonly data = computed(() => {
    const value = this.breakdown.hasValue() ? this.breakdown.value() : null;
    return value ? toBreakdown(value, this.line()) : emptyBreakdown(this.line());
  });
  protected readonly failed = computed(() => this.breakdown.error() !== undefined);
  /** The periods first: without a month there is nothing to ask the breakdown for. */
  protected readonly loadError = computed<unknown>(() => this.periodsResource.error() ?? this.breakdown.error());

  protected retry(): void {
    if (this.periodsResource.error() !== undefined) {
      this.periodsResource.reload();
    } else {
      this.breakdown.reload();
    }
  }
  /** Waiting for the months, then for the groups. */
  protected readonly loading = computed(() => this.periodsResource.isLoading() || this.breakdown.isLoading());
  protected readonly placeholders = [1, 2, 3];
  protected readonly empty = computed(() => this.breakdown.hasValue() && !this.failed() && this.data().groups.length === 0 && this.breakdown.value() !== null);
  /** The Provider's term for a Reporting Division, from the Labels for the Service Line, never typed here. */
  protected readonly locationLabel = computed(() => defaultLabels(this.line()).reportingDivision);
  /** "By base location and aircraft": built from the Label, so a renamed Reporting Division renames the table. */
  protected readonly tableTitle = computed(() => breakdownTableTitle(this.line(), this.locationLabel()));
  protected readonly summary = computed(() => summarizeBreakdown(this.data().groups));

  protected readonly crumbs = computed<EmsBreadcrumb[]>(() => [
    { label: 'Dashboard', link: '/dashboard', icon: 'home' },
    { label: `${this.line() === 'air' ? 'Air' : 'Ground'} net cash${this.periodLabel() ? ', ' + this.periodLabel() : ''}` },
  ]);

  protected readonly groups = computed<readonly GroupView[]>(() => {
    const groups = this.data().groups;
    const largest = Math.max(1, ...groups.flatMap((g) => [g.cash, g.priorCash]));
    return groups.map((g): GroupView => {
      const change = percentChange(g.cash, g.priorCash);
      const steady = change === null || Math.abs(change) < STEADY_PERCENT;
      return {
        key: g.key,
        name: g.name,
        icon: g.icon,
        change: change === null ? 'No change' : `${change < 0 ? '−' : '+'}${Math.round(Math.abs(change))}%`,
        changeIcon: change !== null && change < 0 ? 'trending_down' : 'trending_up',
        changeState: steady ? 'neutral' : change! < 0 ? 'error' : 'success',
        priorBar: (g.priorCash / largest) * 100,
        currentBar: (g.cash / largest) * 100,
        currentBad: change !== null && change <= -10,
        priorCash: formatExact(g.priorCash),
        cash: formatExact(g.cash),
        transports: `${number.format(g.transports)} (${number.format(g.priorTransports)} in ${this.priorName()})`,
        mix: g.mix,
        miles: `${number.format(g.loadedMiles)} statute`,
        drill: g.drill ? { commands: g.drill.commands, queryParams: { ...this.groupParams(), ...g.drill.queryParams } } : null,
      };
    });
  });

  protected readonly rows = computed<readonly RowView[]>(() =>
    this.data().rows.map((r): RowView => {
      const change = percentChange(r.cash, r.priorCash);
      return {
        key: r.key,
        id: r.id,
        groupName: r.groupName,
        icon: r.icon,
        location: r.location,
        transports: number.format(r.transports),
        transportsChange: signed(r.transportsChange),
        miles: number.format(r.loadedMiles),
        cash: formatExact(r.cash),
        cashChange: percent(change),
        cashDirection: change === null ? null : change >= 0 ? 'up' : 'down',
        cashBad: change !== null && change <= -10,
      };
    }),
  );

  constructor() {
    // Keep the Period drop-down on the month in the address, and the address on the drop-down.
    effect(() => {
      const month = this.month();
      if (month && this.period.value !== month) untracked(() => this.period.setValue(month, { emitEvent: false }));
    });
    this.period.valueChanges.subscribe((value) => {
      void this.router.navigate([], { relativeTo: this.route, queryParams: { period: value }, queryParamsHandling: 'merge', replaceUrl: true });
    });
  }
}
