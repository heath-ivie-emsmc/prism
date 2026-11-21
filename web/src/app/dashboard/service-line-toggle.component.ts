import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import {
  EmsIconComponent,
} from '@ems-management-consultants/ui';
import { ServiceLineOption, ServiceLineSelection } from './service-line';

/** One-of-three switch for the Service Line. A radio group: arrow keys move the choice, and only the chosen option is in the tab order. */
@Component({
  selector: 'app-service-line-toggle',
  imports: [EmsIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toggle" role="radiogroup" aria-label="Service line">
      @for (option of options(); track option.value) {
        <button
          type="button"
          role="radio"
          class="toggle__option"
          [class.toggle__option--selected]="option.value === value()"
          [attr.aria-checked]="option.value === value()"
          [tabindex]="option.value === value() ? 0 : -1"
          (click)="value.set(option.value)"
          (keydown)="onKeydown($event)"
        >
          @if (option.value === value()) {
            <ems-icon [name]="option.icon" size="sm" />
          }
          {{ option.label }}
        </button>
      }
    </div>
  `,
  styles: `
    .toggle {
      display: inline-flex;
      gap: 0.25rem;
      padding: 0.25rem;
      border: 1px solid var(--ems-border-neutral-subtle);
      border-radius: 0.75rem;
      background: var(--ems-surface-neutral-subtle);
    }
    .toggle__option {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      padding: 0.5rem 0.875rem;
      border: 0;
      border-radius: 0.5rem;
      background: transparent;
      color: var(--ems-text-muted);
      font: inherit;
      font-weight: var(--ems-font-weight-bold);
      cursor: pointer;
    }
    .toggle__option--selected {
      background: var(--ems-surface-neutral-elevated);
      color: var(--ems-text-default);
      box-shadow: 0 1px 2px rgb(0 0 0 / 12%);
    }
    .toggle__option:focus-visible {
      outline: 2px solid var(--ems-border-focus-default);
      outline-offset: 2px;
    }
  `,
})
export class ServiceLineToggleComponent {
  readonly options = input.required<readonly ServiceLineOption[]>();
  readonly value = model.required<ServiceLineSelection>();

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
