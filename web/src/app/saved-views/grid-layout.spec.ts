import { describe, expect, it } from 'vitest';
import { addWidget, autoArrange, cellFromPoint, rowAtOffset, firstFreeSlot, hasOverlap, moveWidget, newWidgetId, overlaps, presetOf, presetSize, readingOrder, removeWidget, reorder, resizeWidget, settle } from './grid-layout';
import { LayoutWidget, MAX_WIDGETS } from './layout.model';

const widget = (id: string, x: number, y: number, w = 6, h = 3): LayoutWidget => ({ id, metricKey: 'net_collections', presentation: 'line', x, y, w, h });
const BOUNDS = { minWidth: 3, minHeight: 2, maxWidth: 12, maxHeight: 6 };

describe('grid layout', () => {
  it('auto arrange fills gaps beside and below, keeps sizes and reading order, and is stable', () => {
    const messy = [widget('a', 0, 0), widget('b', 6, 7), widget('c', 0, 12, 12, 4), widget('d', 3, 20, 3, 2)];
    const tidy = autoArrange(messy);
    expect(tidy.map((w) => [w.id, w.x, w.y])).toEqual([['a', 0, 0], ['b', 6, 0], ['c', 0, 3], ['d', 0, 7]]);
    expect(tidy.map((w) => [w.w, w.h])).toEqual(messy.map((w) => [w.w, w.h]));
    expect(hasOverlap(tidy)).toBe(false);
    expect(autoArrange(tidy)).toEqual(tidy);
    expect(autoArrange([])).toEqual([]);
  });

  it('finds the row under a point from the measured row heights', () => {
    const rows = [100, 300, 100];
    expect(rowAtOffset(10, rows, 16, 128)).toBe(0);
    expect(rowAtOffset(120, rows, 16, 128)).toBe(1);
    expect(rowAtOffset(400, rows, 16, 128)).toBe(1);
    expect(rowAtOffset(440, rows, 16, 128)).toBe(2);
    expect(rowAtOffset(10_000, [], 16, 128)).toBe(69);
  });

  it('knows touching widgets do not overlap and crossing ones do', () => {
    expect(overlaps(widget('a', 0, 0), widget('b', 6, 0))).toBe(false);
    expect(overlaps(widget('a', 0, 0), widget('b', 0, 3))).toBe(false);
    expect(overlaps(widget('a', 0, 0), widget('b', 5, 2))).toBe(true);
    expect(hasOverlap([widget('a', 0, 0), widget('b', 6, 0), widget('c', 0, 3)])).toBe(false);
  });

  it('lists widgets top to bottom, then left to right', () => {
    expect(readingOrder([widget('c', 6, 3), widget('b', 6, 0), widget('a', 0, 0)]).map((w) => w.id)).toEqual(['a', 'b', 'c']);
  });

  it('finds the first free slot row by row and says when there is none', () => {
    expect(firstFreeSlot([], 6, 3)).toEqual({ x: 0, y: 0 });
    expect(firstFreeSlot([widget('a', 0, 0)], 6, 3)).toEqual({ x: 6, y: 0 });
    expect(firstFreeSlot([widget('a', 0, 0), widget('b', 6, 0)], 6, 3)).toEqual({ x: 0, y: 3 });
    expect(firstFreeSlot([widget('a', 0, 0, 12, 200)], 3, 2)).toBeNull();
  });

  it('adds into the first free slot and refuses past the most widgets', () => {
    const added = addWidget([widget('a', 0, 0)], { id: 'b', metricKey: 'net_collections', presentation: 'line', w: 6, h: 3 });
    expect(added?.find((w) => w.id === 'b')).toMatchObject({ x: 6, y: 0 });
    const full = Array.from({ length: MAX_WIDGETS }, (_, i) => widget(`w${i}`, (i % 4) * 3, Math.floor(i / 4) * 2, 3, 2));
    expect(addWidget(full, { id: 'x', metricKey: 'net_collections', presentation: 'line', w: 3, h: 2 })).toBeNull();
  });

  it('lets widgets fall to the highest free row but keeps gaps at the side', () => {
    const { widgets } = settle([widget('a', 0, 5), widget('b', 6, 9)]);
    expect(widgets.find((w) => w.id === 'a')).toMatchObject({ x: 0, y: 0 });
    expect(widgets.find((w) => w.id === 'b')).toMatchObject({ x: 6, y: 0 });
  });

  it('moves a widget and pushes the one it lands on down, counting it', () => {
    const start = [widget('a', 0, 0), widget('b', 6, 0)];

    const { widgets, pushed } = moveWidget(start, 'a', 6, 0);

    expect(widgets.find((w) => w.id === 'a')).toMatchObject({ x: 6, y: 0 });
    expect(widgets.find((w) => w.id === 'b')?.y).toBe(3);
    expect(pushed).toBe(1);
    expect(hasOverlap(widgets)).toBe(false);
  });

  it('keeps a moved widget inside the grid, whatever it is asked for', () => {
    const { widgets } = moveWidget([widget('a', 0, 0)], 'a', 99, -5);
    expect(widgets[0]).toMatchObject({ x: 6, y: 0 });
    const far = moveWidget([widget('a', 0, 0)], 'a', 0, 99999);
    expect(far.widgets[0]!.y + far.widgets[0]!.h).toBeLessThanOrEqual(200);
  });

  it('ignores a move for a widget that is not there', () => {
    const start = [widget('a', 0, 0)];
    expect(moveWidget(start, 'zzz', 3, 3)).toEqual({ widgets: start, pushed: 0 });
  });

  it('resizes within the bounds and the grid and pushes what is in the way', () => {
    const start = [widget('a', 0, 0), widget('b', 0, 3)];

    const { widgets } = resizeWidget(start, 'a', 99, 99, BOUNDS);

    expect(widgets.find((w) => w.id === 'a')).toMatchObject({ w: 12, h: 6 });
    expect(widgets.find((w) => w.id === 'b')?.y).toBe(6);
    const small = resizeWidget(start, 'a', 0, 0, BOUNDS);
    expect(small.widgets.find((w) => w.id === 'a')).toMatchObject({ w: 3, h: 2 });
  });

  it('pulls a widget left when growing it would run off the right edge', () => {
    const { widgets } = resizeWidget([widget('a', 9, 0, 3, 2)], 'a', 6, 2, BOUNDS);
    expect(widgets[0]).toMatchObject({ x: 6, w: 6 });
  });

  it('removes a widget and closes the gap', () => {
    const left = removeWidget([widget('a', 0, 0), widget('b', 0, 3)], 'a');
    expect(left).toHaveLength(1);
    expect(left[0]).toMatchObject({ id: 'b', y: 0 });
  });

  it('swaps places with the neighbour in reading order for the one-column layout', () => {
    const order = reorder([widget('a', 0, 0), widget('b', 0, 3)], 'a', 1);
    expect(readingOrder(order).map((w) => w.id)).toEqual(['b', 'a']);
    expect(reorder([widget('a', 0, 0)], 'a', -1)).toHaveLength(1);
  });

  it('maps a preset to a size held inside the bounds, and a size back to its preset', () => {
    expect(presetSize('L', { minWidth: 3, minHeight: 3, maxWidth: 6, maxHeight: 3 })).toEqual({ w: 6, h: 3 });
    expect(presetOf({ w: 6, h: 3 })).toBe('M');
    expect(presetOf({ w: 5, h: 3 })).toBeNull();
  });

  it('turns a point into a cell, clamped to the canvas', () => {
    const rect = { left: 100, top: 50, width: 1200 };
    const metrics = { rowHeight: 128, gap: 16 };
    expect(cellFromPoint({ x: 110, y: 60 }, rect, metrics)).toEqual({ x: 0, y: 0 });
    expect(cellFromPoint({ x: 100 + 700, y: 50 + 144 }, rect, metrics)).toEqual({ x: 6, y: 1 });
    expect(cellFromPoint({ x: 99999, y: -50 }, rect, metrics)).toEqual({ x: 11, y: 0 });
  });

  it('makes ids that are letters, digits and hyphens and never repeat', () => {
    const id = newWidgetId([widget('w2', 0, 0)]);
    expect(id).toMatch(/^[a-z0-9-]+$/);
    expect(id).not.toBe('w2');
  });

  it('never leaves overlapping widgets after any run of moves and resizes', () => {
    let widgets: LayoutWidget[] = [widget('a', 0, 0), widget('b', 6, 0), widget('c', 0, 3), widget('d', 6, 3)];
    let seed = 7;
    const next = (n: number): number => {
      seed = (seed * 1103515245 + 12345) % 2147483648;
      return seed % n;
    };
    for (let i = 0; i < 200; i += 1) {
      const id = ['a', 'b', 'c', 'd'][next(4)]!;
      widgets = (next(2) === 0 ? moveWidget(widgets, id, next(12), next(12)) : resizeWidget(widgets, id, 3 + next(10), 2 + next(5), BOUNDS)).widgets as LayoutWidget[];
      expect(hasOverlap(widgets)).toBe(false);
      for (const w of widgets) {
        expect(w.x).toBeGreaterThanOrEqual(0);
        expect(w.x + w.w).toBeLessThanOrEqual(12);
        expect(w.y).toBeGreaterThanOrEqual(0);
      }
    }
  });
});
