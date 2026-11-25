import { LiveAnnouncer } from '@angular/cdk/a11y';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { RouterLink } from '@angular/router';
import { EmsBreadcrumb, EmsBreadcrumbsComponent } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { CustomerRolesService } from './customer-roles.service';
import { ProviderUsersDto, USERS_MANAGE } from './customers.model';
import { RoleChange, UserRolesTableComponent } from './user-roles-table.component';

const CRUMBS: EmsBreadcrumb[] = [{ label: 'Settings', link: '/settings' }, { label: 'Users' }];

function problemText(error: unknown, fallback: string): string {
  const title = error instanceof HttpErrorResponse && error.status === 409 ? (error.error as { title?: unknown } | null)?.title : undefined;
  return typeof title === 'string' && title.length > 0 ? title : fallback;
}

/** A Provider admin choosing what each colleague in their Provider may do. Only the Provider the user is looking at, and only users who have signed in. */
@Component({
  selector: 'app-provider-users',
  imports: [EmsBreadcrumbsComponent, LoadProblemComponent, RouterLink, SkeletonComponent, UserRolesTableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="users" id="main-content">
      <ems-breadcrumbs [items]="crumbs" />
      <h1 tabindex="-1">Users</h1>
      @if (!allowed()) {
        <p>You can't manage users here. Ask an administrator for this Provider to give you the Provider admin role. <a routerLink="/settings">Back to settings</a></p>
      } @else {
        <p class="users__lead">Choose what each person in {{ providerName() }} may do. Roles that show claims or PHI are given by EMS unless EMS has allowed your administrators to.</p>
        @if (problem()) {
          <p class="users__problem" role="alert">{{ problem() }}</p>
        }
        @if (resource.isLoading() && !resource.hasValue()) {
          <app-skeleton height="6rem" />
          <span class="cdk-visually-hidden" role="status">Loading the users</span>
        } @else if (resource.error() !== undefined) {
          <app-load-problem [error]="resource.error()" subject="the users" (retry)="resource.reload()" />
        } @else if (data(); as d) {
          <app-user-roles-table [users]="d.users" [roles]="d.roles" [hasMore]="d.hasMore" [page]="page()" [busy]="busy()" (roleChange)="change($event)" (pageChange)="page.set($event)" />
        }
      }
    </main>
  `,
  styles: `
    .users {
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
    .users__lead {
      margin: 0;
    }
    .users__problem {
      margin: 0;
      color: var(--ems-text-error);
    }
  `,
})
export class ProviderUsersComponent {
  private readonly auth = inject(AuthService);
  private readonly api = inject(CustomerRolesService);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly crumbs = CRUMBS;
  protected readonly page = signal(0);
  protected readonly busy = signal(false);
  protected readonly problem = signal('');
  protected readonly allowed = computed(() => this.auth.can(USERS_MANAGE));
  protected readonly providerName = computed(() => this.auth.activeProvider()?.name ?? 'this Provider');

  protected readonly resource = rxResource<ProviderUsersDto | null, { providerId: string; page: number } | null>({
    params: () => {
      const providerId = this.auth.activeProviderId();
      return providerId && this.allowed() ? { providerId, page: this.page() } : null;
    },
    stream: ({ params }) => params ? this.api.users(params.providerId, params.page) : of(null),
  });
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected change(change: RoleChange): void {
    const providerId = this.auth.activeProviderId();
    if (!providerId || this.busy()) return;
    this.busy.set(true);
    this.problem.set('');
    this.api.setRole(providerId, change.userId, change.roleKey).subscribe({
      next: (user) => {
        this.busy.set(false);
        this.announcer.announce(`${user.displayName}'s role is changed.`, 'polite');
        this.resource.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.problem.set(problemText(error, "We couldn't change that role."));
      },
    });
  }
}
