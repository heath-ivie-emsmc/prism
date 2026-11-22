import { GRID_COLUMNS, LayoutWidget, MAX_ROWS, MAX_WIDGETS, SIZE_PRESETS, SizePreset } from './layout.model';

/** Pure grid arithmetic for the builder. No Angular, no DOM, so every rule is unit tested. Cells are columns (x, w) and rows (y, h), counted from 0. */

export interface SizeBounds {
  readonly minWidth: number;
  readonly minHeight: number;
  readonly maxWidth: number;
  readonly maxHeight: number;
}

export interface Placement {
  readonly widgets: readonly LayoutWidget[];
  /** How many other widgets were pushed further down to make room. */
  readonly pushed: number;
}

export function overlaps(a: Pick<LayoutWidget, 'x' | 'y' | 'w' | 'h'>, b: Pick<LayoutWidget, 'x' | 'y' | 'w' | 'h'>): boolean {
  return a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
}

export function hasOverlap(widgets: readonly LayoutWidget[]): boolean {
  return widgets.some((a, i) => widgets.slice(i + 1).some((b) => overlaps(a, b)));
}

/** Reading order: top to bottom, then left to right. The page lists widgets in this order and a phone shows them in it. */
export function readingOrder(widgets: readonly LayoutWidget[]): LayoutWidget[] {
  return [...widgets].sort((a, b) => a.y - b.y || a.x - b.x || a.id.localeCompare(b.id));
}

export function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), Math.max(min, max));
}

/** The preset whose size matches, or null when the widget was sized by hand. */
export function presetOf(widget: Pick<LayoutWidget, 'w' | 'h'>): SizePreset | null {
  return (Object.keys(SIZE_PRESETS) as SizePreset[]).find((key) => SIZE_PRESETS[key].w === widget.w && SIZE_PRESETS[key].h === widget.h) ?? null;
}

/** A preset's size, held inside what the presentation allows. */
export function presetSize(preset: SizePreset, bounds: SizeBounds): { w: number; h: number } {
  const { w, h } = SIZE_PRESETS[preset];
  return { w: clamp(w, bounds.minWidth, bounds.maxWidth), h: clamp(h, bounds.minHeight, bounds.maxHeight) };
}

/**
 * Settles a layout: widgets fall to the highest free row (no vertical gaps), in reading order. The pinned widget, if any, stays exactly where it is and the
 * others make way for it. Gaps to the left or right are kept.
 */
export function settle(widgets: readonly LayoutWidget[], pinnedId?: string): Placement {
  const pinned = widgets.find((w) => w.id === pinnedId);
  const others = readingOrder(widgets.filter((w) => w.id !== pinnedId));
  const placed: LayoutWidget[] = pinned ? [pinned] : [];
  let pushed = 0;
  for (const widget of others) {
    let y = 0;
    while (placed.some((p) => overlaps({ ...widget, y }, p))) {
      y += 1;
    }
    if (y > widget.y) {
      pushed += 1;
    }
    placed.push({ ...widget, y });
  }
  const byId = new Map(placed.map((w) => [w.id, w]));
  return { widgets: widgets.map((w) => byId.get(w.id) ?? w), pushed };
}

/**
 * Tidies a layout: every widget, in reading order, goes to the first free slot, so gaps to the side and below are filled. Sizes never change.
 * Because widgets are placed in reading order, the page reads the same afterwards, and running it twice changes nothing more.
 */
export function autoArrange(widgets: readonly LayoutWidget[]): LayoutWidget[] {
  const placed: LayoutWidget[] = [];
  for (const widget of readingOrder(widgets)) {
    const slot = firstFreeSlot(placed, widget.w, widget.h) ?? { x: widget.x, y: widget.y };
    placed.push({ ...widget, ...slot });
  }
  const byId = new Map(placed.map((w) => [w.id, w]));
  return widgets.map((w) => byId.get(w.id) ?? w);
}

/** The first row-major slot of the given size that nothing occupies. */
export function firstFreeSlot(widgets: readonly LayoutWidget[], w: number, h: number): { x: number; y: number } | null {
  for (let y = 0; y + h <= MAX_ROWS; y += 1) {
    for (let x = 0; x + w <= GRID_COLUMNS; x += 1) {
      if (!widgets.some((other) => overlaps({ x, y, w, h }, other))) {
        return { x, y };
      }
    }
  }
  return null;
}

