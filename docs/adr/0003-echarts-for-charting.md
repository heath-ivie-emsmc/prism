# 3. ECharts for charts

Date: 2026-11-21

## Status

Accepted

## Context

The dashboard needs trend, bar, stacked and combo charts now, and likely heatmaps, treemaps and funnels later. Charts must follow the design tokens
(light, dark, high contrast), stay fast on dense series, and meet WCAG 2.1 AA, which means every chart needs a text alternative. We compared
ECharts and D3. Highcharts, which EMSight uses, needs a commercial licence and was not considered.

## Decision

Use **Apache ECharts** (v6, Apache-2.0), registered per chart type and loaded lazily.

- **Why not D3.** D3 is a toolkit. Axes, legends, tooltips, zoom, resizing, keyboard behaviour and the accessible description would all be ours to build and
  keep consistent across every chart. It draws SVG by default, which slows down past a few thousand elements. ECharts gives those parts built in, renders to
  canvas, and samples dense series. D3 stays available for a one-off visual ECharts cannot draw (`d3-scale` and `d3-array` can be used alongside).
- **Tree-shaken registration.** `charts/echarts-setup.ts` registers only the chart types and components in use. The chart component imports it with a
  dynamic `import()`, so ECharts is a lazy chunk (about 157 kB transferred with line, grid, tooltip, aria and both renderers) and the initial bundle does not grow.
- **Options are built by pure functions** (`buildNetCashOption`) from typed data and a set of colors read from the design tokens at render time.
  They are unit tested without a browser. Components never hand-write ECharts option objects.
- **Every chart has a text alternative.** The chart element has an `aria-label` summarizing the data, and the same numbers are available as a real table
  (for the net cash panel, the table view of the chart-type control). A chart without both does not ship.
- **Motion** is off when the user asks for reduced motion.
- **Bounds.** A chart draws at most 1,000 points. Callers aggregate first. Animation and point markers turn off for dense series.
- **Renderer.** Canvas in the browser. Tests use SVG because jsdom has no canvas.

## Consequences

- One charting vocabulary for the team and for the Metric Catalog's chart hints later.
- Adding a chart type means registering it in `echarts-setup.ts`, which grows the lazy chunk. Check the size when you do.
- ECharts' own `aria` description is turned off: it dumps every number and would replace our summary. The summary and table are ours.
- Revisit if a required visual cannot be expressed as an ECharts series, or if the lazy chunk passes the budget.
