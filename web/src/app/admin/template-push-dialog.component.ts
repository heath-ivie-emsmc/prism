import { LiveAnnouncer } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialogClose } from '@angular/material/dialog';
import {
  EmsButtonDirective,
  EmsDialogActionsDirective,
  EmsDialogComponent,
} from '@ems-management-consultants/ui';
import { formatDate } from '../dashboard/dashboard-format';
import { describeProblem } from '../shared/load-problem';
import { LoadProblemComponent } from '../shared/load-problem.component';
import {
  AssignmentDto,
  AssignmentKind,
  AssignmentPreviewDto,
  AssignmentScopeDto,
} from './admin.model';
import { AdminTemplatesService } from './admin-templates.service';
import { ProviderChoice, ProviderPickerComponent } from './provider-picker.component';

export interface TemplatePushDialogData {
  readonly templateId: number;
  readonly templateName: string;
}

const PROVIDER_NUMBER = /^[1-9]\d{0,8}$/u;

interface PushRow {
  readonly id: number;
  readonly audience: string;
  readonly detail: string;
  readonly revoked: boolean;
}

function audience(scope: AssignmentScopeDto): string {
  return scope.kind === 'All' ? 'Every Provider' : `Provider ${scope.providerId}`;
}

function toRow(assignment: AssignmentDto): PushRow {
  const delivered = `${assignment.deliveredCount} ${assignment.deliveredCount === 1 ? 'user has' : 'users have'} received it`;
  return {
    id: assignment.id,
    audience: audience(assignment.scope),
    detail: assignment.revokedAt
      ? `Stopped ${formatDate(assignment.revokedAt.slice(0, 10))}. ${delivered}.`
      : `${delivered} so far.`,
    revoked: assignment.revokedAt !== null,
  };
}

/**
 * Pushes a published View Template, and lists and stops earlier pushes. A push is two steps: review what it would do, then confirm it. Each user gets their
 * own copy the next time they open their Saved Views, and a copy already delivered is never replaced or removed. Nobody is named here.
 */
