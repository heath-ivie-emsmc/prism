import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { FormControl } from '@angular/forms';
import { EmsPickerOption } from '@ems-management-consultants/ui';
import { of } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { DataFreshnessService } from '../shared/data-freshness.service';
import { NetCollectionsService } from '../metrics/net-collections.service';
import { NetCollectionsPeriodsDto } from '../metrics/net-collections/net-collections.dto';
import { DashboardContext, MOCK_CONTEXT, divisionsFor } from './dashboard-context';
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

  /** The Provider whose data is shown: the first one the user was assigned. A switcher comes later. The API checks it again. */
  readonly providerId = computed(() => this.auth.activeProviderId());

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
  readonly divisionOptions = computed<EmsPickerOption<string>[]>(() => [
    { value: ALL_DIVISIONS, label: this.labels().allReportingDivisions },
    ...divisionsFor(this.context().reportingDivisions, this.serviceLine()).map((division) => ({
      value: division.id,
      label: division.name,
    })),
  ]);

  readonly selectedDivision = toSignal(this.division.valueChanges, { initialValue: this.division.value });
  /** The Reporting Division key to send to Metrics that honor it, or undefined for all divisions. */
  readonly divisionKey = computed(() => (this.selectedDivision() === ALL_DIVISIONS ? undefined : this.selectedDivision()));

  /** The period to switch to when the chosen one is not in the list (the list changed with the Provider), else null. A derived value, so the effect below only reacts to it. */
  private readonly periodCorrection = computed(() => {
    const options = this.periods();
    const selected = this.endPeriod();
    return options.length > 0 && !options.some((option) => option.value === selected) ? options[0]!.value : null;
  });
  /** "all" when the chosen Reporting Division is no longer in the list (a Service Line change removed it), else null. */
  private readonly divisionCorrection = computed(() => (this.divisionOptions().some((option) => option.value === this.selectedDivision()) ? null : ALL_DIVISIONS));

  constructor() {
    // Start on the newest period once the list arrives, and again if the chosen one is no longer in the list. The effect tracks only the correction and writes inside untracked().
    effect(() => {
      const period = this.periodCorrection();
      if (period !== null) untracked(() => this.period.setValue(period));
    });
    // Changing the Service Line can remove the chosen Reporting Division from the list. Fall back to "all" instead of leaving a dead choice.
    effect(() => {
      const division = this.divisionCorrection();
      if (division !== null) untracked(() => this.division.setValue(division));
    });
  }

  retryPeriods(): void {
    this.periodsResource.reload();
  }
}
