import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EmsBreadcrumb, EmsBreadcrumbsComponent, EmsButtonDirective, EmsChipComponent } from '@ems-management-consultants/ui';
import { of } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { formatDate } from '../dashboard/dashboard-format';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { SavedViewListDto, SavedViewSummaryDto } from './layout.model';
import { SavedViewsService } from './saved-views.service';

interface Row {
  readonly id: number;
  readonly name: string;
  readonly description: string | null;
  readonly owner: string | null;
  readonly widgets: string;
  readonly updated: string;
}

const CRUMBS: EmsBreadcrumb[] = [{ label: 'Saved views' }];

function toRow(view: SavedViewSummaryDto): Row {
  return {
    id: view.id,
    name: view.name,
    description: view.description,
    owner: view.ownerName,
    widgets: `${view.widgetCount} ${view.widgetCount === 1 ? 'metric' : 'metrics'}`,
    updated: `Updated ${formatDate(view.updatedAt.slice(0, 10))}`,
  };
}

/** The views a user can open: the ones they made, and the ones shared with them. Row view models are built once, not in the template. */
@Component({
  selector: 'app-saved-views-list',
  imports: [EmsBreadcrumbsComponent, EmsButtonDirective, EmsChipComponent, LoadProblemComponent, RouterLink, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="views" id="main-content">
      <ems-breadcrumbs [items]="crumbs" />
      <header class="views__header">
        <h1 tabindex="-1">Saved views</h1>
        @if (canManage()) {
          <a emsButton="filled" routerLink="/saved-views/new">Create a view</a>
        }
      </header>

      @if (resource.isLoading() && !resource.hasValue()) {
        <app-skeleton height="4rem" />
        <span class="cdk-visually-hidden" role="status">Loading your saved views</span>
      } @else if (resource.error() !== undefined) {
        <app-load-problem [error]="resource.error()" subject="your saved views" (retry)="resource.reload()" />
      } @else if (resource.hasValue()) {
        @if (mine().length === 0 && shared().length === 0) {
          <section class="views__empty">
            <h2>No saved views yet</h2>
            <p>Arrange the metrics you check most on one page and come back to it any time.</p>
            @if (canManage()) {
              <a emsButton="filled" routerLink="/saved-views/new">Create a view</a>
            } @else {
              <p class="views__note">Your account can't create views yet. Ask an administrator to give you the "Saved views: create, edit, delete and share" permission. Views shared with you will appear here.</p>
            }
          </section>
        } @else {
          <section aria-labelledby="mine-heading">
            <h2 id="mine-heading">My views</h2>
            @if (mine().length === 0) {
              <p class="views__note">You haven't created a view yet.</p>
            } @else {
              <ul class="views__list">
                @for (row of mine(); track row.id) {
                  <li>
                    <a [routerLink]="['/saved-views', row.id]">{{ row.name }}</a>
                    <span class="views__meta">{{ row.widgets }} · {{ row.updated }}</span>
                    @if (row.description) { <span class="views__desc">{{ row.description }}</span> }
                  </li>
                }
              </ul>
            }
          </section>
          <section aria-labelledby="shared-heading">
            <h2 id="shared-heading">Shared with me</h2>
            @if (shared().length === 0) {
              <p class="views__note">Nothing has been shared with you yet. When a colleague shares a view, it shows up here.</p>
            } @else {
              <ul class="views__list">
                @for (row of shared(); track row.id) {
                  <li>
                    <a [routerLink]="['/saved-views', row.id]">{{ row.name }}</a>
                    <ems-chip state="info" size="sm" icon="share">Shared by {{ row.owner ?? 'a colleague' }}</ems-chip>
                    <span class="views__meta">{{ row.widgets }} · {{ row.updated }}</span>
                    @if (row.description) { <span class="views__desc">{{ row.description }}</span> }
                  </li>
                }
              </ul>
            }
          </section>
        }
      }
    </main>
  `,
  styles: `
    .views {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
    }
    .views__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
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
    .views__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    .views__list li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.25rem 0.75rem;
      padding: 0.75rem 1rem;
      background: var(--ems-surface-neutral-elevated);
      border-radius: 8px;
      overflow-wrap: anywhere;
    }
    .views__list a {
      font-weight: 600;
      color: var(--ems-text-primary);
      min-height: 1.5rem;
    }
    .views__meta,
    .views__desc,
    .views__note {
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    .views__desc {
      flex-basis: 100%;
    }
    .views__empty {
      padding: 2rem 1rem;
      text-align: center;
    }
    .views__empty p {
      color: var(--ems-text-muted);
    }
    @media (forced-colors: active) {
      .views__list li {
        border: 1px solid CanvasText;
      }
    }
  `,
})
export class SavedViewsListComponent {
  private readonly auth = inject(AuthService);
  private readonly filters = inject(DashboardFilterContext);
  private readonly api = inject(SavedViewsService);

  protected readonly crumbs = CRUMBS;
  protected readonly canManage = computed(() => this.auth.me()?.permissions.includes('prism.views.manage') ?? false);

  protected readonly resource = rxResource<SavedViewListDto | null, string | null>({
    params: () => this.filters.providerId(),
    stream: ({ params }) => (params ? this.api.list(params) : of(null)),
  });
  protected readonly mine = computed<readonly Row[]>(() => (this.resource.hasValue() ? (this.resource.value()?.mine ?? []) : []).map(toRow));
  protected readonly shared = computed<readonly Row[]>(() => (this.resource.hasValue() ? (this.resource.value()?.shared ?? []) : []).map(toRow));
}
