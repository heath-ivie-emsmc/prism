import { ChangeDetectionStrategy, Component, ElementRef, computed, input, signal, viewChild } from '@angular/core';
import { Labels, defaultLabels } from '../dashboard/service-line';
import { resolveHelp } from '../metrics/help';
import { METRIC_HELP } from '../metrics/help-registry';

let nextId = 0;

/** An info button that opens the Metric's summary and glossary. The panel is part of the page, not an overlay, so it works inside a full screen frame. */
@Component({
  selector: 'app-metric-help',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(keydown.escape)': 'close($event)' },
  template: `
    @if (help(); as h) {
      @if (!compact()) {
        <p class="help__summary">{{ h.summary }}</p>
      }
      <button #toggle type="button" class="help__toggle" [attr.aria-expanded]="open()" [attr.aria-controls]="panelId" (click)="open.set(!open())">
        What these numbers mean@if (heading()) {<span class="cdk-visually-hidden"> for {{ heading() }}</span>}
      </button>
      @if (open()) {
        <div class="help__panel" [id]="panelId">
          @if (compact()) {
            <p class="help__summary">{{ h.summary }}</p>
          }
          <dl>
            @for (g of h.glossary; track g.term) {
              <dt>{{ g.term }}</dt>
              <dd>{{ g.text }}</dd>
            }
          </dl>
        </div>
      }
    }
  `,
  styles: `
    :host {
      display: block;
      margin-top: 0.5rem;
    }
    .help__summary {
      margin: 0 0 0.25rem;
      color: var(--ems-text-muted);
      line-height: var(--ems-line-height-body);
    }
    .help__toggle {
      min-height: 2rem;
      padding: 0;
      border: 0;
      background: none;
      color: var(--ems-text-link, inherit);
      font: inherit;
      text-decoration: underline;
      cursor: pointer;
    }
    dt {
      font-weight: 600;
    }
    dd {
      margin: 0 0 0.5rem;
      color: var(--ems-text-muted);
    }
  `,
})
export class MetricHelpComponent {
  /** The Metric key, as in the catalog. */
  readonly metric = input.required<string>();
  protected readonly panelId = `metric-help-${nextId++}`;
  protected readonly open = signal(false);
  private readonly toggle = viewChild<ElementRef<HTMLButtonElement>>('toggle');

  /** Escape closes the panel and returns focus to the toggle, so a keyboard user is not left on an element that just left the page. */
  protected close(event: Event): void {
    if (!this.open()) return;
    event.stopPropagation();
    this.open.set(false);
    this.toggle()?.nativeElement.focus();
  }
  /** Small cards and widgets show only the disclosure button; the summary opens with the glossary. Keeps a tile's height steady. */
  readonly compact = input(false);
  /** Names the figure for screen readers when several of these share a page ("What these numbers mean for Transports"). */
  readonly heading = input('');
  /** The Service Line the figures cover, so the Reporting Division wording matches the context badge. */
  readonly serviceLine = input<'all' | 'air' | 'ground'>('all');
  /** The Provider's own Labels, when the page has them. Otherwise the defaults for the Service Line. */
  readonly labels = input<Labels | null>(null);
  protected readonly help = computed(() => {
    const entry = METRIC_HELP[this.metric()];
    return entry ? resolveHelp(entry, this.labels() ?? defaultLabels(this.serviceLine() === 'air' ? 'air' : 'both')) : null;
  });
}
