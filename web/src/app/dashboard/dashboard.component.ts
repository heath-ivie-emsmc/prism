import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import {
  EmsBreadcrumbsComponent,
  EmsButtonDirective,
  EmsSelectComponent,
  EmsBreadcrumb,
  EmsPickerOption,
} from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { DashboardContext, MOCK_CONTEXT, divisionsFor } from './dashboard-context';
import { apPeriodEnd, apPeriodLabel, formatDate } from './dashboard-format';
import { CleanClaimRateDto, NetCollectionsPeriodsDto, NetCollectionsService, TransportVolumeDto } from '../metrics/net-collections.service';
import { KpiCardComponent } from './kpi-card.component';
import { NetCashPanelComponent } from './net-cash-panel.component';
import { ProjectionPanelComponent } from './projection-panel.component';
import { SegmentOption, SegmentedControlComponent } from './segmented-control.component';
import { SERVICE_LINE_OPTIONS, ServiceLineSelection, defaultLabels } from './service-line';
import { SkeletonComponent } from '../shared/skeleton.component';
import { ServiceLineToggleComponent } from './service-line-toggle.component';
import { CLEAN_CLAIM_GOAL, toCleanClaimCard } from './clean-claims';
import { MileUnit, toMilesCard, toTransportsCard } from './transport-volume';

const ALL_DIVISIONS = 'all';
const CRUMBS: EmsBreadcrumb[] = [{ label: 'Dashboard' }];

