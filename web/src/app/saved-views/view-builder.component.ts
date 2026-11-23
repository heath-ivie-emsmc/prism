import { metricDivisionSupport } from '../metrics/metric-catalog.service';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { BreakpointObserver } from '@angular/cdk/layout';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, HostListener, computed, effect, inject, signal, untracked, viewChild, ElementRef } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { MatMenuModule } from '@angular/material/menu';
import { EmsBreadcrumb, EmsBreadcrumbsComponent, EmsButtonDirective, EmsChipComponent, EmsDialog, EmsIconComponent, EmsMessageComponent } from '@ems-management-consultants/ui';
import { Observable, map, of } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { SnapshotCaption, captionLines, imageFileName, renderSnapshot, saveBlob } from './view-image';
import { apPeriodLabel } from '../dashboard/dashboard-format';
import { FilterBarComponent } from '../dashboard/filter-bar.component';
import { SegmentOption, SegmentedControlComponent } from '../dashboard/segmented-control.component';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { describeProblem } from '../shared/load-problem';
import { announce } from './announcements';
import { GridCanvasComponent, GridDrop } from './grid-canvas.component';
import { addWidget, autoArrange, moveWidget, newWidgetId, presetSize, readingOrder, removeWidget, reorder, resizeWidget } from './grid-layout';
import {
  CatalogDto,
  CatalogMetricDto,
  Density,
  GRID_COLUMNS,
  LayoutWidget,
  MAX_DESCRIPTION,
  MAX_VIEW_NAME,
  MAX_WIDGETS,
  SavedViewRequest,
  SizePreset,
  LAYOUT_VERSION,
} from './layout.model';
import { WidgetStatus } from './metric-widget.component';
import { MetricPaletteComponent } from './metric-palette.component';
import { PaletteDialogComponent } from './palette-dialog.component';
import { LoadedView, SavedViewsService } from './saved-views.service';
import { ConflictChoice, ConflictDialogComponent, NameDialogComponent } from './view-dialogs.component';
import { ShareDialogComponent } from './share-dialog.component';
import { SettingsChange, WidgetSettingsComponent } from './widget-settings.component';
import { WidgetAction } from './widget-host.component';

const MANAGE = 'prism.views.manage';
const HISTORY_LIMIT = 50;

type Mode = 'view' | 'edit';
interface MoveState {
  readonly id: string;
  readonly origin: { readonly x: number; readonly y: number };
}
interface Step {
  readonly label: string;
  readonly widgets: readonly LayoutWidget[];
}

/**
 * One Saved View: opened to read, or to arrange when its owner presses Edit. The left side is the Metric palette (or a widget's settings), the right side the grid.
 * Every change goes through the grid rules, is announced, and can be undone. Nothing is stored until Save, and leaving with unsaved changes asks first.
 * The page filters are shared with the dashboard and are never saved with a view.
 */
