import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import {
  EmsBreadcrumbsComponent,
  EmsSelectComponent,
  EmsBreadcrumb,
  EmsPickerOption,
} from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { DashboardContext, MOCK_CONTEXT, divisionsFor } from './dashboard-context';
import { formatDate, formatInstant, periodOptions } from './dashboard-format';
import { KpiCardComponent } from './kpi-card.component';
import { NetCashPanelComponent } from './net-cash-panel.component';
import { ProjectionPanelComponent } from './projection-panel.component';
import { SegmentOption, SegmentedControlComponent } from './segmented-control.component';
import { SERVICE_LINE_OPTIONS, ServiceLineSelection, defaultLabels } from './service-line';
import { ServiceLineToggleComponent } from './service-line-toggle.component';

type MileUnit = 'statute' | 'nautical';

const ALL_DIVISIONS = 'all';
const CRUMBS: EmsBreadcrumb[] = [{ label: 'Dashboard' }];

/** The top of the dashboard: Provider, Data Through, and the filters. The metrics below it arrive with the first Metric Catalog slice. */
@Component({
  selector: 'app-dashboard',
  imports: [EmsBreadcrumbsComponent, EmsSelectComponent, KpiCardComponent, NetCashPanelComponent, ProjectionPanelComponent, ReactiveFormsModule, SegmentedControlComponent, ServiceLineToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="dashboard">
      <ems-breadcrumbs class="dashboard__crumbs" [items]="crumbs" />

      <header class="dashboard__header">
        <div class="dashboard__title">
          <h1>{{ providerName() }}</h1>
          <p class="dashboard__subtitle">
            Revenue overview · Data through {{ dataThrough() }} · Updated {{ updatedAt() }}
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

      <!-- Layout only: every figure below is placeholder content until the Metric Catalog serves real values. -->
      <section class="dashboard__overview" aria-label="Key metrics">
        <app-net-cash-panel class="dashboard__cash" />

        <app-kpi-card title="Transports" value="140" [delta]="{ direction: 'down', text: '6 fewer than Sep' }" footnote="Rotor wing 92 · Fixed wing 48">
          <div class="bars" role="img" aria-label="Transports by month, last six months">
            @for (height of transportBars; track $index) {
              <span class="bars__bar" [class.bars__bar--current]="$last" [style.height.%]="height"></span>
            }
          </div>
        </app-kpi-card>

        <app-kpi-card title="Loaded miles" value="18,420" unit="statute mi" [delta]="{ direction: 'down', text: '3% fewer than Sep' }" footnote="Billed miles, patient on board">
          <svg class="spark" viewBox="0 0 100 30" preserveAspectRatio="none" role="img" aria-label="Loaded miles, last three months">
            <polyline points="2,12 50,16 98,6" fill="none" stroke="currentColor" stroke-width="2" vector-effect="non-scaling-stroke" />
          </svg>
          <app-segmented-control label="Mileage unit" [options]="mileUnits" [(value)]="mileUnit" />
        </app-kpi-card>

        <app-kpi-card title="Clean claim rate" value="91.4%" [delta]="{ direction: 'down', text: '1.7 pts lower than Sep' }" footnote="First-pass resolution">
          <div class="goal" role="img" aria-label="91.4 percent, goal 95 percent">
            <div class="goal__track"><span class="goal__fill" style="width: 91.4%"></span><span class="goal__mark" style="left: 95%"></span></div>
            <div class="goal__scale"><span>0%</span><span>Goal 95%</span></div>
          </div>
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
    .dashboard__overview {
      display: grid;
      grid-template-columns: minmax(0, 2fr) repeat(2, minmax(0, 1fr));
      grid-template-rows: repeat(2, auto);
      gap: 1rem;
    }
    .dashboard__cash {
      grid-row: span 2;
    }
    @media (max-width: 70rem) {
      .dashboard__overview {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
      .dashboard__cash {
        grid-column: 1 / -1;
        grid-row: auto;
      }
    }
    @media (max-width: 40rem) {
      .dashboard__overview {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    .bars {
      display: flex;
      align-items: flex-end;
      gap: 0.5rem;
      height: 3rem;
    }
    .bars__bar {
      flex: 1;
      border-radius: 2px;
      background: var(--ems-surface-neutral-strong);
    }
    .bars__bar--current {
      background: var(--ems-surface-accent-emerald);
    }
    .spark {
      width: 100%;
      height: 3rem;
      color: var(--ems-surface-accent-emerald);
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

  /** Mocked: replaced by the Provider context API. */
  protected readonly context = signal<DashboardContext>(MOCK_CONTEXT);

  protected readonly crumbs = CRUMBS;

  // Placeholder visuals, replaced with Metric Catalog data.
  protected readonly transportBars = [70, 85, 72, 95, 90, 80];
  protected readonly negotiations = [true, true, false, false, false, false, false];
  protected readonly mileUnits: readonly SegmentOption<MileUnit>[] = [
    { value: 'statute', label: 'Statute' },
    { value: 'nautical', label: 'Nautical' },
  ];
  protected readonly mileUnit = signal<MileUnit>('statute');

  protected readonly serviceLine = signal<ServiceLineSelection>('air');
  protected readonly period = new FormControl<string>(periodOptions(MOCK_CONTEXT.dataThrough)[0]?.value ?? '', { nonNullable: true });
  protected readonly division = new FormControl<string>(ALL_DIVISIONS, { nonNullable: true });

  /** The first Provider name in the session, exactly as the token carries it. The mock name shows only when the user has no Provider. */
  protected readonly providerName = computed(() => this.auth.me()?.providers[0]?.name || this.context().providerName);

  protected readonly dataThrough = computed(() => formatDate(this.context().dataThrough));
  protected readonly updatedAt = computed(() => formatInstant(this.context().updatedAt, this.context().timeZone));

  protected readonly showServiceLineToggle = computed(() => this.context().enabledServiceLines.length > 1);
  protected readonly serviceLineOptions = computed(() =>
    SERVICE_LINE_OPTIONS.filter((option) => option.value === 'both' || this.context().enabledServiceLines.includes(option.value)),
  );

  protected readonly labels = computed(() => defaultLabels(this.serviceLine()));
  protected readonly periods = computed<EmsPickerOption<string>[]>(() =>
    periodOptions(this.context().dataThrough).map((option) => ({ value: option.value, label: option.label })),
  );
  protected readonly divisionOptions = computed<EmsPickerOption<string>[]>(() => [
    { value: ALL_DIVISIONS, label: this.labels().allReportingDivisions },
    ...divisionsFor(this.context().reportingDivisions, this.serviceLine()).map((division) => ({
      value: division.id,
      label: division.name,
    })),
  ]);

  private readonly selectedDivision = toSignal(this.division.valueChanges, { initialValue: this.division.value });

  constructor() {
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