@Component({
  selector: 'app-template-push-dialog',
  imports: [
    EmsButtonDirective,
    EmsDialogActionsDirective,
    EmsDialogComponent,
    LoadProblemComponent,
    MatDialogClose,
    ProviderPickerComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-dialog [heading]="'Push ' + data.templateName" wide>
      <form (submit)="$event.preventDefault(); review()">
        <fieldset>
          <legend>Who receives it</legend>
          <label
            ><input
              type="radio"
              name="kind"
              value="Provider"
              [checked]="kind() === 'Provider'"
              (change)="setKind('Provider')"
            />
            One Provider</label
          >
          <label
            ><input
              type="radio"
              name="kind"
              value="All"
              [checked]="kind() === 'All'"
              (change)="setKind('All')"
            />
            Every Provider</label
          >
        </fieldset>
        @if (kind() === 'Provider') {
          <app-provider-picker
            label="Provider"
            hint="Type part of the name or the number."
            (chosen)="pickProvider($event)"
          />
        }
        <p id="push-error" class="error" [attr.role]="problem() ? 'alert' : null">
          {{ problem() }}
        </p>
        <button emsButton="raised" type="submit" [attr.aria-disabled]="busy() ? 'true' : null">
          Review
        </button>
      </form>

      @if (preview(); as p) {
        <section class="review" aria-labelledby="push-review-heading">
          <h3 id="push-review-heading">Review</h3>
          <p>{{ summary() }}</p>
          <p class="hint">
            Each user gets their own copy the next time they open their Saved Views. Copies already
            delivered are not replaced, and stopping a push does not remove copies.
          </p>
          <button
            emsButton="filled"
            type="button"
            [attr.aria-disabled]="busy() ? 'true' : null"
            (click)="confirm(p)"
          >
            Confirm push
          </button>
        </section>
      }

      @if (assignments.error() !== undefined) {
        <app-load-problem
          [error]="assignments.error()"
          subject="earlier pushes"
          (retry)="assignments.reload()"
        />
      } @else if (assignments.hasValue()) {
        <h3>Earlier pushes ({{ rows().length }})</h3>
        @if (rows().length === 0) {
          <p class="hint">This template has not been pushed.</p>
        } @else {
          <ul>
            @for (row of rows(); track row.id) {
              <li>
                <span
                  >{{ row.audience }} <span class="hint">{{ row.detail }}</span></span
                >
                @if (!row.revoked) {
                  <button
                    emsButton="flat"
                    type="button"
                    [attr.aria-label]="'Stop the push to ' + row.audience"
                    (click)="stop(row)"
                  >
                    Stop
                  </button>
                }
              </li>
            }
          </ul>
        }
      }

      <div emsDialogActions>
        <button emsButton="filled" type="button" mat-dialog-close>Done</button>
      </div>
    </ems-dialog>
  `,
  styles: `
    fieldset {
      margin: 0 0 0.5rem;
      padding: 0;
      border: 0;
    }
    legend {
      padding: 0;
      margin-block-end: 0.25rem;
      font-weight: 600;
    }
    fieldset label {
      display: block;
      font-weight: normal;
    }
    label {
      display: block;
      margin-block-end: 0.25rem;
      font-weight: 600;
    }
    input[type='text'],
    input:not([type]) {
      box-sizing: border-box;
      width: 100%;
      max-width: 14rem;
      padding: 0.5rem 0.75rem;
      font: inherit;
      color: var(--ems-text-default);
      background: var(--ems-surface-neutral-default);
      border: 1px solid var(--ems-border-neutral-strong, currentColor);
      border-radius: 4px;
    }
    .hint {
      margin: 0.25rem 0;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    .error {
      margin: 0.25rem 0;
      min-height: 1.25rem;
      color: var(--ems-text-error-default);
    }
    h3 {
      margin: 1rem 0 0.25rem;
      font-size: var(--ems-font-size-body);
    }
    ul {
      list-style: none;
      margin: 0;
      padding: 0;
    }
    li {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      padding-block: 0.25rem;
      overflow-wrap: anywhere;
    }
  `,
})
export class TemplatePushDialogComponent {
  protected readonly data = inject<TemplatePushDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(AdminTemplatesService);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly kind = signal<AssignmentKind>('Provider');
  protected readonly providerText = signal('');
  protected readonly problem = signal('');
  protected readonly busy = signal(false);
  protected readonly preview = signal<AssignmentPreviewDto | null>(null);

  protected readonly assignments = rxResource({
    stream: () => this.api.assignments(this.data.templateId),
  });
  protected readonly rows = computed<readonly PushRow[]>(() =>
    (this.assignments.hasValue() ? this.assignments.value().assignments : []).map(toRow),
  );
  protected readonly summary = computed(() => {
    const p = this.preview();
    if (!p) return '';
    const widgets = `${p.widgetCount} ${p.widgetCount === 1 ? 'widget' : 'widgets'}`;
    return p.providerCount === null
      ? `"${p.templateName}" (${widgets}) will go to every Provider. Prism does not have a list of all Providers, so the number of users it reaches is not known yet.`
      : `"${p.templateName}" (${widgets}) will go to Provider ${p.scope.providerId}.`;
  });

  protected setKind(kind: AssignmentKind): void {
    this.kind.set(kind);
    this.reset();
  }

  /** A Provider picked by name. The number is what the push uses; the route and the server still check it. */
  protected pickProvider(provider: ProviderChoice | null): void {
    this.providerText.set(provider?.id ?? '');
    this.reset();
  }

  protected reset(): void {
    this.preview.set(null);
    this.problem.set('');
  }

  /** The audience the form describes, or null (with the reason shown) when it is not one the server would accept. */
  private scope(): AssignmentScopeDto | null {
    if (this.kind() === 'All') {
      return { kind: 'All', providerId: null };
    }
    const text = this.providerText().trim();
    if (!PROVIDER_NUMBER.test(text)) {
      this.problem.set('Pick a Provider by name or number.');
      return null;
    }
    return { kind: 'Provider', providerId: Number(text) };
  }

  protected review(): void {
    const scope = this.scope();
    if (!scope || this.busy()) return;
    this.busy.set(true);
    this.api.preview(this.data.templateId, scope).subscribe({
      next: (preview) => {
        this.busy.set(false);
        this.preview.set(preview);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.problem.set(describeProblem(error, 'the review').detail);
      },
    });
  }

  protected confirm(preview: AssignmentPreviewDto): void {
    if (this.busy()) return;
    this.busy.set(true);
    // The count is the one just reviewed. If the template changed since, the server refuses and nothing is pushed.
    this.api.push(this.data.templateId, preview.scope, preview.widgetCount).subscribe({
      next: () => {
        this.busy.set(false);
        this.preview.set(null);
        this.announcer.announce(
          'Pushed. Users receive their copy the next time they open their Saved Views.',
          'polite',
        );
        this.assignments.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.problem.set(describeProblem(error, 'the push').detail);
      },
    });
  }

  protected stop(row: PushRow): void {
    this.api.revoke(row.id).subscribe(() => {
      this.announcer.announce(
        'Push stopped. Copies already delivered stay with their owners.',
        'polite',
      );
      this.assignments.reload();
    });
  }
}
