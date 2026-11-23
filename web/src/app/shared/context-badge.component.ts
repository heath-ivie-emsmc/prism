import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Labels, defaultLabels } from '../dashboard/service-line';

/** Visible statement of what the figures cover, for example "Air · All bases". Division wording comes from the Labels. */
@Component({
  selector: 'app-context-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="badge">{{ text() }}</p>`,
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
  readonly serviceLine = input<'all' | 'air' | 'ground'>('all');
  /** True only when the figures really are narrowed to one Reporting Division. No Metric does that yet. */
  readonly divisionApplied = input(false);
  readonly labels = input<Labels | null>(null);

  protected readonly text = computed(() => {
    const line = this.serviceLine();
    const labels = this.labels() ?? defaultLabels(line === 'air' ? 'air' : 'both');
    const lineText = { all: 'All service lines', air: 'Air', ground: 'Ground' }[line];
    return `${lineText} · ${this.divisionApplied() ? `Selected ${labels.reportingDivision.toLowerCase()}` : labels.allReportingDivisions}`;
  });
}