@Component({
  selector: 'app-view-builder',
  imports: [
    EmsBreadcrumbsComponent,
    EmsButtonDirective,
    EmsChipComponent,
    EmsIconComponent,
    EmsMessageComponent,
    FilterBarComponent,
    GridCanvasComponent,
    LoadProblemComponent,
    MatMenuModule,
    MetricPaletteComponent,
    SegmentedControlComponent,
    WidgetSettingsComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="builder" id="main-content">
      <a class="skip" href="#view-canvas" (click)="skipTo($event, 'view-canvas')">Skip to canvas</a>
      @if (editing()) {
        <a class="skip" href="#builder-pane" (click)="skipTo($event, 'builder-pane')">Skip to metric palette</a>
      }
      <ems-breadcrumbs class="builder__crumbs" [items]="crumbs()" />

      @if (notFound()) {
        <section class="builder__problem">
          <h1 #pageHeading tabindex="-1">We couldn't find this view for your account</h1>
          <p>It may have been deleted, or it's no longer shared with you.</p>
          <button emsButton="filled" type="button" (click)="back()">Back to saved views</button>
        </section>
      } @else if (loadError() !== undefined) {
        <section class="builder__problem">
          <h1 #pageHeading tabindex="-1">We can't load this view right now</h1>
          <app-load-problem [error]="loadError()" subject="this view" (retry)="reloadAll()" />
        </section>
      } @else {
        <header class="builder__header">
          <div class="builder__title">
            <h1 #pageHeading tabindex="-1">{{ heading() }}</h1>
            <p class="builder__subtitle">
              @if (filters.dataThrough()) { Data through {{ filters.dataThrough() }} } @else { &nbsp; }
            </p>
            <p class="builder__chips">
              @if (dirty()) { <ems-chip state="warn" icon="edit">Unsaved changes</ems-chip> }
              @if (!isOwner() && ownerName()) { <ems-chip state="info" icon="share">Shared by {{ ownerName() }}</ems-chip> }
            </p>
          </div>
          <div class="builder__actions">
            @if (editing()) {
              <button emsButton="filled" type="button" [attr.aria-disabled]="saving() ? 'true' : null" (click)="save()">Save</button>
              <button emsButton="raised" type="button" [attr.aria-disabled]="widgets().length < 2 ? 'true' : null" (click)="arrange()">Auto arrange</button>
              <button emsButton="raised" type="button" [attr.aria-label]="'Undo' + (past().length ? ': ' + past()[past().length - 1]!.label : '')" [attr.aria-disabled]="past().length === 0 ? 'true' : null" (click)="undo()">Undo</button>
              <button emsButton="raised" type="button" [attr.aria-disabled]="future().length === 0 ? 'true' : null" (click)="redo()">Redo</button>
              <button emsButton="flat" type="button" (click)="done()">Done</button>
              <button emsButton="flat" type="button" (click)="discard()">Discard</button>
            } @else if (isOwner() && canManage()) {
              <button emsButton="filled" type="button" (click)="edit()">Edit</button>
              <button emsButton="raised" type="button" (click)="share()">Share</button>
              <button emsButton="flat" fab color="neutral" type="button" aria-label="More actions" [matMenuTriggerFor]="more"><ems-icon name="more_vert" /></button>
              <mat-menu #more="matMenu">
                <button mat-menu-item type="button" (click)="saveCopy()">Save a copy</button>
                <button mat-menu-item type="button" (click)="remove()">Delete view</button>
              </mat-menu>
            } @else if (canManage()) {
              <button emsButton="raised" type="button" (click)="saveCopy()">Save a copy</button>
            }
            @if (!editing() && widgets().length > 0) {
              <button emsButton="raised" type="button" id="download-image" [attr.aria-disabled]="capturing() ? 'true' : null" (click)="downloadImage()">Download image</button>
            }
          </div>
        </header>

        @if (saveError()) {
          <ems-message state="error">{{ saveError() }}</ems-message>
        }
        @if (!isOwner() && !editing()) {
          <ems-message state="neutral">Shared by {{ ownerName() ?? 'a colleague' }}. You can view it or save your own copy. Figures show the data you have access to.</ems-message>
        }

        <app-filter-bar label="View filters" [divisionUnavailable]="!anyDivisionSupport()" [divisionIgnoredBy]="divisionIgnoredBy()" />
        <p class="builder__hint">Filters apply to every metric on this page. They aren't saved with the view.</p>

        @if (editing()) {
          <form class="builder__settings" (submit)="$event.preventDefault()" aria-label="View settings">
            <div class="field">
              <label for="view-name">View name</label>
              <input id="view-name" type="text" autocomplete="off" [maxLength]="maxName" [value]="name()" [attr.aria-invalid]="nameError() ? 'true' : null" aria-describedby="name-hint name-error" (input)="name.set($any($event.target).value); nameError.set('')" />
              <p id="name-hint" class="hint">Up to {{ maxName }} characters.</p>
              <p id="name-error" class="error" [attr.role]="nameError() ? 'alert' : null">{{ nameError() }}</p>
            </div>
            <div class="field">
              <label for="view-description">Description (optional)</label>
              <input id="view-description" type="text" autocomplete="off" [maxLength]="maxDescription" [value]="description()" (input)="description.set($any($event.target).value)" />
            </div>
            <div class="field">
              <app-segmented-control label="Density" [options]="densityOptions" [value]="density()" (valueChange)="setDensity($event)" />
            </div>
          </form>
        }

        <div class="builder__body" [class.builder__body--docked]="docked() && editing()">
          @if (editing() && catalog(); as catalog) {
            <aside id="builder-pane" tabindex="-1" class="builder__pane" [attr.aria-label]="settingsWidget() ? 'Widget settings' : 'Metric palette'">
              @if (settingsWidget(); as widget) {
                @if (docked()) {
                  <app-widget-settings [widget]="widget" [metric]="metricOf(widget)" [title]="titleOf(widget)" (settingsChange)="applySetting(widget, $event)" (done)="closeSettings()" />
                }
              } @else if (docked()) {
                <h2>Metric palette</h2>
                <app-metric-palette [catalog]="catalog" [canAdd]="canAddMore()" [dragEnabled]="gridMode()" (add)="addMetric($event)" (blocked)="blocked($event)" (searched)="searched($event)" (dragMoved)="ghostFor($event)" (dragEnded)="canvasRef()?.hideGhost()" />
              } @else {
                <button emsButton="filled" type="button" id="open-palette" (click)="openPalette()">Add metrics</button>
              }
            </aside>
          }
          <section class="builder__canvas" [class.builder__canvas--capturing]="capturing()" #snapshot aria-labelledby="canvas-heading">
            @if (capturing()) {
              <div class="snapshot-caption" aria-hidden="true">
                @for (line of captionText(); track $index) {
                  <p [class.snapshot-caption__title]="$index === 0">{{ line }}</p>
                }
              </div>
            }
            <h2 id="canvas-heading" class="cdk-visually-hidden">Metrics on this view</h2>
            @if (!docked() && settingsWidget(); as widget) {
              <div class="builder__inlinesettings">
                <app-widget-settings [widget]="widget" [metric]="metricOf(widget)" [title]="titleOf(widget)" (settingsChange)="applySetting(widget, $event)" (done)="closeSettings()" />
              </div>
            }
            @if (catalog()) {
              <app-grid-canvas
                #canvas
                [widgets]="widgets()"
                [metrics]="metrics()"
                [editing]="editing()"
                [density]="density()"
                [movingId]="moving()?.id ?? null"
                (action)="onAction($event.id, $event.action)"
                (gridDrop)="onDrop($event)"
                (addRequested)="requestAdd()"
                (status)="onStatus($event.id, $event.status)"
                (gridModeChange)="gridMode.set($event)"
              />
            } @else if (catalogLoading() || viewLoading()) {
              <p class="builder__hint" role="status">Loading…</p>
            }
          </section>
        </div>
      }
    </main>
  `,
  styles: `
    .builder {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
    }
    .builder__crumbs {
      margin-top: 0.5rem;
    }
    @media (max-width: 899.98px) {
      .builder {
        padding-block-start: 0.25rem;
      }
      .builder__crumbs {
        margin-top: 0;
      }
    }
    .skip {
      position: absolute;
      inset-inline-start: -999rem;
    }
    .skip:focus {
      position: static;
      padding: 0.5rem;
      font-weight: 700;
    }
    .builder__header {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: 1rem 1.5rem;
    }
    .builder__title h1,
    .builder__problem h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
      overflow-wrap: anywhere;
    }
    .builder__subtitle {
      margin: 0.25rem 0 0;
      color: var(--ems-text-muted);
      line-height: var(--ems-line-height-body);
    }
    .builder__chips {
      margin: 0.25rem 0 0;
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .builder__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .builder__hint,
    .hint {
      margin: 0;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    .builder__settings {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
      gap: 1rem;
      align-items: start;
    }
    .field label {
      display: block;
      margin-block-end: 0.25rem;
      font-weight: 600;
    }
    .field input {
      width: 100%;
      box-sizing: border-box;
      min-height: 2.5rem;
      padding: 0 0.75rem;
      font: inherit;
      color: var(--ems-text-default);
      background: var(--ems-surface-neutral-default);
      border: 1px solid var(--ems-border-neutral-strong, currentColor);
      border-radius: 4px;
    }
    .error {
      margin: 0.25rem 0 0;
      min-height: 1.25rem;
      color: var(--ems-text-error-default);
    }
    .builder__body {
      display: grid;
      gap: 1rem;
      align-items: start;
    }
    .builder__body--docked {
      grid-template-columns: 18rem minmax(0, 1fr);
    }
    .builder__pane {
      min-width: 0;
    }
    .builder__pane h2 {
      margin: 0 0 0.5rem;
      font-size: var(--ems-font-size-subheading);
    }
    .builder__canvas {
      min-width: 0;
    }
    .builder__canvas--capturing {
      padding: 1rem;
    }
    .snapshot-caption {
      margin-block-end: 1rem;
      padding: 0.5rem 0;
      border-block-end: 1px solid var(--ems-border-neutral-default, currentColor);
    }
    .snapshot-caption p {
      margin: 0 0 0.125rem;
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
    }
    .snapshot-caption .snapshot-caption__title {
      color: var(--ems-text-default);
      font-size: var(--ems-font-size-subheading);
      font-weight: var(--ems-font-weight-heavy);
    }
    .builder__inlinesettings {
      margin-block-end: 1rem;
      padding: 1rem;
      background: var(--ems-surface-neutral-elevated);
      border-radius: 8px;
    }
    @media (forced-colors: active) {
      .builder__inlinesettings {
        border: 1px solid CanvasText;
      }
    }
  `,
})
export class ViewBuilderComponent {
  private readonly auth = inject(AuthService);
  protected readonly filters = inject(DashboardFilterContext);
  private readonly api = inject(SavedViewsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialogs = inject(EmsDialog);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly titleService = inject(Title);
  private readonly heading$ = viewChild<ElementRef<HTMLElement>>('pageHeading');
  protected readonly canvasRef = viewChild(GridCanvasComponent);

  protected readonly maxName = MAX_VIEW_NAME;
  protected readonly maxDescription = MAX_DESCRIPTION;
  protected readonly densityOptions: readonly SegmentOption<Density>[] = [
    { value: 'comfortable', label: 'Comfortable' },
    { value: 'compact', label: 'Compact' },
  ];

  // Route and permissions.
  private readonly routeId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: this.route.snapshot.paramMap.get('id') });
  protected readonly isNew = computed(() => this.routeId() === null);
  private readonly viewId = computed(() => (this.routeId() !== null && /^\d{1,9}$/u.test(this.routeId()!) ? Number(this.routeId()) : null));
  protected readonly canManage = computed(() => this.auth.me()?.permissions.includes(MANAGE) ?? false);
  protected readonly docked = toSignal(inject(BreakpointObserver).observe('(min-width: 75rem)').pipe(map((s) => s.matches)), { initialValue: false });
  protected readonly gridMode = signal(true);

  // What the API serves.
  private readonly catalogResource = rxResource<CatalogDto | null, string | null>({
    params: () => this.filters.providerId(),
    stream: ({ params }) => (params ? this.api.catalog(params) : of(null)),
  });
  protected readonly catalog = computed(() => (this.catalogResource.hasValue() ? this.catalogResource.value() : null));
  protected readonly catalogLoading = computed(() => this.catalogResource.isLoading());
  protected readonly metrics = computed<ReadonlyMap<string, CatalogMetricDto>>(() => new Map((this.catalog()?.metrics ?? []).map((m) => [m.key, m])));
  private readonly widgetMetrics = computed(() => {
    const keys = [...new Set(this.widgets().map((w) => w.metricKey))];
    return keys.flatMap((key) => this.metrics().get(key) ?? []);
  });
  protected readonly anyDivisionSupport = computed(() => this.widgetMetrics().some((m) => metricDivisionSupport(m) === 'supported'));
  protected readonly divisionIgnoredBy = computed(() => this.widgetMetrics().filter((m) => !this.anyDivisionSupport() || metricDivisionSupport(m) === 'none').map((m) => m.name));

  private readonly viewResource = rxResource<LoadedView | null, { providerId: string; id: number } | null>({
    params: () => {
      const providerId = this.filters.providerId();
      const id = this.viewId();
      return providerId && id !== null ? { providerId, id } : null;
    },
    stream: ({ params }) => (params ? this.api.get(params.providerId, params.id) : of(null)),
  });
  protected readonly viewLoading = computed(() => this.viewResource.isLoading());
  protected readonly loadError = computed<unknown>(() => {
    const error = this.catalogResource.error() ?? this.viewResource.error();
    return error !== undefined && !this.isNotFoundError(error) ? error : undefined;
  });
  protected readonly notFound = computed(() => (this.routeId() !== null && this.viewId() === null) || this.isNotFoundError(this.viewResource.error()));

  // The draft.
  protected readonly mode = signal<Mode>('view');
  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly density = signal<Density>('comfortable');
  protected readonly widgets = signal<readonly LayoutWidget[]>([]);
  protected readonly ownerName = signal<string | null>(null);
  protected readonly isOwner = signal(true);
  protected readonly nameError = signal('');
  protected readonly saveError = signal('');
  protected readonly saving = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly moving = signal<MoveState | null>(null);
  protected readonly past = signal<readonly Step[]>([]);
  protected readonly future = signal<readonly Step[]>([]);
  private readonly savedId = signal<number | null>(null);
  private readonly etag = signal('');
  private readonly baseline = signal('');
  private dirtyAnnounced = false;

  protected readonly editing = computed(() => this.mode() === 'edit');
  private readonly snapshot = computed(() => JSON.stringify([this.name(), this.description(), this.density(), [...this.widgets()].sort((a, b) => a.id.localeCompare(b.id))]));
  protected readonly dirty = computed(() => this.editing() && this.snapshot() !== this.baseline());
  protected readonly canAddMore = computed(() => this.widgets().length < MAX_WIDGETS);
  protected readonly heading = computed(() => (this.editing() ? this.name().trim() || 'New view' : this.name() || 'Saved view'));
  protected readonly settingsWidget = computed(() => this.widgets().find((w) => w.id === this.selectedId() && this.editing()) ?? null);
  protected readonly crumbs = computed<EmsBreadcrumb[]>(() => [{ label: 'Saved views', route: '/saved-views' }, { label: this.heading() }]);

  // One announcement for a filter change, not one per widget.
  private readonly statuses = signal<ReadonlyMap<string, WidgetStatus>>(new Map());
  private pendingFilterAnnouncement = false;

  constructor() {
    effect(() => {
      const loaded = this.viewResource.hasValue() ? this.viewResource.value() : null;
      if (loaded) {
        untracked(() => this.hydrate(loaded));
      }
    });
    effect(() => {
      if (this.isNew() && this.mode() === 'view' && this.canManage() && this.baseline() === '') {
        untracked(() => this.startNew());
      }
    });
    effect(() => {
      this.titleService.setTitle(`${this.dirty() ? '• ' : ''}${this.heading()} · Saved views · Prism`);
    });
    effect(() => {
      const key = `${this.filters.endPeriod()}|${this.filters.serviceLine()}`;
      untracked(() => this.filtersChanged(key));
    });
    effect(() => {
      const all = [...this.statuses().values()];
      if (this.pendingFilterAnnouncement && all.length > 0 && !all.includes('loading')) {
        untracked(() => this.announceFilterResult(all));
      }
    });
    effect(() => {
      // Moving focus to the new page's h1 on arrival is the screen reader's cue that the page changed.
      if (this.heading$() && this.routeId() !== undefined) {
        untracked(() => setTimeout(() => this.heading$()?.nativeElement.focus({ preventScroll: true })));
      }
    });
  }

  /** Read by the route guard: true while there is something unsaved to lose. */
  hasUnsavedChanges(): boolean {
    return this.dirty();
  }

  /** The leave-without-saving question, for the route guard. */
  confirmLeave(): Observable<boolean> {
    return this.dialogs.confirm({
      heading: 'Leave without saving?',
      message: `You have unsaved changes to "${this.name().trim() || 'this view'}".`,
      confirmLabel: 'Discard changes',
      cancelLabel: 'Keep editing',
      danger: true,
    });
  }

  @HostListener('window:beforeunload', ['$event'])
  protected warnBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty()) {
      event.preventDefault();
    }
  }

  // --- loading and modes ---

  private isNotFoundError(error: unknown): boolean {
    return error instanceof HttpErrorResponse ? error.status === 404 : (error as { cause?: unknown } | undefined)?.cause instanceof HttpErrorResponse && (error as { cause: HttpErrorResponse }).cause.status === 404;
  }

  private hydrate(loaded: LoadedView): void {
    const view = loaded.view;
    this.savedId.set(view.id);
    this.etag.set(loaded.etag);
    this.isOwner.set(view.isOwner);
    this.ownerName.set(view.ownerName);
    this.name.set(view.name);
    this.description.set(view.description ?? '');
    this.density.set(view.layout.density === 'compact' ? 'compact' : 'comfortable');
    this.widgets.set(view.layout.widgets);
    this.past.set([]);
    this.future.set([]);
    this.selectedId.set(null);
    this.baseline.set(this.snapshot());
    if (this.mode() === 'edit') {
      this.mode.set('view');
    }
  }

  private startNew(): void {
    this.mode.set('edit');
    this.name.set('');
    this.widgets.set([]);
    this.baseline.set(this.snapshot());
    this.announcer.announce(announce.editing('a new view'), 'polite');
  }

  protected edit(): void {
    this.mode.set('edit');
    this.announcer.announce(announce.editing(this.name()), 'polite');
    this.dirtyAnnounced = false;
  }

  protected done(): void {
    if (!this.dirty()) {
      this.leaveEditing();
      return;
    }
    this.confirmLeave().subscribe((leave) => leave && this.discardNow());
  }

  protected discard(): void {
    if (!this.dirty()) {
      this.discardNow();
      return;
    }
    this.dialogs.confirm({ heading: 'Discard changes?', message: 'Your changes since the last save will be lost.', confirmLabel: 'Discard changes', cancelLabel: 'Keep editing', danger: true }).subscribe((ok) => ok && this.discardNow());
  }

  private discardNow(): void {
    this.announcer.announce(announce.discarded(), 'polite');
    if (this.savedId() === null) {
      void this.router.navigateByUrl('/saved-views');
      return;
    }
    const loaded = this.viewResource.hasValue() ? this.viewResource.value() : null;
    if (loaded) {
      this.hydrate(loaded);
    }
    this.leaveEditing();
  }

  private leaveEditing(): void {
    this.mode.set('view');
    this.selectedId.set(null);
    this.moving.set(null);
    this.saveError.set('');
    this.announcer.announce(announce.viewing(this.name()), 'polite');
    setTimeout(() => document.querySelector<HTMLElement>('.builder__actions button')?.focus());
  }

  protected back(): void {
    void this.router.navigateByUrl('/saved-views');
  }

  protected reloadAll(): void {
    this.catalogResource.reload();
    this.viewResource.reload();
  }

  protected skipTo(event: Event, id: string): void {
    event.preventDefault();
    document.getElementById(id)?.focus({ preventScroll: false });
    if (id === 'view-canvas') {
      document.querySelector<HTMLElement>('#view-canvas .host__tool, #empty-add')?.focus();
    }
  }

  // --- changing the layout ---

  private commit(label: string, next: readonly LayoutWidget[]): void {
    this.past.update((past) => [...past.slice(-(HISTORY_LIMIT - 1)), { label, widgets: this.widgets() }]);
    this.future.set([]);
    this.widgets.set(next);
    if (!this.dirtyAnnounced && this.snapshot() !== this.baseline()) {
      this.dirtyAnnounced = true;
      this.announcer.announce(announce.dirty(), 'polite');
    }
  }

  /** Moves every metric to the first free spot, in reading order, so gaps beside and below are filled. One undo step. */
  protected arrange(): void {
    const current = this.widgets();
    if (current.length < 2) {
      return;
    }
    const next = autoArrange(current);
    const changed = next.some((w, i) => w.x !== current[i]!.x || w.y !== current[i]!.y);
    if (!changed) {
      this.announcer.announce(announce.alreadyTidy(), 'polite');
      return;
    }
    this.commit('auto arrange', next);
    this.announcer.announce(announce.arranged(next.length), 'polite');
  }

  protected readonly capturing = signal(false);
  protected readonly captionText = signal<string[]>([]);
  private readonly snapshotRef = viewChild<ElementRef<HTMLElement>>('snapshot');

  /** Saves what is on screen as a PNG, with a caption that says which filters it shows. Nothing is sent to the server. */
  protected async downloadImage(): Promise<void> {
    const node = this.snapshotRef()?.nativeElement;
    if (this.capturing() || !node) {
      return;
    }
    const states = [...this.statuses().values()];
    if (states.includes('loading')) {
      this.announcer.announce(announce.imageWaiting(), 'polite');
      return;
    }
    const failed = states.filter((s) => s === 'failed').length;
    if (failed > 0) {
      this.announcer.announce(announce.imageBlocked(failed), 'assertive');
      this.saveError.set(announce.imageBlocked(failed));
      return;
    }
    const now = new Date();
    this.captionText.set(captionLines(this.caption(now)));
    this.capturing.set(true);
    try {
      await this.settleFrames();
      const blob = await renderSnapshot(node);
      const fileName = imageFileName(this.name(), now);
      saveBlob(blob, fileName);
      this.saveError.set('');
      this.announcer.announce(announce.imageSaved(fileName), 'polite');
    } catch {
      this.saveError.set(announce.imageFailed());
      this.announcer.announce(announce.imageFailed(), 'assertive');
    } finally {
      this.capturing.set(false);
    }
  }

  private caption(now: Date): SnapshotCaption {
    const filters = [
      `Period ${this.filters.endPeriod() ? apPeriodLabel(this.filters.endPeriod()) : 'not chosen'}`,
      this.filters.serviceLine() === 'both' ? 'Air and ground' : this.filters.serviceLine() === 'air' ? 'Air' : 'Ground',
    ];
    if (this.anyDivisionSupport()) {
      const division = this.filters.divisionOptions().find((o) => o.value === this.filters.selectedDivision());
      filters.push(`${this.filters.labels().reportingDivision}: ${division?.label ?? ''}`);
    }
    return {
      viewName: this.name(),
      providerName: this.filters.providerName(),
      filters,
      dataThrough: this.filters.dataThrough(),
      takenAt: now.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }),
    };
  }

  /** Two animation frames: enough for the caption to be drawn before the picture is taken. */
  private settleFrames(): Promise<void> {
    return new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
  }

  protected undo(): void {
    const step = this.past().at(-1);
    if (!step) {
      return;
    }
    this.future.update((future) => [...future, { label: step.label, widgets: this.widgets() }]);
    this.past.update((past) => past.slice(0, -1));
    this.widgets.set(step.widgets);
    this.announcer.announce(announce.undone(step.label), 'polite');
  }

  protected redo(): void {
    const step = this.future().at(-1);
    if (!step) {
      return;
    }
    this.past.update((past) => [...past, { label: step.label, widgets: this.widgets() }]);
    this.future.update((future) => future.slice(0, -1));
    this.widgets.set(step.widgets);
    this.announcer.announce(announce.redone(step.label), 'polite');
  }

  @HostListener('keydown', ['$event'])
  protected canvasKey(event: KeyboardEvent): void {
    const target = event.target as HTMLElement;
    if (!target.closest?.('.builder__canvas, .builder__pane')) {
      return;
    }
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z' && !['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) && this.editing()) {
      event.preventDefault();
      if (event.shiftKey) {
        this.redo();
      } else {
        this.undo();
      }
    }
  }

  protected metricOf(widget: LayoutWidget): CatalogMetricDto {
    return this.metrics().get(widget.metricKey)!;
  }

  protected titleOf(widget: LayoutWidget): string {
    const same = this.widgets().filter((w) => w.metricKey === widget.metricKey);
    const name = this.metricOf(widget).name;
    return same.length > 1 ? `${name} (${readingOrder(same).findIndex((w) => w.id === widget.id) + 1} of ${same.length})` : name;
  }

  private bounds(widget: LayoutWidget) {
    const presentation = this.metricOf(widget).presentations.find((p) => p.key === widget.presentation);
    return { minWidth: presentation?.minWidth ?? 1, minHeight: presentation?.minHeight ?? 1, maxWidth: presentation?.maxWidth ?? GRID_COLUMNS, maxHeight: presentation?.maxHeight ?? 12 };
  }

  protected addMetric(metric: CatalogMetricDto, at?: { x: number; y: number }): void {
    const presentation = metric.presentations.find((p) => p.isDefault) ?? metric.presentations[0];
    if (!presentation) {
      return;
    }
    const bounds = { minWidth: presentation.minWidth, minHeight: presentation.minHeight, maxWidth: presentation.maxWidth, maxHeight: presentation.maxHeight };
    const preset: SizePreset = (presentation.sizes.includes('M') ? 'M' : (presentation.sizes[0] ?? 'M')) as SizePreset;
    const size = presetSize(preset, bounds);
    const id = newWidgetId(this.widgets());
    const added = addWidget(this.widgets(), { id, metricKey: metric.key, presentation: presentation.key, w: size.w, h: size.h });
    if (!added) {
      this.announcer.announce(this.canAddMore() ? announce.noRoom(metric.name) : announce.limitReached(metric.name, MAX_WIDGETS), 'polite');
      return;
    }
    const placed = at ? moveWidget(added, id, at.x, at.y).widgets : added;
    this.commit(`add ${metric.name}`, placed);
    const widget = placed.find((w) => w.id === id)!;
    this.announcer.announce(announce.added(metric.name, widget.x, widget.y, preset, widget.w, placed.length), 'polite');
    this.focusWidget(id);
  }

  protected blocked(event: { metric: CatalogMetricDto; reason: 'planned' | 'full' }): void {
    this.announcer.announce(event.reason === 'planned' ? announce.planned(event.metric.name) : announce.limitReached(event.metric.name, MAX_WIDGETS), 'polite');
  }

  protected searched(event: { count: number; query: string }): void {
    this.announcer.announce(announce.searchResults(event.count, event.query), 'polite');
  }

  protected ghostFor(event: { metric: CatalogMetricDto; point: { x: number; y: number } }): void {
    const presentation = event.metric.presentations.find((p) => p.isDefault) ?? event.metric.presentations[0];
    const size = presentation ? presetSize('M', { minWidth: presentation.minWidth, minHeight: presentation.minHeight, maxWidth: presentation.maxWidth, maxHeight: presentation.maxHeight }) : { w: 6, h: 3 };
    this.canvasRef()?.showGhostFor(event.point, size);
  }

  protected requestAdd(): void {
    if (this.docked()) {
      document.getElementById('palette-search')?.focus();
    } else {
      this.openPalette();
    }
  }

  protected openPalette(): void {
    const catalog = this.catalog();
    if (!catalog) {
      return;
    }
    const ref = this.dialogs.open(PaletteDialogComponent, {
      data: { catalog, canAdd: this.canAddMore, add: (m: CatalogMetricDto) => this.addMetric(m), blocked: (e: { metric: CatalogMetricDto; reason: 'planned' | 'full' }) => this.blocked(e), searched: (e: { count: number; query: string }) => this.searched(e) },
      autoFocus: 'first-tabbable',
    });
    ref.afterClosed().subscribe(() => setTimeout(() => document.getElementById('open-palette')?.focus()));
  }

  private focusWidget(id: string): void {
    setTimeout(() => document.querySelector<HTMLElement>(`[data-widget-id="${CSS.escape(id)}"] .host__tool`)?.focus());
  }

  protected onDrop(drop: GridDrop): void {
    if (drop.metric) {
      this.addMetric(drop.metric, { x: drop.x, y: drop.y });
    } else if (drop.widgetId) {
      this.moveTo(drop.widgetId, drop.x, drop.y, 'move');
    }
  }

  private moveTo(id: string, x: number, y: number, label: string): number {
    const widget = this.widgets().find((w) => w.id === id);
    if (!widget) {
      return 0;
    }
    const result = moveWidget(this.widgets(), id, x, y);
    this.commit(`${label} ${this.titleOf(widget)}`, result.widgets);
    return result.pushed;
  }

  protected onAction(id: string, action: WidgetAction): void {
    const widget = this.widgets().find((w) => w.id === id);
    if (!widget) {
      return;
    }
    switch (action.kind) {
      case 'move-start':
        this.moving.set({ id, origin: { x: widget.x, y: widget.y } });
        this.announcer.announce(announce.moveStart(this.titleOf(widget), widget.x, widget.y), 'polite');
        break;
      case 'move-key':
        this.moveKey(widget, action.event);
        break;
      case 'move-blur':
        this.cancelMove(widget);
        break;
      case 'preset':
        this.applyPreset(widget, action.preset);
        break;
      case 'presentation':
        this.applySetting(widget, { kind: 'presentation', key: action.key });
        break;
      case 'settings':
        this.selectedId.set(id);
        setTimeout(() => document.getElementById('settings-heading')?.focus());
        break;
      case 'remove':
        this.removeWidget(widget);
        break;
      case 'reorder':
        this.commit(`reorder ${this.titleOf(widget)}`, reorder(this.widgets(), id, action.direction));
        break;
    }
  }

  private moveKey(widget: LayoutWidget, event: KeyboardEvent): void {
    const state = this.moving();
    if (!state) {
      return;
    }
    const step: Record<string, [number, number]> = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1], PageUp: [0, -4], PageDown: [0, 4] };
    const title = this.titleOf(widget);
    if (event.key === 'Escape') {
      event.preventDefault();
      this.cancelMove(widget);
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.moving.set(null);
      this.announcer.announce(announce.dropped(title, widget.x, widget.y), 'polite');
    } else if (event.key === 'Tab') {
      this.cancelMove(widget);
    } else if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault();
      this.stepMove(widget, event.key === 'Home' ? -GRID_COLUMNS : GRID_COLUMNS, 0, title);
    } else if (step[event.key]) {
      event.preventDefault();
      this.stepMove(widget, step[event.key]![0], step[event.key]![1], title);
    }
  }

  private stepMove(widget: LayoutWidget, dx: number, dy: number, title: string): void {
    const nextX = widget.x + dx;
    const nextY = widget.y + dy;
    const clamped = Math.min(Math.max(nextX, 0), GRID_COLUMNS - widget.w);
    if (dx < 0 && nextX < 0 && widget.x === 0) {
      this.announcer.announce(announce.edgeLeft(), 'polite');
      return;
    }
    if (dx > 0 && widget.x === GRID_COLUMNS - widget.w) {
      this.announcer.announce(announce.edgeRight(title, widget.w), 'polite');
      return;
    }
    if (dy < 0 && nextY < 0 && widget.y === 0) {
      this.announcer.announce(announce.edgeTop(), 'polite');
      return;
    }
    // A move in progress is one undo step: the first key press records it and later ones replace it.
    const before = this.past().at(-1);
    const sameMove = before?.label === `move ${title}` && this.moving()?.id === widget.id && this.moveStepped;
    const result = moveWidget(this.widgets(), widget.id, clamped, Math.max(0, nextY));
    if (sameMove) {
      this.widgets.set(result.widgets);
    } else {
      this.commit(`move ${title}`, result.widgets);
      this.moveStepped = true;
    }
    const moved = result.widgets.find((w) => w.id === widget.id)!;
    this.announcer.announce(announce.moveStep(moved.x, moved.y, result.pushed), 'polite');
  }

  private moveStepped = false;

  private cancelMove(widget: LayoutWidget): void {
    const state = this.moving();
    if (!state) {
      return;
    }
    this.moving.set(null);
    this.moveStepped = false;
    const last = this.past().at(-1);
    if (last && (widget.x !== state.origin.x || widget.y !== state.origin.y)) {
      this.past.update((past) => past.slice(0, -1));
      this.widgets.set(last.widgets);
    }
    this.announcer.announce(announce.moveCancelled(this.titleOf(widget), state.origin.x, state.origin.y), 'polite');
  }

  private applyPreset(widget: LayoutWidget, preset: SizePreset): void {
    const size = presetSize(preset, this.bounds(widget));
    this.commit(`resize ${this.titleOf(widget)}`, resizeWidget(this.widgets(), widget.id, size.w, size.h, this.bounds(widget)).widgets);
    this.announcer.announce(announce.preset(this.titleOf(widget), preset, size.w, size.h), 'polite');
  }

  private removeWidget(widget: LayoutWidget): void {
    const order = readingOrder(this.widgets());
    const index = order.findIndex((w) => w.id === widget.id);
    const neighbour = order[index + 1] ?? order[index - 1];
    const title = this.titleOf(widget);
    if (this.selectedId() === widget.id) {
      this.selectedId.set(null);
    }
    this.commit(`remove ${title}`, removeWidget(this.widgets(), widget.id));
    this.announcer.announce(announce.removed(title, this.widgets().length), 'polite');
    if (neighbour) {
      this.focusWidget(neighbour.id);
    } else {
      setTimeout(() => document.getElementById('empty-add')?.focus());
    }
  }

  protected applySetting(widget: LayoutWidget, change: SettingsChange): void {
    const title = this.titleOf(widget);
    switch (change.kind) {
      case 'presentation': {
        const presentation = this.metricOf(widget).presentations.find((p) => p.key === change.key);
        if (!presentation) {
          return;
        }
        // Options that do not apply to the new presentation are dropped, so a layout never holds one the server would refuse.
        const options = Object.fromEntries(Object.entries(widget.options ?? {}).filter(([key]) => this.metricOf(widget).options.some((o) => o.key === key && o.appliesTo.includes(change.key))));
        const next = { ...widget, presentation: change.key, options };
        const size = { w: Math.min(Math.max(widget.w, presentation.minWidth), presentation.maxWidth), h: Math.min(Math.max(widget.h, presentation.minHeight), presentation.maxHeight) };
        const widgets = this.widgets().map((w) => (w.id === widget.id ? next : w));
        this.commit(`show ${title} as ${presentation.name.toLowerCase()}`, resizeWidget(widgets, widget.id, size.w, size.h, { minWidth: presentation.minWidth, minHeight: presentation.minHeight, maxWidth: presentation.maxWidth, maxHeight: presentation.maxHeight }).widgets);
        this.announcer.announce(announce.optionChanged('Presentation', presentation.name, title), 'polite');
        return;
      }
      case 'option': {
        const option = this.metricOf(widget).options.find((o) => o.key === change.key);
        if (!option) {
          return;
        }
        this.commit(`change ${option.name.toLowerCase()} of ${title}`, this.widgets().map((w) => (w.id === widget.id ? { ...w, options: { ...(w.options ?? {}), [change.key]: change.value } } : w)));
        this.announcer.announce(announce.optionChanged(option.name, change.value, title), 'polite');
        return;
      }
      case 'preset':
        this.applyPreset(widget, change.preset);
        return;
      case 'size': {
        const result = resizeWidget(this.widgets(), widget.id, change.w, change.h, this.bounds(widget));
        const after = result.widgets.find((w) => w.id === widget.id)!;
        this.commit(`resize ${title}`, result.widgets);
        const limit = after.w === this.bounds(widget).minWidth && after.h === this.bounds(widget).minHeight ? 'min' : after.w === this.bounds(widget).maxWidth && after.h === this.bounds(widget).maxHeight ? 'max' : null;
        this.announcer.announce(announce.resizeStep(after.w, after.h, limit), 'polite');
        return;
      }
      case 'position': {
        const pushed = this.moveTo(widget.id, change.x, change.y, 'move');
        const after = this.widgets().find((w) => w.id === widget.id)!;
        this.announcer.announce(announce.moveStep(after.x, after.y, pushed), 'polite');
        return;
      }
    }
  }

  protected closeSettings(): void {
    const id = this.selectedId();
    this.selectedId.set(null);
    if (id) {
      setTimeout(() => document.querySelector<HTMLElement>(`[data-widget-id="${CSS.escape(id)}"] [aria-label^="Settings for"]`)?.focus());
    }
  }

  protected setDensity(density: Density): void {
    this.density.set(density);
  }

  // --- saving, sharing, deleting ---

  private request(): SavedViewRequest {
    return { name: this.name().trim(), description: this.description().trim() || null, layout: { version: LAYOUT_VERSION, density: this.density(), widgets: this.widgets() } };
  }

  protected save(): void {
    const providerId = this.filters.providerId();
    if (!providerId || this.saving()) {
      return;
    }
    if (!this.name().trim()) {
      this.nameError.set('Give the view a name.');
      document.getElementById('view-name')?.focus();
      return;
    }
    this.saving.set(true);
    this.saveError.set('');
    const id = this.savedId();
    const call = id === null ? this.api.create(providerId, this.request()) : this.api.update(providerId, id, this.request(), this.etag());
    call.subscribe({
      next: (loaded) => this.saved(loaded, id === null),
      error: (error: unknown) => this.saveFailed(error),
    });
  }

  private saved(loaded: LoadedView, wasNew: boolean): void {
    this.saving.set(false);
    this.etag.set(loaded.etag);
    this.savedId.set(loaded.view.id);
    this.name.set(loaded.view.name);
    this.baseline.set(this.snapshot());
    this.dirtyAnnounced = false;
    this.announcer.announce(announce.saved(loaded.view.name), 'polite');
    if (wasNew) {
      void this.router.navigate(['/saved-views', loaded.view.id], { replaceUrl: true });
    }
  }

  private saveFailed(error: unknown): void {
    this.saving.set(false);
    const status = error instanceof HttpErrorResponse ? error.status : (error as { cause?: HttpErrorResponse } | undefined)?.cause?.status;
    const name = this.name().trim() || 'the view';
    if (status === 412) {
      this.announcer.announce(announce.conflict(name), 'assertive');
      this.dialogs.open<ConflictDialogComponent, unknown, ConflictChoice>(ConflictDialogComponent, { autoFocus: '.conflict-default' }).afterClosed().subscribe((choice) => this.resolveConflict(choice));
      return;
    }
    if (status === 409) {
      this.nameError.set('You already have a view with this name. Choose another.');
      document.getElementById('view-name')?.focus();
      return;
    }
    if (status === 400) {
      this.saveError.set('This view is too large or has a metric that is no longer available. Remove it and save again.');
    } else {
      const problem = describeProblem(error, 'your changes');
      this.saveError.set(`We couldn't save "${name}". Your changes are still on this page. Try again.${problem.reference ? ` Reference: ${problem.reference}` : ''}`);
    }
    this.announcer.announce(announce.saveFailed(name), 'assertive');
  }

  private resolveConflict(choice: ConflictChoice | undefined): void {
    if (choice === 'copy') {
      this.createCopy(`${this.name().trim()} (copy)`.slice(0, MAX_VIEW_NAME));
    } else if (choice === 'reload') {
      this.dialogs.confirm({ heading: 'Load their version?', message: 'Your unsaved changes will be lost.', confirmLabel: 'Load their version', cancelLabel: 'Keep editing', danger: true }).subscribe((ok) => {
        if (ok) {
          this.viewResource.reload();
        }
      });
    }
  }

  protected saveCopy(): void {
    this.dialogs
      .open<NameDialogComponent, unknown, string>(NameDialogComponent, { data: { heading: 'Save a copy', confirmLabel: 'Save copy', name: `${this.name()} (copy)`.slice(0, MAX_VIEW_NAME) }, autoFocus: 'first-tabbable' })
      .afterClosed()
      .subscribe((name) => name && this.createCopy(name));
  }

  private createCopy(name: string): void {
    const providerId = this.filters.providerId();
    if (!providerId) {
      return;
    }
    this.api.create(providerId, { ...this.request(), name, layout: this.request().layout }).subscribe({
      next: (loaded) => {
        this.baseline.set(this.snapshot());
        this.mode.set('view');
        this.announcer.announce(announce.saved(loaded.view.name), 'polite');
        void this.router.navigate(['/saved-views', loaded.view.id]);
      },
      error: (error: unknown) => this.saveFailed(error),
    });
  }

  protected share(): void {
    const providerId = this.filters.providerId();
    const id = this.savedId();
    if (!providerId || id === null) {
      return;
    }
    this.dialogs.open(ShareDialogComponent, { data: { providerId, viewId: id, viewName: this.name() }, autoFocus: 'first-tabbable' });
  }

  protected remove(): void {
    const providerId = this.filters.providerId();
    const id = this.savedId();
    if (!providerId || id === null) {
      return;
    }
    this.dialogs.confirm({ heading: `Delete "${this.name()}"?`, message: 'This permanently deletes the view. People it was shared with lose access.', confirmLabel: 'Delete view', cancelLabel: 'Keep view', danger: true }).subscribe((ok) => {
      if (ok) {
        this.api.delete(providerId, id).subscribe(() => {
          this.announcer.announce(announce.deleted(this.name()), 'polite');
          this.baseline.set(this.snapshot());
          void this.router.navigateByUrl('/saved-views');
        });
      }
    });
  }

  // --- one announcement for a filter change ---

  protected onStatus(id: string, status: WidgetStatus): void {
    this.statuses.update((map) => new Map(map).set(id, status));
  }

  private filtersChanged(key: string): void {
    if (this.lastFilterKey !== null && this.lastFilterKey !== key && this.widgets().length > 0) {
      this.pendingFilterAnnouncement = true;
      const period = this.filters.endPeriod() ? apPeriodLabel(this.filters.endPeriod()) : 'this period';
      this.announcer.announce(announce.filtersUpdating(this.widgets().length, `${period}, ${this.filters.serviceLine() === 'both' ? 'air and ground' : this.filters.serviceLine()}`), 'polite');
    }
    this.lastFilterKey = key;
  }

  private lastFilterKey: string | null = null;

  private announceFilterResult(statuses: readonly WidgetStatus[]): void {
    this.pendingFilterAnnouncement = false;
    const failed = statuses.filter((s) => s === 'failed').length;
    const count = this.widgets().length;
    this.announcer.announce(failed > 0 ? announce.filtersPartial(failed, count) : announce.filtersDone(count, this.filters.dataThrough()), 'polite');
  }
}

