import { HttpParams } from '@angular/common/http';

/** What any Metric loader needs. It comes from the page filters and the saved options, never from a stored value. */
export interface MetricQuery {
  readonly providerId: string;
  readonly period: string;
  readonly serviceLine: 'all' | 'air' | 'ground';
  readonly months: number;
  /** A Reporting Division key, sent only to Metrics the catalog says honor it. Absent means all divisions. */
  readonly division?: string;
}

/** The Provider is in the path, and the API checks it against the user's assignments on every call. */
export function metricsBase(providerId: string): string {
  return `/api/v1/providers/${encodeURIComponent(providerId)}/metrics/net-collections`;
}

export function periodParams(period: string, serviceLine: string, group?: string, division?: string): HttpParams {
  let params = new HttpParams().set('period', period).set('serviceLine', serviceLine);
  if (group) params = params.set('group', group);
  return division ? params.set('division', division) : params;
}

/** The period, Service Line and Division, plus the number of months for the Metrics that draw a trend. */
export function trendParams(q: Pick<MetricQuery, 'period' | 'serviceLine' | 'months' | 'division'>): HttpParams {
  return periodParams(q.period, q.serviceLine, undefined, q.division).set('months', q.months);
}
