import { CdkDrag, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { EmsButtonDirective, EmsChipComponent, EmsIconComponent } from '@ems-management-consultants/ui';
import { AppliesTo, LineSelection, appliesToText, coverageReason, metricAppliesTo } from '../metrics/shared/applies-to';
import { CatalogDto, CatalogMetricDto } from './layout.model';
import { LOADABLE_METRICS } from './widget-data.service';

const MAX_QUERY = 40;
/** With this many Metrics or fewer every category starts open, so no Add button is hidden behind a collapsed heading. More than that and only the first opens. The catalog has grown past 12. */
const DEFAULT_OPEN_LIMIT = 24;

interface Group {
  readonly key: string;
  readonly name: string;
  readonly metrics: readonly CatalogMetricDto[];
}

/**
 * The categorized Metric list. Each entry has an Add button (the way in for keyboard, touch and voice) and, where dragging is on, a grip to drag it onto the grid.
 * A planned Metric is listed but cannot be added: its button says why and stays focusable. Insights that are not placeable are left out.
 */
@Component({
  selector: 'app-metric-palette',
  imports: [CdkDrag, CdkDragHandle, CdkDropList, EmsButtonDirective, EmsChipComponent, EmsIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div role="search" class="palette__search">
      <label for="palette-search">Search metrics</label>
      <div class="palette__searchrow">
        <input id="palette-search" type="search" autocomplete="off" placeholder="Search by name" [maxLength]="maxQuery" [value]="query()" (input)="setQuery($any($event.target).value)" (keydown.escape)="setQuery('')" />
        @if (query()) {
          <button emsButton="flat" type="button" aria-label="Clear search" (click)="setQuery('')"><ems-icon name="close" size="sm" /></button>
        }
      </div>
    </div>

    @if (groups().length === 0) {
      <p class="palette__empty">No metrics match "{{ query() }}".</p>
    }

    <div cdkDropList id="metric-palette" cdkDropListSortingDisabled [cdkDropListConnectedTo]="['view-canvas']" [cdkDropListEnterPredicate]="never">
      @for (group of groups(); track group.key) {
        <section class="palette__group">
          <h3>
            <button type="button" class="palette__toggle" [attr.aria-expanded]="isOpen(group.key)" [attr.aria-controls]="'palette-' + group.key" (click)="toggle(group.key)">
              <ems-icon [name]="isOpen(group.key) ? 'expand_less' : 'expand_more'" size="sm" />
              {{ group.name }} <span class="palette__count">({{ group.metrics.length }})</span>
            </button>
          </h3>
          @if (isOpen(group.key)) {
            <ul [id]="'palette-' + group.key" class="palette__list">
              @for (metric of group.metrics; track metric.key) {
                <li class="palette__item" [class.palette__item--planned]="!canPlace(metric) || !!lineReason(metric)" cdkDrag [cdkDragData]="metric" [cdkDragDisabled]="!dragEnabled() || !canPlace(metric) || !canAdd() || !!lineReason(metric)" cdkDragPreviewContainer="parent" (cdkDragMoved)="dragMoved.emit({ metric, point: $event.pointerPosition })" (cdkDragEnded)="dragEnded.emit()">
                  @if (dragEnabled() && canPlace(metric) && !lineReason(metric)) {
                    <span class="palette__grip" cdkDragHandle aria-hidden="true"><ems-icon name="drag_indicator" size="sm" /></span>
                  }
                  <div class="palette__text">
                    <span class="palette__name">{{ metric.name }}</span>
                    @if (placedText(metric); as placed) {
                      <ems-chip class="palette__placed" state="success" size="sm" icon="check">{{ placed }}</ems-chip>
                    }
                    <span class="palette__desc">{{ metric.description }}</span>
                    @if (!canPlace(metric)) {
                      <ems-chip state="secondary" size="sm" icon="schedule">Coming soon</ems-chip>
                    } @else {
                      <span class="palette__scope" [attr.data-applies-to]="appliesTo(metric)">{{ scopeText(metric) }}</span>
                      @if (lineReason(metric); as reason) {
                        <span class="palette__reason" [id]="'line-reason-' + metric.key">{{ reason }}</span>
                      }
                    }
                  </div>
                  <button
                    emsButton="raised"
                    type="button"
                    class="palette__add"
                    [attr.aria-label]="'Add ' + metric.name + ' to view' + (placedText(metric) ? ' again. ' + placedText(metric) : '')"
                    [attr.aria-disabled]="!canPlace(metric) || !canAdd() || lineReason(metric) ? 'true' : null"
                    [attr.aria-describedby]="!canPlace(metric) ? 'planned-reason' : lineReason(metric) ? 'line-reason-' + metric.key : null"
                    (click)="press(metric)"
                  >
                    Add
                  </button>
                </li>
              }
            </ul>
          }
        </section>
      }
    </div>
    <p id="planned-reason" class="cdk-visually-hidden">Coming soon. Can't be added yet.</p>
  `,
  styles: `
    :host {
      display: block;
    }
    .palette__search label {
      display: block;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
      margin-block-end: 0.25rem;
    }
    .palette__searchrow {
      display: flex;
      gap: 0.25rem;
      align-items: center;
    }
    input[type='search'] {
      flex: 1;
      min-width: 0;
      min-height: 2.5rem;
      padding: 0 0.75rem;
      font: inherit;
      color: var(--ems-text-default);
      background: var(--ems-surface-neutral-default);
      border: 1px solid var(--ems-border-neutral-strong, currentColor);
      border-radius: 4px;
    }
    h3 {
      margin: 1rem 0 0.25rem;
      font-size: var(--ems-font-size-body);
    }
    .palette__toggle {
      display: flex;
      align-items: center;
      gap: 0.25rem;
      min-height: 2rem;
      width: 100%;
      padding: 0;
      font: inherit;
      font-weight: 700;
      color: inherit;
      background: none;
      border: 0;
      cursor: pointer;
      text-align: start;
    }
    .palette__count {
      font-weight: 400;
      color: var(--ems-text-muted);
    }
    .palette__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    .palette__item {
      display: flex;
      align-items: flex-start;
      gap: 0.5rem;
      padding: 0.5rem;
      background: var(--ems-surface-neutral-elevated);
      border: 1px solid var(--ems-border-neutral-default, transparent);
      border-radius: 6px;
    }
    .palette__item--planned {
      border-style: dashed;
    }
    .palette__grip {
      display: inline-flex;
      align-items: center;
      min-width: 1.5rem;
      min-height: 1.5rem;
      cursor: grab;
      color: var(--ems-text-muted);
    }
    .palette__text {
      flex: 1;
      min-width: 0;
      display: grid;
      gap: 0.125rem;
    }
    .palette__name {
      font-weight: 600;
      overflow-wrap: anywhere;
    }
    .palette__scope,
    .palette__reason {
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      color: var(--ems-text-muted);
    }
    .palette__reason {
      font-weight: 600;
      color: var(--ems-text-default);
    }
    .palette__desc {
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      color: var(--ems-text-muted);
      overflow-wrap: anywhere;
    }
    .palette__add {
      min-width: 3rem;
      min-height: 2rem;
    }
    .palette__add:focus-visible {
      outline: 2px solid var(--ems-border-focus-default);
      outline-offset: 2px;
    }
    .palette__add[aria-disabled='true'] {
      opacity: 1;
      cursor: not-allowed;
      border-style: dashed;
    }
    .palette__empty {
      color: var(--ems-text-muted);
    }
    .cdk-drag-preview {
      box-shadow: 0 4px 12px rgb(0 0 0 / 0.3);
      border-radius: 6px;
    }
    @media (pointer: coarse) {
      .palette__add,
      .palette__toggle {
        min-height: 2.75rem;
      }
    }
  `,
})
export class MetricPaletteComponent {
  readonly catalog = input.required<CatalogDto>();
  /** False when the view already holds the most widgets. Add buttons say so instead of doing nothing. */
  readonly canAdd = input(true);
  /** Pointer dragging works only where the grid has room (grid mode). Add works everywhere. */
  readonly dragEnabled = input(false);
  /** The page's Service Line. An air-only Metric is listed but cannot be added under Ground only, and the reverse. */
  readonly serviceLine = input<LineSelection>('both');

  /** How many Widgets of each Metric key the view already holds. A Metric already there still has an Add button, with a check mark beside its name. */
  readonly placed = input<ReadonlyMap<string, number>>(new Map());

  readonly add = output<CatalogMetricDto>();
  readonly blocked = output<{ metric: CatalogMetricDto; reason: 'planned' | 'full' | 'line' }>();
  readonly searched = output<{ count: number; query: string }>();
  /** Where a metric being dragged is, so the grid can show where it would land. */
  readonly dragMoved = output<{ metric: CatalogMetricDto; point: { x: number; y: number } }>();
  readonly dragEnded = output<void>();

  protected readonly maxQuery = MAX_QUERY;
  protected readonly query = signal('');
  private readonly opened = signal<ReadonlySet<string> | null>(null);

  /** A drop on the palette itself never lands. */
  protected readonly never = (): boolean => false;

  private readonly placeable = computed(() => this.catalog().metrics.filter((m) => m.placeable || m.status === 'planned'));

  protected readonly groups = computed<Group[]>(() => {
    const q = this.query().trim().toLowerCase();
    const matches = this.placeable().filter((m) => !q || m.name.toLowerCase().includes(q) || m.description.toLowerCase().includes(q));
    return this.catalog()
      .categories.map((category) => ({ key: category.key, name: category.name, metrics: matches.filter((m) => m.category === category.key) }))
      .filter((group) => group.metrics.length > 0);
  });

  protected canPlace(metric: CatalogMetricDto): boolean {
    return metric.status === 'available' && metric.placeable && LOADABLE_METRICS.includes(metric.key);
  }

  /** "On this view" or "On this view (2)", or null when the Metric is not on the view. Visible text, so it does not rely on the icon. */
  protected placedText(metric: CatalogMetricDto): string | null {
    const count = this.placed().get(metric.key) ?? 0;
    if (count === 0) {
      return null;
    }
    return count === 1 ? 'On this view' : `On this view (${count})`;
  }

  protected appliesTo(metric: CatalogMetricDto): AppliesTo {
    return metricAppliesTo(metric);
  }

  /** "Air only", "Ground only" or "Air and ground": every Metric names its scope in the list. */
  protected scopeText(metric: CatalogMetricDto): string {
    return appliesToText(metricAppliesTo(metric));
  }

  /** Why the Metric cannot be added under the page's Service Line, or null. Visible text, so it is not only a tooltip. */
  protected lineReason(metric: CatalogMetricDto): string | null {
    return coverageReason(metricAppliesTo(metric), this.serviceLine());
  }

  protected isOpen(key: string): boolean {
    if (this.query().trim()) {
      return true;
    }
    const opened = this.opened();
    if (opened) {
      return opened.has(key);
    }
    return this.placeable().length <= DEFAULT_OPEN_LIMIT || this.groups()[0]?.key === key;
  }

  protected toggle(key: string): void {
    const current = new Set(this.opened() ?? this.groups().filter((g) => this.isOpen(g.key)).map((g) => g.key));
    if (current.has(key)) {
      current.delete(key);
    } else {
      current.add(key);
    }
    this.opened.set(current);
  }

  protected setQuery(value: string): void {
    const query = value.slice(0, MAX_QUERY);
    this.query.set(query);
    if (query.trim()) {
      const q = query.trim().toLowerCase();
      this.searched.emit({ count: this.placeable().filter((m) => m.name.toLowerCase().includes(q) || m.description.toLowerCase().includes(q)).length, query });
    }
  }

  protected press(metric: CatalogMetricDto): void {
    if (!this.canPlace(metric)) {
      this.blocked.emit({ metric, reason: 'planned' });
    } else if (this.lineReason(metric)) {
      this.blocked.emit({ metric, reason: 'line' });
    } else if (!this.canAdd()) {
      this.blocked.emit({ metric, reason: 'full' });
    } else {
      this.add.emit(metric);
    }
  }
}
