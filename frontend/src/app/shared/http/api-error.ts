import { HttpErrorResponse } from '@angular/common/http';

export type ApiErrorKind =
  | 'bad-request'
  | 'unauthorized'
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'server'
  | 'unavailable'
  | 'network'
  | 'unknown';

export interface ApiError {
  kind: ApiErrorKind;
  message: string;
  code?: string;
  traceId?: string;
}

const messages: Record<number, { kind: ApiErrorKind; message: string }> = {
  400: {
    kind: 'bad-request',
    message: 'The request could not be completed. Check your information and try again.',
  },
  401: { kind: 'unauthorized', message: 'Please sign in to continue.' },
  403: { kind: 'forbidden', message: 'You do not have permission to do that.' },
  404: { kind: 'not-found', message: 'The requested item could not be found.' },
  409: {
    kind: 'conflict',
    message: 'This change conflicts with the current state. Refresh and try again.',
  },
  500: { kind: 'server', message: 'Something went wrong on our end. Please try again later.' },
  503: {
    kind: 'unavailable',
    message: 'The service is temporarily unavailable. Please try again later.',
  },
};

const fallback: ApiError = { kind: 'unknown', message: 'Something went wrong. Please try again.' };

/** Uses local messages only: server diagnostics and field text are never presentation content. */
export function parseApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) return { ...fallback };
  if (error.status === 0) {
    return { kind: 'network', message: 'Unable to connect. Check your connection and try again.' };
  }

  const result: ApiError = { ...(messages[error.status] ?? fallback) };
  const body: unknown = error.error;
  if (body === null || typeof body !== 'object' || Array.isArray(body)) return result;

  const problem = body as Record<string, unknown>;
  if (
    typeof problem['type'] !== 'string' ||
    typeof problem['title'] !== 'string' ||
    problem['status'] !== error.status
  )
    return result;

  const code = problem['code'];
  const traceId = problem['traceId'];
  if (typeof code === 'string' && /^[a-z][a-z0-9_]{0,63}$/.test(code)) result.code = code;
  if (typeof traceId === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(traceId))
    result.traceId = traceId;
  return result;
}
