import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import {
  EmsCardComponent,
} from '@ems-management-consultants/ui';
import { MetricCardModel } from './dashboard-metrics';

/** One Metric: its value, an optional projection, and the breakdown that explains it. */
@Component({
  selector: 'app-metric-card',
  imports: [EmsCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-card [heading]="metric().title">
      <p class="metric__value">{{ metric().value }}</p>

      @if (metric().projection; as projection) {
        <p class="metric__projection">
          {{ projection.label }}: <strong>{{ projection.value }}</strong>
          <span class="metric__change">({{ projection.change }})</span>
        </p>
      }

      @if (metric().breakdown.length > 0) {
        <dl class="metric__breakdown">
          @for (row of metric().breakdown; track row.label) {
            <div class="metric__row">
              <dt>{{ row.label }}</dt>
              <dd>{{ row.value }}</dd>
            </div>
          }
        </dl>
      }
    </ems-card>
  `,
  styles: `
    .metric__value {
      margin: 0;
      font-size: var(--ems-font-size-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .metric__projection {
      margin: 0.5rem 0 0;
      color: var(--ems-text-muted);
    }
    .metric__change {
      margin-left: 0.25rem;
    }
    .metric__breakdown {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem 1.5rem;
      margin: 1rem 0 0;
      padding-top: 0.75rem;
      border-top: 1px solid var(--ems-border-neutral-subtle);
    }
    .metric__row {
      display: flex;
      flex-direction: column;
    }
    .metric__row dt {
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
    }
    .metric__row dd {
      margin: 0;
      font-weight: var(--ems-font-weight-bold);
    }
  `,
})
export class MetricCardComponent {
  readonly metric = input.required<MetricCardModel>();
}
