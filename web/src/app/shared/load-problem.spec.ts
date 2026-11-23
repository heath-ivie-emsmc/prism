import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { describeProblem, problemReference } from './load-problem';

const TRACE = '77114806214001843f6761e5254e96e3';
const http = (status: number, body: unknown = null) => new HttpErrorResponse({ status, error: body });

describe('load problems', () => {
  it('says a warehouse outage is brief and safe to retry, without the server text', () => {
    const problem = describeProblem(http(503, { title: 'The data warehouse is not available. Try again shortly.', traceId: `00-${TRACE}-cfc570a3d3f659d3-03` }), 'net cash');

    expect(problem.title).toBe(`We can't load net cash right now`);
    expect(problem.retryable).toBe(true);
    expect(problem.reference).toBe(TRACE);
    expect(JSON.stringify(problem)).not.toContain('warehouse');
  });

  it('tells a person with no connection to check it', () => {
    expect(describeProblem(http(0), 'net cash').detail).toContain('internet connection');
  });

  it('does not offer a retry for something the account cannot see', () => {
    expect(describeProblem(http(404), 'the breakdown')).toMatchObject({ retryable: false, title: `We couldn't find the breakdown for your account` });
    expect(describeProblem(http(403), 'x').retryable).toBe(false);
  });

  it('asks the person to wait when reads are rate limited, and to narrow the request when too many claims match', () => {
    expect(describeProblem(http(429), 'the claims')).toMatchObject({ retryable: true, title: `We're loading the claims too often` });
    expect(describeProblem(http(429), 'x').detail).toContain('Wait a minute');
    expect(describeProblem(http(422), 'the claims')).toMatchObject({ retryable: false });
    expect(describeProblem(http(422), 'x').detail).toContain('10,000');
  });

  it('points to support for other server errors', () => {
    expect(describeProblem(http(500), 'net cash')).toMatchObject({ title: 'Something went wrong loading net cash', retryable: true });
  });

  it('finds the error inside a resource wrapper', () => {
    expect(problemReference({ cause: http(503, { traceId: TRACE }) })).toBe(TRACE);
  });

  it('shows a reference only if it is shaped like a trace id', () => {
    expect(problemReference(http(503, { traceId: '<script>alert(1)</script>' }))).toBeNull();
    expect(problemReference(http(503, { traceId: 'x'.repeat(5000) }))).toBeNull();
    expect(problemReference(http(503, { traceId: 42 }))).toBeNull();
    expect(problemReference(http(503))).toBeNull();
    expect(problemReference(new Error('boom'))).toBeNull();
    expect(problemReference(null)).toBeNull();
  });
});
