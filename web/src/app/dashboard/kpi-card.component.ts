import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { EmsCardComponent, EmsIconComponent } from '@ems-management-consultants/ui';

export interface KpiDelta {
  readonly direction: 'up' | 'down';
  readonly text: string;
}

/** Small Metric tile: title, big value with a unit, a visual slot, a change line and a footnote. Layout only: it knows nothing about any Metric. */
@Component({
  selector: 'app-kpi-card',
  imports: [EmsCardComponent, EmsIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-card [heading]="title()">
      <p class="kpi__value">
        {{ value() }}
        @if (unit()) {
          <span class="kpi__unit">{{ unit() }}</span>
        }
      </p>
      <div class="kpi__visual"><ng-content /></div>
      @if (delta(); as delta) {
        <p class="kpi__delta">
          <ems-icon [name]="delta.direction === 'up' ? 'arrow_upward' : 'arrow_downward'" size="sm" />
          {{ delta.text }}
        </p>
      }
      @if (footnote()) {
        <p class="kpi__footnote">{{ footnote() }}</p>
      }
    </ems-card>
  `,
  styles: `
    :host {
      display: block;
      height: 100%;
    }
    ems-card {
      height: 100%;
    }
    .kpi__value {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .kpi__unit {
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      font-weight: var(--ems-font-weight-regular, 400);
      color: var(--ems-text-muted);
    }
    .kpi__visual {
      margin: 0.75rem 0;
      min-height: 3rem;
    }
    .kpi__delta,
    .kpi__footnote {
      display: flex;
      align-items: center;
      gap: 0.25rem;
      margin: 0.25rem 0 0;
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      color: var(--ems-text-muted);
    }
  `,
})
export class KpiCardComponent {
  readonly title = input.required<string>();
  readonly value = input.required<string>();
  readonly unit = input('');
  readonly delta = input<KpiDelta | null>(null);
  readonly footnote = input('');
}
