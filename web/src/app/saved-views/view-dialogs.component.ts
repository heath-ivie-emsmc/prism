import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogClose, MatDialogRef } from '@angular/material/dialog';
import { EmsButtonDirective, EmsDialogActionsDirective, EmsDialogComponent, EmsMessageComponent } from '@ems-management-consultants/ui';
import { MAX_VIEW_NAME } from './layout.model';

export interface NameDialogData {
  readonly heading: string;
  readonly confirmLabel: string;
  readonly name: string;
  /** Shown when the server said the name is already used. */
  readonly error?: string;
}

/** Asks for a view's name. Up to 80 characters, never empty. Returns the trimmed name, or nothing when cancelled. */
@Component({
  selector: 'app-name-dialog',
  imports: [EmsButtonDirective, EmsDialogActionsDirective, EmsDialogComponent, EmsMessageComponent, MatDialogClose],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-dialog [heading]="data.heading">
      <form (submit)="$event.preventDefault(); submit()">
        <label for="view-name">View name</label>
        <input id="view-name" #field type="text" autocomplete="off" [maxLength]="max" [value]="name()" [attr.aria-invalid]="error() ? 'true' : null" [attr.aria-describedby]="'view-name-hint' + (error() ? ' view-name-error' : '')" (input)="name.set($any($event.target).value); error.set('')" />
        <p id="view-name-hint" class="hint">Up to {{ max }} characters.</p>
        @if (error()) {
          <ems-message id="view-name-error" state="error">{{ error() }}</ems-message>
        }
      </form>
      <div emsDialogActions>
        <button emsButton="flat" type="button" mat-dialog-close>Cancel</button>
        <button emsButton="filled" type="button" (click)="submit()">{{ data.confirmLabel }}</button>
      </div>
    </ems-dialog>
  `,
  styles: `
    label {
      display: block;
      margin-block-end: 0.25rem;
      font-weight: 600;
    }
    input {
      width: 100%;
      box-sizing: border-box;
      min-height: 2.5rem;
      padding: 0 0.75rem;
      font: inherit;
      color: var(--ems-text-default);
      background: var(--ems-surface-neutral-default);
      border: 1px solid var(--ems-border-neutral-strong, currentColor);
      border-radius: 4px;
    }
    .hint {
      margin: 0.25rem 0 0;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
  `,
})
export class NameDialogComponent {
  protected readonly data = inject<NameDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<NameDialogComponent, string>>(MatDialogRef);
  protected readonly max = MAX_VIEW_NAME;
  protected readonly name = signal(this.data.name);
  protected readonly error = signal(this.data.error ?? '');

  protected submit(): void {
    const trimmed = this.name().trim();
    if (!trimmed) {
      this.error.set('Give the view a name.');
      return;
    }
    this.ref.close(trimmed);
  }
}

export type ConflictChoice = 'copy' | 'reload' | 'keep';

/** The view was saved by someone else while this one was open. Nothing is overwritten: the safe choice is first and the default. */
@Component({
  selector: 'app-conflict-dialog',
  imports: [EmsButtonDirective, EmsDialogActionsDirective, EmsDialogComponent, MatDialogClose],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-dialog heading="This view changed while you were editing">
      <p>Someone saved changes to this view after you opened it. Your changes haven't been saved.</p>
      <div emsDialogActions>
        <button emsButton="filled" type="button" class="conflict-default" [mat-dialog-close]="'copy'">Save as a copy</button>
        <button emsButton="raised" type="button" [mat-dialog-close]="'keep'">Keep editing</button>
        <button emsButton="flat" type="button" [mat-dialog-close]="'reload'">Load their version</button>
      </div>
    </ems-dialog>
  `,
})
export class ConflictDialogComponent {}
