import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { EmsButtonDirective, EmsPickerOption, EmsSelectComponent } from '@ems-management-consultants/ui';
import { ALL_DIVISIONS, DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { ServiceLineSelection, defaultLabels } from '../dashboard/service-line';

/** The text a followed key shows while the list has not loaded or does not hold it. */
export const CHOSEN_ON_DASHBOARD = 'Chosen on the dashboard';
export const FOLLOWS_DASHBOARD = 'Set on the dashboard. This page follows that choice.';

/**
 * The one Reporting Division control, fed by the shared DashboardFilterContext (the Provider's real divisions from the warehouse).
 * `edit` (dashboard, saved views) lets the user choose; `follow` (pages that take the division from the address, `division`) shows the
 * chosen one off, with the reason. Both say plainly when the list is loading, failed or empty, and which Metrics ignore the choice.
 * Division names are warehouse text: they are bound as text only, never as HTML.
 */
@Component({
  selector: 'app-division-select',
  imports: [EmsSelectComponent, EmsButtonDirective, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="division">
      @if (mode() === 'edit' && !blocked()) {
        <ems-select
          class="division__select"
          [label]="labels().reportingDivision"
          [options]="filters.divisionOptions()"
          [formControl]="filters.division"
          [clearable]="false"
          [attr.aria-describedby]="note() ? 'division-reason' : null"
        />
      } @else {
        <ems-select class="division__select" [label]="labels().reportingDivision" [options]="shownOptions()" [formControl]="idle" [clearable]="false" aria-describedby="division-reason" />
      }
      <!-- A persistent live region: a Service Line change that leaves the chosen division with no Transport is announced, not just drawn. -->
      <span class="cdk-visually-hidden" role="status">{{ outsideNote() }}</span>
      @if (note(); as text) {
        <p id="division-reason" class="division__reason">
          {{ text }}
          @if (problem() === 'error') {
            <button emsButton="flat" type="button" class="division__retry" (click)="filters.retryDivisions()">Try again</button>
          }
        </p>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .division {
      display: grid;
      gap: 0.25rem;
    }
    .division__select {
      min-width: 12rem;
    }
    .division__reason {
      margin: 0;
      max-width: min(18rem, 100%);
      overflow-wrap: anywhere;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
  `,
})
export class DivisionSelectComponent {
  protected readonly filters = inject(DashboardFilterContext);
  /** `edit` offers the choice. `follow` shows the key in `selectedKey` (from the address) and cannot change it. */
  readonly mode = input<'edit' | 'follow'>('edit');
  /** In `follow` mode, the division key from the address, or undefined for all. Ignored in `edit` mode. */
  readonly selectedKey = input<string | undefined>(undefined);
  /** The Service Line whose Label to use, for pages that take it from the address. Defaults to the dashboard's. */
  readonly line = input<ServiceLineSelection | undefined>(undefined);
  /** True when nothing on the page can be split by Reporting Division yet: the control stays visible but off, with the reason beside it. */
  readonly unavailable = input(false);
  /** Names of the Metrics on the page that always show every Reporting Division, from the Metric Catalog. */
  readonly ignoredBy = input<readonly string[]>([]);

  protected readonly labels = computed(() => (this.line() ? defaultLabels(this.line()!) : this.filters.labels()));
  protected readonly problem = this.filters.divisionsProblem;
  private readonly noDivisions = computed(() => this.problem() === '' && this.filters.divisions().length === 0);
  /** The list can't be chosen from: off, with the reason. */
  protected readonly blocked = computed(() => this.unavailable() || this.problem() !== '' || this.noDivisions());

  /** The followed key as an option, or "Chosen on the dashboard" when the list does not hold it (yet). */
  private readonly followedOption = computed<EmsPickerOption<string>>(() => {
    const key = this.selectedKey();
    if (!key) return { value: ALL_DIVISIONS, label: this.labels().allReportingDivisions };
    return { value: key, label: this.filters.divisionName(key) ?? CHOSEN_ON_DASHBOARD };
  });
  protected readonly shownOptions = computed<EmsPickerOption<string>[]>(() => {
    if (this.mode() === 'follow') return [this.followedOption()];
    return [{ value: ALL_DIVISIONS, label: this.labels().allReportingDivisions }];
  });
  protected readonly idle = new FormControl({ value: ALL_DIVISIONS, disabled: true }, { nonNullable: true });

  protected readonly ignoredNote = computed(() => {
    const names = this.ignoredBy();
    if (names.length === 0) return '';
    return `${this.labels().reportingDivision} does not change ${names.join(', ')}. ${names.length === 1 ? 'It shows' : 'They show'} ${this.labels().allReportingDivisions.toLowerCase()}.`;
  });
  private readonly unavailableNote = computed(() => {
    const term = this.labels().reportingDivision;
    const names = this.ignoredBy();
    return names.length === 0 ? `Not applied yet: the metrics on this view can't be split by ${term}.` : `Not applied yet: ${names.join(', ')} can't be split by ${term}.`;
  });
  /** The one line under the control: why it is off, or which Metrics ignore it. Empty when there is nothing to say. */
  protected readonly note = computed(() => {
    const term = this.labels().reportingDivision;
    if (this.mode() === 'follow') return FOLLOWS_DASHBOARD;
    if (this.unavailable()) return this.unavailableNote();
    if (this.problem() === 'loading') return `Loading the list of ${term} values.`;
    if (this.problem() === 'error') return `The list of ${term} values could not be loaded.`;
    if (this.noDivisions()) return `No ${term} values were found for this Provider.`;
    return this.outsideNote() || this.ignoredNote();
  });
  /**
   * Said when the Service Line changed and the chosen division has no Transport under the new one. The choice is kept (not silently reset), so the figures say why they are
   * empty; one pick of the all choice clears it.
   */
  protected readonly outsideNote = computed(() => {
    const outside = this.mode() === 'edit' ? this.filters.divisionOutsideLine() : null;
    return outside ? `${outside.name} has no ${outside.line} transports, so its figures are empty. Choose ${this.labels().allReportingDivisions.toLowerCase()} to see ${outside.line}.` : '';
  });

  constructor() {
    // The disabled control mirrors what it stands for. The effect reads the display option and writes the control inside untracked().
    effect(() => {
      const value = this.mode() === 'follow' ? this.followedOption().value : ALL_DIVISIONS;
      untracked(() => this.idle.setValue(value));
    });
  }
}
