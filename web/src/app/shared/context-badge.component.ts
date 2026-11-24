import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { Labels, defaultLabels } from '../dashboard/service-line';
import { AppliesTo, appliesToText } from '../metrics/shared/applies-to';

/** Visible statement of what the figures cover, for example "Air · All bases" or "Air · Mercy Flights Base 1". The all-divisions wording comes from the Labels, the chosen division is named from the Provider's loaded list (text only). */
@Component({
  selector: 'app-context-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="badge">{{ text() }}@if (scope()) {<span class="badge__scope"> · {{ scope() }}</span>}</p>`,
  styles: `
    :host {
      display: block;
    }
    .badge {
      margin: 0;
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
  `,
})
export class ContextBadgeComponent {
  private readonly filters = inject(DashboardFilterContext);
  /** The warehouse name of the chosen division, or '' when it is not in the loaded list (the Label wording is used then). */
  private readonly divisionName = computed(() => {
    const key = this.divisionKey() ?? this.filters.divisionKey();
    return key ? (this.filters.divisionName(key) ?? '') : '';
  });

  readonly serviceLine = input<'all' | 'air' | 'ground'>('all');
  /** True only when the figures really are narrowed to one Reporting Division. No Metric does that yet. */
  readonly divisionApplied = input(false);
  /** The chosen division's key when the page takes it from the address. Without it the shared filter's choice is used. */
  readonly divisionKey = input<string | undefined>(undefined);
  readonly labels = input<Labels | null>(null);
  /** The Metric's own scope. "Air only" and "Ground only" are shown beside the badge, and they decide the line the badge names, since such a Metric covers that line whatever the page filter says. "Air and ground" shows nothing unless `showBoth` is set. */
  readonly appliesTo = input<AppliesTo | null>(null);
  /** Names "Air and ground" too: the Saved View palette lists every Metric's scope. */
  readonly showBoth = input(false);

  private readonly effectiveLine = computed<'all' | 'air' | 'ground'>(() => {
    const scope = this.appliesTo();
    return scope === 'air' || scope === 'ground' ? scope : this.serviceLine();
  });

  protected readonly scope = computed(() => {
    const scope = this.appliesTo();
    if (!scope || (scope === 'both' && !this.showBoth())) return '';
    return appliesToText(scope);
  });

  protected readonly text = computed(() => {
    const line = this.effectiveLine();
    // The Labels passed in belong to the page filter. When the Metric overrides the line, the default Labels of the line in effect apply.
    const labels = (line === this.serviceLine() ? this.labels() : null) ?? defaultLabels(line === 'air' ? 'air' : 'both');
    const lineText = { all: 'All service lines', air: 'Air', ground: 'Ground' }[line];
    return `${lineText} · ${this.divisionApplied() ? (this.divisionName() || `Selected ${labels.reportingDivision.toLowerCase()}`) : labels.allReportingDivisions}`;
  });
}
