import { LiveAnnouncer } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, input, output, signal } from '@angular/core';
import { EmsButtonDirective, EmsCardComponent, EmsIconComponent } from '@ems-management-consultants/ui';
import { ContextBadgeComponent } from './context-badge.component';
import { FreshnessStampComponent } from './freshness-stamp.component';
import { Labels } from '../dashboard/service-line';
import { EXITED_ANNOUNCEMENT, UNAVAILABLE_ANNOUNCEMENT, enteredAnnouncement, expandButtonName, fullscreenSupported } from './fullscreen';

/**
 * The one container for a piece of data on screen: a card with the heading, an optional expand-to-full-screen button at the top left, and room for
 * actions at the top right. Anything that shows a chart or table goes in one, so behaviour every data view should share lives here, once.
 *
 * Full screen uses the browser's Fullscreen API on this element, so the chart is not moved or rebuilt (it just resizes) and Escape works with no code of ours.
 * The state follows the browser's `fullscreenchange` event, never the click, so Escape and the browser's own controls stay in step with the button.
 * Menus and dialogs open in an overlay outside this element and would not show over it, so nothing inside may open one.
 */
@Component({
  selector: 'app-data-frame',
  imports: [ContextBadgeComponent, EmsButtonDirective, EmsCardComponent, EmsIconComponent, FreshnessStampComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:fullscreenchange)': 'sync()' },
  template: `
    <ems-card [heading]="heading()">
      @if (canExpand()) {
        <button emsCardLeading emsButton="flat" fab color="neutral" type="button" class="frame__expand" data-snapshot-skip [attr.aria-label]="buttonName()" (click)="toggle()">
          <ems-icon [name]="expanded() ? 'fullscreen_exit' : 'fullscreen'" />
        </button>
      }
      <div emsCardActions><ng-content select="[frameActions]" /></div>
      @if (expanded() && context()) {
        <p class="frame__context">{{ context() }}</p>
      }
      <ng-content />
      @if (dataThrough()) {
        <div class="frame__foot">
          <app-freshness-stamp [dataThrough]="dataThrough()" />
          <app-context-badge [serviceLine]="serviceLine()" [divisionApplied]="divisionApplied()" [labels]="labels()" />
        </div>
      }
    </ems-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
      height: 100%;
    }
    /* The card fills what the frame was given, and its content area fills the card, so a taller frame really is taller. */
    ems-card {
      height: 100%;
    }
    :host ::ng-deep ems-card .ems-card__content {
      display: flex;
      flex-direction: column;
      flex: 1 1 auto;
      min-height: 0;
    }
    /* Narrow phones: the actions (a subtitle, a view switcher) take their own full-width row under the title, so neither is squeezed into a sliver. */
    @media (max-width: 400px) {
      :host ::ng-deep ems-card .ems-card__header {
        flex-wrap: wrap;
        min-height: 0;
      }
      :host ::ng-deep ems-card .ems-card__title {
        flex: 1 1 auto;
        min-width: 0;
      }
      :host ::ng-deep ems-card .ems-card__actions {
        flex: 1 1 100%;
        flex-wrap: wrap;
        min-width: 0;
      }
    }
    .frame__expand {
      min-width: 2rem;
      min-height: 2rem;
    }
    /* Wraps instead of overflowing at 320px; the two statements are separate lines when there is no room side by side. */
    .frame__foot {
      display: flex;
      flex-wrap: wrap;
      gap: 0 1rem;
      margin-top: auto;
      padding-top: 0.5rem;
      min-width: 0;
      overflow-wrap: anywhere;
    }
    .frame__context {
      margin: 0 0 0.5rem;
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
    /* The browser paints a black backdrop behind a full screen element, so the frame brings its own opaque surface. */
    :host(:fullscreen) {
      box-sizing: border-box;
      padding: 0.5rem;
      background: var(--ems-surface-neutral-elevated);
    }
    :host(:fullscreen) ::ng-deep ems-card .ems-card__content {
      overflow: auto;
    }
    @media (pointer: coarse) {
      .frame__expand {
        min-width: 2.75rem;
        min-height: 2.75rem;
      }
    }
    @media (forced-colors: active) {
      .frame__expand:focus-visible {
        outline: 2px solid CanvasText;
        outline-offset: 2px;
      }
    }
  `,
})
export class DataFrameComponent {
  /** The visible title, already the Provider's own Label where the term is renamable. */
  readonly heading = input.required<string>();
  /** False while the layout is being edited: moving things about and a full screen view do not mix. */
  readonly expandable = input(true);
  /** What the figures cover, for example "October 2026 · Air". Shown only when expanded, because the page filters are out of sight then. */
  readonly context = input('');

  /** yyyy-MM-dd. When given, a footer shows the freshness stamp and what the figures cover. Omit it and the frame looks as before. */
  readonly dataThrough = input<string | null>(null);
  readonly serviceLine = input<'all' | 'air' | 'ground'>('all');
  readonly divisionApplied = input(false);
  readonly labels = input<Labels | null>(null);

  /** Tells the content when it is full screen, for content that has to draw larger (a fixed-height picture). Report only: the frame owns the state. */
  readonly expandedChange = output<boolean>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly expanded = signal(false);
  protected readonly supported = signal(fullscreenSupported(document, this.host));
  protected readonly canExpand = computed(() => this.expandable() && this.supported());
  protected readonly buttonName = computed(() => expandButtonName(this.heading(), this.expanded()));

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      // A frame that goes away while it is full screen (route change, widget removed) must not leave the browser stuck on an empty screen.
      if (document.fullscreenElement === this.host) {
        document.exitFullscreen().catch(() => undefined);
      }
    });
  }

  protected toggle(): void {
    if (document.fullscreenElement === this.host) {
      document.exitFullscreen().catch(() => undefined);
      return;
    }
    this.host.requestFullscreen().catch(() => void this.announcer.announce(UNAVAILABLE_ANNOUNCEMENT));
  }

  /** Follows the browser: it fires for our own click, for Escape, and for the browser's own exit controls. */
  protected sync(): void {
    const now = document.fullscreenElement === this.host;
    if (now === this.expanded()) return;
    this.expanded.set(now);
    this.expandedChange.emit(now);
    void this.announcer.announce(now ? enteredAnnouncement(this.heading()) : EXITED_ANNOUNCEMENT);
    if (!now) this.returnFocus();
  }

  /** Leaving full screen can drop focus to the page body; put it back on the button that opened it so a keyboard user is not lost. */
  private returnFocus(): void {
    const active = document.activeElement;
    if (active && active !== document.body) return;
    // The button is redrawn as the state flips, so wait for it.
    queueMicrotask(() => this.host.querySelector<HTMLButtonElement>('.frame__expand')?.focus());
  }
}
