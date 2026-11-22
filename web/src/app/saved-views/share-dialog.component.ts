import { LiveAnnouncer } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialogClose } from '@angular/material/dialog';
import { EmsButtonDirective, EmsDialogActionsDirective, EmsDialogComponent } from '@ems-management-consultants/ui';
import { announce } from './announcements';
import { MAX_EMAILS_PER_INVITE, PendingInvitationDto, RecipientDto } from './layout.model';
import { SavedViewsService } from './saved-views.service';
import { formatDate } from '../dashboard/dashboard-format';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { describeProblem } from '../shared/load-problem';

export interface ShareDialogData {
  readonly providerId: string;
  readonly viewId: number;
  readonly viewName: string;
}

/** Splits what was typed on commas, semicolons and whitespace, drops blanks and repeats (ignoring case), and caps the count. Pure, so it is tested alone. */
export function parseAddresses(text: string): { readonly emails: readonly string[]; readonly tooMany: boolean } {
  const all = [...new Set(text.split(/[\s,;]+/u).map((part) => part.trim().toLowerCase()).filter(Boolean))];
  return { emails: all.slice(0, MAX_EMAILS_PER_INVITE), tooMany: all.length > MAX_EMAILS_PER_INVITE };
}

const LOOKS_LIKE_EMAIL = /^[^\s@<>,;"]{1,64}@[^\s@<>,;"]{1,255}\.[^\s@<>,;"]{2,}$/u;

/** True for one @ with something either side and a dot after it. The server checks again and only a delivery proves an address. */
export function looksLikeEmail(value: string): boolean {
  return value.length <= 320 && LOOKS_LIKE_EMAIL.test(value);
}

/**
 * Shares a view by emailed invitation. The owner types addresses; each person gets a link that works once and expires. Who has access, and
 * which invitations are still waiting, are listed with a button to take either away. It never says whether an address belongs to a user.
 */
@Component({
  selector: 'app-share-dialog',
  imports: [EmsButtonDirective, EmsDialogActionsDirective, EmsDialogComponent, LoadProblemComponent, MatDialogClose],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-dialog [heading]="'Share ' + data.viewName" wide>
      <form (submit)="$event.preventDefault(); send()">
        <label for="share-emails">Add people</label>
        <textarea id="share-emails" rows="2" maxlength="4000" autocomplete="off" aria-describedby="share-help share-error" [value]="text()" (input)="text.set($any($event.target).value); problem.set('')"></textarea>
        <p id="share-help" class="hint">Enter email addresses, separated by commas or spaces. Each person gets a link that works once and expires. They only see figures they're already allowed to see.</p>
        <p id="share-error" class="error" [attr.role]="problem() ? 'alert' : null">{{ problem() }}</p>
        <button emsButton="filled" type="submit" [attr.aria-disabled]="busy() ? 'true' : null">Send invitation</button>
      </form>

      @if (sharing.error() !== undefined) {
        <app-load-problem [error]="sharing.error()" subject="who has access" (retry)="sharing.reload()" />
      } @else if (sharing.hasValue()) {
        <h3>People with access ({{ recipients().length }})</h3>
        @if (recipients().length === 0) {
          <p class="hint">Nobody yet.</p>
        } @else {
          <ul>
            @for (person of recipients(); track person.shareId) {
              <li>
                <span>{{ person.email ?? 'A colleague' }}</span>
                <button emsButton="flat" type="button" [attr.aria-label]="'Remove access for ' + (person.email ?? 'a colleague')" (click)="removeShare(person)">Remove</button>
              </li>
            }
          </ul>
        }
        <h3>Waiting to be opened ({{ pending().length }})</h3>
        @if (pending().length === 0) {
          <p class="hint">No invitations waiting.</p>
        } @else {
          <ul>
            @for (item of pending(); track item.invitationId) {
              <li>
                <span>{{ item.email }} <span class="hint">{{ item.isExpired ? 'expired' : 'expires ' + date(item.expiresAt) }}</span></span>
                <button emsButton="flat" type="button" [attr.aria-label]="'Cancel the invitation to ' + item.email" (click)="cancel(item)">Cancel</button>
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
    label {
      display: block;
      margin-block-end: 0.25rem;
      font-weight: 600;
    }
    textarea {
      width: 100%;
      box-sizing: border-box;
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
export class ShareDialogComponent {
  protected readonly data = inject<ShareDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(SavedViewsService);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly text = signal('');
  protected readonly problem = signal('');
  protected readonly busy = signal(false);

  protected readonly sharing = rxResource({ stream: () => this.api.sharing(this.data.providerId, this.data.viewId) });
  protected readonly recipients = computed<readonly RecipientDto[]>(() => (this.sharing.hasValue() ? this.sharing.value().recipients : []));
  protected readonly pending = computed<readonly PendingInvitationDto[]>(() => (this.sharing.hasValue() ? this.sharing.value().pending : []));

  protected date(iso: string): string {
    return formatDate(iso.slice(0, 10));
  }

  protected send(): void {
    if (this.busy()) {
      return;
    }
    const { emails, tooMany } = parseAddresses(this.text());
    if (emails.length === 0 || tooMany || !emails.every(looksLikeEmail)) {
      this.problem.set(tooMany ? `Send up to ${MAX_EMAILS_PER_INVITE} addresses at a time.` : 'Enter one or more valid email addresses.');
      return;
    }
    this.busy.set(true);
    this.api.invite(this.data.providerId, this.data.viewId, emails).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.text.set(result.failed.join(', '));
        this.problem.set(result.failed.length > 0 ? `We couldn't send to ${result.failed.length === 1 ? 'one address' : `${result.failed.length} addresses`}. They are left above so you can try again.` : '');
        this.announcer.announce(announce.invited(result.sent.length), 'polite');
        this.sharing.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.problem.set(describeProblem(error, 'the invitation').detail);
      },
    });
  }

  protected removeShare(person: RecipientDto): void {
    this.api.revokeShare(this.data.providerId, this.data.viewId, person.shareId).subscribe(() => {
      this.announcer.announce(announce.sharingRemoved(), 'polite');
      this.sharing.reload();
    });
  }

  protected cancel(item: PendingInvitationDto): void {
    this.api.revokeInvitation(this.data.providerId, this.data.viewId, item.invitationId).subscribe(() => {
      this.announcer.announce('Invitation cancelled.', 'polite');
      this.sharing.reload();
    });
  }
}
