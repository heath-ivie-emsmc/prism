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
import { mockMetricCards } from './dashboard-metrics';
import { MetricCardComponent } from './metric-card.component';
import { SERVICE_LINE_OPTIONS, ServiceLineSelection, defaultLabels } from './service-line';
import { ServiceLineToggleComponent } from './service-line-toggle.component';

const ALL_DIVISIONS = 'all';
const CRUMBS: EmsBreadcrumb[] = [{ label: 'Dashboard' }];

/** The top of the dashboard: Provider, Data Through, and the filters. The metrics below it arrive with the first Metric Catalog slice. */
@Component({
  selector: 'app-dashboard',
  imports: [EmsBreadcrumbsComponent, EmsSelectComponent, MetricCardComponent, ReactiveFormsModule, ServiceLineToggleComponent],
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

      <section class="dashboard__metrics" aria-label="Key metrics">
        @for (metric of metrics(); track metric.key) {
          <app-metric-card [metric]="metric" />
        }
      </section>
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
      font-weight: var(--ems-font-weight-heavy);
    }
    .dashboard__subtitle {
      margin: 0.25rem 0 0;
      color: var(--ems-text-muted);
    }
    .dashboard__filters {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 1rem;
    }
    .dashboard__metrics {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr));
      gap: 1rem;
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

  protected readonly serviceLine = signal<ServiceLineSelection>('air');
  protected readonly period = new FormControl<string>(periodOptions(MOCK_CONTEXT.dataThrough)[0]?.value ?? '', { nonNullable: true });
  protected readonly division = new FormControl<string>(ALL_DIVISIONS, { nonNullable: true });

  /** The first Provider name in the session, exactly as the token carries it. The mock name shows only when the user has no Provider. */
  protected readonly providerName = computed(() => this.auth.me()?.providers[0]?.name || this.context().providerName);

  protected readonly metrics = computed(() => mockMetricCards(this.serviceLine()));
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
