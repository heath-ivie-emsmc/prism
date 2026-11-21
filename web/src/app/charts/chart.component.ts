import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { EmsThemeService } from '@ems-management-consultants/ui';
import type { ECharts, EChartsCoreOption } from 'echarts/core';

/** Colors read from the design tokens at draw time, so light, dark and high contrast follow the theme. */
export interface ChartColors {
  readonly text: string;
  readonly mutedText: string;
  readonly grid: string;
  /** Bars and points for months that have happened. */
  readonly actual: string;
  /** The latest actual month, drawn to stand out. */
  readonly highlight: string;
  /** Outline and line for projected months. */
  readonly projected: string;
  /** The chart is too narrow for a figure on every bar or point, so only the key one is shown. */
  readonly narrow: boolean;
}

/** Below this width in CSS pixels, labels on every bar would run into each other. */
export const NARROW_CHART_PX = 520;

/** Builds the ECharts option for the current data. Pure, so it is unit tested without a browser. */
export type ChartOptionBuilder = (colors: ChartColors) => EChartsCoreOption;

const FALLBACK_SIZE = { width: 600, height: 300 };

function token(style: CSSStyleDeclaration, name: string, fallback: string): string {
  return style.getPropertyValue(name).trim() || fallback;
}

/**
 * Hosts one ECharts chart. ECharts loads on first render, not with the app. Canvas by default; tests use SVG.
 * The chart itself is not readable by a screen reader, so `label` is its text description (WCAG 1.1.1): the owner of the chart also
 * has to offer the same numbers as a table.
 */
@Component({
  selector: 'app-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div #host class="chart" role="img" [attr.aria-label]="label()"></div>`,
  styles: `
    :host {
      display: block;
    }
    .chart {
      width: 100%;
      height: var(--chart-height, 300px);
    }
  `,
})
export class ChartComponent {
  readonly option = input.required<ChartOptionBuilder>();
  readonly label = input.required<string>();
  readonly renderer = input<'canvas' | 'svg'>('canvas');

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly theme = inject(EmsThemeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ready = signal(false);
  private chart: ECharts | undefined;
  private destroyed = false;
  private lastNarrow: boolean | undefined;

  constructor() {
    afterNextRender(() => void this.start());
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.chart?.dispose();
    });
    // Redraw when the data or the theme changes. The chart handle is not a signal, so nothing is written back here.
    effect(() => {
      const build = this.option();
      this.theme.colorMode();
      if (this.ready()) {
        // Wait a tick so the theme's CSS variables have switched before they are read.
        untracked(() => queueMicrotask(() => this.draw(build)));
      }
    });
  }

  private async start(): Promise<void> {
    const { echarts } = await import('./echarts-setup');
    if (this.destroyed) {
      return;
    }
    const element = this.host().nativeElement;
    const sized = element.clientWidth > 0 && element.clientHeight > 0;
    this.chart = echarts.init(element, null, { renderer: this.renderer(), ...(sized ? {} : FALLBACK_SIZE) });
    if (typeof ResizeObserver !== 'undefined') {
      const observer = new ResizeObserver(() => {
        this.chart?.resize();
        // Crossing the narrow threshold changes which labels are drawn, so draw again.
        const narrow = element.clientWidth < NARROW_CHART_PX;
        if (narrow !== this.lastNarrow) {
          this.lastNarrow = narrow;
          this.draw(this.option());
        }
      });
      observer.observe(element);
      this.destroyRef.onDestroy(() => observer.disconnect());
    }
    this.ready.set(true);
  }

  private draw(build: ChartOptionBuilder): void {
    if (this.destroyed || !this.chart) {
      return;
    }
    // Motion is off for people who ask for it (WCAG 2.3.3), and so screenshots and tests see the finished chart.
    const reduceMotion = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    const option = build(this.colors());
    this.chart.setOption(reduceMotion ? { ...option, animation: false } : option, true);
  }

  private colors(): ChartColors {
    const style = getComputedStyle(this.host().nativeElement);
    return {
      text: token(style, '--ems-text-default', '#1a1a1a'),
      mutedText: token(style, '--ems-text-muted', '#5c5c5c'),
      grid: token(style, '--ems-border-neutral-default', '#d9d9d9'),
      actual: token(style, '--ems-surface-neutral-strong', '#606060'),
      highlight: token(style, '--ems-surface-error-stronger', '#a01f1f'),
      projected: token(style, '--ems-surface-accent-emerald', '#00704a'),
      narrow: this.host().nativeElement.clientWidth < NARROW_CHART_PX,
    };
  }
}
