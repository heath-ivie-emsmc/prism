import { LiveAnnouncer } from '@angular/cdk/a11y';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { EmsButtonDirective } from '@ems-management-consultants/ui';
import { Observable, catchError, map, of } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { DashboardComponent } from '../dashboard/dashboard.component';
import { FilterBarComponent } from '../dashboard/filter-bar.component';
import { metricDivisionSupport } from '../metrics/metric-catalog.service';
import { GridCanvasComponent } from '../saved-views/grid-canvas.component';
import { CatalogDto, CatalogMetricDto, SavedViewLayout } from '../saved-views/layout.model';
import { SavedViewsService } from '../saved-views/saved-views.service';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { HomePageDto } from './personas.model';
import { HomeService } from './personas.service';

const STANDARD: HomePageDto = { source: 'default', layout: null, savedViewId: null };

/**
 * The home page. It is the standard dashboard unless the user's Persona points at a published View Template, which is then followed live,
 * or the user has made their own copy. It fails open: if the page cannot be resolved the user gets the standard dashboard, never an error.
 * The Persona itself is never shown.
 */
@Component({
  selector: 'app-home',
  imports: [
    DashboardComponent,
    EmsButtonDirective,
    FilterBarComponent,
    GridCanvasComponent,
    LoadProblemComponent,
    RouterLink,
    SkeletonComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (home.isLoading() && !home.hasValue()) {
      <main class="home" id="main-content">
        <app-skeleton height="6rem" />
        <span class="cdk-visually-hidden" role="status">Loading your home page</span>
      </main>
    } @else if (showStandard()) {
      <app-dashboard />
    } @else {
      <main class="home" id="main-content">
        <header class="home__header">
          <h1 tabindex="-1">{{ providerName() }}</h1>
          <div class="home__actions">
            @if (source() === 'persona') {
              <button
                emsButton="raised"
                type="button"
                [attr.aria-disabled]="busy() ? 'true' : null"
                (click)="customize()"
              >
                Customize this page
              </button>
            } @else {
              <a emsButton="raised" [routerLink]="['/saved-views', savedViewId()]"
                >Edit this page</a
              >
              @if (confirmingReset()) {
                <span class="home__confirm" role="alert"
                  >This deletes your copy and brings back the recommended page.</span
                >
                <button
                  emsButton="filled"
                  type="button"
                  #confirmReset
                  [attr.aria-disabled]="busy() ? 'true' : null"
                  (click)="reset()"
                >
                  Confirm reset
                </button>
                <button emsButton="flat" type="button" (click)="cancelReset()">Cancel</button>
              } @else {
                <button emsButton="flat" type="button" #startReset (click)="askToReset()">
                  Reset to the recommended page
                </button>
              }
            }
          </div>
        </header>
        @if (problem()) {
          <p class="home__problem" role="alert">{{ problem() }}</p>
        }
        <app-filter-bar
          label="Dashboard filters"
          [divisionUnavailable]="!anyDivisionSupport()"
          [divisionIgnoredBy]="divisionIgnoredBy()"
        />
        <h2 class="cdk-visually-hidden">Metrics on this page</h2>
        @if (layoutResource.error() !== undefined || catalogResource.error() !== undefined) {
          <app-load-problem
            [error]="layoutResource.error() ?? catalogResource.error()"
            subject="your home page"
            (retry)="retry()"
          />
        } @else if (layout() && catalog()) {
          <app-grid-canvas
            [widgets]="layout()!.widgets"
            [metrics]="metrics()"
            [editing]="false"
            [density]="layout()!.density"
          />
        } @else {
          <app-skeleton height="12rem" />
          <span class="cdk-visually-hidden" role="status">Loading your home page</span>
        }
      </main>
    }
  `,
  styles: `
    .home {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
    }
    .home__header {
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
    .home__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .home__confirm,
    .home__problem {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .home__problem {
      color: var(--ems-text-error);
    }
  `,
})
export class HomeComponent {
  private readonly auth = inject(AuthService);
  private readonly api = inject(HomeService);
  private readonly views = inject(SavedViewsService);
  private readonly router = inject(Router);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly busy = signal(false);
  protected readonly problem = signal('');
  protected readonly confirmingReset = signal(false);
  private readonly injector = inject(Injector);
  private readonly confirmButton = viewChild<ElementRef<HTMLButtonElement>>('confirmReset');
  private readonly startButton = viewChild<ElementRef<HTMLButtonElement>>('startReset');

  protected readonly home = rxResource<HomePageDto, string | null>({
    params: () => this.auth.activeProviderId(),
    stream: ({ params }) =>
      params ? this.api.get(params).pipe(catchError(() => of(STANDARD))) : of(STANDARD),
  });
  protected readonly source = computed(() =>
    this.home.hasValue() ? this.home.value().source : 'default',
  );
  protected readonly savedViewId = computed(() =>
    this.home.hasValue() ? this.home.value().savedViewId : null,
  );
  protected readonly providerName = computed(() => this.auth.activeProvider()?.name ?? '');

  protected readonly layoutResource = rxResource<
    SavedViewLayout | null,
    { providerId: string; home: HomePageDto } | null
  >({
    params: () => {
      const providerId = this.auth.activeProviderId();
      return providerId && this.home.hasValue() && this.home.value().source !== 'default'
        ? { providerId, home: this.home.value() }
        : null;
    },
    stream: ({ params }) => (params ? this.layoutOf(params.providerId, params.home) : of(null)),
  });
  protected readonly catalogResource = rxResource<CatalogDto | null, string | null>({
    params: () => (this.source() === 'default' ? null : this.auth.activeProviderId()),
    stream: ({ params }) => (params ? this.views.catalog(params) : of(null)),
  });
  protected readonly layout = computed(() =>
    this.layoutResource.hasValue() ? this.layoutResource.value() : null,
  );
  protected readonly catalog = computed(() =>
    this.catalogResource.hasValue() ? this.catalogResource.value() : null,
  );
  protected readonly metrics = computed<ReadonlyMap<string, CatalogMetricDto>>(
    () => new Map((this.catalog()?.metrics ?? []).map((m) => [m.key, m])),
  );

  /** The standard dashboard: nothing configured for this user, or their own copy could not be opened. */
  protected readonly showStandard = computed(
    () =>
      this.source() === 'default' ||
      (this.source() === 'custom' && this.layoutResource.error() !== undefined),
  );

  private readonly widgetMetrics = computed(() => {
    const keys = [...new Set((this.layout()?.widgets ?? []).map((w) => w.metricKey))];
    return keys.flatMap((key) => this.metrics().get(key) ?? []);
  });
  protected readonly anyDivisionSupport = computed(() =>
    this.widgetMetrics().some((m) => metricDivisionSupport(m) === 'supported'),
  );
  protected readonly divisionIgnoredBy = computed(() =>
    this.widgetMetrics()
      .filter((m) => !this.anyDivisionSupport() || metricDivisionSupport(m) === 'none')
      .map((m) => m.name),
  );

  protected retry(): void {
    this.layoutResource.reload();
    this.catalogResource.reload();
  }

  /** The button that was pressed goes away, so focus is moved to where the user is looking next. */
  protected askToReset(): void {
    this.confirmingReset.set(true);
    afterNextRender(() => this.confirmButton()?.nativeElement.focus(), { injector: this.injector });
  }

  protected cancelReset(): void {
    this.confirmingReset.set(false);
    afterNextRender(() => this.startButton()?.nativeElement.focus(), { injector: this.injector });
  }

  protected customize(): void {
    const providerId = this.auth.activeProviderId();
    if (!providerId || this.busy()) return;
    this.busy.set(true);
    this.problem.set('');
    this.api.customize(providerId).subscribe({
      next: ({ savedViewId }) => {
        this.busy.set(false);
        this.announcer.announce('This page is now your own copy.', 'polite');
        void this.router.navigate(['/saved-views', savedViewId]);
      },
      error: () => {
        this.busy.set(false);
        this.problem.set("We couldn't make your own copy. Please try again.");
      },
    });
  }

  protected reset(): void {
    const providerId = this.auth.activeProviderId();
    if (!providerId || this.busy()) return;
    this.busy.set(true);
    this.problem.set('');
    this.api.reset(providerId).subscribe({
      next: () => {
        this.busy.set(false);
        this.confirmingReset.set(false);
        this.announcer.announce('Your home page is back to the recommended page.', 'polite');
        this.home.reload();
      },
      error: () => {
        this.busy.set(false);
        this.problem.set("We couldn't reset your home page. Please try again.");
      },
    });
  }

  private layoutOf(providerId: string, home: HomePageDto): Observable<SavedViewLayout | null> {
    if (home.source === 'persona') return of(home.layout);
    return home.savedViewId === null
      ? of(null)
      : this.views.get(providerId, home.savedViewId).pipe(map((loaded) => loaded.view.layout));
  }
}
