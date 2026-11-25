import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { EmsButtonDirective, EmsChipComponent } from '@ems-management-consultants/ui';
import { formatDate } from '../dashboard/dashboard-format';
import { ProviderUserDto, RoleDto } from './customers.model';

export interface RoleChange {
  readonly userId: number;
  readonly roleKey: string;
}

interface Row {
  readonly id: number;
  readonly name: string;
  readonly isYou: boolean;
  readonly roleKey: string;
  readonly lastSignIn: string;
  readonly options: readonly { readonly key: string; readonly label: string; readonly disabled: boolean }[];
}

/**
 * One user per row with their role as a list to choose from. Rows are built once. A role the viewer may not give is shown but cannot be picked, and the
 * viewer's own role cannot be changed. Names are bound as text only.
 */
@Component({
  selector: 'app-user-roles-table',
  imports: [EmsButtonDirective, EmsChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rows().length === 0) {
      <p>No users have signed in to this Provider yet. A user appears here after their first sign-in.</p>
    } @else {
      <ul class="roles__list">
        @for (row of rows(); track row.id) {
          <li>
            <span class="roles__name">
              {{ row.name }}
              @if (row.isYou) { <ems-chip state="info" size="sm">You</ems-chip> }
            </span>
            <span class="roles__meta">{{ row.lastSignIn }}</span>
            <label class="roles__field">
              <span class="cdk-visually-hidden">Role for {{ row.name }}</span>
              <select [disabled]="row.isYou || busy()" (change)="pick(row, $any($event.target).value, $event)">
                @for (option of row.options; track option.key) {
                  <option [value]="option.key" [selected]="option.key === row.roleKey" [disabled]="option.disabled">{{ option.label }}</option>
                }
              </select>
            </label>
          </li>
        }
      </ul>
    }
    @if (page() > 0 || hasMore()) {
      <nav class="roles__pages" aria-label="Pages of users">
        <button emsButton="flat" type="button" [attr.aria-disabled]="page() === 0 ? 'true' : null" (click)="page() > 0 && pageChange.emit(page() - 1)">Previous</button>
        <span>Page {{ page() + 1 }}</span>
        <button emsButton="flat" type="button" [attr.aria-disabled]="hasMore() ? null : 'true'" (click)="hasMore() && pageChange.emit(page() + 1)">Next</button>
      </nav>
    }
  `,
  styles: `
    .roles__list {
      container-type: inline-size;
      list-style: none;
      margin: 0 0 0.75rem;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    .roles__list li {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      align-items: center;
      gap: 0.25rem 0.75rem;
      overflow-wrap: anywhere;
    }
    /* Side by side when the list itself has room, so the role lists line up in one column. This follows the space the list gets (beside a menu, at large text), not the window. */
    @container (min-width: 42rem) {
      .roles__list li {
        grid-template-columns: minmax(10rem, 1fr) minmax(12rem, auto) minmax(14rem, 18rem);
      }
    }
    .roles__name {
      font-weight: 600;
    }
    .roles__meta {
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    .roles__field select {
      box-sizing: border-box;
      inline-size: 100%;
      max-inline-size: 100%;
      min-block-size: 2.5rem;
      padding: 0.5rem;
      font: inherit;
    }
    .roles__pages {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }
  `,
})
export class UserRolesTableComponent {
  readonly users = input.required<readonly ProviderUserDto[]>();
  readonly roles = input.required<readonly RoleDto[]>();
  readonly hasMore = input(false);
  readonly page = input(0);
  readonly busy = input(false);
  readonly roleChange = output<RoleChange>();
  readonly pageChange = output<number>();

  protected readonly rows = computed<readonly Row[]>(() =>
    this.users().map((u) => ({
      id: u.portalUserId,
      name: u.displayName,
      isYou: u.isYou,
      roleKey: u.roleKey,
      lastSignIn: `Last signed in ${formatDate(u.lastSignIn.slice(0, 10))}`,
      options: this.roles().map((r) => ({
        key: r.key,
        label: r.restricted ? `${r.name} (claims or PHI)` : r.name,
        // The role they already hold stays selectable, so an existing restricted role is shown as it is.
        disabled: !r.assignable && r.key !== u.roleKey,
      })),
    })),
  );

  protected pick(row: Row, roleKey: string, event: Event): void {
    if (roleKey === row.roleKey) return;
    this.roleChange.emit({ userId: row.id, roleKey });
    // Put the list back until the server has said yes: the table shows what is stored, not what was clicked.
    (event.target as HTMLSelectElement).value = row.roleKey;
  }
}
