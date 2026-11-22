import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { EmsButtonDirective } from '@ems-management-consultants/ui';
import { of } from 'rxjs';
import { ChartColors, ChartComponent } from '../charts/chart.component';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { CatalogMetricDto, LayoutWidget } from './layout.model';
import { ChartKind, buildWidgetOption } from './widget-chart.options';
import { LOADABLE_METRICS, WidgetDataService, WidgetQuery } from './widget-data.service';
import { WidgetData, dataThroughText } from './widget-data';

/** How a widget reports its state up, so the page can announce one message for all of them. */
export type WidgetStatus = 'loading' | 'ready' | 'failed' | 'idle';

const CHART_KINDS: ReadonlySet<string> = new Set(['line', 'column', 'stacked', 'donut']);

/**
 * The body of one widget: loads its Metric through the page filters and draws it as a tile, a chart or a table.
 * It reads the shared filter context as the params of its own resource, so a change to Service Line or period refetches without any event wiring.
 * Every chart has the same numbers as a real table one control away (WCAG 1.1.1), and an unavailable Metric never fetches.
 */
@Component({
  selector: 'app-metric-widget',
  imports: [ChartComponent, EmsButtonDirective, LoadProblemComponent, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (unavailable()) {
      <p class="widget__note"><strong>Metric no longer available</strong><br />This metric isn't available any more.@if (editing()) { Remove it from this view. }</p>
    } @else if (unsupported()) {
      <p class="widget__note">{{ metric().name }} isn't available for {{ lineName() }}. Choose another Service Line to see it.</p>
    } @else if (!ready()) {
      <app-skeleton height="6rem" />
    } @else if (resource.isLoading() && !data()) {
      <span class="cdk-visually-hidden">Loading {{ metric().name }}</span>
      <app-skeleton height="6rem" />
    } @else if (resource.error() !== undefined) {
      <app-load-problem [error]="resource.error()" [subject]="metric().name" (retry)="resource.reload()" />
    } @else if (data(); as data) {
      @if (data.empty) {
        <p class="widget__note">No {{ metric().name.toLowerCase() }} data for {{ periodLabel() }}.</p>
      } @else {
        @if (isTile()) {
          <p class="widget__value">{{ data.headline?.value }}</p>
          <p class="widget__note">{{ data.headline?.note }}</p>
        } @else {
          @if (canToggle()) {
            <div class="widget__toggle" data-snapshot-skip role="group" [attr.aria-label]="'View ' + metric().name + ' as'">
              <button emsButton="flat" type="button" [attr.aria-pressed]="!showTable()" (click)="showTable.set(false)">Chart</button>
              <button emsButton="flat" type="button" [attr.aria-pressed]="showTable()" (click)="showTable.set(true)">Table</button>
            </div>
          }
          @if (showTable() || widget().presentation === 'table') {
            <div class="widget__table" role="region" tabindex="0" [attr.aria-label]="data.table.caption">
              <table>
                <caption>{{ data.table.caption }}</caption>
                <thead>
                  <tr>
                    @for (column of data.table.columns; track $index) {
                      <th scope="col">{{ column }}</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (row of data.table.rows; track $index) {
                    <tr>
                      @for (cell of row; track $index) {
                        @if ($first) {
                          <th scope="row">{{ cell }}</th>
                        } @else {
                          <td>{{ cell }}</td>
                        }
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          } @else {
            <app-chart [option]="optionBuilder()" [label]="data.summary" />
          }
        }
        @if (extraNote()) {
          <p class="widget__foot">{{ extraNote() }}</p>
        }
        @if (data.caveat) {
          <p class="widget__foot">{{ data.caveat }}</p>
        }
        @if (dataThrough()) {
          <p class="widget__foot">{{ dataThrough() }}</p>
        }
      }
    }
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
      --chart-height: 14rem;
    }
    .widget__value {
      margin: 0;
      font-size: var(--ems-font-size-heading, 1.75rem);
      font-weight: var(--ems-font-weight-heavy);
    }
    .widget__note,
    .widget__foot {
      margin: 0.25rem 0 0;
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      overflow-wrap: anywhere;
    }
    .widget__toggle {
      display: flex;
      gap: 0.25rem;
      margin-block-end: 0.5rem;
    }
    .widget__toggle button[aria-pressed='true'] {
      outline: 2px solid var(--ems-border-focus-default);
      outline-offset: -2px;
    }
    .widget__table {
      max-width: 100%;
      overflow-x: auto;
    }
    table {
      border-collapse: collapse;
      font-size: var(--ems-font-size-small);
      font-variant-numeric: tabular-nums;
    }
    caption {
      text-align: start;
      color: var(--ems-text-muted);
      padding-block-end: 0.25rem;
    }
    th,
    td {
      padding: 0.25rem 0.75rem 0.25rem 0;
      text-align: end;
      white-space: nowrap;
    }
    th[scope='row'],
    th:first-child {
      text-align: start;
      white-space: normal;
    }
  `,
})
export class MetricWidgetComponent {
  readonly widget = input.required<LayoutWidget>();
  readonly metric = input.required<CatalogMetricDto>();
  readonly editing = input(false);
  /** Reports loading, ready or failed, so the page can say one thing about all of its widgets. */
  readonly status = output<WidgetStatus>();

  private readonly filters = inject(DashboardFilterContext);
  private readonly loader = inject(WidgetDataService);
  protected readonly showTable = signal(false);

  protected readonly unavailable = computed(() => !LOADABLE_METRICS.includes(this.widget().metricKey) || !this.metric().presentations.some((p) => p.key === this.widget().presentation));
  protected readonly lineName = computed(() => ({ air: 'air', ground: 'ground', both: 'air and ground together' })[this.filters.serviceLine()]);
  protected readonly unsupported = computed(() => !this.metric().serviceLines.includes(this.filters.serviceLine()));
  protected readonly ready = computed(() => !!this.filters.providerId() && !!this.filters.endPeriod() && !this.filters.periodsLoading());
  protected readonly isTile = computed(() => this.widget().presentation === 'tile');
  protected readonly periodLabel = computed(() => this.filters.periods().find((p) => p.value === this.filters.endPeriod())?.label ?? 'this period');
  protected readonly canToggle = computed(() => CHART_KINDS.has(this.widget().presentation));

  /** The query, or null when the widget cannot or should not load yet. The key changes only when something the Metric depends on changes. */
  private readonly query = computed<WidgetQuery | null>(() => {
    const providerId = this.filters.providerId();
    const period = this.filters.endPeriod();
    if (this.unavailable() || this.unsupported() || !providerId || !period || this.filters.periodsLoading()) {
      return null;
    }
    const months = Number(this.widget().options?.['months'] ?? this.metric().options.find((o) => o.key === 'months')?.default ?? 6);
    const selected = this.filters.serviceLine();
    const serviceLine: WidgetQuery['serviceLine'] = selected === 'both' ? 'all' : selected;
    return { metricKey: this.widget().metricKey, providerId, period, serviceLine, months };
  });

  protected readonly resource = rxResource<WidgetData | null, WidgetQuery | null>({
    params: () => this.query(),
    stream: ({ params }) => (params ? this.loader.load(params) : of(null)),
  });

  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  /** A Metric that ignores the Reporting Division filter says so when a division is chosen, instead of looking filtered. */
  protected readonly extraNote = computed(() =>
    !this.metric().dimensions.includes('division') && this.filters.selectedDivision() !== 'all' ? `Not filtered by ${this.filters.labels().reportingDivision} yet.` : '',
  );
  protected readonly dataThrough = computed(() => (this.data() ? dataThroughText(this.data()!) : ''));

  protected readonly optionBuilder = computed(() => {
    const data = this.data();
    const kind = this.widget().presentation as ChartKind;
    return (colors: ChartColors) => buildWidgetOption(data!, CHART_KINDS.has(kind) ? kind : 'line', colors);
  });

  constructor() {
    effect(() => {
      const state: WidgetStatus = !this.query() ? 'idle' : this.resource.isLoading() ? 'loading' : this.resource.error() !== undefined ? 'failed' : 'ready';
      untracked(() => this.status.emit(state));
    });
  }
}
