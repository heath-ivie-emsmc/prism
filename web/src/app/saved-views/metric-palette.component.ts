import { CdkDrag, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { EmsButtonDirective, EmsChipComponent, EmsIconComponent } from '@ems-management-consultants/ui';
import { CatalogDto, CatalogMetricDto } from './layout.model';
import { LOADABLE_METRICS } from './widget-data.service';

const MAX_QUERY = 40;
const DEFAULT_OPEN_LIMIT = 12;

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
                <li class="palette__item" [class.palette__item--planned]="!canPlace(metric)" cdkDrag [cdkDragData]="metric" [cdkDragDisabled]="!dragEnabled() || !canPlace(metric) || !canAdd()" cdkDragPreviewContainer="parent" (cdkDragMoved)="dragMoved.emit({ metric, point: $event.pointerPosition })" (cdkDragEnded)="dragEnded.emit()">
                  @if (dragEnabled() && canPlace(metric)) {
                    <span class="palette__grip" cdkDragHandle aria-hidden="true"><ems-icon name="drag_indicator" size="sm" /></span>
                  }
                  <div class="palette__text">
                    <span class="palette__name">{{ metric.name }}</span>
                    <span class="palette__desc">{{ metric.description }}</span>
                    @if (!canPlace(metric)) {
                      <ems-chip state="secondary" size="sm" icon="schedule">Coming soon</ems-chip>
                    }
                  </div>
                  <button
                    emsButton="raised"
                    type="button"
                    class="palette__add"
                    [attr.aria-label]="'Add ' + metric.name + ' to view'"
                    [attr.aria-disabled]="!canPlace(metric) || !canAdd() ? 'true' : null"
                    [attr.aria-describedby]="!canPlace(metric) ? 'planned-reason' : null"
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

  readonly add = output<CatalogMetricDto>();
  readonly blocked = output<{ metric: CatalogMetricDto; reason: 'planned' | 'full' }>();
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
    } else if (!this.canAdd()) {
      this.blocked.emit({ metric, reason: 'full' });
    } else {
      this.add.emit(metric);
    }
  }
}
