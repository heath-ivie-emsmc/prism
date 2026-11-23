import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, effect, inject, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { EmsBreadcrumb, EmsBreadcrumbsComponent } from '@ems-management-consultants/ui';
import { MetricHelpComponent } from '../../shared/metric-help.component';
import { SkeletonComponent } from '../../shared/skeleton.component';
import { MetricWidgetComponent } from '../../saved-views/metric-widget.component';
import { LayoutWidget } from '../../saved-views/layout.model';
import { DRILL_TARGETS } from '../drill-targets';
import { metricPageRoute } from '../drill-link';
import { MetricCatalogService } from '../metric-catalog.service';
import { rendererFor } from '../metric-renderers';

const KEY = /^[a-z0-9_]{1,64}$/;

/**
 * The generic address of a Metric view, `/dashboard/metrics/:key`, with the month, Service Line, Division and drill parameters in the query string.
 * A Metric that has a page of its own (the breakdown, the payer mix and the payer breakdown) is sent there, replacing this address so Back does not stop here.
 * Any other Metric the catalog can draw is shown full width with its Context Badge, Data Through stamp and help text. An unknown key gets a calm "not found" message.
 * The heading takes focus on every navigation, and the router's scroll restoration returns to where the reader was on Back.
 */
@Component({
  selector: 'app-metric-view',
  imports: [RouterLink, EmsBreadcrumbsComponent, MetricHelpComponent, MetricWidgetComponent, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page">
      <ems-breadcrumbs class="page__crumbs" [items]="crumbs()" />
      <header class="page__header">
        <h1 #heading tabindex="-1">{{ title() }}</h1>
      </header>
      @switch (state()) {
        @case ('notFound') {
          <section class="app-panel" aria-labelledby="missing-title">
            <h2 id="missing-title">Nothing to show here</h2>
            <p>This view does not exist, or it needs more detail than the address gave.</p>
            <p><a routerLink="/dashboard">Back to the dashboard</a></p>
          </section>
        }
        @case ('view') {
          <section class="app-panel" [attr.aria-label]="metric()?.name">
            <p class="page__description">{{ metric()?.description }}</p>
            <div class="page__widget">
              <app-metric-widget [widget]="widget()!" [metric]="metric()!" />
            </div>
            <app-metric-help [metric]="key()" [serviceLine]="line()" />
          </section>
        }
        @default {
          <p class="cdk-visually-hidden" role="status">Loading</p>
          <app-skeleton height="12rem" />
        }
      }
    </main>
  `,
  styles: `
    .page {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
    }
    .page__crumbs {
      margin-top: 0.5rem;
    }
    @media (max-width: 899.98px) {
      .page {
        padding-block-start: 0.25rem;
      }
      .page__crumbs {
        margin-top: 0;
      }
    }
    h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    /* Focus moves here when the page opens. It is not a control, so no ring is drawn. */
    h1:focus {
      outline: none;
    }
    h2 {
      margin: 0;
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
      font-weight: var(--ems-font-weight-heavy);
    }
    .page__description {
      margin: 0 0 0.75rem;
      color: var(--ems-text-muted);
      line-height: var(--ems-line-height-body);
    }
    .page__widget {
      display: flex;
      flex-direction: column;
      min-height: 24rem;
      min-width: 0;
    }
  `,
})
export class MetricViewComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly catalog = inject(MetricCatalogService);
  private readonly injector = inject(Injector);
  private readonly headingRef = viewChild.required<ElementRef<HTMLElement>>('heading');

  private readonly params = toSignal(this.route.paramMap.pipe(map((p) => p.get('key') ?? '')), { initialValue: '' });
  private readonly query = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  /** The Metric key from the address, or empty when it is not a plausible key. */
  protected readonly key = computed(() => (KEY.test(this.params()) ? this.params() : ''));
  /** A key the SPA knows: one a Saved View can draw, or a drill target. Own properties only, so `constructor` and friends are unknown. */
  private readonly known = computed(() => this.key() !== '' && (Object.hasOwn(DRILL_TARGETS, this.key()) || rendererFor(this.key()) !== undefined));
  private readonly page = computed(() => (this.known() ? metricPageRoute(this.key(), (name) => this.query().get(name)) : null));
  protected readonly metric = computed(() => this.catalog.metrics().get(this.key()));
  protected readonly line = computed<'all' | 'air' | 'ground'>(() => {
    const line = this.query().get('line') ?? this.query().get('serviceLine');
    return line === 'air' || line === 'ground' ? line : 'all';
  });

  protected readonly state = computed<'loading' | 'view' | 'notFound'>(() => {
    if (!this.known() || (this.page() === null && Object.hasOwn(DRILL_TARGETS, this.key()))) return 'notFound';
    if (this.page() !== null) return 'loading';
    if (!this.catalog.loaded()) return 'loading';
    return this.metric() ? 'view' : 'notFound';
  });

  /** The widget that draws the Metric: its table when the catalog offers one, else its default presentation. Never saved, never carries figures. */
  protected readonly widget = computed<LayoutWidget | null>(() => {
    const metric = this.metric();
    if (!metric) return null;
    const presentation = metric.presentations.find((p) => p.key === 'table') ?? metric.presentations.find((p) => p.isDefault) ?? metric.presentations[0];
    return presentation ? { id: 'metric-view', metricKey: metric.key, presentation: presentation.key, x: 0, y: 0, w: 12, h: 4 } : null;
  });

  protected readonly title = computed(() => (this.state() === 'notFound' ? 'View not found' : (this.metric()?.name ?? 'Loading')));
  protected readonly crumbs = computed<EmsBreadcrumb[]>(() => [{ label: 'Dashboard', link: '/dashboard', icon: 'home' }, { label: this.title() }]);

  constructor() {
    // A Metric with a page of its own is opened there, replacing this address.
    effect(() => {
      const page = this.page();
      if (page) untracked(() => void this.router.navigate([...page.commands], { queryParams: page.queryParams, replaceUrl: true }));
    });
    // Following a link moves focus to the heading, so a screen reader starts at the new page and not where the link was. Only the key is tracked.
    effect(() => {
      this.key();
      untracked(() => afterNextRender(() => this.headingRef().nativeElement.focus({ preventScroll: true }), { injector: this.injector }));
    });
  }
}
