import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { FormControl } from '@angular/forms';
import { EmsPickerOption } from '@ems-management-consultants/ui';
import { of, tap } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { DataFreshnessService } from '../shared/data-freshness.service';
import { NetCollectionsService } from '../metrics/net-collections.service';
import { NetCollectionsPeriodsDto, ReportingDivisionDto, ReportingDivisionsDto } from '../metrics/net-collections/net-collections.dto';
import { DashboardContext, MOCK_CONTEXT } from './dashboard-context';
import { apPeriodEnd, apPeriodLabel, formatDate } from './dashboard-format';
import { MileUnit } from './transport-volume';
import { SERVICE_LINE_OPTIONS, ServiceLineSelection, defaultLabels } from './service-line';

export const ALL_DIVISIONS = 'all';

/**
 * The page-level filters (Provider, Service Line, period, Reporting Division, mile unit) shared by the dashboard and the saved views.
 * Every widget reads these signals as the params of its own resource, so a change here refreshes each widget that depends on it.
 * The Provider comes from the session and is checked again by the API on every request. It is never stored with a view.
 */
@Injectable({ providedIn: 'root' })
export class DashboardFilterContext {
  private readonly auth = inject(AuthService);
  private readonly netCollections = inject(NetCollectionsService);

  /** Mocked: replaced by the Provider context API. */
  readonly context = signal<DashboardContext>(MOCK_CONTEXT);

  readonly serviceLine = signal<ServiceLineSelection>('air');
  /** Nautical miles are an aviation unit. Ground transports are always in statute miles, so the switch is not offered for them. */
  readonly mileUnit = signal<MileUnit>('statute');
  /** The newest accounting period to show, as yyyyMM. Empty until the Provider's periods have loaded. */
  readonly period = new FormControl<string>('', { nonNullable: true });
  readonly division = new FormControl<string>(ALL_DIVISIONS, { nonNullable: true });

  /** The first Provider name in the session, exactly as the token carries it. The mock name shows only when the user has no Provider. */
  readonly providerName = computed(() => this.auth.activeProvider()?.name || this.context().providerName);

  /**
   * Set only while an Internal User lays out a View Template: the Provider whose figures are drawn, chosen by number. Cleared when they leave.
   * The API checks it again: it only works for the customer data viewer tier, on aggregate routes, and each use is audited.
   */
  readonly previewProviderId = signal<string | null>(null);

  /** The Provider whose data is shown: the preview Provider when an Internal User chose one, otherwise the first one the user was assigned. The API checks it again. */
  readonly providerId = computed(() => this.previewProviderId() ?? this.auth.activeProviderId());

  private readonly freshness = inject(DataFreshnessService);

  private readonly periodsResource = rxResource<NetCollectionsPeriodsDto | null, { providerId: string; rev: number } | null>({
    params: () => {
      const providerId = this.providerId();
      return providerId ? { providerId, rev: this.freshness.revision() } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.periods(params.providerId) : of(null)),
  });

  readonly periodsError = computed<unknown>(() => this.periodsResource.error());
  /** Nothing can be asked for until the list of months is here, so every figure waits on it too. */
  readonly periodsLoading = computed(() => this.periodsResource.isLoading());

