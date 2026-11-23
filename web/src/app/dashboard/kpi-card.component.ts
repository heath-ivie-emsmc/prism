import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { EmsIconComponent } from '@ems-management-consultants/ui';
import { DataFrameComponent } from '../shared/data-frame.component';
import { SkeletonComponent } from '../shared/skeleton.component';

export interface KpiDelta {
  readonly direction: 'up' | 'down';
  readonly text: string;
}

/** Small Metric tile: title, big value with a unit, a visual slot, a change line and a footnote. Layout only: it knows nothing about any Metric. */
@Component({
  selector: 'app-kpi-card',
  imports: [DataFrameComponent, EmsIconComponent, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-data-frame [heading]="title()" [expandable]="expandable()" [context]="context()" [attr.aria-busy]="loading()" (expandedChange)="expanded.set($event)">
      @if (loading()) {
        <span class="cdk-visually-hidden" role="status">Loading {{ title() }}</span>
      }
      <div>
        <p class="kpi__value">
          @if (loading()) {
            <app-skeleton class="kpi__skeleton-value" width="6rem" height="2rem" />
          } @else {
            {{ value() }}
          }
          @if (unit() && !loading()) {
            <span class="kpi__unit" [class.kpi__unit--hidden]="unitHidden()">{{ unit() }}</span>
          }
        </p>
      </div>
      <div class="kpi__visual" [class.kpi__visual--expanded]="expanded()"><ng-content /></div>
      <!-- Always laid out, empty when there is no change to report, so the footnote under it sits on the same line in every tile of a row. -->
      <p class="kpi__delta" [attr.aria-hidden]="delta() ? null : 'true'">
        @if (loading()) {
          <app-skeleton width="9rem" height="0.75rem" />
        } @else if (delta(); as delta) {
          <ems-icon [name]="delta.direction === 'up' ? 'arrow_upward' : 'arrow_downward'" size="sm" />
          {{ delta.text }}
        }
      </p>
      <!-- The footnote line is the same height in every tile, tall enough for a control beside it (a unit switch), so the lines below stay level across a row. -->
      <div class="kpi__footrow">
        @if (loading()) {
          <p class="kpi__footnote"><app-skeleton width="12rem" height="0.75rem" /></p>
        } @else {
          <p class="kpi__footnote">{{ footnote() }}</p>
        }
        <ng-content select="[kpiAction]" />
      </div>
      @if (caveat() && !loading()) {
        <p class="kpi__footnote kpi__caveat">{{ caveat() }}</p>
      }
    </app-data-frame>
  `,
  styles: `
    :host {
      display: block;
      height: 100%;
    }
    /* Two lines high in every tile, so a footnote that wraps beside a control does not push the lines below it out of line with the next tile. */
    .kpi__footrow {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 0.75rem;
      min-height: 2.75rem;
    }
    .kpi__footrow .kpi__footnote {
      flex: 1 1 0;
      min-width: 0;
      margin: 0;
    }
    .kpi__footrow > :not(.kpi__footnote) {
      flex: none;
      align-self: center;
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
    /* Every picture fits in this height, so the change line and footnote below start at the same place in every tile of a row. */
    .kpi__unit--hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
    .kpi__visual {
      margin: 0.75rem 0;
      min-height: 3rem;
    }
    /* Full screen: the picture is a fixed height in the page, so it is told to be tall, and the tile centres what it holds. */
    .kpi__visual--expanded {
      --chart-height: 55dvh;
      margin-block: 1.5rem;
    }
    .kpi__delta {
      min-height: var(--ems-line-height-small);
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
  /** The unit is already shown by a control beside the figure, so it is kept for screen readers and not drawn. */
  readonly unitHidden = input(false);
  readonly delta = input<KpiDelta | null>(null);
  readonly footnote = input('');
  /** A second note under the footnote, such as a warning that the figure is not final yet. */
  readonly caveat = input('');
  /** The figures are on their way: grey blocks stand in for them. The picture in the slot is the page's to replace with its own block. */
  readonly loading = input(false);
  /** Offer full screen. Only for a tile whose picture is worth a bigger look, and only when the picture is there. */
  readonly expandable = input(false);
  /** What the figure covers, shown when the tile is full screen and the page filters are out of sight. */
  readonly context = input('');

  protected readonly expanded = signal(false);
}
