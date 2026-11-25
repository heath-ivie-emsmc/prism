import { LiveAnnouncer } from '@angular/cdk/a11y';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import {
  EmsBreadcrumb,
  EmsBreadcrumbsComponent,
  EmsButtonDirective,
} from '@ems-management-consultants/ui';
import { Observable, forkJoin, map, of } from 'rxjs';
import { CustomerRolesService } from '../customers/customer-roles.service';
import { ProviderSettingsDto, ProviderUsersDto } from '../customers/customers.model';
import { RoleChange, UserRolesTableComponent } from '../customers/user-roles-table.component';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';

const CRUMBS: EmsBreadcrumb[] = [{ label: 'Admin', link: '/admin' }, { label: 'Customer users' }];
const PROVIDER_NUMBER = /^[1-9]\d{0,8}$/u;

interface Loaded {
  readonly users: ProviderUsersDto;
  readonly settings: ProviderSettingsDto;
}

function problemText(error: unknown, fallback: string): string {
  const title =
    error instanceof HttpErrorResponse && error.status === 409
      ? (error.error as { title?: unknown } | null)?.title
      : undefined;
  return typeof title === 'string' && title.length > 0 ? title : fallback;
}

/**
 * EMS staff managing a customer Provider's roles: make the first Provider admin, change any role, set the role new users start with, and decide whether the
 * Provider's own admins may give claim and PHI roles. The Provider is the number staff type: it is never guessed from a name.
 */
