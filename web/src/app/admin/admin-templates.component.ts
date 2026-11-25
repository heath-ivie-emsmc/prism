import { LiveAnnouncer } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import {
  EmsBreadcrumb,
  EmsBreadcrumbsComponent,
  EmsButtonDirective,
  EmsChipComponent,
  EmsDialog,
} from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { formatDate } from '../dashboard/dashboard-format';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import {
  TIER_AUTHOR,
  TIER_PUBLISH,
  ViewTemplateListDto,
  ViewTemplateSummaryDto,
} from './admin.model';
import { AdminTemplatesService } from './admin-templates.service';
import { TemplatePushDialogComponent } from './template-push-dialog.component';

interface Row {
  readonly id: number;
  readonly name: string;
  readonly description: string | null;
  readonly published: boolean;
  readonly widgets: string;
  readonly updated: string;
}

const CRUMBS: EmsBreadcrumb[] = [{ label: 'Admin', link: '/admin' }, { label: 'View Templates' }];

function toRow(template: ViewTemplateSummaryDto): Row {
  return {
    id: template.id,
    name: template.name,
    description: template.description,
    published: template.isPublished,
    widgets: `${template.widgetCount} ${template.widgetCount === 1 ? 'widget' : 'widgets'}`,
    updated: `Updated ${formatDate(template.updatedAt.slice(0, 10))}`,
  };
}

/** The View Templates: open one to lay it out, publish it, and push it to Providers. Row view models are built once, not in the template. */
@Component({
  selector: 'app-admin-templates',
  imports: [
    EmsBreadcrumbsComponent,
    EmsButtonDirective,
    EmsChipComponent,
    LoadProblemComponent,
    RouterLink,
    SkeletonComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="templates" id="main-content">
      <ems-breadcrumbs [items]="crumbs" />
      <header class="templates__header">
        <h1 tabindex="-1">View Templates</h1>
        @if (canAuthor()) {
          <a emsButton="filled" routerLink="/admin/templates/new">Create a template</a>
        }
      </header>

      @if (resource.isLoading() && !resource.hasValue()) {
        <app-skeleton height="4rem" />
        <span class="cdk-visually-hidden" role="status">Loading the templates</span>
      } @else if (resource.error() !== undefined) {
        <app-load-problem
          [error]="resource.error()"
          subject="the templates"
          (retry)="resource.reload()"
        />
      } @else if (rows().length === 0) {
        <section>
          <h2>No templates yet</h2>
          <p>
            A template is a starting layout you can push to customers. Create one, publish it, then
            push it to a Provider.
          </p>
        </section>
      } @else {
        <ul class="templates__list">
          @for (row of rows(); track row.id) {
            <li>
              <a [routerLink]="['/admin/templates', row.id]">{{ row.name }}</a>
              @if (row.published) {
                <ems-chip state="success" size="sm" icon="check">Published</ems-chip>
              } @else {
                <ems-chip state="neutral" size="sm" icon="edit">Draft</ems-chip>
              }
              <span class="templates__meta">{{ row.widgets }} · {{ row.updated }}</span>
              @if (row.description) {
                <span class="templates__desc">{{ row.description }}</span>
              }
              @if (canPublish()) {
                <span class="templates__actions">
                  @if (row.published) {
                    <button
                      emsButton="raised"
                      type="button"
                      [attr.aria-label]="'Push ' + row.name"
                      (click)="push(row)"
                    >
                      Push
                    </button>
                    <button
                      emsButton="flat"
                      type="button"
                      [attr.aria-label]="'Unpublish ' + row.name"
                      (click)="unpublish(row)"
                    >
                      Unpublish
                    </button>
                  } @else {
                    <button
                      emsButton="raised"
                      type="button"
                      [attr.aria-label]="'Publish ' + row.name"
                      (click)="publish(row)"
                    >
                      Publish
                    </button>
                  }
                </span>
              }
            </li>
          }
        </ul>
      }
    </main>
  `,
  styles: `
    .templates {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
    }
    .templates__header {
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
    .templates__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    .templates__list li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.25rem 0.75rem;
      overflow-wrap: anywhere;
    }
    .templates__meta,
    .templates__desc {
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    .templates__desc {
      flex-basis: 100%;
    }
    .templates__actions {
      display: inline-flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
  `,
})
export class AdminTemplatesComponent {
  private readonly auth = inject(AuthService);
  private readonly api = inject(AdminTemplatesService);
  private readonly dialogs = inject(EmsDialog);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly crumbs = CRUMBS;
  protected readonly canAuthor = computed(() => this.auth.adminPermissions().includes(TIER_AUTHOR));
  protected readonly canPublish = computed(() =>
    this.auth.adminPermissions().includes(TIER_PUBLISH),
  );

  protected readonly resource = rxResource<ViewTemplateListDto, void>({
    stream: () => this.api.list(),
  });
  protected readonly rows = computed<readonly Row[]>(() =>
    (this.resource.hasValue() ? this.resource.value().templates : []).map(toRow),
  );

  protected publish(row: Row): void {
    this.api.publish(row.id).subscribe(() => {
      this.announcer.announce(`${row.name} is published.`, 'polite');
      this.resource.reload();
    });
  }

  protected unpublish(row: Row): void {
    this.api.unpublish(row.id).subscribe(() => {
      this.announcer.announce(
        `${row.name} is a draft again. Earlier pushes are not affected.`,
        'polite',
      );
      this.resource.reload();
    });
  }

  protected push(row: Row): void {
    this.dialogs.open(TemplatePushDialogComponent, {
      data: { templateId: row.id, templateName: row.name },
      autoFocus: 'first-tabbable',
    });
  }
}
