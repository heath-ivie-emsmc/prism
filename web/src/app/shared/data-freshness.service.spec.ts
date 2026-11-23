import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { DataFreshnessService, MIN_REVALIDATE_MS, freshnessInterceptor } from './data-freshness.service';

const PERIODS = '/api/v1/providers/526/metrics/net-collections/periods';

describe('DataFreshnessService', () => {
  let now = 1_000_000;

  afterEach(() => vi.restoreAllMocks());

  function setup(): { service: DataFreshnessService; http: HttpTestingController; client: HttpClient } {
    vi.spyOn(Date, 'now').mockImplementation(() => now);
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([freshnessInterceptor])), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush({ userName: 'jdoe', displayName: 'Jane', providers: [{ id: '526', name: 'P', facilityCount: 0 }], permissions: [] });
    return { service: TestBed.inject(DataFreshnessService), http, client: TestBed.inject(HttpClient) };
  }

  it('learns the date on screen from a Metric response and offers no update by itself', () => {
    const { service, http, client } = setup();
    client.get('/api/v1/providers/526/metrics/net-collections').subscribe();
    http.expectOne('/api/v1/providers/526/metrics/net-collections').flush({ dataThrough: '2026-11-22', points: [] });

    expect(service.shown()).toBe('2026-11-22');
    expect(service.updateAvailable()).toBe(false);
  });

  it('ignores a response without a real date, and responses from other routes', () => {
    const { service, http, client } = setup();
    client.get('/api/v1/providers/526/metrics/net-collections').subscribe();
    http.expectOne('/api/v1/providers/526/metrics/net-collections').flush({ dataThrough: 'tomorrow' });
    client.get('/api/v1/providers/526/saved-views').subscribe();
    http.expectOne('/api/v1/providers/526/saved-views').flush({ dataThrough: '2026-11-30' });

    expect(service.shown()).toBeNull();
  });

  it('does not check before any figures are on screen', () => {
    const { service, http } = setup();
    service.revalidate();
    http.expectNone(PERIODS);
  });

  it('raises the notice when the check finds a newer date, and leaves the figures alone until applied', () => {
    const { service, http } = setup();
    service.recordShown('526', '2026-11-22');

    service.revalidate();
    http.expectOne(PERIODS).flush({ providerId: '526', periods: [], dataThrough: '2026-11-23' });

    expect(service.updateAvailable()).toBe(true);
    expect(service.latest()).toBe('2026-11-23');
    expect(service.shown()).toBe('2026-11-22');
    expect(service.revision()).toBe(0);

    expect(service.apply()).toBe('2026-11-23');
    expect(service.shown()).toBe('2026-11-23');
    expect(service.revision()).toBe(1);
    expect(service.updateAvailable()).toBe(false);
  });

  it('does not treat its own check as figures on screen', () => {
    const { service, http } = setup();
    service.recordShown('526', '2026-11-22');
    service.revalidate();
    http.expectOne(PERIODS).flush({ dataThrough: '2026-11-23' });

    expect(service.shown()).toBe('2026-11-22');
  });

  it('checks at most once in the minimum interval and never twice at once', () => {
    const { service, http } = setup();
    service.recordShown('526', '2026-11-22');
    service.revalidate();
    service.revalidate();
    const first = http.expectOne(PERIODS);
    first.flush({ dataThrough: '2026-11-22' });

    now += MIN_REVALIDATE_MS - 1;
    service.revalidate();
    http.expectNone(PERIODS);

    now += 2;
    service.revalidate();
    http.expectOne(PERIODS).flush({ dataThrough: '2026-11-22' });
    expect(service.updateAvailable()).toBe(false);
  });

  it('stays quiet when the check fails', () => {
    const { service, http } = setup();
    service.recordShown('526', '2026-11-22');
    service.revalidate();
    http.expectOne(PERIODS).flush({ title: 'x' }, { status: 503, statusText: 'Service Unavailable' });

    expect(service.updateAvailable()).toBe(false);
  });

  it('applies nothing when there is no update', () => {
    const { service } = setup();
    service.recordShown('526', '2026-11-22');
    expect(service.apply()).toBeNull();
    expect(service.revision()).toBe(0);
  });

  it('checks again when the window regains focus', () => {
    const { service, http } = setup();
    service.recordShown('526', '2026-11-22');
    window.dispatchEvent(new Event('focus'));
    http.expectOne(PERIODS).flush({ dataThrough: '2026-11-22' });
    expect(service.updateAvailable()).toBe(false);
  });

  it('checks again when the tab becomes visible', () => {
    const { service, http } = setup();
    service.recordShown('526', '2026-11-22');
    document.dispatchEvent(new Event('visibilitychange'));
    http.expectOne(PERIODS).flush({ dataThrough: '2026-11-22' });
    expect(service.updateAvailable()).toBe(false);
  });

  it('follows the active Provider, not another one the user may see', () => {
    const { service, http, client } = setup();
    const auth = TestBed.inject(AuthService);
    client.get('/api/v1/providers/526/metrics/net-collections').subscribe();
    http.expectOne('/api/v1/providers/526/metrics/net-collections').flush({ dataThrough: '2026-11-22' });
    expect(service.shown()).toBe('2026-11-22');

    // Two Providers: the first is active, so a figure loaded for the second is not what is on screen.
    auth.refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush({ userName: 'jdoe', displayName: 'Jane', providers: [{ id: '526', name: 'P', facilityCount: 0 }, { id: '527', name: 'Q', facilityCount: 0 }], permissions: [] });
    client.get('/api/v1/providers/527/metrics/net-collections').subscribe();
    http.expectOne('/api/v1/providers/527/metrics/net-collections').flush({ dataThrough: '2026-12-31' });
    expect(service.shown()).toBe('2026-11-22');

    // The active Provider changes: the date on screen is that Provider's own.
    auth.refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush({ userName: 'jdoe', displayName: 'Jane', providers: [{ id: '527', name: 'Q', facilityCount: 0 }, { id: '526', name: 'P', facilityCount: 0 }], permissions: [] });
    expect(service.shown()).toBe('2026-12-31');
  });
});
