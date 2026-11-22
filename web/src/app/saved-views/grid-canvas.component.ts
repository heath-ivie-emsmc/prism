import { CdkDrag, CdkDragDrop, CdkDragMove, CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { EmsButtonDirective } from '@ems-management-consultants/ui';
import { cellFromPoint, presetOf, readingOrder } from './grid-layout';
import { CatalogMetricDto, Density, LayoutWidget, SizePreset } from './layout.model';
import { WidgetAction, WidgetHostComponent } from './widget-host.component';
import { WidgetStatus } from './metric-widget.component';

/** A drop on the grid: where, and what. Either a Metric from the palette or a widget already on the grid. */
export interface GridDrop {
  readonly x: number;
  readonly y: number;
  readonly metric: CatalogMetricDto | null;
  readonly widgetId: string | null;
}

/** The grid is 12 columns while the canvas is at least this wide, and one column below it. In rem, so larger text switches to one column sooner. */
export const GRID_MODE_MIN_REM = 56;

function isMetric(data: unknown): data is CatalogMetricDto {
  return typeof data === 'object' && data !== null && 'presentations' in data && 'key' in data;
}

/**
 * The right-hand side of the builder: a 12-column grid of widgets. Positions come from the layout, so the page lists them in reading order and a one-column
 * layout is the same list. Dragging is a pointer extra on top of the Add, Move, Size and Settings controls and only works where the grid has room.
 * The ghost says in words where a drop will land, so it never relies on colour.
 */
@Component({
  selector: 'app-grid-canvas',
  imports: [CdkDrag, CdkDropList, EmsButtonDirective, WidgetHostComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wrap" #wrap>
      @if (widgets().length === 0) {
        <div class="empty" [class.empty--edit]="editing()" id="view-canvas-empty">
          <h3>This view is empty</h3>
          @if (editing()) {
            <p>Add a metric from the palette to get started.@if (gridMode()) { You can also drag one onto the page. }</p>
            <button emsButton="filled" type="button" id="empty-add" (click)="addRequested.emit()">Add a metric</button>
          } @else {
            <p>There are no metrics on this view yet.</p>
          }
        </div>
      }
      <ul
        #canvas
        class="canvas"
        [class.canvas--compact]="density() === 'compact'"
        [class.canvas--empty]="widgets().length === 0"
        [attr.aria-label]="'Metrics on this view'"
        cdkDropList
        id="view-canvas"
        cdkDropListSortingDisabled
        (cdkDropListDropped)="dropped($event)"
      >
        @for (widget of ordered(); track widget.id) {
          @if (metrics().get(widget.metricKey); as metric) {
            <li
              class="item"
              [attr.data-widget-id]="widget.id"
              [class.item--moving]="movingId() === widget.id"
              [style.--x]="widget.x"
              [style.--y]="widget.y"
              [style.--w]="widget.w"
              [style.--h]="widget.h"
              cdkDrag
              [cdkDragData]="widget"
              [cdkDragDisabled]="!editing() || !gridMode()"
              [cdkDragStartDelay]="{ touch: 200, mouse: 0 }"
              cdkDragPreviewContainer="parent"
              cdkDragBoundary=".wrap"
              (cdkDragMoved)="moved($event)"
              (cdkDragEnded)="ghost.set(null)"
            >
              <app-widget-host
                [widget]="widget"
                [metric]="metric"
                [title]="titles().get(widget.id)!"
                [editing]="editing()"
                [moving]="movingId() === widget.id"
                [stacked]="!gridMode()"
                [size]="sizeOf(widget)"
                (action)="action.emit({ id: widget.id, action: $event })"
                (status)="status.emit({ id: widget.id, status: $event })"
              />
            </li>
          } @else {
            <li class="item" [style.--x]="widget.x" [style.--y]="widget.y" [style.--w]="widget.w" [style.--h]="widget.h">
              <div class="gone">
                <strong>Metric no longer available</strong>
                <p>This metric isn't available any more.@if (editing()) { <button emsButton="flat" type="button" (click)="action.emit({ id: widget.id, action: { kind: 'remove' } })">Remove it from this view</button> }</p>
              </div>
            </li>
          }
        }
        @if (ghost(); as g) {
          <li class="ghost" aria-hidden="true" [style.--x]="g.x" [style.--y]="g.y" [style.--w]="g.w" [style.--h]="g.h">
            <span>Drop here: row {{ g.y + 1 }}, column {{ g.x + 1 }}</span>
          </li>
        }
      </ul>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .wrap {
      container: canvas / inline-size;
      position: relative;
    }
    .canvas {
      --gap: 1rem;
      --row: 8rem;
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      grid-auto-rows: minmax(var(--row), auto);
      gap: var(--gap);
    }
    .canvas--compact {
      --gap: 0.5rem;
      --row: 6rem;
    }
    .canvas--empty {
      display: none;
    }
    .item,
    .ghost {
      grid-column: calc(var(--x) + 1) / span var(--w);
      grid-row: calc(var(--y) + 1) / span var(--h);
      min-width: 0;
    }
    .item--moving {
      outline: 3px solid var(--ems-border-focus-default);
      outline-offset: 2px;
    }
    .ghost {
      position: relative;
      pointer-events: none;
      z-index: 1;
      border: 2px dashed var(--ems-border-focus-default);
      border-radius: 8px;
      background: color-mix(in srgb, var(--ems-border-focus-default) 10%, transparent);
    }
    .ghost span {
      position: absolute;
      inset-block-start: 0.5rem;
      inset-inline-start: 0.5rem;
      max-width: calc(100% - 1rem);
      padding: 0.125rem 0.5rem;
      border-radius: 999px;
      background: var(--ems-surface-neutral-elevated, Canvas);
      border: 1px solid var(--ems-border-focus-default);
      color: var(--ems-text-default);
      font-size: var(--ems-font-size-small);
      font-weight: 600;
    }
    .empty {
      padding: 2rem 1rem;
      text-align: center;
      border: 2px dashed var(--ems-border-neutral-strong, currentColor);
      border-radius: 8px;
    }
    .empty h3 {
      margin: 0 0 0.5rem;
    }
    .empty p {
      margin: 0 0 1rem;
      color: var(--ems-text-muted);
    }
    .gone {
      padding: 1rem;
      border: 1px dashed currentColor;
      border-radius: 6px;
    }
    .gone p {
      margin: 0.25rem 0 0;
      color: var(--ems-text-muted);
    }
    .cdk-drag-preview {
      border-radius: 6px;
      box-shadow: 0 4px 12px rgb(0 0 0 / 0.3);
    }
    .cdk-drag-placeholder {
      opacity: 0.3;
    }
    @container canvas (max-width: 56rem) {
      .canvas {
        grid-template-columns: minmax(0, 1fr);
      }
      .item,
      .ghost {
        grid-column: 1;
        grid-row: auto;
      }
    }
    @media (forced-colors: active) {
      .item,
      .gone {
        border: 1px solid CanvasText;
      }
      .ghost {
        border-color: Highlight;
      }
      .ghost span {
        border-color: Highlight;
        background: Canvas;
        color: CanvasText;
      }
      .item--moving {
        outline-color: Highlight;
      }
    }
    @media (prefers-reduced-motion: reduce) {
      * {
        transition: none !important;
        animation: none !important;
      }
    }
  `,
})
export class GridCanvasComponent {
  readonly widgets = input.required<readonly LayoutWidget[]>();
  readonly metrics = input.required<ReadonlyMap<string, CatalogMetricDto>>();
  readonly editing = input(false);
  readonly density = input<Density>('comfortable');
  readonly movingId = input<string | null>(null);

  readonly action = output<{ readonly id: string; readonly action: WidgetAction }>();
  readonly gridDrop = output<GridDrop>();
  readonly addRequested = output<void>();
  readonly status = output<{ readonly id: string; readonly status: WidgetStatus }>();
  readonly gridModeChange = output<boolean>();

  private readonly canvas = viewChild.required<ElementRef<HTMLElement>>('canvas');
  private readonly wrap = viewChild.required<ElementRef<HTMLElement>>('wrap');
  private readonly destroyRef = inject(DestroyRef);

  protected readonly gridMode = signal(true);
  protected readonly ghost = signal<{ x: number; y: number; w: number; h: number } | null>(null);
  protected readonly ordered = computed(() => readingOrder(this.widgets()));

  /** A widget's accessible name: the Metric's name, and "(1 of 2)" when the same Metric is on the view twice. */
  protected readonly titles = computed(() => {
    const names = this.ordered().map((w) => ({ id: w.id, name: this.metrics().get(w.metricKey)?.name ?? 'Metric' }));
    const totals = new Map<string, number>();
    names.forEach((n) => totals.set(n.name, (totals.get(n.name) ?? 0) + 1));
    const seen = new Map<string, number>();
    return new Map(
      names.map((n) => {
        const nth = (seen.get(n.name) ?? 0) + 1;
        seen.set(n.name, nth);
        return [n.id, totals.get(n.name)! > 1 ? `${n.name} (${nth} of ${totals.get(n.name)})` : n.name] as const;
      }),
    );
  });

  constructor() {
    afterNextRender(() => {
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      const observer = new ResizeObserver(() => this.measure());
      observer.observe(this.wrap().nativeElement);
      this.destroyRef.onDestroy(() => observer.disconnect());
      this.measure();
    });
  }

  /** The element id of the palette's connected drop list target. */
  focusEmptyAdd(): void {
    document.getElementById('empty-add')?.focus();
  }

  protected sizeOf(widget: LayoutWidget): SizePreset | null {
    return presetOf(widget);
  }

  private measure(): void {
    const remPx = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    const wide = this.wrap().nativeElement.clientWidth >= GRID_MODE_MIN_REM * remPx;
    if (wide !== this.gridMode()) {
      this.gridMode.set(wide);
      this.gridModeChange.emit(wide);
    }
  }

  private metricsOfPoint(): { rect: DOMRect; rowHeight: number; gap: number; rows: number[] } {
    const remPx = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    const compact = this.density() === 'compact';
    const element = this.canvas().nativeElement;
    // The browser reports the real height of every row ("120px 340px ..."), which grows with the content of the widgets in it.
    const rows = getComputedStyle(element).gridTemplateRows.split(' ').map(parseFloat).filter((n) => Number.isFinite(n));
    return { rect: element.getBoundingClientRect(), rowHeight: remPx * (compact ? 6 : 8), gap: remPx * (compact ? 0.5 : 1), rows };
  }

  /** Shows where a metric dragged in from the palette would land. The palette owns that drag, so the page passes the pointer here. */
  showGhostFor(point: { x: number; y: number }, size: { w: number; h: number }): void {
    const { rect, rowHeight, gap, rows } = this.metricsOfPoint();
    const cell = cellFromPoint(point, rect, { rowHeight, gap, rows });
    this.ghost.set({ x: Math.min(cell.x, 12 - size.w), y: cell.y, ...size });
  }

  hideGhost(): void {
    this.ghost.set(null);
  }

  protected moved(event: CdkDragMove<LayoutWidget | CatalogMetricDto>): void {
    const { rect, rowHeight, gap, rows } = this.metricsOfPoint();
    const cell = cellFromPoint(event.pointerPosition, rect, { rowHeight, gap, rows });
    const data = event.source.data;
    const w = isMetric(data) ? 6 : data.w;
    this.ghost.set({ x: Math.min(cell.x, 12 - w), y: cell.y, w, h: isMetric(data) ? 3 : data.h });
  }

  protected dropped(event: CdkDragDrop<unknown>): void {
    const { rect, rowHeight, gap, rows } = this.metricsOfPoint();
    const cell = cellFromPoint(event.dropPoint, rect, { rowHeight, gap, rows });
    const data = event.item.data as unknown;
    this.ghost.set(null);
    this.gridDrop.emit(isMetric(data) ? { ...cell, metric: data, widgetId: null } : { ...cell, metric: null, widgetId: (data as LayoutWidget).id });
  }
}
