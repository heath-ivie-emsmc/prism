import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { CatalogMetricDto } from '../saved-views/layout.model';
import { MetricCatalogService, metricDivisionSupport } from './metric-catalog.service';

const metric = (key: string, over: Partial<CatalogMetricDto> = {}): CatalogMetricDto => ({
  key, name: key, description: 'd', unit: 'x', category: 'cash', status: 'available', placeable: true, claimLevel: false,
  serviceLines: ['air'], dimensions: [], presentations: [], options: [], ...over,
});

describe('metricDivisionSupport', () => {
  it('reads the catalog context, and falls back to the division dimension for an older API', () => {
    expect(metricDivisionSupport(metric('a', { context: { divisionSupport: 'supported' } }))).toBe('supported');
    expect(metricDivisionSupport(metric('a', { context: { divisionSupport: 'none' }, dimensions: ['division'] }))).toBe('none');
    expect(metricDivisionSupport(metric('a', { dimensions: ['division'] }))).toBe('supported');
    expect(metricDivisionSupport(metric('a'))).toBe('none');
  });
});

describe('MetricCatalogService', () => {
  it('loads the catalog once for the first Provider and answers per Metric', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush({ userName: 'j', displayName: 'J', providers: [{ id: '526', name: 'P', facilityCount: 0 }], permissions: [] });
    const service = TestBed.inject(MetricCatalogService);
    expect(service.loaded()).toBe(false);
    expect(service.divisionSupport('payer_mix')).toBeUndefined();

    TestBed.tick();
    http.expectOne('/api/v1/providers/526/metrics/catalog').flush({
      categories: [],
      metrics: [metric('payer_mix', { context: { divisionSupport: 'supported' } }), metric('clean_claim_rate', { context: { divisionSupport: 'none' } })],
    });
    await Promise.resolve();
    TestBed.tick();

    expect(service.loaded()).toBe(true);
    expect(service.honorsDivision('payer_mix')).toBe(true);
    expect(service.honorsDivision('clean_claim_rate')).toBe(false);
    expect(service.divisionSupport('unknown')).toBeUndefined();
  });
});
