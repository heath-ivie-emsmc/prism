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

/** A payer group with its transport counts for the chosen month and the one before. */
export interface PayerMixShareDto {
  readonly key: string;
  readonly name: string;
  readonly current: number;
  readonly prior: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/payer-mix. */
export interface PayerMixDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly priorPeriod: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly group: string | null;
  readonly groupName: string | null;
  readonly shares: readonly PayerMixShareDto[];
}

/** Transports and loaded miles (statute) in one month of service. */
export interface TransportVolumePointDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly transports: number;
  readonly loadedMiles: number;
}

/** The last month's transports for one aircraft type or level of service. */
export interface TransportVolumeGroupDto {
  readonly key: string;
  readonly name: string;
  readonly serviceLine: 'air' | 'ground';
  readonly transports: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/transports. */
export interface TransportVolumeDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly endPeriod: string | null;
  readonly months: number;
  readonly serviceLine: 'all' | 'air' | 'ground';
  /** True while the last month's accounting period is open, so transports for it may still be added. */
  readonly periodOpen: boolean;
  readonly points: readonly TransportVolumePointDto[];
  readonly groups: readonly TransportVolumeGroupDto[];
}

/** Claims accepted first time, rejected and in total for one month of service, and the rate (0 to 1, null when there were no claims). */
export interface CleanClaimPointDto {
  readonly period: string;
  readonly label: string;
  readonly year: number;
  readonly cleanClaims: number;
  readonly rejectedClaims: number;
  readonly totalClaims: number;
  readonly rate: number | null;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/clean-claims. The newest point can be older than `endPeriod`. */
export interface CleanClaimRateDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly endPeriod: string | null;
  readonly months: number;
  readonly snapshotDate: string | null;
  readonly points: readonly CleanClaimPointDto[];
}

/** Cash posted in one month for transports of one payer group that were a given number of months old when paid. */
export interface PaymentAgeCellDto {
  readonly payerKey: string;
  readonly payerName: string;
  readonly postingPeriod: string;
  readonly ageMonths: number;
  readonly cash: number;
  readonly transports: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/payment-age. */
export interface PaymentAgeDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly group: string | null;
  readonly cells: readonly PaymentAgeCellDto[];
}

/** Matured net cash per transport for one payer group. `cashPerTransport` is null when there are no transports. */
export interface PayerCashPerTransportDto {
  readonly key: string;
  readonly name: string;
  readonly transports: number;
  readonly cash: number;
  readonly cashPerTransport: number | null;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/cash-per-transport. */
export interface CashPerTransportDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly cohortFrom: string | null;
  readonly cohortThrough: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly group: string | null;
  readonly payers: readonly PayerCashPerTransportDto[];
}

/** Cash posted in one month for one aircraft type or level of service. */
export interface NetCashByGroupPointDto {
  readonly groupKey: string;
  readonly groupName: string;
  readonly serviceLine: 'air' | 'ground';
  readonly period: string;
  readonly cash: number;
  readonly transports: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/by-group. */
export interface NetCashByGroupDto {
  readonly metric: MetricDto;
  readonly providerId: string;
  readonly period: string | null;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly points: readonly NetCashByGroupPointDto[];
}

/** Reads net cash for a Provider. The Provider is in the path, and the API checks it against the user's assignments on every call. */
@Injectable({ providedIn: 'root' })
export class NetCollectionsService {
  private readonly http = inject(HttpClient);

  periods(providerId: string): Observable<NetCollectionsPeriodsDto> {
    return this.http.get<NetCollectionsPeriodsDto>(`${this.base(providerId)}/periods`);
  }

  series(providerId: string, endPeriod: string, months: number, serviceLine: 'all' | 'air' | 'ground' = 'all'): Observable<NetCollectionsDto> {
    const params = new HttpParams().set('endPeriod', endPeriod).set('months', months).set('serviceLine', serviceLine);
    return this.http.get<NetCollectionsDto>(this.base(providerId), { params });
  }

  projection(providerId: string, afterPeriod: string, serviceLine: 'all' | 'air' | 'ground'): Observable<NetCashProjectionDto> {
    const params = new HttpParams().set('endPeriod', afterPeriod).set('serviceLine', serviceLine);
    return this.http.get<NetCashProjectionDto>(`${this.base(providerId)}/projection`, { params });
  }

  breakdown(providerId: string, period: string, serviceLine: 'all' | 'air' | 'ground'): Observable<NetCashBreakdownDto> {
    const params = new HttpParams().set('period', period).set('serviceLine', serviceLine);
    return this.http.get<NetCashBreakdownDto>(`${this.base(providerId)}/breakdown`, { params });
  }

  payerMix(providerId: string, period: string, serviceLine: 'all' | 'air' | 'ground', group?: string): Observable<PayerMixDto> {
    return this.http.get<PayerMixDto>(`${this.base(providerId)}/payer-mix`, { params: this.periodParams(period, serviceLine, group) });
  }

  paymentAge(providerId: string, period: string, serviceLine: 'all' | 'air' | 'ground'): Observable<PaymentAgeDto> {
    return this.http.get<PaymentAgeDto>(`${this.base(providerId)}/payment-age`, { params: this.periodParams(period, serviceLine) });
  }

  cashPerTransport(providerId: string, period: string, serviceLine: 'all' | 'air' | 'ground'): Observable<CashPerTransportDto> {
    return this.http.get<CashPerTransportDto>(`${this.base(providerId)}/cash-per-transport`, { params: this.periodParams(period, serviceLine) });
  }

  byGroup(providerId: string, period: string, serviceLine: 'all' | 'air' | 'ground'): Observable<NetCashByGroupDto> {
    return this.http.get<NetCashByGroupDto>(`${this.base(providerId)}/by-group`, { params: this.periodParams(period, serviceLine) });
  }

  private periodParams(period: string, serviceLine: string, group?: string): HttpParams {
    const params = new HttpParams().set('period', period).set('serviceLine', serviceLine);
    return group ? params.set('group', group) : params;
  }

  transports(providerId: string, endPeriod: string, months: number, serviceLine: 'all' | 'air' | 'ground'): Observable<TransportVolumeDto> {
    const params = new HttpParams().set('endPeriod', endPeriod).set('months', months).set('serviceLine', serviceLine);
    return this.http.get<TransportVolumeDto>(`${this.base(providerId)}/transports`, { params });
  }

  cleanClaims(providerId: string, endPeriod: string, months: number): Observable<CleanClaimRateDto> {
    const params = new HttpParams().set('endPeriod', endPeriod).set('months', months);
    return this.http.get<CleanClaimRateDto>(`${this.base(providerId)}/clean-claims`, { params });
  }

  private base(providerId: string): string {
    return `/api/v1/providers/${encodeURIComponent(providerId)}/metrics/net-collections`;
  }
}
