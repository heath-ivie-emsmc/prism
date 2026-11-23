import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { AuthService } from '../auth/auth.service';
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
    await TestBed.inject(ApplicationRef).whenStable();
  }

  const settle = (): Promise<void> => TestBed.inject(ApplicationRef).whenStable();

  it('starts on the newest period once the list arrives', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611', '202610']);
    expect(context.period.value).toBe('202611');
  });

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

  it('falls back to all Reporting Divisions when a Service Line change removes the chosen one', async () => {
    const { context, http } = setup();
    await loadPeriods(http, ['202611']);
    context.serviceLine.set('air');
    context.division.setValue('a1');
    await settle();
    expect(context.divisionKey()).toBe('a1');

    context.serviceLine.set('ground');
    await settle();
    expect(context.division.value).toBe(ALL_DIVISIONS);
    expect(context.divisionKey()).toBeUndefined();
  });
});
