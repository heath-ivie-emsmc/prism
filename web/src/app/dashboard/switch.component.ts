import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

/** An on/off switch with a visible label, as a native button with the switch role so it works by keyboard and screen reader. */
@Component({
  selector: 'app-switch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" role="switch" class="switch" [attr.aria-checked]="checked()" [disabled]="disabled()" (click)="checked.set(!checked())">
      <span class="switch__track" aria-hidden="true"><span class="switch__thumb"></span></span>
      <span class="switch__label">{{ label() }}</span>
    </button>
  `,
  styles: `
    :host {
      display: inline-block;
    }
    .switch {
      display: inline-flex;
      align-items: center;
      gap: 0.75rem;
      min-height: 2.75rem;
      padding: 0 0.25rem;
      border: 0;
      background: none;
      color: var(--ems-text-muted);
      font: inherit;
      font-weight: var(--ems-font-weight-bold);
      cursor: pointer;
    }
    .switch:disabled {
      cursor: not-allowed;
      opacity: 0.6;
    }
    .switch:focus-visible {
      outline: 2px solid var(--ems-border-primary-default, currentColor);
      outline-offset: 2px;
      border-radius: 0.5rem;
    }
    .switch__track {
      position: relative;
      flex: none;
      width: 2.5rem;
      height: 1.5rem;
      border: 1px solid var(--ems-border-neutral-strong, #707070);
      border-radius: 999px;
      background: var(--ems-surface-neutral-elevated, #fff);
      transition: background-color 0.15s;
    }
    .switch__thumb {
      position: absolute;
      top: 50%;
      left: 0.1875rem;
      width: 1rem;
      height: 1rem;
      border-radius: 50%;
      background: var(--ems-border-neutral-strong, #707070);
      transform: translateY(-50%);
      transition: left 0.15s, background-color 0.15s;
    }
    .switch[aria-checked='true'] .switch__track {
      border-color: var(--ems-surface-accent-emerald, #00704a);
      background: var(--ems-surface-accent-emerald, #00704a);
    }
    .switch[aria-checked='true'] .switch__thumb {
      left: 1.1875rem;
      background: #fff;
    }
    .switch[aria-checked='true'] .switch__label {
      color: var(--ems-text-default);
    }
    @media (prefers-reduced-motion: reduce) {
      .switch__track,
      .switch__thumb {
        transition: none;
      }
    }
    @media (forced-colors: active) {
      .switch__thumb {
        background: CanvasText;
      }
      .switch[aria-checked='true'] .switch__track {
        background: Highlight;
      }
    }
  `,
})
export class SwitchComponent {
  readonly label = input.required<string>();
  readonly checked = model(false);
  readonly disabled = input(false);
}