  readonly periods = computed<EmsPickerOption<string>[]>(() =>
    ((this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods ?? []).map((p) => ({ value: p.period, label: apPeriodLabel(p.period) })),
  );
  readonly endPeriod = toSignal(this.period.valueChanges, { initialValue: this.period.value });

  /** The newest accounting period that has data. The data is complete through the end of it. */
  private readonly newestPeriod = computed(() => (this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.periods[0]?.period ?? '');
  private readonly periodsDataThrough = computed(() => (this.periodsResource.hasValue() ? this.periodsResource.value() : undefined)?.dataThrough ?? '');
  /** yyyy-MM-dd, or null before the periods load. The response's own Data Through when it has one, else the end of the newest period. */
  readonly dataThroughIso = computed(() => this.periodsDataThrough() || apPeriodEnd(this.newestPeriod()) || null);
  readonly dataThrough = computed(() => {
    const end = this.dataThroughIso();
    return end ? formatDate(end) : '';
  });

  readonly showServiceLineToggle = computed(() => this.context().enabledServiceLines.length > 1);
  readonly serviceLineOptions = computed(() =>
    SERVICE_LINE_OPTIONS.filter((option) => option.value === 'both' || this.context().enabledServiceLines.includes(option.value)),
  );

  readonly labels = computed(() => defaultLabels(this.serviceLine()));
  /**
   * The Provider's real Reporting Divisions from the warehouse, for the Service Line on screen: under Air only or Ground only, just the divisions that have a Transport
   * under it, so the picker never offers a choice that can only be empty. Air + ground lists all of them. The keys are the same under every line.
   */
  private readonly divisionsResource = rxResource<ReportingDivisionsDto | null, { providerId: string; line: 'all' | 'air' | 'ground'; rev: number } | null>({
    params: () => {
      const providerId = this.providerId();
      const line = this.serviceLine();
      return providerId ? { providerId, line: line === 'both' ? 'all' : line, rev: this.freshness.revision() } : null;
    },
    stream: ({ params }) => (params ? this.netCollections.divisions(params.providerId, params.line).pipe(tap((dto) => this.remember(params.providerId, params.rev, dto))) : of(null)),
  });
  /**
   * The last list that arrived for a Provider, shown while the next one loads (a Service Line change) so the picker does not blank out, and the names of every division
   * any list of this Provider and data revision held. A chosen division stays valid (and keeps its name) after a Service Line change that leaves it out of the picked list.
   */
  private readonly lastList = signal<{ providerId: string; divisions: readonly ReportingDivisionDto[] } | null>(null);
  private readonly known = signal<{ providerId: string; rev: number; names: ReadonlyMap<string, string> }>({ providerId: '', rev: -1, names: new Map() });

  private remember(providerId: string, rev: number, dto: ReportingDivisionsDto): void {
    this.lastList.set({ providerId, divisions: dto.divisions });
    this.known.update((k) => {
      const names = new Map(k.providerId === providerId && k.rev === rev ? k.names : []);
      for (const d of dto.divisions) names.set(d.key, d.name);
      return { providerId, rev, names };
    });
  }

  readonly divisionsError = computed<unknown>(() => this.divisionsResource.error());
  readonly divisionsLoading = computed(() => this.divisionsResource.isLoading());
  /** The names are untrusted warehouse text: they are only ever bound as text, never as HTML. */
  readonly divisions = computed<readonly ReportingDivisionDto[]>(() => {
    if (this.divisionsResource.hasValue()) return this.divisionsResource.value()?.divisions ?? [];
    const last = this.lastList();
    return last && last.providerId === this.providerId() ? last.divisions : [];
  });
  /** The name of a division of this Provider by key, from any list loaded for it, or undefined. */
  divisionName(key: string): string | undefined {
    const known = this.known();
    return known.providerId === this.providerId() ? known.names.get(key) : undefined;
  }
  /** The chosen division when the Service Line on screen has no Transport for it: still chosen (not silently reset), with its name. Null otherwise. */
  readonly divisionOutsideLine = computed<{ key: string; name: string; line: 'air' | 'ground' } | null>(() => {
    const line = this.serviceLine();
    const key = this.selectedDivision();
    if (line === 'both' || key === ALL_DIVISIONS || this.divisionsProblem() !== '' || this.divisions().some((d) => d.key === key)) return null;
    const name = this.divisionName(key);
    return name === undefined ? null : { key, name, line };
  });
  readonly divisionOptions = computed<EmsPickerOption<string>[]>(() => {
    const outside = this.divisionOutsideLine();
    return [
      { value: ALL_DIVISIONS, label: this.labels().allReportingDivisions },
      ...(outside ? [{ value: outside.key, label: `${outside.name} (no ${outside.line} transports)` }] : []),
      ...this.divisions().map((division) => ({ value: division.key, label: division.name })),
    ];
  });
  /** Why the list can't be used right now, or '' when it can. Shown beside the control, never silent. */
  readonly divisionsProblem = computed<'loading' | 'error' | ''>(() => {
    if (this.divisionsResource.error() !== undefined) return 'error';
    if (this.divisionsResource.hasValue()) return '';
    const last = this.lastList();
    return last && last.providerId === this.providerId() ? '' : 'loading';
  });

  readonly selectedDivision = toSignal(this.division.valueChanges, { initialValue: this.division.value });
  /** The Reporting Division key to send to Metrics that honor it, or undefined for all divisions. */
  readonly divisionKey = computed(() => {
    const selected = this.selectedDivision();
    // Only a key of this Provider's divisions is ever sent, so a stale choice (Provider change) cannot reach a Metric before it is reset. A division the chosen
    // Service Line has no Transport for is still sent: the answer is empty, and the page says that is why.
    return selected === ALL_DIVISIONS || this.divisionName(selected) === undefined ? undefined : selected;
  });
  /** The chosen division's name for the pages that say what narrowed an empty answer, or undefined for all (or while its name is unknown). */
  readonly selectedDivisionName = computed(() => {
    const selected = this.selectedDivision();
    return selected === ALL_DIVISIONS ? undefined : this.divisionName(selected);
  });

  /** The period to switch to when the chosen one is not in the list (the list changed with the Provider), else null. A derived value, so the effect below only reacts to it. */
  private readonly periodCorrection = computed(() => {
    const options = this.periods();
    const selected = this.endPeriod();
    return options.length > 0 && !options.some((option) => option.value === selected) ? options[0]!.value : null;
  });
  /**
   * "all" when the chosen Reporting Division is not among the options (the Provider changed or the division is gone), else null. A division the Service Line has no Transport for
   * stays among the options, so a Service Line change never resets the choice.
   * Not while the list is loading or failed: an empty list then says nothing about the choice.
   */
  private readonly divisionCorrection = computed(() => {
    if (this.divisionsProblem() !== '' || this.selectedDivision() === ALL_DIVISIONS) return null;
    return this.divisionOptions().some((option) => option.value === this.selectedDivision()) ? null : ALL_DIVISIONS;
  });

  constructor() {
    // Start on the newest period once the list arrives, and again if the chosen one is no longer in the list. The effect tracks only the correction and writes inside untracked().
    effect(() => {
      const period = this.periodCorrection();
      if (period !== null) untracked(() => this.period.setValue(period));
    });
    // A new Provider (or a division that left the warehouse) can remove the chosen Reporting Division from the list. Fall back to "all" instead of leaving a dead choice.
    effect(() => {
      const division = this.divisionCorrection();
      if (division !== null) untracked(() => this.division.setValue(division));
    });
  }

  retryDivisions(): void {
    // A reload says what is there now: a division it leaves out is gone, not just quiet under this Service Line, so forget what earlier lists held.
    this.lastList.set(null);
    this.known.set({ providerId: '', rev: -1, names: new Map() });
    this.divisionsResource.reload();
  }

  retryPeriods(): void {
    this.periodsResource.reload();
  }
}
