import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { EmsSelectComponent } from '@ems-management-consultants/ui';
import { DashboardFilterContext } from './dashboard-filter-context';
import { ServiceLineToggleComponent } from './service-line-toggle.component';

/** Service Line, period and Reporting Division, bound to the shared filter context. Used by the dashboard and the saved views. */
@Component({
  selector: 'app-filter-bar',
  imports: [EmsSelectComponent, ReactiveFormsModule, ServiceLineToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="filters" [attr.aria-label]="label()" (submit)="$event.preventDefault()">
      @if (filters.showServiceLineToggle()) {
        <app-service-line-toggle [options]="filters.serviceLineOptions()" [(value)]="filters.serviceLine" />
      }
      <ems-select class="filters__select" label="Period" [options]="filters.periods()" [formControl]="filters.period" [clearable]="false" />
      @if (divisionUnavailable()) {
        <div class="filters__unavailable">
          <ems-select class="filters__select" [label]="filters.labels().reportingDivision" [options]="filters.divisionOptions().slice(0, 1)" [formControl]="idle" [clearable]="false" aria-describedby="division-reason" />
          <p id="division-reason" class="filters__reason">{{ unavailableNote() }}</p>
        </div>
      } @else {
        <div class="filters__unavailable">
          <ems-select
            class="filters__select"
            [label]="filters.labels().reportingDivision"
            [options]="filters.divisionOptions()"
            [formControl]="filters.division"
            [clearable]="false"
            [attr.aria-describedby]="ignoredNote() ? 'division-reason' : null"
          />
          @if (ignoredNote()) {
            <p id="division-reason" class="filters__reason">{{ ignoredNote() }}</p>
          }
        </div>
      }
    </form>
  `,
  styles: `
    .filters {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 1rem;
    }
    .filters__select {
      min-width: 12rem;
    }
    .filters__unavailable {
      display: grid;
      gap: 0.25rem;
    }
    .filters__reason {
      margin: 0;
      max-width: min(18rem, 100%);
      overflow-wrap: anywhere;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
  `,
})
export class FilterBarComponent {
  protected readonly filters = inject(DashboardFilterContext);
  /** The accessible name of the form. */
  readonly label = input('Dashboard filters');
  /** True when nothing on the page can be split by Reporting Division yet: the control stays visible but off, with the reason beside it. */
  readonly divisionUnavailable = input(false);
  /** Names of the Metrics on the page that always show every Reporting Division, from the Metric Catalog. */
  readonly divisionIgnoredBy = input<readonly string[]>([]);
  protected readonly ignoredNote = computed(() => {
    const names = this.divisionIgnoredBy();
    if (names.length === 0) return '';
    return `${this.filters.labels().reportingDivision} does not change ${names.join(', ')}. ${names.length === 1 ? 'It shows' : 'They show'} ${this.filters.labels().allReportingDivisions.toLowerCase()}.`;
  });
  /** Why the control is off. Names the Metrics (by their catalog names, when the page passes them) that can't be split, and uses the Label for the term. */
  protected readonly unavailableNote = computed(() => {
    const term = this.filters.labels().reportingDivision;
    const names = this.divisionIgnoredBy();
    return names.length === 0 ? `Not applied yet: the metrics on this view can't be split by ${term}.` : `Not applied yet: ${names.join(', ')} can't be split by ${term}.`;
  });
  protected readonly idle = new FormControl({ value: 'all', disabled: true }, { nonNullable: true });
}
