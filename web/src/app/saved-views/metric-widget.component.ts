import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, untracked } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { ChartColors, ChartComponent } from '../charts/chart.component';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { drillLinkName } from '../metrics/drill-link';
import { metricDivisionSupport } from '../metrics/metric-catalog.service';
import { DataFreshnessService } from '../shared/data-freshness.service';
import { ContextBadgeComponent } from '../shared/context-badge.component';
import { FreshnessStampComponent } from '../shared/freshness-stamp.component';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { CatalogMetricDto, LayoutWidget } from './layout.model';
import { ChartKind, buildWidgetOption } from './widget-chart.options';
import { LOADABLE_METRICS, WidgetDataService, WidgetQuery } from './widget-data.service';
import { WidgetData } from './widget-data';

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
  imports: [RouterLink, ChartComponent, ContextBadgeComponent, FreshnessStampComponent, LoadProblemComponent, SkeletonComponent],
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
          @if (widget().presentation === 'table') {
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
                  @for (row of tableRows(); track $index) {
                    <tr>
                      @for (cell of row.cells; track $index) {
                        @if ($first) {
                          <th scope="row">
                            @if (row.drill; as drill) {
                              <a [routerLink]="drill.commands" [queryParams]="drill.queryParams" [attr.aria-label]="drill.name">{{ cell }}</a>
                            } @else {
                              {{ cell }}
                            }
                          </th>
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
            @if (drillLinks().length > 0) {
              <ul class="widget__drills" aria-label="Open a breakdown">
                @for (link of drillLinks(); track link.name) {
                  <li><a [routerLink]="link.commands" [queryParams]="link.queryParams" [attr.aria-label]="link.name">{{ link.label }}</a></li>
                }
              </ul>
            }
          }
        }
        @if (extraNote()) {
          <p class="widget__foot">{{ extraNote() }}</p>
        }
        @if (data.caveat) {
          <p class="widget__foot">{{ data.caveat }}</p>
        }
        <app-context-badge [serviceLine]="badgeLine()" [divisionApplied]="divisionApplied()" [labels]="filters.labels()" />
        <app-freshness-stamp [dataThrough]="data.dataThrough" />
      }
    }
  `,
  styles: `
    /* The widget fills the card the grid gave it, and the chart takes whatever is left after the notes. A taller widget gives a taller chart. */
    :host {
      display: flex;
      flex-direction: column;
      flex: 1 1 auto;
      min-width: 0;
      min-height: 0;
      --chart-height: 100%;
    }
    app-chart {
      flex: 1 1 auto;
      min-height: 14rem;
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
    .widget__drills {
      display: flex;
      flex-wrap: wrap;
      gap: 0.25rem 1rem;
      margin: 0.5rem 0 0;
      padding: 0;
      list-style: none;
      font-size: var(--ems-font-size-small);
    }
    .widget__drills a,
    th a {
      display: inline-block;
      min-height: 1.5rem;
      color: var(--ems-text-link, inherit);
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
      flex: 1 1 auto;
      min-height: 0;
      overflow-y: auto;
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

  protected readonly filters = inject(DashboardFilterContext);
  private readonly loader = inject(WidgetDataService);
  private readonly freshness = inject(DataFreshnessService);

  private readonly honorsDivision = computed(() => metricDivisionSupport(this.metric()) === 'supported');
  protected readonly unavailable = computed(() => !LOADABLE_METRICS.includes(this.widget().metricKey) || !this.metric().presentations.some((p) => p.key === this.widget().presentation));
  protected readonly lineName = computed(() => ({ air: 'air', ground: 'ground', both: 'air and ground together' })[this.filters.serviceLine()]);
  protected readonly unsupported = computed(() => !this.metric().serviceLines.includes(this.filters.serviceLine()));
  protected readonly ready = computed(() => !!this.filters.providerId() && !!this.filters.endPeriod() && !this.filters.periodsLoading());
  protected readonly isTile = computed(() => this.widget().presentation === 'tile');
  protected readonly periodLabel = computed(() => this.filters.periods().find((p) => p.value === this.filters.endPeriod())?.label ?? 'this period');

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
    const division = this.honorsDivision() ? this.filters.divisionKey() : undefined;
    return { metricKey: this.widget().metricKey, providerId, period, serviceLine, months, ...(division ? { division } : {}), revision: this.freshness.revision() };
  });

  protected readonly resource = rxResource<WidgetData | null, WidgetQuery | null>({
    params: () => this.query(),
    stream: ({ params }) => (params ? this.loader.load(params) : of(null)),
  });

  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  /** A Metric that ignores the Reporting Division filter says so when a division is chosen, instead of looking filtered. */
  protected readonly extraNote = computed(() =>
    !this.honorsDivision() && this.filters.selectedDivision() !== 'all' ? `Not filtered by ${this.filters.labels().reportingDivision}: showing ${this.filters.labels().allReportingDivisions.toLowerCase()}.` : '',
  );
  /** True only when the figures really are narrowed: the Metric honors the Division filter and one is selected. Otherwise the badge says all divisions. */
  protected readonly divisionApplied = computed(() => this.honorsDivision() && this.filters.selectedDivision() !== 'all');
  protected readonly badgeLine = computed<'all' | 'air' | 'ground'>(() => {
    const selected = this.filters.serviceLine();
    return selected === 'both' ? 'all' : selected;
  });

  /** Table rows with the link each leads to, if any, worked out once so the template calls nothing. */
  protected readonly tableRows = computed(() => {
    const data = this.data();
    if (!data) return [];
    return data.table.rows.map((cells, i) => ({ cells, drill: this.drillAt(data, i) }));
  });
  /** One link per category that has somewhere to go, for the chart presentations: keyboard users reach the drill here, as a chart's marks are not focusable. */
  protected readonly drillLinks = computed(() => {
    const data = this.data();
    if (!data?.drills) return [];
    return data.categories.flatMap((label, i) => {
      const drill = this.drillAt(data, i);
      return drill ? [{ label, ...drill }] : [];
    });
  });

  private drillAt(data: WidgetData, index: number): { commands: readonly string[]; queryParams: Record<string, string>; name: string } | null {
    const route = data.drills?.[index];
    const label = data.categories[index];
    return route && label ? { commands: route.commands, queryParams: { ...route.queryParams }, name: drillLinkName(label) } : null;
  }

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