/** Adds a widget in the first free slot. Null when the view already holds the most widgets, or there is no room. */
export function addWidget(widgets: readonly LayoutWidget[], widget: Omit<LayoutWidget, 'x' | 'y'>): LayoutWidget[] | null {
  if (widgets.length >= MAX_WIDGETS) {
    return null;
  }
  const slot = firstFreeSlot(widgets, widget.w, widget.h);
  return slot ? [...widgets, { ...widget, ...slot }] : null;
}

export function removeWidget(widgets: readonly LayoutWidget[], id: string): LayoutWidget[] {
  return settle(widgets.filter((w) => w.id !== id)).widgets as LayoutWidget[];
}

/** Moves a widget to a column and row, kept inside the grid. Others are pushed down and the layout is settled. */
export function moveWidget(widgets: readonly LayoutWidget[], id: string, x: number, y: number): Placement {
  const target = widgets.find((w) => w.id === id);
  if (!target) {
    return { widgets, pushed: 0 };
  }
  const moved = { ...target, x: clamp(Math.trunc(x), 0, GRID_COLUMNS - target.w), y: clamp(Math.trunc(y), 0, MAX_ROWS - target.h) };
  return settle(widgets.map((w) => (w.id === id ? moved : w)), id);
}

/** Resizes a widget inside the presentation's bounds and the grid, then settles. */
export function resizeWidget(widgets: readonly LayoutWidget[], id: string, w: number, h: number, bounds: SizeBounds): Placement {
  const target = widgets.find((item) => item.id === id);
  if (!target) {
    return { widgets, pushed: 0 };
  }
  const width = clamp(Math.trunc(w), bounds.minWidth, Math.min(bounds.maxWidth, GRID_COLUMNS));
  const height = clamp(Math.trunc(h), bounds.minHeight, Math.min(bounds.maxHeight, MAX_ROWS));
  const resized = { ...target, w: width, h: height, x: clamp(target.x, 0, GRID_COLUMNS - width) };
  return settle(widgets.map((item) => (item.id === id ? resized : item)), id);
}

/** Swaps a widget with its neighbour in reading order, for the one-column layout where left and right mean nothing. */
export function reorder(widgets: readonly LayoutWidget[], id: string, direction: -1 | 1): LayoutWidget[] {
  const order = readingOrder(widgets);
  const index = order.findIndex((w) => w.id === id);
  const swapWith = index + direction;
  if (index < 0 || swapWith < 0 || swapWith >= order.length) {
    return [...widgets];
  }
  const a = order[index]!;
  const b = order[swapWith]!;
  const swapped = widgets.map((w) => (w.id === a.id ? { ...w, x: b.x, y: b.y } : w.id === b.id ? { ...w, x: a.x, y: a.y } : w));
  return settle(swapped, a.id).widgets as LayoutWidget[];
}

/** The row under a vertical offset from the top of the grid, given the real height of each row track. Rows grow with their content, so they are measured, not assumed. */
export function rowAtOffset(offset: number, rows: readonly number[], gap: number, fallbackRow: number): number {
  let top = 0;
  for (let i = 0; i < rows.length; i += 1) {
    const bottom = top + rows[i]! + gap;
    if (offset < bottom) {
      return i;
    }
    top = bottom;
  }
  return rows.length + Math.max(0, Math.floor((offset - top) / (fallbackRow + gap)));
}

/** The cell under a point, for a drop. `rect` is the canvas box and `rowHeight` and `gap` are in the same units as the point. */
export function cellFromPoint(
  point: { x: number; y: number },
  rect: { left: number; top: number; width: number },
  metrics: { rowHeight: number; gap: number; rows?: readonly number[] },
): { x: number; y: number } {
  const column = (rect.width + metrics.gap) / GRID_COLUMNS;
  const row = rowAtOffset(point.y - rect.top, metrics.rows ?? [], metrics.gap, metrics.rowHeight);
  return {
    x: clamp(Math.floor((point.x - rect.left) / column), 0, GRID_COLUMNS - 1),
    y: clamp(row, 0, MAX_ROWS - 1),
  };
}

/** A new widget id: letters, digits and hyphens only, as the server requires. */
export function newWidgetId(existing: readonly LayoutWidget[]): string {
  const used = new Set(existing.map((w) => w.id));
  let n = existing.length + 1;
  while (used.has(`w${n}`)) {
    n += 1;
  }
  return `w${n}`;
}
