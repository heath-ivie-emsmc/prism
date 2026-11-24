import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Labels } from '../dashboard/service-line';
import { AppliesTo } from '../metrics/shared/applies-to';
import { ContextBadgeComponent } from './context-badge.component';
import { FreshnessStampComponent } from './freshness-stamp.component';

/**
 * The line under every Metric figure: the Data Through stamp, then the Context Badge. One component so a data frame, a Saved View widget and a
 * Metric page place them identically (see docs/metric-design-guide.md). Renders nothing without a Data Through date.
 */
@Component({
  selector: 'app-scope-foot',
  imports: [ContextBadgeComponent, FreshnessStampComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (dataThrough()) {
      <div class="foot">
        <app-freshness-stamp [dataThrough]="dataThrough()" />
        <app-context-badge [serviceLine]="serviceLine()" [divisionApplied]="divisionApplied()" [labels]="labels()" [appliesTo]="appliesTo()" />
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      margin-top: auto;
    }
    /* Wraps instead of overflowing at 320px; the two statements are separate lines when there is no room side by side. */
    .foot {
      display: flex;
      flex-wrap: wrap;
      gap: 0 1rem;
      padding-top: 0.5rem;
      min-width: 0;
      overflow-wrap: anywhere;
    }
  `,
})
export class ScopeFootComponent {
  /** yyyy-MM-dd, or null when unknown. */
  readonly dataThrough = input<string | null>(null);
  readonly serviceLine = input<'all' | 'air' | 'ground'>('all');
  readonly divisionApplied = input(false);
  readonly labels = input<Labels | null>(null);
  readonly appliesTo = input<AppliesTo | null>(null);
}
