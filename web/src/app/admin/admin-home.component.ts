import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmsBreadcrumb, EmsBreadcrumbsComponent } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';

const CRUMBS: EmsBreadcrumb[] = [{ label: 'Admin' }];

/** What each admin tier lets an Internal User do. Plain words, in the order they are shown. */
const TIERS: readonly {
  readonly permission: string;
  readonly name: string;
  readonly description: string;
}[] = [
  {
    permission: 'prism.admin.templates.author',
    name: 'Template author',
    description: 'Create and edit View Templates, Personas and the first-login questionnaire.',
  },
  {
    permission: 'prism.admin.templates.publish',
    name: 'Template publisher',
    description: 'Push View Templates to Providers, Personas or everyone.',
  },
  {
    permission: 'prism.admin.customers.view',
    name: 'Customer data viewer',
    description: "See a customer's dashboards as one of their users, read-only.",
  },
  {
    permission: 'prism.admin.manage',
    name: 'Admin manager',
    description: "Manage customer users' roles, admin settings and read the admin audit log.",
  },
];

interface TierRow {
  readonly name: string;
  readonly description: string;
}

/** The front door of the internal admin area. It lists what the signed-in Internal User may do; the tools arrive in later slices. */
@Component({
  selector: 'app-admin-home',
  imports: [EmsBreadcrumbsComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="admin" id="main-content">
      <ems-breadcrumbs [items]="crumbs" />
      <h1 tabindex="-1">Admin</h1>
      @if (canTemplates()) {
        <p><a routerLink="/admin/templates">View Templates</a></p>
        <p><a routerLink="/admin/personas">Personas and first-login questions</a></p>
      }
      @if (canCustomers()) {
        <p><a routerLink="/admin/customers">Customer users and roles</a></p>
      }
      @if (rows().length === 0) {
        <section class="admin__note">
          <h2>No admin tools yet</h2>
          <p>
            You are an internal user, but your account has no admin permission. Ask an administrator
            to give you one.
          </p>
        </section>
      } @else {
        <section aria-labelledby="tiers-heading">
          <h2 id="tiers-heading">What you can do</h2>
          <ul class="admin__list">
            @for (row of rows(); track row.name) {
              <li>
                <strong>{{ row.name }}</strong>
                <span>{{ row.description }}</span>
              </li>
            }
          </ul>
        </section>
      }
    </main>
  `,
  styles: `
    .admin {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
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
    .admin__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    .admin__list li {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      overflow-wrap: anywhere;
    }
    p {
      margin: 0;
    }
  `,
})
export class AdminHomeComponent {
  private readonly auth = inject(AuthService);

  protected readonly crumbs = CRUMBS;
  protected readonly canTemplates = computed(() =>
    this.auth
      .adminPermissions()
      .some((p) => p === 'prism.admin.templates.author' || p === 'prism.admin.templates.publish'),
  );
  protected readonly canCustomers = computed(() =>
    this.auth.adminPermissions().includes('prism.admin.manage'),
  );
  protected readonly rows = computed<readonly TierRow[]>(() => {
    const held = new Set(this.auth.adminPermissions());
    return TIERS.filter((tier) => held.has(tier.permission)).map(({ name, description }) => ({
      name,
      description,
    }));
  });
}