/** The top of the dashboard: Provider, Data Through, and the filters. The metrics below it arrive with the first Metric Catalog slice. */
@Component({
  selector: 'app-dashboard',
  imports: [EmsBreadcrumbsComponent, EmsButtonDirective, EmsSelectComponent, KpiCardComponent, NetCashPanelComponent, ProjectionPanelComponent, ReactiveFormsModule, SegmentedControlComponent, ServiceLineToggleComponent, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="dashboard">
      <ems-breadcrumbs class="dashboard__crumbs" [items]="crumbs" />

      <header class="dashboard__header">
        <div class="dashboard__title">
          <h1>{{ providerName() }}</h1>
          <p class="dashboard__subtitle">
            Revenue overview@if (dataThrough()) { · Data through {{ dataThrough() }} }
          </p>
        </div>

        <form class="dashboard__filters" aria-label="Dashboard filters" (submit)="$event.preventDefault()">
          @if (showServiceLineToggle()) {
            <app-service-line-toggle [options]="serviceLineOptions()" [(value)]="serviceLine" />
          }
          <ems-select class="dashboard__select" label="Period" [options]="periods()" [formControl]="period" [clearable]="false" />
          <ems-select
            class="dashboard__select"
            [label]="labels().reportingDivision"
            [options]="divisionOptions()"
            [formControl]="division"
            [clearable]="false"
          />
        </form>
      </header>

      <!-- Net cash, transports and loaded miles are real. The other tiles and the projection panel below are layout only: placeholder content until the Metric Catalog serves them. -->
      <section class="dashboard__overview" aria-label="Key metrics">
        <app-net-cash-panel class="dashboard__cash" [periodsLoading]="periodsLoading()" [providerId]="providerId()" [endPeriod]="endPeriod()" [serviceLine]="serviceLine()" [periodsError]="periodsError()" (retryPeriods)="retryPeriods()" />

        <div class="dashboard__tiles">
        <app-kpi-card title="Transports" [value]="transportsCard().value" [delta]="transportsCard().delta" [footnote]="transportsCard().footnote" [caveat]="volumeCaveat()" [loading]="volumeLoading()">
          @if (volumeLoading()) {
            <app-skeleton height="4.75rem" />
          } @else if (volumeFailed()) {
            <p class="card-problem" role="status">
              We can't load this right now.
              <button emsButton="flat" type="button" (click)="retryVolume()">Try again</button>
            </p>
          } @else if (transportsCard().bars.length) {
            <div class="bars" role="img" [attr.aria-label]="transportsCard().description">
              @for (height of transportsCard().bars; track $index) {
                <span class="bars__col" [title]="transportsCard().titles[$index]!">
                  <span class="chart-label" [class.chart-label--current]="$last">{{ transportsCard().labels[$index] }}</span>
                  <span class="bars__track"><span class="bars__bar" [class.bars__bar--current]="$last" [style.height.%]="height"></span></span>
                </span>
              }
            </div>
          }
        </app-kpi-card>

        <app-kpi-card title="Loaded miles" [value]="milesCard().value" [unit]="milesCard().unit" [unitHidden]="true" [delta]="milesCard().delta" footnote="Billed miles, patient on board" [caveat]="volumeCaveat()" [loading]="volumeLoading()">
          @if (volumeLoading()) {
            <app-skeleton height="4.75rem" />
          } @else if (volumeFailed()) {
            <p class="card-problem" role="status">
              We can't load this right now.
              <button emsButton="flat" type="button" (click)="retryVolume()">Try again</button>
            </p>
          } @else if (milesCard().line) {
            <div class="spark" role="img" [attr.aria-label]="milesCard().description">
              <svg class="spark__line" viewBox="0 0 100 30" preserveAspectRatio="none" aria-hidden="true">
                <polyline [attr.points]="milesCard().line" fill="none" stroke="currentColor" stroke-width="2" vector-effect="non-scaling-stroke" />
              </svg>
              @for (mark of milesCard().marks; track $index) {
                <span class="chart-label spark__label" [class.chart-label--current]="$last" [style.left.%]="mark.x" [style.--y]="mark.y">{{ mark.label }}</span>
              }
            </div>
          }
          @if (serviceLine() !== 'ground') {
            <app-segmented-control kpiAction [compact]="true" label="Mileage unit" [options]="mileUnits" [(value)]="mileUnit" />
          }
        </app-kpi-card>

        <app-kpi-card title="Clean claim rate" [value]="cleanCard().value" [delta]="cleanCard().delta" footnote="First-pass resolution" [caveat]="cleanCaveat()" [loading]="cleanLoading()">
          @if (cleanLoading()) {
            <app-skeleton height="1.5rem" />
          } @else if (cleanFailed()) {
            <p class="card-problem" role="status">
              We can't load this right now.
              <button emsButton="flat" type="button" (click)="retryClean()">Try again</button>
            </p>
          } @else if (cleanCard().percent !== null) {
            <div class="goal" role="img" [attr.aria-label]="cleanCard().description">
              <div class="goal__track"><span class="goal__fill" [style.width.%]="cleanCard().percent"></span><span class="goal__mark" [style.left.%]="goal"></span></div>
              <div class="goal__scale"><span>0%</span><span>Goal {{ goal }}%</span></div>
            </div>
          }
        </app-kpi-card>

        <app-kpi-card
          title="Open negotiation"
          value="7"
          [delta]="{ direction: 'up', text: '3 settled in Oct at 92% of expected' }"
          footnote="No Surprises Act, 30-day window. 2 close this month (highlighted), then go to IDR"
        >
          <div class="dots" role="img" aria-label="7 open negotiations, 2 closing this month">
            @for (closing of negotiations; track $index) {
              <span class="dots__dot" [class.dots__dot--closing]="closing"></span>
            }
          </div>
        </app-kpi-card>
        </div>
      </section>

      <app-projection-panel />
    </main>
  `,
  styles: `
    .dashboard {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
    }
    .dashboard__crumbs {
      margin-top: 0.5rem;
    }
    /* The menu wraps under the logo on small screens and brings its own space, so the page starts closer to it. */
    @media (max-width: 899.98px) {
      .dashboard {
        padding-block-start: 0.25rem;
      }
      .dashboard__crumbs {
        margin-top: 0;
      }
    }
    .dashboard__header {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: 1.5rem;
    }
    .dashboard__title h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .dashboard__subtitle {
      margin: 0.25rem 0 0;
      line-height: var(--ems-line-height-body);
      color: var(--ems-text-muted);
    }
    .dashboard__filters {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 1rem;
    }
    /* Net cash on the left, the four tiles on the right. The tiles take the height they need and the net cash panel stretches to match, so the spare height goes to its chart. */
    .dashboard__overview {
      display: flex;
      align-items: stretch;
      gap: 1rem;
    }
    .dashboard__cash {
      flex: 1 1 0;
      min-width: 0;
    }
    .dashboard__tiles {
      flex: 1 1 0;
      min-width: 0;
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      align-content: start;
      gap: 1rem;
    }
    @media (max-width: 70rem) {
      .dashboard__overview {
        flex-direction: column;
      }
    }
    @media (max-width: 40rem) {
      .dashboard__tiles {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    :host {
      --chart-height: 4.75rem;
    }
    .bars {
      display: flex;
      gap: 0.5rem;
      height: var(--chart-height);
    }
    .bars__col {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
    }
    .bars__track {
      flex: 1;
      display: flex;
      align-items: flex-end;
    }
    .chart-label {
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      color: var(--ems-text-muted);
      text-align: center;
      white-space: nowrap;
    }
    .chart-label--current {
      color: var(--ems-text-default);
      font-weight: 600;
    }
    .bars__bar {
      width: 100%;
      border-radius: 2px;
      background: var(--ems-surface-neutral-strong);
    }
    .bars__bar--current {
      background: var(--ems-surface-accent-emerald);
    }
    .card-problem {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.25rem 0.75rem;
      margin: 0;
      color: var(--ems-text-muted);
      line-height: var(--ems-line-height-body);
    }
    .spark {
      position: relative;
      height: var(--chart-height);
      color: var(--ems-surface-accent-emerald);
    }
    /* The line sits below a strip kept for the figures. The box is stretched, so the figures are plain text laid over it at the same percentages. */
    .spark__line {
      position: absolute;
      inset: 1.25rem 0 0.25rem;
      width: 100%;
      height: calc(100% - 1.5rem);
    }
    .spark__label {
      position: absolute;
      top: calc(1.25rem + var(--y) / 30 * (100% - 1.5rem));
      transform: translate(-50%, -100%);
      /* A halo in the card colour keeps the line from running through the figure. */
      text-shadow: 0 0 3px var(--ems-surface-neutral-elevated), 0 0 3px var(--ems-surface-neutral-elevated), 0 0 2px var(--ems-surface-neutral-elevated);
    }
    .goal__track {
      position: relative;
      height: 0.5rem;
      border-radius: 999px;
      background: var(--ems-surface-neutral-strong);
    }
    .goal__fill {
      display: block;
      height: 100%;
      border-radius: 999px;
      background: var(--ems-surface-accent-emerald);
    }
    .goal__mark {
      position: absolute;
      top: -0.25rem;
      width: 2px;
      height: 1rem;
      background: var(--ems-text-default);
    }
    .goal__scale {
      display: flex;
      justify-content: space-between;
      margin-top: 0.25rem;
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      color: var(--ems-text-muted);
    }
    .dots {
      display: flex;
      flex-wrap: wrap;
      gap: 0.375rem;
    }
    .dots__dot {
      width: 1rem;
      height: 1rem;
      border-radius: 4px;
      background: var(--ems-surface-neutral-strong);
    }
    .dots__dot--closing {
      background: var(--ems-surface-accent-citrus);
    }
    .dashboard__select {
      min-width: 12rem;
    }
  `,
})
export class DashboardComponent {
  private readonly auth = inject(AuthService);
  private readonly netCollections = inject(NetCollectionsService);

  /** Mocked: replaced by the Provider context API. */
  protected readonly context = signal<DashboardContext>(MOCK_CONTEXT);

  protected readonly crumbs = CRUMBS;

  // Placeholder visuals, replaced with Metric Catalog data.
  protected readonly negotiations = [true, true, false, false, false, false, false];
  protected readonly mileUnits: readonly SegmentOption<MileUnit>[] = [
    { value: 'statute', label: 'Statute' },
    { value: 'nautical', label: 'Nautical' },
  ];
  /** Nautical miles are an aviation unit. Ground transports are always in statute miles, so the switch is not offered for them. */
  protected readonly mileUnit = signal<MileUnit>('statute');

  protected readonly serviceLine = signal<ServiceLineSelection>('air');
  /** The newest accounting period to show, as yyyyMM. Empty until the Provider's periods have loaded. */
  protected readonly period = new FormControl<string>('', { nonNullable: true });
  protected readonly division = new FormControl<string>(ALL_DIVISIONS, { nonNullable: true });

  /** The first Provider name in the session, exactly as the token carries it. The mock name shows only when the user has no Provider. */
  protected readonly providerName = computed(() => this.auth.me()?.providers[0]?.name || this.context().providerName);

  /** The Provider whose data is shown: the first one the user was assigned. A switcher comes later. The API checks it again. */
  protected readonly providerId = computed(() => this.auth.me()?.providers[0]?.id ?? null);

  private readonly periodsResource = rxResource<NetCollectionsPeriodsDto | null, string | null>({
    params: () => this.providerId(),
    stream: ({ params }) => (params ? this.netCollections.periods(params) : of(null)),
  });

  protected readonly periodsError = computed<unknown>(() => this.periodsResource.error());
  /** Nothing can be asked for until the list of months is here, so every figure waits on it too. */
  protected readonly periodsLoading = computed(() => this.periodsResource.isLoading());

  protected retryPeriods(): void {
    this.periodsResource.reload();
  }

  /** The newest accounting period that has data. The data is complete through the end of it. */
  private readonly newestPeriod = computed(() => (this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods[0]?.period ?? '');
  protected readonly dataThrough = computed(() => {
    const end = apPeriodEnd(this.newestPeriod());
    return end ? formatDate(end) : '';
  });

  protected readonly showServiceLineToggle = computed(() => this.context().enabledServiceLines.length > 1);
  protected readonly serviceLineOptions = computed(() =>
    SERVICE_LINE_OPTIONS.filter((option) => option.value === 'both' || this.context().enabledServiceLines.includes(option.value)),
  );

  protected readonly labels = computed(() => defaultLabels(this.serviceLine()));
  protected readonly periods = computed<EmsPickerOption<string>[]>(() =>
    ((this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods ?? []).map((p) => ({ value: p.period, label: apPeriodLabel(p.period) })),
  );
  protected readonly endPeriod = toSignal(this.period.valueChanges, { initialValue: this.period.value });

  /** Transports and loaded miles for the six months ending at the chosen period, for the Service Line on screen. One request feeds both tiles. */
  private readonly volume = rxResource<TransportVolumeDto | null, { providerId: string; endPeriod: string; line: 'all' | 'air' | 'ground' } | null>({
    params: () => {
      const providerId = this.providerId();
      const endPeriod = this.endPeriod();
      const line = this.serviceLine();
      return providerId && endPeriod ? { providerId, endPeriod, line: line === 'both' ? 'all' : line } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.transports(params.providerId, params.endPeriod, 6, params.line) : of(null)),
  });
  private readonly volumeValue = computed(() => (this.volume.hasValue() ? this.volume.value() : null));
  protected readonly volumeLoading = computed(() => this.periodsLoading() || this.volume.isLoading());
  protected readonly volumeFailed = computed(() => this.volume.error() !== undefined);
  protected readonly transportsCard = computed(() => toTransportsCard(this.volumeValue()));
  protected readonly milesCard = computed(() => toMilesCard(this.volumeValue(), this.serviceLine() === 'ground' ? 'statute' : this.mileUnit()));
  protected readonly volumeCaveat = computed(() => (this.volumeValue()?.periodOpen ? 'This month is still open, so counts may rise.' : ''));

  protected readonly goal = CLEAN_CLAIM_GOAL;

  /** The clean claim rate for the six months up to the chosen period. The warehouse table is not split by Service Line, so the toggle does not change it. */
  private readonly clean = rxResource<CleanClaimRateDto | null, { providerId: string; endPeriod: string } | null>({
    params: () => {
      const providerId = this.providerId();
      const endPeriod = this.endPeriod();
      return providerId && endPeriod ? { providerId, endPeriod } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.cleanClaims(params.providerId, params.endPeriod, 6) : of(null)),
  });
  protected readonly cleanLoading = computed(() => this.periodsLoading() || this.clean.isLoading());
  protected readonly cleanFailed = computed(() => this.clean.error() !== undefined);
  protected readonly cleanCard = computed(() => toCleanClaimCard(this.clean.hasValue() ? this.clean.value() : null, this.endPeriod()));
  protected readonly cleanCaveat = computed(() =>
    [this.cleanCard().latestNote, this.serviceLine() === 'both' ? '' : 'Covers all service lines: the data is not split by air or ground.'].filter(Boolean).join(' '),
  );

  protected retryClean(): void {
    this.clean.reload();
  }

  protected retryVolume(): void {
    this.volume.reload();
  }
  protected readonly divisionOptions = computed<EmsPickerOption<string>[]>(() => [
    { value: ALL_DIVISIONS, label: this.labels().allReportingDivisions },
    ...divisionsFor(this.context().reportingDivisions, this.serviceLine()).map((division) => ({
      value: division.id,
      label: division.name,
    })),
  ]);

  private readonly selectedDivision = toSignal(this.division.valueChanges, { initialValue: this.division.value });

  constructor() {
    // Start on the newest period once the list arrives, and again if the chosen one is no longer in the list (a different Provider).
    effect(() => {
      const options = this.periods();
      const selected = this.endPeriod();
      if (options.length > 0 && !options.some((option) => option.value === selected)) {
        untracked(() => this.period.setValue(options[0]!.value));
      }
    });
    // Changing the Service Line can remove the chosen Reporting Division from the list. Fall back to "all" instead of leaving a dead choice.
    effect(() => {
      const options = this.divisionOptions();
      const selected = this.selectedDivision();
      if (!options.some((option) => option.value === selected)) {
        untracked(() => this.division.setValue(ALL_DIVISIONS));
      }
    });
  }
}
