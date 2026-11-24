import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

/** The permission that gates claim-level data. The API checks it too; the browser only uses it to decide what to show. */
export const CLAIMS_PERMISSION = 'prism.claims.view';

export interface AttentionCountDto {
  readonly key: string;
  readonly name: string;
  readonly count: number;
}

/** A denial reason with how many denied claims have it. The text is warehouse text and may hold markup: it is only ever bound as text. */
export interface DenialReasonDto {
  readonly reason: string;
  /** The one row that stands for every reason outside the top ten. */
  readonly isOther: boolean;
  readonly count: number;
  readonly balanceDue: number;
}

/** One category of claim. A claim can be in more than one, so the categories overlap and are never added together. */
export interface AttentionCategoryDto {
  readonly key: 'denied' | 'on_hold' | 'in_collections';
  readonly name: string;
  readonly count: number;
  readonly balanceDue: number;
}

/** Claims needing attention by days left to file. Estimates: the filing limits are a placeholder. Always four rows. */
export interface FilingBandDto {
  readonly key: 'past_due' | 'due_within_10_days' | 'due_within_30_days' | 'due_later';
  readonly name: string;
  readonly claims: number;
  readonly balanceDue: number;
}

/** Response of GET .../claims/attention/counts. Aggregates only. */
export interface AttentionCountsDto {
  readonly providerId: string;
  readonly period: string;
  readonly dataThrough: string | null;
  readonly counts: readonly AttentionCountDto[];
  /** What is still owed on the denied claims. */
  readonly deniedBalanceDue: number;
  /** Denied claims divided by all claims in the period, 0 to 1. Null when there are no claims. */
  readonly deniedRate: number | null;
  readonly reasons: readonly DenialReasonDto[];
  readonly categories: readonly AttentionCategoryDto[];
  readonly filingBands: readonly FilingBandDto[];
}

/** One flagged claim. Every field is PHI: it is shown, never logged or stored by the browser. */
export interface AttentionClaimDto {
  readonly id: string;
  readonly claimRef: string;
  /** yyyy-MM-dd */
  readonly serviceDate: string;
  readonly payer: string | null;
  readonly payerCategory: string | null;
  readonly vehicle: string | null;
  readonly reportingDivision: string;
  readonly status: string;
  readonly denialCode: string | null;
  readonly denialReason: string | null;
  /** What is still owed, not an estimate of what will be paid. */
  readonly balanceDue: number;
  /** An estimate: the service date plus a filing limit for the payer category. yyyy-MM-dd */
  readonly filingDueDate: string;
  readonly daysLeft: number;
}

/** Response of GET .../claims/attention/{checkKey}. */
export interface AttentionClaimsDto {
  readonly providerId: string;
  readonly check: string;
  readonly period: string;
  readonly dataThrough: string | null;
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly claims: readonly AttentionClaimDto[];
}

/** What the counts and the claims are asked about. The Provider goes in the path, and the API checks it against the user's assignments. */
export interface AttentionQuery {
  readonly providerId: string;
  /** yyyyMM */
  readonly period: string;
  readonly line: 'air' | 'ground';
  /** An aircraft type or level of service key such as fixed-wing, or null for the whole Service Line. */
  readonly group: string | null;
}

export interface DownloadedFile {
  readonly blob: Blob;
  readonly fileName: string;
}

@Injectable({ providedIn: 'root' })
export class AttentionService {
  private readonly http = inject(HttpClient);

  counts(query: AttentionQuery): Observable<AttentionCountsDto> {
    return this.http.get<AttentionCountsDto>(`${this.base(query)}/counts`, { params: this.params(query) });
  }

  claims(query: AttentionQuery, check: string, page: number, pageSize: number): Observable<AttentionClaimsDto> {
    const params = this.params(query).set('page', page).set('pageSize', pageSize);
    return this.http.get<AttentionClaimsDto>(`${this.base(query)}/${encodeURIComponent(check)}`, { params });
  }

  /** The same claims as the list, as a CSV file. The server names the file; the fallback is used only if it sends no name. */
  exportCsv(query: AttentionQuery, check: string): Observable<DownloadedFile> {
    return this.http
      .get(`${this.base(query)}/${encodeURIComponent(check)}/export`, { params: this.params(query), observe: 'response', responseType: 'blob' })
      .pipe(map((response) => ({ blob: response.body ?? new Blob(), fileName: fileNameOf(response, check) })));
  }

  private base(query: AttentionQuery): string {
    return `/api/v1/providers/${encodeURIComponent(query.providerId)}/claims/attention`;
  }

  private params(query: AttentionQuery): HttpParams {
    let params = new HttpParams().set('period', query.period).set('serviceLine', query.line);
    if (query.group) params = params.set('group', query.group);
    return params;
  }
}

const NAME_PATTERN = /filename\*?=(?:UTF-8'')?"?([A-Za-z0-9._-]{1,100})"?/i;

/** The file name the server chose, or one built from the check. Only letters, digits, dots, hyphens and underscores are accepted. */
export function fileNameOf(response: HttpResponse<Blob>, check: string): string {
  const match = NAME_PATTERN.exec(response.headers.get('content-disposition') ?? '');
  return match?.[1] ?? `prism-needs-attention-${check.replace(/[^a-z0-9]+/g, '-')}.csv`;
}
