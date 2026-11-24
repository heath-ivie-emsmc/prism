import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { EmsChipComponent } from '@ems-management-consultants/ui';
import { of } from 'rxjs';
import { NetCashProjectionDto } from '../metrics/net-cash-projection/net-cash-projection.dto';
import {
  COMPONENTS_FOOTNOTE, EXISTING_TRIPS_LABEL, FUTURE_TRIPS_LABEL, HORIZONS, RANGE_NOTE, toProjectionView,
} from '../metrics/net-cash-projection/net-cash-projection.model';
import { NetCollectionsService } from '../metrics/net-collections.service';
import { DataFrameComponent } from '../shared/data-frame.component';
import { DataFreshnessService } from '../shared/data-freshness.service';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { MetricHelpComponent } from '../shared/metric-help.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { SegmentOption, SegmentedControlComponent } from './segmented-control.component';
import { Labels } from './service-line';

type HorizonChoice = '1' | '2' | '3';

const HORIZON_OPTIONS: readonly SegmentOption<HorizonChoice>[] = HORIZONS.map((n) => ({ value: String(n) as HorizonChoice, label: n === 1 ? '1 month' : `${n} months` }));

interface ProjectionRequest {
  readonly providerId: string;
  readonly endPeriod: string;
  readonly line: 'all' | 'air' | 'ground';
  readonly horizon: number;
  readonly rev: number;
}

/**
 * The projection: what the warehouse forecasts for the months after the chosen period, for a horizon of 1, 2 or 3 months (3 by default).
 * It shows the expected total and its range, each month's change from the month before (the first month against the latest actual month),
 * the running total, and the two parts of the forecast with their shares. The shares are separate figures, never a stacked 100% bar,
 * because the warehouse's parts may add up to more than the total. Everything is also in a table, the same figures month by month.
 */
