import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { EmsButtonDirective, EmsIconComponent } from '@ems-management-consultants/ui';
import { describeProblem } from './load-problem';

/**
 * Shown where figures should be when they could not be loaded. It is calm on purpose: it says what could not load, that trying again usually
 * works, and gives a reference for support. It never shows server text. The owner passes `retry` to reload, and hides nothing else on the page.
 */
@Component({
  selector: 'app-load-problem',
  imports: [EmsButtonDirective, EmsIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="problem" role="status">
      <ems-icon class="problem__icon" name="cloud_off" />
      <div class="problem__text">
        <p class="problem__title">{{ problem().title }}</p>
        <p class="problem__detail">{{ problem().detail }}</p>
        @if (problem().reference; as reference) {
          <p class="problem__reference">Reference: <code>{{ reference }}</code></p>
        }
      </div>
      @if (problem().retryable) {
        <button emsButton="flat" type="button" class="problem__retry" (click)="retry.emit()">Try again</button>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .problem {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem 1rem;
      padding: 1rem 1.25rem;
      border-radius: var(--ems-corner-md);
      background: var(--ems-surface-neutral-subtle);
      box-shadow: inset 0 0 0 var(--ems-border-width-default) var(--ems-border-neutral-default);
    }
    .problem__icon {
      color: var(--ems-text-muted);
    }
    .problem__text {
      flex: 1 1 16rem;
      min-width: 0;
    }
    .problem__text p {
      margin: 0;
      line-height: var(--ems-line-height-body);
    }
    .problem__title {
      font-weight: var(--ems-font-weight-bold);
    }
    .problem__detail,
    .problem__reference {
      color: var(--ems-text-muted);
    }
    .problem__reference {
      margin-top: 0.25rem;
      font-size: var(--ems-font-size-small);
      overflow-wrap: anywhere;
    }
  `,
})
export class LoadProblemComponent {
  /** The error from the failed load. Only its kind and its trace id are used. */
  readonly error = input.required<unknown>();

  /** What could not load, in a phrase that fits "We can't load ___ right now". */
  readonly subject = input.required<string>();

  readonly retry = output<void>();

  protected readonly problem = computed(() => describeProblem(this.error(), this.subject()));
}
