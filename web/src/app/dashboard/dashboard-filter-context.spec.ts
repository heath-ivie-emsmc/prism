import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { DIVISIONS, flushDivisions } from '../shared/division.testing';
import { ALL_DIVISIONS, DashboardFilterContext } from './dashboard-filter-context';

describe('DashboardFilterContext', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush({ userName: 'jdoe', displayName: 'Jane', providers: [{ id: '526', name: 'P', facilityCount: 0 }], permissions: [] });
    const context = TestBed.inject(DashboardFilterContext);
    return { context, http };
  }

  async function loadPeriods(http: HttpTestingController, periods: string[]): Promise<void> {
    TestBed.tick();
    http.expectOne('/api/v1/providers/526/metrics/net-collections/periods').flush({ dataThrough: '2026-11-22', periods: periods.map((period) => ({ period })) });
    flushDivisions(http);
    await TestBed.inject(ApplicationRef).whenStable();
  }

  const settle = (): Promise<void> => TestBed.inject(ApplicationRef).whenStable();

  it('starts on the newest period once the list arrives', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611', '202610']);
    expect(context.period.value).toBe('202611');
  });

  const DIVISIONS_URL = '/api/v1/providers/526/metrics/net-collections/divisions';

  async function loadDivisions(http: HttpTestingController, body: object = DIVISIONS): Promise<void> {
    TestBed.tick();
    flushDivisions(http, body);
    await settle();
  }

  it('keeps a period the user chose that is still in the list', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611', '202610']);
    context.period.setValue('202610');
    await settle();
    expect(context.period.value).toBe('202610');
  });

  it('falls back to the newest period when the chosen one is not in the list', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611', '202610']);
    context.period.setValue('199901');
    await settle();
    expect(context.period.value).toBe('202611');
  });

  it('lists the Provider\'s real divisions after All, by their key and warehouse name', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611']);
    expect(context.divisionOptions().map((o) => o.value)).toEqual([ALL_DIVISIONS, 'riverside', 'summit', 'x-img']);
    expect(context.divisionOptions().map((o) => o.label).slice(1, 3)).toEqual(['Riverside Base', 'Summit Base']);
    expect(context.divisionOptions()[0]!.label).toBe(context.labels().allReportingDivisions);
    expect(context.divisionsProblem()).toBe('');
  });

  /** Answers the list for one Service Line (the request's `serviceLine` parameter, or none for all) with its own body. */
  async function answerLine(http: HttpTestingController, byLine: Record<string, object>): Promise<string[]> {
    TestBed.tick();
    const asked: string[] = [];
    for (const request of http.match((r) => r.url === DIVISIONS_URL)) {
      const line = request.request.params.get('serviceLine') ?? 'all';
      asked.push(line);
      if (!request.cancelled) request.flush(byLine[line] ?? DIVISIONS);
    }
    await settle();
    return asked;
  }

  const AIR_ONLY = { providerId: '526', divisions: [{ key: 'riverside', name: 'Riverside Base' }] };
  const GROUND_ONLY = { providerId: '526', divisions: [{ key: 'summit', name: 'Summit Base' }] };

  it('asks for the divisions of the Service Line on screen, and for all of them under Air + ground', async () => {
    const { context, http } = setup();
    TestBed.tick();
    http.expectOne('/api/v1/providers/526/metrics/net-collections/periods').flush({ dataThrough: '2026-11-22', periods: [{ period: '202611' }] });
    expect(await answerLine(http, { air: AIR_ONLY })).toEqual(['air']);
    expect(context.divisionOptions().map((o) => o.value)).toEqual([ALL_DIVISIONS, 'riverside']);

    context.serviceLine.set('ground');
    expect(await answerLine(http, { ground: GROUND_ONLY })).toEqual(['ground']);
    expect(context.divisionOptions().map((o) => o.value)).toEqual([ALL_DIVISIONS, 'summit']);

    context.serviceLine.set('both');
    expect(await answerLine(http, {})).toEqual(['all']);
    expect(context.divisionOptions().map((o) => o.value)).toEqual([ALL_DIVISIONS, 'riverside', 'summit', 'x-img']);
  });

  it('keeps the list on screen while the next Service Line loads, instead of blanking the picker', async () => {
    const { context, http } = setup();
    TestBed.tick();
    http.expectOne('/api/v1/providers/526/metrics/net-collections/periods').flush({ dataThrough: '2026-11-22', periods: [{ period: '202611' }] });
    await answerLine(http, { air: AIR_ONLY });

    context.serviceLine.set('ground');
    TestBed.tick();
    expect(context.divisionsProblem()).toBe('');
    expect(context.divisionOptions().map((o) => o.value)).toEqual([ALL_DIVISIONS, 'riverside']);
    http.match((r) => r.url === DIVISIONS_URL);
  });

  it('keeps a chosen division that the new Service Line has no transports for, named and still sent, and never resets it silently', async () => {
    const { context, http } = setup();
    TestBed.tick();
    http.expectOne('/api/v1/providers/526/metrics/net-collections/periods').flush({ dataThrough: '2026-11-22', periods: [{ period: '202611' }] });
    await answerLine(http, { air: AIR_ONLY });
    context.division.setValue('riverside');
    await settle();

    context.serviceLine.set('ground');
    await answerLine(http, { ground: GROUND_ONLY });

    expect(context.division.value).toBe('riverside');
    expect(context.divisionKey()).toBe('riverside');
    expect(context.selectedDivisionName()).toBe('Riverside Base');
    expect(context.divisionOutsideLine()).toEqual({ key: 'riverside', name: 'Riverside Base', line: 'ground' });
    expect(context.divisionOptions().map((o) => o.label)).toEqual([context.labels().allReportingDivisions, 'Riverside Base (no ground transports)', 'Summit Base']);

    // Back on a line that has it, the plain name returns. Air + ground has it too.
    context.serviceLine.set('both');
    await answerLine(http, {});
    expect(context.divisionOutsideLine()).toBeNull();
    expect(context.divisionOptions().map((o) => o.label)).toContain('Riverside Base');
  });

  it('forgets a division the Provider no longer has when the list is asked for again, even under one Service Line', async () => {
    const { context, http } = setup();
    TestBed.tick();
    http.expectOne('/api/v1/providers/526/metrics/net-collections/periods').flush({ dataThrough: '2026-11-22', periods: [{ period: '202611' }] });
    await answerLine(http, { air: AIR_ONLY });
    context.division.setValue('riverside');
    await settle();

    context.retryDivisions();
    await answerLine(http, { air: GROUND_ONLY });

    expect(context.division.value).toBe(ALL_DIVISIONS);
  });

  it('sends the chosen division key only while it is in the list', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611']);
    context.division.setValue('summit');
    await settle();
    expect(context.divisionKey()).toBe('summit');
  });

  it('falls back to all when the chosen division is no longer in the list after a reload', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611']);
    context.division.setValue('summit');
    await settle();

    context.retryDivisions();
    await loadDivisions(http, { providerId: '526', divisions: [{ key: 'riverside', name: 'Riverside Base' }] });
    expect(context.division.value).toBe(ALL_DIVISIONS);
    expect(context.divisionKey()).toBeUndefined();
  });

  it('says loading first, and keeps a chosen key while the list is not here', async () => {
    const { context, http } = setup();
    TestBed.tick();
    expect(context.divisionsProblem()).toBe('loading');
    expect(context.divisionOptions()).toHaveLength(1);
    http.match((r) => r.url === DIVISIONS_URL);
  });

  it('reports an error without clearing the choice, and recovers on retry', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611']);
    context.division.setValue('summit');
    await settle();

    context.retryDivisions();
    TestBed.tick();
    http.expectOne((r) => r.url === DIVISIONS_URL).flush({ title: 'x' }, { status: 503, statusText: 'Service Unavailable' });
    await settle();
    expect(context.divisionsProblem()).toBe('error');
    expect(context.division.value).toBe('summit');
    expect(context.divisionKey()).toBeUndefined();

    context.retryDivisions();
    await loadDivisions(http);
    expect(context.divisionsProblem()).toBe('');
    expect(context.divisionKey()).toBe('summit');
  });

  it('has no problem and only All when the Provider has no divisions', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611']);
    context.retryDivisions();
    await loadDivisions(http, { providerId: '526', divisions: [] });
    expect(context.divisionsProblem()).toBe('');
    expect(context.divisionOptions()).toHaveLength(1);
  });

  it('keeps a hostile division name as plain text in the option label', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611']);
    expect(context.divisionOptions().at(-1)!.label).toBe('<img src=x onerror=alert(1)> Base');
  });
});
