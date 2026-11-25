import { LiveAnnouncer } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EmsBreadcrumb, EmsBreadcrumbsComponent, EmsButtonDirective, EmsChipComponent } from '@ems-management-consultants/ui';
import { Observable, catchError, of } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { HomePageDto, OnboardingDto } from '../personas/personas.model';
import { HomeService, OnboardingService } from '../personas/personas.service';
import { SavedViewListDto } from '../saved-views/layout.model';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { SettingsService } from './settings.service';

const CRUMBS: EmsBreadcrumb[] = [{ label: 'Settings' }];
const STANDARD: HomePageDto = { source: 'default', layout: null, savedViewId: null };

interface PushedRow {
  readonly id: number;
  readonly name: string;
  readonly hidden: boolean;
}

/**
 * Your own settings: reset your home page to the recommended one, answer the first-login questions again, and hide or bring back the views the Prism
 * team pushed to you. It only ever acts on the signed-in user, in the Provider they are looking at.
 */
@Component({
  selector: 'app-settings',
  imports: [EmsBreadcrumbsComponent, EmsButtonDirective, EmsChipComponent, LoadProblemComponent, RouterLink, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="settings" id="main-content">
      <ems-breadcrumbs [items]="crumbs" />
      <h1 tabindex="-1">Settings</h1>
      @if (problem()) {
        <p class="settings__problem" role="alert">{{ problem() }}</p>
      }

      <section aria-labelledby="home-heading">
        <h2 id="home-heading">Your home page</h2>
        @if (home.isLoading() && !home.hasValue()) {
          <app-skeleton height="3rem" />
        } @else if (source() === 'custom') {
          <p>Your home page is your own copy. Reset it to go back to the page we recommend, which keeps up with our changes.</p>
          @if (confirmingReset()) {
            <p role="alert">This deletes your copy and brings back the recommended page.</p>
            <div class="settings__actions">
              <button emsButton="filled" type="button" #confirmReset [attr.aria-disabled]="busy() ? 'true' : null" (click)="reset()">Confirm reset</button>
              <button emsButton="flat" type="button" (click)="cancelReset()">Cancel</button>
            </div>
          } @else {
            <button emsButton="raised" type="button" #startReset (click)="askToReset()">Reset to the recommended page</button>
          }
        } @else if (source() === 'persona') {
          <p>Your home page is the one we recommend, and it keeps up with our changes. To make changes of your own, choose Customize this page on the dashboard.</p>
        } @else {
          <p>You are using the standard dashboard.</p>
        }
        @if (canRetake()) {
          <p><a routerLink="/settings/questionnaire">Answer the questions again</a> to get a recommended page that fits your work now.</p>
        }
      </section>

      @if (canManageUsers()) {
        <section aria-labelledby="users-heading">
          <h2 id="users-heading">Users</h2>
          <p><a routerLink="/settings/users">Manage what each person in your Provider may do</a></p>
        </section>
      }

      <section aria-labelledby="pushed-heading">
        <h2 id="pushed-heading">Views from the Prism team</h2>
        @if (pushed.isLoading() && !pushed.hasValue()) {
          <app-skeleton height="3rem" />
        } @else if (pushed.error() !== undefined) {
          <app-load-problem [error]="pushed.error()" subject="your pushed views" (retry)="pushed.reload()" />
        } @else if (rows().length === 0) {
          <p>No views have been pushed to you.</p>
        } @else {
          <ul class="settings__list">
            @for (row of rows(); track row.id) {
              <li>
                <span>{{ row.name }}</span>
                <ems-chip state="neutral" size="sm" icon="star">From EMS</ems-chip>
                @if (row.hidden) {
                  <ems-chip state="warn" size="sm" icon="visibility_off">Hidden</ems-chip>
                  <button emsButton="raised" type="button" [id]="'pushed-' + row.id" [attr.aria-label]="'Restore ' + row.name" (click)="restore(row)">Restore</button>
                } @else {
                  <button emsButton="flat" type="button" [id]="'pushed-' + row.id" [attr.aria-label]="'Hide ' + row.name" (click)="hide(row)">Hide</button>
                }
              </li>
            }
          </ul>
          <p class="settings__note">Hiding a view takes it off your Saved views list. It is not deleted, and it will not come back on its own.</p>
        }
      </section>
    </main>
  `,
  styles: `
    .settings {
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
    p {
      margin: 0 0 0.5rem;
    }
    .settings__problem {
      color: var(--ems-text-error);
    }
    .settings__note {
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    .settings__actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .settings__list {
      list-style: none;
      margin: 0 0 0.5rem;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    .settings__list li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.25rem 0.75rem;
      overflow-wrap: anywhere;
    }
  `,
})
export class SettingsComponent {
  private readonly auth = inject(AuthService);
  private readonly homes = inject(HomeService);
  private readonly questions = inject(OnboardingService);
  private readonly api = inject(SettingsService);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly crumbs = CRUMBS;
  protected readonly busy = signal(false);
  protected readonly problem = signal('');
  protected readonly confirmingReset = signal(false);
  /** What the user just did to a view, shown at once so the button they pressed is replaced in place. */
  private readonly hiddenNow = signal<Readonly<Record<number, boolean>>>({});
  private readonly injector = inject(Injector);
  private readonly confirmButton = viewChild<ElementRef<HTMLButtonElement>>('confirmReset');
  private readonly startButton = viewChild<ElementRef<HTMLButtonElement>>('startReset');

  protected readonly home = rxResource<HomePageDto, string | null>({
    params: () => this.auth.activeProviderId(),
    stream: ({ params }) => (params ? this.homes.get(params).pipe(catchError(() => of(STANDARD))) : of(STANDARD)),
  });
  private readonly questionsResource = rxResource<OnboardingDto, undefined>({
    stream: () => this.questions.questions().pipe(catchError(() => of({ questions: [] }))),
  });
  protected readonly pushed = rxResource<SavedViewListDto | null, string | null>({
    params: () => this.auth.activeProviderId(),
    stream: ({ params }) => (params ? this.api.pushedViews(params) : of(null)),
  });

  protected readonly canManageUsers = computed(() => this.auth.can('prism.users.manage'));
  protected readonly source = computed(() => (this.home.hasValue() ? this.home.value().source : 'default'));
  /** Only offered when a questionnaire is published, so the link never leads to an empty page. */
  protected readonly canRetake = computed(() => this.questionsResource.hasValue() && this.questionsResource.value().questions.length > 0);
  protected readonly rows = computed<readonly PushedRow[]>(() => (this.pushed.hasValue() ? (this.pushed.value()?.mine ?? []) : []).map((v) => ({ id: v.id, name: v.name, hidden: this.hiddenNow()[v.id] ?? v.isHidden === true })));

  /** The pressed button goes away, so focus is moved to where the user is looking next. */
  protected askToReset(): void {
    this.confirmingReset.set(true);
    afterNextRender(() => this.confirmButton()?.nativeElement.focus(), { injector: this.injector });
  }

  protected cancelReset(): void {
    this.confirmingReset.set(false);
    afterNextRender(() => this.startButton()?.nativeElement.focus(), { injector: this.injector });
  }

  protected reset(): void {
    const providerId = this.auth.activeProviderId();
    if (!providerId) return;
    this.run(this.homes.reset(providerId), 'Your home page is back to the recommended page.', "We couldn't reset your home page.", () => {
      this.confirmingReset.set(false);
      this.home.reload();
    });
  }

  protected hide(row: PushedRow): void {
    const providerId = this.auth.activeProviderId();
    if (providerId) this.run(this.api.hide(providerId, row.id), `${row.name} is hidden.`, "We couldn't hide that view.", () => this.flip(row, true));
  }

  protected restore(row: PushedRow): void {
    const providerId = this.auth.activeProviderId();
    if (providerId) this.run(this.api.restore(providerId, row.id), `${row.name} is back in your Saved views.`, "We couldn't restore that view.", () => this.flip(row, false));
  }

  private flip(row: PushedRow, hidden: boolean): void {
    this.hiddenNow.update((now) => ({ ...now, [row.id]: hidden }));
    afterNextRender(() => document.getElementById(`pushed-${row.id}`)?.focus(), { injector: this.injector });
  }

  private run(call: Observable<unknown>, done: string, failed: string, after: () => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.problem.set('');
    call.subscribe({
      next: () => {
        this.busy.set(false);
        after();
        this.announcer.announce(done, 'polite');
      },
      error: () => {
        this.busy.set(false);
        this.problem.set(failed);
      },
    });
  }
}
