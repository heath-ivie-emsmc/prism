import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** What a Metric is, as the API describes it. */
export interface MetricDto {
  readonly key: string;
  readonly name: string;
  readonly description: string;
  readonly unit: string;
}

/** One month: `period` is yyyyMM, `label` the short month name. */
export interface NetCollectionsPointDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly value: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections. */
export interface NetCollectionsDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly endPeriod: string | null;
  readonly months: number;
  /** Date of the warehouse load the figures come from, yyyy-MM-dd. */
  readonly snapshotDate: string | null;
  readonly points: readonly NetCollectionsPointDto[];
}

export interface PeriodDto {
  readonly period: string;
  readonly label: string;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/periods. */
export interface NetCollectionsPeriodsDto {
  readonly providerId: string;
  readonly periods: readonly PeriodDto[];
}

/** One projected month: the projected cash and the likely range. */
export interface ProjectionMonthDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly value: number;
  readonly low: number;
  readonly high: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/projection. Total, low and high are sums over the months. */
export interface NetCashProjectionDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly afterPeriod: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly snapshotDate: string | null;
  readonly total: number;
  readonly low: number;
  readonly high: number;
  readonly months: readonly ProjectionMonthDto[];
}

/** Net cash, transports (encounters with cash posted in the month), loaded miles and the two mix counts (scene and interfacility for air, emergency and non-emergency for ground). */
export interface BreakdownFiguresDto {
  readonly cash: number;
  readonly transports: number;
  readonly loadedMiles: number;
  readonly primaryMix: number;
  readonly secondaryMix: number;
}

/** An aircraft type or level of service. */
export interface BreakdownGroupDto {
  readonly key: string;
  readonly name: string;
  readonly serviceLine: 'air' | 'ground';
  readonly current: BreakdownFiguresDto;
  readonly prior: BreakdownFiguresDto;
}

/** A vehicle (tail number or unit) at a base or station. */
export interface BreakdownRowDto {
  readonly groupKey: string;
  readonly groupName: string;
  readonly serviceLine: 'air' | 'ground';
  readonly base: string | null;
  readonly vehicle: string;
  readonly current: BreakdownFiguresDto;
  readonly prior: BreakdownFiguresDto;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/breakdown. */
export interface NetCashBreakdownDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly priorPeriod: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  /** True while the accounting period is still open: cash is still posting into it, so the figures are not final. */
  readonly periodOpen: boolean;
  readonly groups: readonly BreakdownGroupDto[];
  readonly rows: readonly BreakdownRowDto[];
}

/** Reads net cash for a Provider. The Provider is in the path, and the API checks it against the user's assignments on every call. */
@Injectable({ providedIn: 'root' })
export class NetCollectionsService {
  private readonly http = inject(HttpClient);

  periods(providerId: string): Observable<NetCollectionsPeriodsDto> {
    return this.http.get<NetCollectionsPeriodsDto>(`${this.base(providerId)}/periods`);
  }

  series(providerId: string, endPeriod: string, months: number): Observable<NetCollectionsDto> {
    const params = new HttpParams().set('endPeriod', endPeriod).set('months', months);
    return this.http.get<NetCollectionsDto>(this.base(providerId), { params });
  }

  projection(providerId: string, afterPeriod: string, serviceLine: 'all' | 'air' | 'ground'): Observable<NetCashProjectionDto> {
    const params = new HttpParams().set('endPeriod', afterPeriod).set('serviceLine', serviceLine);
    return this.http.get<NetCashProjectionDto>(`${this.base(providerId)}/projection`, { params });
  }

  breakdown(providerId: string, period: string, serviceLine: 'air' | 'ground'): Observable<NetCashBreakdownDto> {
    const params = new HttpParams().set('period', period).set('serviceLine', serviceLine);
    return this.http.get<NetCashBreakdownDto>(`${this.base(providerId)}/breakdown`, { params });
  }

  private base(providerId: string): string {
    return `/api/v1/providers/${encodeURIComponent(providerId)}/metrics/net-collections`;
  }
}
