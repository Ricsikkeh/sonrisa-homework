import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { parseApiError } from './api-error';

describe('parseApiError', () => {
  it('preserves valid extensions without exposing unsafe problem text', () => {
    const result = parseApiError(
      new HttpErrorResponse({
        status: 400,
        error: {
          type: 'about:blank',
          title: 'SECRET TITLE',
          status: 400,
          detail: 'SECRET DETAIL',
          errors: { name: ['SECRET FIELD'] },
          code: 'invalid_input',
          traceId: 'trace-123',
        },
      }),
    );
    expect(result.kind).toBe('bad-request');
    expect(result.message).not.toMatch(/SECRET/);
    expect(result.code).toBe('invalid_input');
    expect(result.traceId).toBe('trace-123');
  });

  it.each(
    [
      null,
      undefined,
      '<html>oops</html>',
      'plain text',
      [],
      { type: 'x', title: 'x', status: 401, code: 'invalid_input', traceId: 'trace-123' },
      { title: 'x', status: 400, code: 'invalid_input', traceId: 'trace-123' },
      { type: 'x', title: 12, status: 400, code: 'invalid_input', traceId: 'trace-123' },
      { type: 'x', title: 'x', status: '400', code: 'invalid_input', traceId: 'trace-123' },
    ].map((body) => ({ body })),
  )('ignores malformed body $body', ({ body }) => {
    const result = parseApiError(new HttpErrorResponse({ status: 400, error: body }));
    expect(result.kind).toBe('bad-request');
    expect(result.code).toBeUndefined();
    expect(result.traceId).toBeUndefined();
  });

  it('classifies network and timeout status zero', () => {
    expect(parseApiError(new HttpErrorResponse({ status: 0 })).kind).toBe('network');
    expect(
      parseApiError(new HttpErrorResponse({ status: 0, statusText: 'Timeout' })).message,
    ).toMatch(/connect/i);
  });

  it.each([
    [400, 'bad-request'],
    [401, 'unauthorized'],
    [403, 'forbidden'],
    [404, 'not-found'],
    [409, 'conflict'],
    [500, 'server'],
    [503, 'unavailable'],
  ] as const)('uses safe local message for %i', (status, kind) => {
    const result = parseApiError(
      new HttpErrorResponse({ status, error: { title: 'unsafe', detail: 'unsafe' } }),
    );
    expect(result.kind).toBe(kind);
    expect(result.message).not.toMatch(/unsafe/);
  });

  it.each([
    ['BAD', 'bad value'],
    ['code\n', 'trace\n'],
    ['a'.repeat(65), 'a'.repeat(129)],
    [123, { secret: 'not an identifier' }],
    ['', ''],
  ])('rejects invalid extension values %j and %j', (code, traceId) => {
    const result = parseApiError(
      new HttpErrorResponse({
        status: 400,
        error: {
          type: 'x',
          title: 'x',
          status: 400,
          code,
          traceId,
        },
      }),
    );
    expect(result.code).toBeUndefined();
    expect(result.traceId).toBeUndefined();
  });

  it('uses generic fallback for unknown errors and statuses', () => {
    expect(parseApiError(new Error('secret')).kind).toBe('unknown');
    expect(parseApiError(new HttpErrorResponse({ status: 418 })).kind).toBe('unknown');
  });
});
