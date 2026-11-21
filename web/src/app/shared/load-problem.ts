import { HttpErrorResponse } from '@angular/common/http';

/** What to tell a customer when figures could not be loaded. Plain words, no status codes or server text. */
export interface LoadProblem {
  readonly title: string;
  readonly detail: string;
  /** A trace id support can find in the logs, or null when the response had none. */
  readonly reference: string | null;
  /** Trying again can help: the server or the network, not a missing or forbidden resource. */
  readonly retryable: boolean;
}

/** A W3C trace id as the API puts it in a problem response: `00-<32 hex>-<16 hex>-<2 hex>`, or the bare 32 hex. */
const TRACE_ID = /^(?:[0-9a-f]{2}-)?([0-9a-f]{32})(?:-[0-9a-f]{16}-[0-9a-f]{2})?$/i;

/** The HTTP error inside whatever a resource, an interceptor or a try/catch handed over. */
function httpError(error: unknown): HttpErrorResponse | null {
  if (error instanceof HttpErrorResponse) return error;
  const cause = (error as { cause?: unknown } | null)?.cause;
  return cause instanceof HttpErrorResponse ? cause : null;
}

/** The trace id from a problem response, only if it is shaped like one: nothing else from the body is ever shown. */
export function problemReference(error: unknown): string | null {
  const body = httpError(error)?.error as { traceId?: unknown } | null | undefined;
  const id = typeof body?.traceId === 'string' ? TRACE_ID.exec(body.traceId.slice(0, 80))?.[1] : undefined;
  return id ? id.toLowerCase() : null;
}

/** Calm wording for a failed load, by what kind of failure it was. `subject` is what could not load, such as "net cash". */
export function describeProblem(error: unknown, subject: string): LoadProblem {
  const status = httpError(error)?.status;
  const reference = problemReference(error);
  if (status === 0) {
    return { title: `We can't reach Prism right now`, detail: 'Check your internet connection, then try again.', reference, retryable: true };
  }
  if (status === 403 || status === 404) {
    return { title: `We couldn't find ${subject} for your account`, detail: 'If you expected to see it, contact your administrator.', reference, retryable: false };
  }
  if (status === 502 || status === 503 || status === 504) {
    return {
      title: `We can't load ${subject} right now`,
      detail: 'This is usually brief and nothing you did caused it. Please try again in a minute or two.',
      reference,
      retryable: true,
    };
  }
  return {
    title: `Something went wrong loading ${subject}`,
    detail: 'Please try again. If it keeps happening, contact support and give them the reference below.',
    reference,
    retryable: true,
  };
}
