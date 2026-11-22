import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * A grey block that stands where a figure or chart will be while it loads, so the page keeps its shape and does not look stopped.
 * It is decoration: the screen reader is told by the `role="status"` text of the container that is loading, never by the blocks.
 */
@Component({
  selector: 'app-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true', '[style.width]': 'width()', '[style.height]': 'height()', '[style.border-radius]': 'radius()' },
  template: '',
  styles: `
    :host {
      display: block;
      max-width: 100%;
      background-color: color-mix(in srgb, var(--ems-surface-neutral-strong) 22%, transparent);
      background-image: linear-gradient(90deg, transparent 0, color-mix(in srgb, var(--ems-surface-neutral-strong) 30%, transparent) 50%, transparent 100%);
      background-size: 200% 100%;
      background-repeat: no-repeat;
      animation: skeleton-shimmer 1.4s ease-in-out infinite;
    }
    @keyframes skeleton-shimmer {
      from {
        background-position: 200% 0;
      }
      to {
        background-position: -200% 0;
      }
    }
    @media (prefers-reduced-motion: reduce) {
      :host {
        animation: none;
        background-image: none;
      }
    }
    @media (forced-colors: active) {
      :host {
        animation: none;
        background-image: none;
        border: 1px solid GrayText;
      }
    }
  `,
})
export class SkeletonComponent {
  readonly width = input('100%');
  readonly height = input('1rem');
  readonly radius = input('0.375rem');
}
