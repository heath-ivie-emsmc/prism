import { LiveAnnouncer } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, computed, effect, inject, linkedSignal, signal, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { ActivatedRoute, Router } from '@angular/router';
import { map, of } from 'rxjs';
import {
  EmsBadgeComponent, EmsBreadcrumb, EmsBreadcrumbsComponent, EmsButtonDirective, EmsChipComponent, EmsDialog, EmsIconComponent, EmsPageChange, EmsPaginationComponent, EmsPickerOption, EmsSelectComponent,
} from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { apPeriodEnd, apPeriodLabel, formatDate } from '../dashboard/dashboard-format';
import { SegmentedControlComponent } from '../dashboard/segmented-control.component';
import { NetCollectionsService } from '../metrics/net-collections.service';
import { NetCollectionsPeriodsDto } from '../metrics/net-collections/net-collections.dto';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { saveBlob } from '../saved-views/view-image';
import {
  ClaimRowView, FALLBACK_TABS, downloadFailureMessage, downloadName, groupWords, headerState, reviewQuery, safeCheck, safeGroup, selectedMessage, sendButtonName, tabOptions, toClaimRow,
} from './attention';
import {
  AttentionClaimsDto, AttentionCountsDto, AttentionQuery, AttentionService, CLAIMS_PERMISSION,
} from './attention.service';
import { SendToCseData, SendToCseDrawerComponent } from './send-to-cse-drawer.component';

interface ClaimsParams extends AttentionQuery {
  readonly check: string;
  readonly page: number;
  readonly pageSize: number;
}

/**
 * The claims that need a customer's attention, one Attention Check at a time. The check, month, Service Line and aircraft type are in the address,
 * so the page can be shared. Claim rows are PHI: the page asks for them only when the user has the claims permission, and every read is audited by the API.
 * Ticking claims enables Send to CSE, which opens a drawer listing them. Nothing is sent yet.
 */
