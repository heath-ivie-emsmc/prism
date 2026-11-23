import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { shortDate } from './data-through';

/** Visible "Data through Nov 22 · Updates once a day". Renders nothing without a date. */
@Component({
  selector: 'app-freshness-stamp',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (text()) {<p class="stamp">{{ text() }}</p>}`,
  styles: `
    :host {
      display: block;
    }
    .stamp {
      margin: 0;
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
  `,
})
export class FreshnessStampComponent {
  /** yyyy-MM-dd, or null when unknown. */
  readonly dataThrough = input<string | null>(null);
  protected readonly text = computed(() => {
    const date = shortDate(this.dataThrough());
    return date ? `Data through ${date} · Updates once a day` : '';
  });
}
