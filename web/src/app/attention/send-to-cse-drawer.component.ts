import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogClose } from '@angular/material/dialog';
import { EmsButtonDirective, EmsIconComponent } from '@ems-management-consultants/ui';
import { ClaimRowView, formatMoney, totalBalance } from './attention';

/** What the drawer is opened with: the claims the user ticked. It reads nothing from the server. */
export interface SendToCseData {
  readonly claims: readonly ClaimRowView[];
}

/**
 * The Send to your CSE drawer, opened over the right edge of the page. In this first version it lists what was selected and nothing is sent:
 * there is no recipient directory or delivery channel yet, and the page says so. It is a dialog underneath, so focus is held inside it,
 * Escape closes it, and focus goes back to the button that opened it.
 */
@Component({
  selector: 'app-send-to-cse-drawer',
  imports: [EmsButtonDirective, EmsIconComponent, MatDialogClose],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="drawer__header">
      <h2 id="cse-title"><ems-icon name="send" aria-hidden="true" />Send to your CSE</h2>
      <button emsButton="flat" fab color="neutral" type="button" mat-dialog-close aria-label="Close">
        <ems-icon name="close" />
      </button>
    </header>

    <div class="drawer__body">
      <p class="drawer__to"><span class="drawer__muted">To</span><strong>Your client success executive</strong></p>

      <section class="drawer__box" aria-labelledby="cse-attached">
        <h3 id="cse-attached">Claims attached ({{ data.claims.length }}) · {{ total() }}</h3>
        <ul class="drawer__claims">
          @for (claim of data.claims; track claim.id) {
            <li>
              <ems-icon name="receipt_long" aria-hidden="true" />
              <span class="drawer__ref">{{ claim.claimRef }}</span>
              <span class="drawer__muted drawer__meta">{{ claim.payer }} · {{ claim.status }}</span>
              <span class="drawer__days" [class]="'drawer__days drawer__days--' + claim.urgency">
                <ems-icon [name]="claim.urgencyIcon" size="sm" aria-hidden="true" />{{ claim.daysText }}
              </span>
            </li>
          }
        </ul>
        <p class="drawer__muted drawer__basis">The total is the balance still owed. The deadline is an estimate.</p>
      </section>

      <div class="drawer__field">
        <label for="cse-note">Note</label>
        <textarea id="cse-note" rows="6" maxlength="2000" aria-describedby="cse-unavailable"></textarea>
      </div>

      <p id="cse-unavailable" class="drawer__notice"><ems-icon name="info" aria-hidden="true" />Sending is not available yet. For now this shows what you selected.</p>
    </div>

    <footer class="drawer__footer">
      <button emsButton="flat" type="button" mat-dialog-close>Cancel</button>
      <button emsButton="raised" type="button" aria-disabled="true" aria-describedby="cse-unavailable">Save view</button>
      <button emsButton="filled" type="button" aria-disabled="true" aria-describedby="cse-unavailable"><ems-icon name="send" aria-hidden="true" />Send</button>
    </footer>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
      color: var(--ems-text-default);
    }
    .drawer__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
      padding: 1rem 1.25rem 0.5rem;
    }
    h2,
    h3 {
      margin: 0;
    }
    h2 {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 1.25rem;
      line-height: 1.4;
      font-weight: var(--ems-font-weight-heavy);
    }
    h3 {
      font-size: 0.9375rem;
      font-weight: var(--ems-font-weight-bold);
    }
    .drawer__body {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      flex: 1 1 auto;
      min-height: 0;
      overflow-y: auto;
      padding: 0.5rem 1.25rem 1rem;
    }
    .drawer__muted {
      color: var(--ems-text-muted);
    }
    .drawer__to {
      display: flex;
      flex-direction: column;
      margin: 0;
      font-size: 0.8125rem;
    }
    .drawer__to strong {
      font-size: 0.9375rem;
    }
    .drawer__box {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      padding: 1rem;
      border: 1px solid var(--ems-border-neutral-subtle);
      border-radius: var(--ems-corner-md);
    }
    .drawer__claims {
      display: flex;
      flex-direction: column;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .drawer__claims li {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) auto;
      grid-template-areas: 'icon ref days' 'icon meta days';
      column-gap: 0.75rem;
      align-items: center;
      padding-block: 0.5rem;
      font-size: 0.8125rem;
    }
    .drawer__claims li + li {
      border-top: 1px solid var(--ems-border-neutral-subtle);
    }
    .drawer__claims ems-icon {
      grid-area: icon;
    }
    .drawer__ref {
      grid-area: ref;
      font-weight: var(--ems-font-weight-bold);
      overflow-wrap: anywhere;
    }
    .drawer__meta {
      grid-area: meta;
      overflow-wrap: anywhere;
    }
    .drawer__days {
      grid-area: days;
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      white-space: nowrap;
      font-weight: var(--ems-font-weight-bold);
    }
    .drawer__days--soon {
      color: var(--ems-text-warning-default);
    }
    .drawer__days--overdue {
      color: var(--ems-text-error-default);
    }
    .drawer__basis {
      margin: 0;
      font-size: 0.75rem;
    }
    .drawer__field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .drawer__field label {
      font-size: 0.8125rem;
      color: var(--ems-text-muted);
    }
    textarea {
      width: 100%;
      box-sizing: border-box;
      padding: 0.75rem;
      border: 1px solid var(--ems-border-neutral-default, var(--ems-border-neutral-subtle));
      border-radius: var(--ems-corner-md);
      background: var(--ems-surface-neutral-default);
      color: var(--ems-text-default);
      font: inherit;
      resize: vertical;
    }
    .drawer__notice {
      display: flex;
      align-items: flex-start;
      gap: 0.5rem;
      margin: 0;
      padding: 0.75rem;
      border-radius: var(--ems-corner-md);
      background: var(--ems-surface-neutral-subtle);
      color: var(--ems-text-muted);
      font-size: 0.8125rem;
      line-height: 1.4;
    }
    /* The actions stay in view when the claims list is long, and have room so nothing floating can cover them. */
    .drawer__footer {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: 0.5rem;
      padding: 1rem 1.25rem calc(1rem + env(safe-area-inset-bottom, 0px));
      border-top: 1px solid var(--ems-border-neutral-subtle);

    }
    .drawer__footer button[aria-disabled='true'] {
      opacity: 0.6;
      cursor: not-allowed;
    }
    @media (forced-colors: active) {
      .drawer__box,
      textarea,
      .drawer__notice {
        border: 1px solid CanvasText;
      }
    }
  `,
})
export class SendToCseDrawerComponent {
  protected readonly data = inject<SendToCseData>(MAT_DIALOG_DATA);
  protected readonly total = computed(() => formatMoney(totalBalance(this.data.claims)));
}
