import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { EmsSelectComponent } from '@ems-management-consultants/ui';
import { DashboardFilterContext } from './dashboard-filter-context';
import { DivisionSelectComponent } from '../shared/division-select.component';
import { ServiceLineToggleComponent } from './service-line-toggle.component';

/** Service Line, period and Reporting Division, bound to the shared filter context. Used by the dashboard and the saved views. */
@Component({
  selector: 'app-filter-bar',
  imports: [DivisionSelectComponent, EmsSelectComponent, ReactiveFormsModule, ServiceLineToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="filters" [attr.aria-label]="label()" (submit)="$event.preventDefault()">
      @if (filters.showServiceLineToggle()) {
        <app-service-line-toggle [options]="filters.serviceLineOptions()" [(value)]="filters.serviceLine" />
      }
      <ems-select class="filters__select" label="Period" [options]="filters.periods()" [formControl]="filters.period" [clearable]="false" />
      <app-division-select [unavailable]="divisionUnavailable()" [ignoredBy]="divisionIgnoredBy()" />
    </form>
  `,
  styles: `
    :host {
      display: block;
      max-inline-size: 100%;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      /* Centered: the Service Line toggle is shorter than the selects, and a centered row keeps all three on one line. */
      align-items: center;
      gap: 1rem;
    }
    .filters__select {
      min-width: 12rem;
      max-inline-size: 100%;
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
}