@Component({
  selector: 'app-needs-attention',
  imports: [
    EmsBadgeComponent, EmsBreadcrumbsComponent, EmsButtonDirective, EmsChipComponent, EmsIconComponent, EmsPaginationComponent, EmsSelectComponent, LoadProblemComponent,
    MatCheckboxModule, ReactiveFormsModule, SegmentedControlComponent, SkeletonComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page" id="main-content">
      <ems-breadcrumbs class="page__crumbs" [items]="crumbs()" />

      <header class="page__header">
        <div class="page__title">
          <h1 tabindex="-1">{{ providerName() }}</h1>
          <p class="page__subtitle">Needs attention@if (dataThrough()) { · Data through {{ dataThrough() }} }</p>
        </div>
        <form class="page__filters" aria-label="Needs attention filters" (submit)="$event.preventDefault()">
          <ems-select class="page__select" label="Period" [options]="periods()" [formControl]="period" [clearable]="false" />
        </form>
      </header>

      <section class="app-panel claims" aria-labelledby="claims-title">
        <header class="claims__header">
          <div class="claims__heading">
            <h2 id="claims-title">{{ title() }}</h2>
            @if (allCount() !== null) {
              <ems-chip size="md" state="neutral">{{ total() }} of {{ allCount() }}</ems-chip>
            }
          </div>
          <div class="claims__actions">
            <app-segmented-control label="Claim category" [options]="tabs()" [value]="check()" (valueChange)="chooseCheck($event)" />
            <button emsButton="flat" fab color="neutral" type="button" [disabled]="!canDownload()" [attr.aria-label]="downloadLabel()" [title]="downloadLabel()" [attr.aria-busy]="downloading()" (click)="download()">
              <ems-icon name="download" />
            </button>
            <span class="send-wrap">
              <button emsButton="raised" class="send" type="button" [disabled]="selectedCount() === 0" [attr.aria-label]="sendLabel()" (click)="openDrawer()">
                <ems-icon name="send" aria-hidden="true" />Send to CSE
              </button>
              @if (selectedCount() > 0) {
                <span class="send__badge" aria-hidden="true"><ems-badge [count]="selectedCount()" state="error" /></span>
              }
            </span>
          </div>
        </header>

        <p class="cdk-visually-hidden" role="status">{{ announcement() }}</p>

        @if (!allowed()) {
          <p class="claims__note"><strong>You don't have access to claim details.</strong><br />Ask your administrator for the claims permission to review claims here.</p>
        } @else if (loadError() !== undefined) {
          <app-load-problem [error]="loadError()" subject="the claims" (retry)="retry()" />
        } @else if (claimsLoading() && !dto()) {
          <span class="cdk-visually-hidden" role="status">Loading claims</span>
          <div class="claims__skeleton" aria-hidden="true">
            <app-skeleton height="3rem" />
            <app-skeleton height="3rem" />
            <app-skeleton height="3rem" />
          </div>
        } @else if (rows().length === 0) {
          <p class="claims__note">No {{ tabName().toLowerCase() }} claims for {{ periodLabel() }}.</p>
        } @else {
          <div class="table-wrap" role="region" tabindex="0" aria-labelledby="claims-title">
            <table class="table">
              <caption class="cdk-visually-hidden">{{ title() }}, {{ tabName() }}. Sorted by estimated filing deadline, soonest first.</caption>
              <thead>
                <tr>
                  <th scope="col" class="col-check">
                    <mat-checkbox
                      [checked]="header() === 'all'"
                      [indeterminate]="header() === 'some'"
                      [aria-label]="'Select all ' + rows().length + ' claims on this page'"
                      (change)="toggleAll($event.checked)"
                    />
                  </th>
                  <th scope="col">Claim</th>
                  <th scope="col">Payer</th>
                  <th scope="col">{{ vehicleHeading() }}</th>
                  <th scope="col">Status</th>
                  <th scope="col" class="num">Balance due</th>
                  <th scope="col">Filing deadline (estimated)</th>
                </tr>
              </thead>
              <tbody>
                @for (row of rows(); track row.id) {
                  <tr [class.row--selected]="selected().has(row.id)">
                    <td class="col-check">
                      <mat-checkbox [checked]="selected().has(row.id)" [aria-label]="'Select claim ' + row.claimRef" (change)="toggle(row, $event.checked)" />
                    </td>
                    <th scope="row" class="cell">
                      <span class="cell__main">{{ row.claimRef }}</span>
                      <span class="cell__sub">Service date {{ row.serviceDate }}</span>
                    </th>
                    <td class="cell">
                      <span class="cell__main">{{ row.payer }}</span>
                      <span class="cell__sub">{{ row.payerCategory }}</span>
                    </td>
                    <td class="cell">
                      <span class="cell__main">{{ row.vehicle }}</span>
                      <span class="cell__sub">{{ row.division }}</span>
                    </td>
                    <td>
                      <span class="status">
                        <ems-chip [state]="row.statusState" [contrast]="row.statusState === 'error'">{{ row.status }}</ems-chip>
                        @if (row.reason) {
                          <span class="cell__sub">{{ row.reason }}</span>
                        }
                      </span>
                    </td>
                    <td class="num">{{ row.balance }}</td>
                    <td class="cell">
                      <span class="due" [class]="'due due--' + row.urgency"><ems-icon [name]="row.urgencyIcon" size="sm" aria-hidden="true" />{{ row.daysText }}</span>
                      <span class="cell__sub">Due {{ row.due }}</span>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <footer class="claims__footer">
            <p class="claims__note">Sorted by estimated filing deadline, soonest first. Balance due is what is still owed. Showing {{ rows().length }} of {{ total() }} claims.</p>
            <ems-pagination itemLabel="claims" label="Claims pages" [length]="total()" [page]="page()" [pageSize]="pageSize()" (changed)="changePage($event)" />
          </footer>
        }
      </section>
    </main>
  `,
  styles: `
    .page {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
    }
    .page:focus,
    h1:focus {
      outline: none;
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
    .page__header {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: 1.5rem;
    }
    h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    h2 {
      margin: 0;
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
      font-weight: var(--ems-font-weight-heavy);
      overflow-wrap: anywhere;
    }
    .page__subtitle {
      margin: 0.25rem 0 0;
      color: var(--ems-text-muted);
    }
    .page__filters {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem;
    }
    .page__select {
      min-width: 12rem;
    }
    .claims {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .claims__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem 1.5rem;
    }
    .claims__heading,
    .claims__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
      min-width: 0;
    }
    .claims__skeleton {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }
    .claims__note {
      margin: 0;
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-body);
      overflow-wrap: anywhere;
    }
    .claims__footer {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    /* The count badge sits on the button's top right corner, as in the mockup. The wrapper gives it room, so it stays inside its container. The button's own name carries the count. */
    .send-wrap {
      position: relative;
      display: inline-flex;
      padding: 0.5rem 0.5rem 0 0;
    }
    .send__badge {
      position: absolute;
      inset-block-start: 0;
      inset-inline-end: 0;
      display: inline-flex;
    }
    .table-wrap {
      overflow-x: auto;
      border: 1px solid var(--ems-border-neutral-subtle);
      border-radius: var(--ems-corner-md);
    }
    .table {
      width: 100%;
      min-width: 56rem;
      border-collapse: collapse;
    }
    .table th,
    .table td {
      padding: 0.75rem 1rem;
      text-align: left;
      vertical-align: middle;
      border-bottom: 1px solid var(--ems-border-neutral-subtle);
      line-height: var(--ems-line-height-body);
    }
    .table thead th {
      font-weight: var(--ems-font-weight-bold);
    }
    .table tbody th {
      font-weight: var(--ems-font-weight-regular, normal);
    }
    .table tr:last-child th,
    .table tr:last-child td {
      border-bottom: 0;
    }
    .table .col-check {
      width: 3rem;
      padding-inline-end: 0;
    }
    .table .num {
      text-align: right;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }
    .row--selected {
      background: var(--ems-surface-brand-subtle, var(--ems-surface-neutral-subtle));
    }
    .cell {
      min-width: 0;
    }
    .cell__main,
    .cell__sub {
      display: block;
      overflow-wrap: anywhere;
    }
    .cell__main {
      font-weight: var(--ems-font-weight-regular, normal);
    }
    .cell__sub {
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
    }
    .status {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .due {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      font-weight: var(--ems-font-weight-bold);
    }
    .due--soon {
      color: var(--ems-text-warning-default);
    }
    .due--overdue {
      color: var(--ems-text-error-default);
    }
    @media (forced-colors: active) {
      .row--selected {
        outline: 2px solid Highlight;
        outline-offset: -2px;
      }
    }
  `,
})
export class NeedsAttentionComponent {
  private readonly auth = inject(AuthService);
  private readonly attention = inject(AttentionService);
  private readonly netCollections = inject(NetCollectionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(EmsDialog);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly period = new FormControl<string>('', { nonNullable: true });

  private readonly query = toSignal(
    this.route.queryParamMap.pipe(map((q) => ({ check: safeCheck(q.get('check')), period: q.get('period') ?? '', line: q.get('line'), group: safeGroup(q.get('group')) }))),
    { initialValue: { check: 'denied', period: '', line: null as string | null, group: null as string | null } },
  );

  protected readonly check = computed(() => this.query().check);
  protected readonly line = computed<'air' | 'ground'>(() => (this.query().line === 'ground' ? 'ground' : 'air'));
  private readonly group = computed(() => this.query().group);
  protected readonly providerName = computed(() => this.auth.activeProvider()?.name ?? '');
  private readonly providerId = computed(() => this.auth.activeProviderId());
  protected readonly allowed = computed(() => this.auth.me()?.permissions.includes(CLAIMS_PERMISSION) ?? false);

  private readonly periodsResource = rxResource<NetCollectionsPeriodsDto | null, string | null>({
    params: () => this.providerId(),
    stream: ({ params }) => (params ? this.netCollections.periods(params) : of(null)),
  });
  private readonly periodList = computed(() => (this.periodsResource.hasValue() ? this.periodsResource.value() : null)?.periods ?? []);
  protected readonly periods = computed<EmsPickerOption<string>[]>(() => this.periodList().map((p) => ({ value: p.period, label: apPeriodLabel(p.period) })));
  protected readonly month = computed(() => (/^\d{6}$/.test(this.query().period) ? this.query().period : (this.periodList()[0]?.period ?? '')));
  protected readonly periodLabel = computed(() => apPeriodLabel(this.month()));

  private readonly base = computed<AttentionQuery | null>(() => {
    const providerId = this.providerId();
    const period = this.month();
    return this.allowed() && providerId && period ? { providerId, period, line: this.line(), group: this.group() } : null;
  });

  /** Back to the first page whenever what is being asked about changes. */
  protected readonly page = linkedSignal<AttentionQuery | null, number>({ source: () => this.base(), computation: () => 1 });
  protected readonly pageSize = signal(25);

  private readonly countsResource = rxResource<AttentionCountsDto | null, AttentionQuery | null>({
    params: () => this.base(),
    stream: ({ params }) => (params ? this.attention.counts(params) : of(null)),
  });

  private readonly claimsResource = rxResource<AttentionClaimsDto | null, ClaimsParams | null>({
    params: () => {
      const base = this.base();
      return base ? { ...base, check: this.check(), page: this.page(), pageSize: this.pageSize() } : null;
    },
    stream: ({ params }) => (params ? this.attention.claims(params, params.check, params.page, params.pageSize) : of(null)),
  });

  protected readonly dto = computed(() => (this.claimsResource.hasValue() ? this.claimsResource.value() : null));
  private readonly counts = computed(() => (this.countsResource.hasValue() ? this.countsResource.value() : null));
  protected readonly claimsLoading = computed(() => this.claimsResource.isLoading() || this.periodsResource.isLoading());
  protected readonly loadError = computed<unknown>(() => this.periodsResource.error() ?? this.claimsResource.error() ?? this.countsResource.error());
  protected readonly rows = computed<readonly ClaimRowView[]>(() => (this.dto()?.claims ?? []).map(toClaimRow));
  protected readonly total = computed(() => this.dto()?.total ?? 0);
  protected readonly allCount = computed(() => this.counts()?.counts.find((c) => c.key === 'all')?.count ?? null);
  protected readonly tabs = computed(() => tabOptions(this.counts()?.counts ?? FALLBACK_TABS));
  protected readonly tabName = computed(() => (this.counts()?.counts ?? FALLBACK_TABS).find((c) => c.key === this.check())?.name ?? 'Denied');
  protected readonly tabCount = computed(() => this.counts()?.counts.find((c) => c.key === this.check())?.count ?? this.total());
  protected readonly dataThrough = computed(() => {
    const through = this.dto()?.dataThrough ?? this.counts()?.dataThrough ?? apPeriodEnd(this.periodList()[0]?.period ?? '');
    return through ? formatDate(through) : '';
  });
  protected readonly title = computed(() => {
    const group = this.group();
    const subject = group ? `${groupWords(group)} claims` : this.line() === 'air' ? 'Air claims' : 'Ground claims';
    return `${subject}, ${this.periodLabel()}`.replace(/, $/, '');
  });
  protected readonly vehicleHeading = computed(() => (this.line() === 'air' ? 'Aircraft' : 'Vehicle'));

  /** The ticked claims, by id, across tabs and pages. They are cleared when the month, Service Line or aircraft type changes. */
  protected readonly selected = linkedSignal<AttentionQuery | null, ReadonlyMap<string, ClaimRowView>>({ source: () => this.base(), computation: () => new Map() });
  protected readonly selectedCount = computed(() => this.selected().size);
  protected readonly header = computed(() => headerState(new Set(this.selected().keys()), this.rows().map((r) => r.id)));
  protected readonly sendLabel = computed(() => sendButtonName(this.selectedCount()));
  protected readonly announcement = signal('');

  protected readonly downloading = signal(false);
  protected readonly canDownload = computed(() => this.allowed() && !this.downloading() && this.total() > 0);
  protected readonly downloadLabel = computed(() => downloadName(this.tabName(), this.tabCount()));

  protected readonly crumbs = computed<EmsBreadcrumb[]>(() => [{ label: 'Dashboard', link: '/dashboard', icon: 'home' }, { label: 'Needs attention' }]);

  constructor() {
    effect(() => {
      const month = this.month();
      if (month && this.period.value !== month) untracked(() => this.period.setValue(month, { emitEvent: false }));
    });
    this.period.valueChanges.subscribe((value) => this.navigate({ period: value }));
  }

  protected chooseCheck(key: string): void {
    this.navigate({ check: key });
    this.page.set(1);
  }

  protected changePage(change: EmsPageChange): void {
    this.page.set(change.page);
    this.pageSize.set(change.pageSize);
  }

  protected toggle(row: ClaimRowView, checked: boolean): void {
    const next = new Map(this.selected());
    if (checked) next.set(row.id, row);
    else next.delete(row.id);
    this.selected.set(next);
    this.say(next.size);
  }

  protected toggleAll(checked: boolean): void {
    const next = new Map(this.selected());
    for (const row of this.rows()) {
      if (checked) next.set(row.id, row);
      else next.delete(row.id);
    }
    this.selected.set(next);
    this.say(next.size);
  }

  protected openDrawer(): void {
    if (this.selected().size === 0) return;
    const data: SendToCseData = { claims: [...this.selected().values()] };
    this.dialog.open(SendToCseDrawerComponent, {
      data,
      ariaLabelledBy: 'cse-title',
      panelClass: 'cse-drawer-panel',
      position: { top: '0', right: '0' },
      height: '100dvh',
      maxHeight: '100dvh',
      width: 'min(28rem, 100vw)',
      maxWidth: '100vw',
      autoFocus: 'dialog',
    });
  }

  protected download(): void {
    const base = this.base();
    if (!base || this.downloading()) return;
    this.downloading.set(true);
    this.attention.exportCsv(base, this.check()).subscribe({
      next: (file) => {
        saveBlob(file.blob, file.fileName);
        this.downloading.set(false);
        this.announcer.announce(`Downloaded ${file.fileName}`, 'polite');
      },
      error: (error: unknown) => {
        this.downloading.set(false);
        this.announcer.announce(downloadFailureMessage(error), 'assertive');
      },
    });
  }

  protected retry(): void {
    if (this.periodsResource.error() !== undefined) this.periodsResource.reload();
    else {
      this.claimsResource.reload();
      this.countsResource.reload();
    }
  }

  private say(count: number): void {
    this.announcement.set(selectedMessage(count));
  }

  private navigate(queryParams: Record<string, string>): void {
    const base = this.base();
    const merged = base ? { ...reviewQuery(this.check(), this.month(), this.line(), this.group()), ...queryParams } : queryParams;
    void this.router.navigate([], { relativeTo: this.route, queryParams: merged, queryParamsHandling: 'merge', replaceUrl: true });
  }
}
