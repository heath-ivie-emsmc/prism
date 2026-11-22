import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { EmsIconComponent } from '@ems-management-consultants/ui';

export interface SegmentOption<T extends string> {
  readonly value: T;
  readonly label: string;
  /** Material icon shown on the chosen option, or on every option when `iconOnly`. */
  readonly icon?: string;
}

/**
 * One-of-N switch. A radio group: arrow keys move the choice, and only the chosen option is in the tab order.
 * `iconOnly` shows icons with the label as the accessible name (chart type, for example).
 */
@Component({
  selector: 'app-segmented-control',
  imports: [EmsIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="segments" [class.segments--compact]="compact()" role="radiogroup" [attr.aria-label]="label()">
      @for (option of options(); track option.value) {
        <button
          type="button"
          role="radio"
          class="segments__option"
          [class.segments__option--selected]="option.value === value()"
          [attr.aria-checked]="option.value === value()"
          [attr.aria-label]="iconOnly() ? option.label : null"
          [attr.title]="iconOnly() ? option.label : null"
          [disabled]="disabled()"
          [tabindex]="option.value === value() ? 0 : -1"
          (click)="value.set(option.value)"
          (keydown)="onKeydown($event)"
        >
          @if (option.icon && (iconOnly() || option.value === value())) {
            <ems-icon [name]="option.icon" size="sm" />
          }
          @if (!iconOnly()) {
            {{ option.label }}
          }
        </button>
      }
    </div>
  `,
  styles: `
    .segments {
      display: inline-flex;
      flex-wrap: wrap;
      max-width: 100%;
      gap: 0.25rem;
      padding: 0.25rem;
      border: 1px solid var(--ems-border-neutral-subtle);
      border-radius: 0.75rem;
      background: var(--ems-surface-neutral-subtle);
    }
    /* Smaller, for a switch that sits beside a figure. Still over the 24px minimum target size. */
    .segments--compact {
      gap: 0.125rem;
      padding: 0.125rem;
    }
    .segments--compact .segments__option {
      padding: 0.25rem 0.5rem;
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
    }
    .segments__option {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      padding: 0.375rem 0.75rem;
      border: 0;
      border-radius: 0.5rem;
      background: transparent;
      color: var(--ems-text-muted);
      font: inherit;
      font-weight: var(--ems-font-weight-bold);
      cursor: pointer;
      white-space: nowrap;
    }
    .segments__option--selected {
      background: var(--ems-surface-neutral-elevated);
      color: var(--ems-text-default);
      box-shadow: 0 1px 2px rgb(0 0 0 / 12%);
    }
    .segments__option:disabled {
      cursor: not-allowed;
      opacity: 0.6;
    }
    .segments__option:focus-visible {
      outline: 2px solid var(--ems-border-focus-default);
      outline-offset: 2px;
    }
  `,
})
export class SegmentedControlComponent<T extends string> {
  readonly options = input.required<readonly SegmentOption<T>[]>();
  readonly value = model.required<T>();
  readonly label = input.required<string>();
  readonly iconOnly = input(false);
  /** A smaller control, for beside a figure rather than in a toolbar. */
  readonly compact = input(false);
  /** The choice is shown but cannot be changed, for a filter the data does not support yet. */
  readonly disabled = input(false);

  protected onKeydown(event: KeyboardEvent): void {
    const step = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0;
    if (step === 0) return;
    event.preventDefault();
    const options = this.options();
    const current = options.findIndex((option) => option.value === this.value());
    const next = options[(current + step + options.length) % options.length];
    this.value.set(next.value);
    const buttons = (event.currentTarget as HTMLElement).parentElement!.querySelectorAll<HTMLElement>('[role="radio"]');
    queueMicrotask(() => buttons[options.indexOf(next)]?.focus());
  }
}