@Component({
  selector: 'app-admin-customers',
  imports: [
    EmsBreadcrumbsComponent,
    EmsButtonDirective,
    LoadProblemComponent,
    SkeletonComponent,
    UserRolesTableComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="customers" id="main-content">
      <ems-breadcrumbs [items]="crumbs" />
      <h1 tabindex="-1">Customer users</h1>
      <form class="customers__chooser" (submit)="$event.preventDefault(); open()">
        <label for="customer-provider">Provider number</label>
        <input
          id="customer-provider"
          inputmode="numeric"
          autocomplete="off"
          maxlength="9"
          aria-describedby="customer-provider-help customer-provider-error"
          [value]="text()"
          (input)="text.set($any($event.target).value); error.set('')"
        />
        <button emsButton="raised" type="submit">Open Provider</button>
      </form>
      <p id="customer-provider-help" class="customers__meta">
        Digits only, for example 526. Only users who have signed in appear.
      </p>
      <p
        id="customer-provider-error"
        class="customers__problem"
        [attr.role]="error() ? 'alert' : null"
      >
        {{ error() }}
      </p>

      @if (providerId(); as id) {
        @if (resource.isLoading() && !resource.hasValue()) {
          <app-skeleton height="6rem" />
          <span class="cdk-visually-hidden" role="status">Loading Provider {{ id }}</span>
        } @else if (resource.error() !== undefined) {
          <app-load-problem
            [error]="resource.error()"
            subject="this Provider's users"
            (retry)="resource.reload()"
          />
        } @else if (data(); as d) {
          <section aria-labelledby="settings-heading">
            <h2 id="settings-heading">Provider {{ id }} settings</h2>
            <form class="customers__settings" (submit)="$event.preventDefault(); saveSettings()">
              <label for="default-role">Role new users start with</label>
              <select
                id="default-role"
                [value]="defaultRole()"
                (change)="defaultRole.set($any($event.target).value)"
              >
                <option value="viewer" [selected]="defaultRole() === 'viewer'">Viewer</option>
                <option value="analyst" [selected]="defaultRole() === 'analyst'">Analyst</option>
              </select>
              <label class="customers__check">
                <input
                  type="checkbox"
                  [checked]="allowRestricted()"
                  (change)="allowRestricted.set($any($event.target).checked)"
                />
                Let this Provider's admins give roles with claims or PHI access
              </label>
              <button
                emsButton="raised"
                type="submit"
                [attr.aria-disabled]="busy() ? 'true' : null"
              >
                Save settings
              </button>
            </form>
          </section>
          <section aria-labelledby="users-heading">
            <h2 id="users-heading">Users</h2>
            @if (problem()) {
              <p class="customers__problem" role="alert">{{ problem() }}</p>
            }
            <app-user-roles-table
              [users]="d.users.users"
              [roles]="d.users.roles"
              [hasMore]="d.users.hasMore"
              [page]="page()"
              [busy]="busy()"
              (roleChange)="change($event)"
              (pageChange)="page.set($event)"
            />
          </section>
        }
      }
    </main>
  `,
  styles: `
    .customers {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
      max-width: 50rem;
    }
    h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    h2 {
      margin: 0 0 0.5rem;
      font-size: var(--ems-font-size-subheading);
    }
    .customers__chooser,
    .customers__settings {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem 0.75rem;
    }
    .customers__chooser label,
    .customers__settings > label:not(.customers__check) {
      font-weight: 600;
    }
    .customers__check {
      flex-basis: 100%;
    }
    .customers__meta,
    .customers__problem {
      margin: 0;
      font-size: var(--ems-font-size-small);
    }
    .customers__meta {
      color: var(--ems-text-muted);
    }
    .customers__problem {
      color: var(--ems-text-error);
    }
    .customers__problem:empty {
      display: none;
    }
    input[type='text'],
    input:not([type]),
    select {
      box-sizing: border-box;
      max-inline-size: 100%;
      min-block-size: 2.5rem;
      padding: 0.5rem;
      font: inherit;
    }
  `,
})
export class AdminCustomersComponent {
  private readonly api = inject(CustomerRolesService);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly crumbs = CRUMBS;
  protected readonly text = signal('');
  protected readonly error = signal('');
  protected readonly providerId = signal<number | null>(null);
  protected readonly page = signal(0);
  protected readonly busy = signal(false);
  protected readonly problem = signal('');
  protected readonly defaultRole = signal('viewer');
  protected readonly allowRestricted = signal(false);

  protected readonly resource = rxResource<
    Loaded | null,
    { providerId: number; page: number } | null
  >({
    params: () => {
      const providerId = this.providerId();
      return providerId === null ? null : { providerId, page: this.page() };
    },
    stream: ({ params }) =>
      params
        ? forkJoin({
            users: this.api.staffUsers(params.providerId, params.page),
            settings: this.api.staffSettings(params.providerId),
          }).pipe(
            map((loaded) => {
              this.defaultRole.set(loaded.settings.defaultRoleKey);
              this.allowRestricted.set(loaded.settings.allowAdminRestrictedGrants);
              return loaded;
            }),
          )
        : of(null),
  });
  protected readonly data = computed(() =>
    this.resource.hasValue() ? this.resource.value() : null,
  );

  protected open(): void {
    const value = this.text().trim();
    if (!PROVIDER_NUMBER.test(value)) {
      this.error.set('Enter the Provider number using digits only, for example 526.');
      return;
    }

    this.page.set(0);
    this.problem.set('');
    this.providerId.set(Number(value));
  }

  protected change(change: RoleChange): void {
    const providerId = this.providerId();
    if (providerId === null || this.busy()) return;
    this.run(
      this.api.staffSetRole(providerId, change.userId, change.roleKey),
      'The role is changed.',
      "We couldn't change that role.",
    );
  }

  protected saveSettings(): void {
    const providerId = this.providerId();
    if (providerId === null || this.busy()) return;
    this.run(
      this.api.staffSetSettings(providerId, {
        defaultRoleKey: this.defaultRole(),
        allowAdminRestrictedGrants: this.allowRestricted(),
      }),
      'The settings are saved.',
      "We couldn't save the settings.",
    );
  }

  private run(call: Observable<unknown>, done: string, failed: string): void {
    this.busy.set(true);
    this.problem.set('');
    call.subscribe({
      next: () => {
        this.busy.set(false);
        this.announcer.announce(done, 'polite');
        this.resource.reload();
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.problem.set(problemText(e, failed));
      },
    });
  }
}