@Component({
  selector: 'app-projection-panel',
  imports: [DataFrameComponent, EmsChipComponent, LoadProblemComponent, MetricHelpComponent, SegmentedControlComponent, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-data-frame heading="Net cash projection" [expandable]="!!view()" [context]="context()" [dataThrough]="dataThrough()" [serviceLine]="frameLine()" [labels]="labels()">
      <app-segmented-control frameActions class="panel__horizon" label="Months to project" [options]="horizons" [(value)]="horizon" />
      <div class="panel" [attr.aria-busy]="resource.isLoading()">
        <span class="cdk-visually-hidden" role="status">{{ announcement() }}</span>
        @if (resource.error() !== undefined) {
          <app-load-problem [error]="resource.error()" subject="the projection" (retry)="resource.reload()" />
        } @else if (waiting()) {
          <app-skeleton height="8rem" />
        } @else if (!view()) {
          <p class="panel__state" role="status">There is no forecast for the months after this period.</p>
        } @else if (view(); as v) {
          <div class="panel__intro">
            <ems-chip state="info" icon="query_stats">Projected</ems-chip>
            <span class="panel__sub">Next {{ v.horizonMonths === 1 ? 'month' : v.horizonMonths + ' months' }}. An estimate, not posted cash.</span>
          </div>

          <dl class="panel__stats">
            <div>
              <dt>Expected net cash</dt>
              <dd class="panel__stat">{{ v.expected }}</dd>
              <dd class="panel__sub">Forecast range {{ v.range }}</dd>
            </div>
            @if (v.latestActual) {
              <div>
                <dt>Latest actual month, {{ v.latestActualMonth }}</dt>
                <dd class="panel__stat">{{ v.latestActual }}</dd>
                <dd class="panel__sub">The first projected month is compared with this</dd>
              </div>
            }
            <div>
              <dt>{{ existingLabel }}</dt>
              <dd class="panel__stat">{{ v.existing }}</dd>
              <dd class="panel__sub">{{ v.existingShare }} of the expected net cash</dd>
            </div>
            <div>
              <dt>{{ futureLabel }}</dt>
              <dd class="panel__stat">{{ v.future }}</dd>
              <dd class="panel__sub">{{ v.futureShare }} of the expected net cash</dd>
            </div>
          </dl>
          <p class="panel__note">{{ componentsFootnote }}</p>

          <div class="panel__table-wrap" role="region" tabindex="0" aria-label="Projection by month">
            <table class="panel__table">
              <caption class="panel__caption">Projected net cash by month, with the change from the month before, the running total and the two parts of the forecast</caption>
              <thead>
                <tr>
                  <th scope="col">Figure</th>
                  @for (month of v.months; track month.key) {
                    <th scope="col" class="panel__num">{{ month.label }}</th>
                  }
                </tr>
              </thead>
              <tbody>
                <tr>
                  <th scope="row">Expected net cash</th>
                  @for (month of v.months; track month.key) {
                    <td class="panel__num">{{ month.expected }}</td>
                  }
                </tr>
                <tr>
                  <th scope="row">Change from the month before</th>
                  @for (month of v.months; track month.key) {
                    <td class="panel__num">{{ month.change }}<span class="panel__cell-sub">vs {{ month.changeBase }}</span></td>
                  }
                </tr>
                <tr>
                  <th scope="row">Forecast range</th>
                  @for (month of v.months; track month.key) {
                    <td class="panel__num">{{ month.range }}</td>
                  }
                </tr>
                <tr>
                  <th scope="row">Cumulative expected net cash</th>
                  @for (month of v.months; track month.key) {
                    <td class="panel__num">{{ month.cumulative }}</td>
                  }
                </tr>
                <tr>
                  <th scope="row">Cumulative forecast range</th>
                  @for (month of v.months; track month.key) {
                    <td class="panel__num">{{ month.cumulativeRange }}</td>
                  }
                </tr>
                <tr>
                  <th scope="row">{{ existingLabel }}</th>
                  @for (month of v.months; track month.key) {
                    <td class="panel__num">{{ month.existing }}</td>
                  }
                </tr>
                <tr>
                  <th scope="row">Share of expected net cash from trips that already existed</th>
                  @for (month of v.months; track month.key) {
                    <td class="panel__num">{{ month.existingShare }}</td>
                  }
                </tr>
                <tr>
                  <th scope="row">{{ futureLabel }}</th>
                  @for (month of v.months; track month.key) {
                    <td class="panel__num">{{ month.future }}</td>
                  }
                </tr>
                <tr>
                  <th scope="row">Share of expected net cash from trips that do not exist yet</th>
                  @for (month of v.months; track month.key) {
                    <td class="panel__num">{{ month.futureShare }}</td>
                  }
                </tr>
              </tbody>
            </table>
          </div>
          <p class="panel__note">{{ rangeNote }}</p>
        }
        <app-metric-help metric="net_cash_projection" [serviceLine]="frameLine()" />
      </div>
    </app-data-frame>
  `,
  styles: `
    :host {
      display: block;
    }
    .panel {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      min-width: 0;
    }
    .panel__intro {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem 0.75rem;
    }
    .panel__stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
      gap: 1rem;
      margin: 0;
    }
    dt,
    .panel__sub,
    .panel__note,
    .panel__state,
    .panel__caption,
    .panel__cell-sub {
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    /* A dollar figure is never split across lines. It is a little smaller than a page title so the longest still fits a 320px phone. */
    .panel__stat {
      white-space: nowrap;
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
      font-weight: var(--ems-font-weight-heavy);
      color: var(--ems-text-default);
    }
    .panel__note,
    .panel__state {
      margin: 0;
    }
    .panel__table-wrap {
      max-width: 100%;
      overflow-x: auto;
    }
    .panel__table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--ems-font-size-small);
      font-variant-numeric: tabular-nums;
    }
    .panel__caption {
      text-align: start;
      padding-block-end: 0.25rem;
    }
    .panel__table th,
    .panel__table td {
      padding: 0.5rem 0.75rem 0.5rem 0;
      vertical-align: top;
      border-bottom: 1px solid var(--ems-border-neutral-subtle);
    }
    .panel__table th[scope='row'] {
      min-width: 7.5rem;
      max-width: 16rem;
      text-align: start;
      font-weight: var(--ems-font-weight-regular, normal);
    }
    .panel__table thead th {
      font-weight: var(--ems-font-weight-bold);
    }
    .panel__num {
      text-align: end;
      min-width: 6.5rem;
    }
    thead .panel__num {
      white-space: nowrap;
    }
    .panel__cell-sub {
      display: block;
    }
    @media (forced-colors: active) {
      .panel__table-wrap:focus-visible {
        outline: 2px solid CanvasText;
      }
    }
  `,
})
export class ProjectionPanelComponent {
  private readonly netCollections = inject(NetCollectionsService);
  private readonly freshness = inject(DataFreshnessService);

  readonly providerId = input<string | null>(null);
  /** yyyyMM: the projection covers the months after it. */
  readonly endPeriod = input('');
  readonly serviceLine = input<'air' | 'ground' | 'both'>('both');
  readonly periodsLoading = input(false);
  readonly dataThrough = input<string | null>(null);
  readonly labels = input<Labels | null>(null);
  /** What the figures cover, shown when the panel is full screen. */
  readonly context = input('');

  protected readonly horizons = HORIZON_OPTIONS;
  protected readonly existingLabel = EXISTING_TRIPS_LABEL;
  protected readonly futureLabel = FUTURE_TRIPS_LABEL;
  protected readonly componentsFootnote = COMPONENTS_FOOTNOTE;
  protected readonly rangeNote = RANGE_NOTE;

  /** How many months to project, 1 to 3. Three by default. */
  protected readonly horizon = signal<HorizonChoice>('3');
  protected readonly frameLine = computed<'all' | 'air' | 'ground'>(() => {
    const line = this.serviceLine();
    return line === 'both' ? 'all' : line;
  });

  protected readonly resource = rxResource<NetCashProjectionDto | null, ProjectionRequest | null>({
    params: () => {
      const providerId = this.providerId();
      const endPeriod = this.endPeriod();
      return providerId && endPeriod && !this.periodsLoading()
        ? { providerId, endPeriod, line: this.frameLine(), horizon: Number(this.horizon()), rev: this.freshness.revision() }
        : null;
    },
    stream: ({ params }) => (params ? this.netCollections.projection(params.providerId, params.endPeriod, params.line, params.horizon) : of(null)),
  });

  protected readonly waiting = computed(() => this.periodsLoading() || this.resource.isLoading());
  /** The projection as text, or null when the forecast does not reach the months after this period. */
  protected readonly view = computed(() => {
    const dto = this.resource.hasValue() ? this.resource.value() : null;
    return dto && dto.months.length > 0 ? toProjectionView(dto) : null;
  });
  protected readonly announcement = computed(() => {
    const v = this.view();
    return v && !this.resource.isLoading() ? `Showing the next ${v.horizonMonths === 1 ? 'month' : v.horizonMonths + ' months'}.` : '';
  });
}
