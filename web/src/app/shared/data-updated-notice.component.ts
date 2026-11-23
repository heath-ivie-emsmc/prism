import { LiveAnnouncer } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { EmsButtonDirective } from '@ems-management-consultants/ui';
import { DataFreshnessService } from './data-freshness.service';
import { shortDate } from './data-through';

/**
 * A quiet notice that the warehouse has newer data than the figures on screen. It never changes the figures and never takes focus:
 * the region is always in the page, so a screen reader announces the text politely when it appears, and "Update view" is a plain button in the tab order.
 */
@Component({
  selector: 'app-data-updated-notice',
  imports: [EmsButtonDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="notice" role="status" aria-live="polite" [class.notice--on]="visible()">
      @if (visible()) {
        <span class="notice__text">Data updated through {{ latestText() }}.</span>
        <button emsButton="raised" type="button" class="notice__action" (click)="update()">Update view</button>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .notice--on {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: center;
      gap: 0.25rem 0.75rem;
      padding: 0.25rem 1.5rem;
      border-bottom: 1px solid var(--ems-border-neutral-default);
      background: var(--ems-surface-neutral-elevated);
      color: var(--ems-text-default);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      overflow-wrap: anywhere;
    }
    /* A bordered secondary button so it reads as a control, not as text. The border and focus ring are real colours, so forced-colors keeps them. */
    .notice__action {
      min-height: 2rem;
      min-width: 1.5rem;
      border: 1px solid var(--ems-border-neutral-strong, currentColor);
      color: var(--ems-text-default);
    }
    .notice__action:focus-visible {
      outline: 2px solid var(--ems-border-focus-default, Highlight);
      outline-offset: 2px;
    }
    @media (forced-colors: active) {
      .notice__action {
        border-color: ButtonText;
      }
      .notice__action:focus-visible {
        outline-color: Highlight;
      }
    }
  `,
})
export class DataUpdatedNoticeComponent {
  private readonly freshness = inject(DataFreshnessService);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly visible = this.freshness.updateAvailable;
  protected readonly latestText = computed(() => shortDate(this.freshness.latest()));

  protected update(): void {
    const date = this.freshness.apply();
    if (date) void this.announcer.announce(`Updated to ${shortDate(date)}`);
  }
}
