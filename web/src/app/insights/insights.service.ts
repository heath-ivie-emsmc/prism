import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type InsightLabelDto = 'temporary' | 'structural' | 'watch' | 'info';

/** One finding. `impact` is USD and signed: negative lowered cash, or is expected to. */
export interface InsightCardDto {
  readonly kind: string;
  readonly label: InsightLabelDto;
  readonly title: string;
  readonly body: string;
  readonly impact: number;
}

/** The part of the change no driver explains, how it compares with the usual month-to-month range, and the sentence for it. */
export interface InsightRemainderDto {
  readonly amount: number;
  readonly judgment: 'withinNormalRange' | 'outsideNormalRange' | 'notEnoughHistory';
  readonly text: string;
}

/** Response of GET /api/v1/providers/{providerId}/insights/what-changed. The API answers 204 (no body) when neither month has cash to explain. */
export interface WhatChangedDto {
  readonly providerId: string;
  readonly period: string;
  readonly priorPeriod: string;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly group: string | null;
  readonly groupName: string;
  /** The Reporting Division key applied, else null, and its name. */
  readonly division?: string | null;
  readonly divisionName?: string | null;
  /** Date of the warehouse load the figures come from, yyyy-MM-dd. */
  readonly dataThrough: string | null;
  readonly headline: string;
  readonly cashNow: number;
  readonly cashPrior: number;
  readonly delta: number;
  readonly deltaRatio: number | null;
  readonly normalizedDeltaRatio: number | null;
  readonly drivers: readonly InsightCardDto[];
  readonly expected: readonly InsightCardDto[];
  readonly remainder: InsightRemainderDto;
}

/** Reads the written explanation of what changed. The Provider is in the path, and the API checks it against the user's assignments on every call. */
@Injectable({ providedIn: 'root' })
export class InsightsService {
  private readonly http = inject(HttpClient);

  whatChanged(providerId: string, period: string, serviceLine: 'air' | 'ground', group: string, division?: string): Observable<WhatChangedDto | null> {
    let params = new HttpParams().set('period', period).set('serviceLine', serviceLine).set('group', group);
    if (division) params = params.set('division', division);
    return this.http.get<WhatChangedDto | null>(`/api/v1/providers/${encodeURIComponent(providerId)}/insights/what-changed`, { params });
  }
}
