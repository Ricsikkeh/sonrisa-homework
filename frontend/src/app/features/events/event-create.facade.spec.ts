import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthFacade } from '../auth/auth.facade';
import { EventCreateFacade, EventDraft } from './event-create.facade';

const typeId = '11111111-1111-4111-8111-111111111111';
const draft: EventDraft = { eventTypeId: typeId, title: 'Storm', message: 'Message' };
const created = {
  id: '22222222-2222-4222-8222-222222222222',
  ...draft,
  eventTypeName: 'Weather',
  publishedAt: '2026-10-31T12:00:00Z',
};

describe('EventCreateFacade', () => {
  let facade: EventCreateFacade;
  let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [EventCreateFacade, provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthFacade).restore();
    http.expectOne('/api/auth/me').flush({ id: 'owner-a', userName: 'alice', roles: ['Editor'] });
    facade = TestBed.inject(EventCreateFacade);
    TestBed.tick();
  });
  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });
  function load() {
    facade.load();
    http.expectOne('/api/events/event-types').flush([{ id: typeId, name: 'Weather' }]);
  }
  function csrf() {
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'fresh' });
  }
  it('loads empty/error/retry states and blocks submit while loading', () => {
    facade.load();
    facade.submit(draft).subscribe((result) => expect(result).toBe(false));
    http.expectNone('/api/auth/csrf');
    http.expectOne('/api/events/event-types').flush(null, { status: 500, statusText: 'Error' });
    expect(facade.error()).toBeTruthy();
    facade.load();
    http.expectOne('/api/events/event-types').flush([]);
    expect(facade.types()).toEqual([]);
    facade.submit(draft).subscribe((result) => expect(result).toBe(false));
    http.expectNone('/api/auth/csrf');
    load();
    expect(facade.error()).toBeNull();
  });
  it('uses fresh CSRF/key, blocks simultaneous submission, resets after confirmed success', () => {
    load();
    facade.submit(draft).subscribe((result) => expect(result).toBe(true));
    facade.submit(draft).subscribe((result) => expect(result).toBe(false));
    expect(facade.busy()).toBe(true);
    csrf();
    const request = http.expectOne('/api/events');
    const key = request.request.headers.get('Idempotency-Key');
    expect(key).toMatch(/^[0-9a-f-]{36}$/i);
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('fresh');
    expect(request.request.headers.get('X-Expected-User-Id')).toBe('owner-a');
    expect(request.request.body).toEqual(draft);
    request.flush(created);
    expect(facade.busy()).toBe(false);
    expect(facade.pending()).toBeNull();
    expect(facade.result()?.id).toBe(created.id);
    facade.submit(draft).subscribe();
    csrf();
    const next = http.expectOne('/api/events');
    expect(next.request.headers.get('Idempotency-Key')).not.toBe(key);
    next.flush(created);
  });
  it('keeps identical key/payload for an explicit response-loss retry and never retries automatically', () => {
    load();
    facade.submit(draft).subscribe((result) => expect(result).toBe(false));
    csrf();
    const request = http.expectOne('/api/events');
    const key = request.request.headers.get('Idempotency-Key');
    request.error(new ProgressEvent('error'));
    expect(facade.pending()?.payload).toEqual(draft);
    expect(facade.error()).toContain('may have succeeded');
    http.expectNone('/api/auth/csrf');
    facade.submit({ ...draft, title: 'Changed' }).subscribe((result) => expect(result).toBe(false));
    facade.retry().subscribe((result) => expect(result).toBe(true));
    csrf();
    const retry = http.expectOne('/api/events');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    expect(retry.request.body).toEqual(draft);
    retry.flush(created);
  });
  it('recovers a validated pending request after recreation but never auto-replays', () => {
    load();
    facade.submit(draft).subscribe();
    csrf();
    const request = http.expectOne('/api/events');
    const key = request.request.headers.get('Idempotency-Key');
    request.error(new ProgressEvent('error'));
    const injector = TestBed.inject(AuthFacade);
    // A new provider instance simulates leaving/re-entering the route in this tab.
    const recovered = TestBed.runInInjectionContext(() => new EventCreateFacade());
    expect(recovered.pending()?.key).toBe(key);
    http.expectNone('/api/events');
    recovered.retry().subscribe();
    csrf();
    const retry = http.expectOne('/api/events');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush(created);
    expect(injector.user()?.id).toBe('owner-a');
  });
  it('never exposes/replays a different account pending request', () => {
    load();
    facade.submit(draft).subscribe();
    csrf();
    http.expectOne('/api/events').error(new ProgressEvent('error'));
    const auth = TestBed.inject(AuthFacade);
    auth.restore();
    http.expectOne('/api/auth/me').flush({ id: 'owner-b', userName: 'bob', roles: ['Editor'] });
    TestBed.tick();
    expect(facade.pending()).toBeNull();
    expect(facade.result()).toBeNull();
    facade.retry().subscribe((result) => expect(result).toBe(false));
    http.expectNone('/api/auth/csrf');
  });
  for (const status of [401, 403])
    it(`blocks further writes after ${status}`, () => {
      load();
      facade.submit(draft).subscribe();
      csrf();
      http.expectOne('/api/events').flush(null, { status, statusText: 'Denied' });
      expect(facade.accessDenied()).toBe(true);
      facade.retry().subscribe((result) => expect(result).toBe(false));
      http.expectNone('/api/auth/csrf');
      load();
      expect(facade.accessDenied()).toBe(false);
    });
  for (const code of [
    'event_type_unavailable',
    'idempotency_key_conflict',
    'idempotency_request_in_progress',
    'antiforgery_failed',
  ])
    it(`maps ${code} safely without discarding pending content`, () => {
      load();
      facade.submit(draft).subscribe();
      csrf();
      http
        .expectOne('/api/events')
        .flush(
          { type: 'about:blank', title: 'Conflict', status: 409, code, detail: 'SECRET' },
          { status: 409, statusText: 'Conflict' },
        );
      expect(facade.pending()?.payload).toEqual(draft);
      expect(facade.error()).toBeTruthy();
      expect(facade.error()).not.toContain('SECRET');
      const expected: Record<string, string> = {
        event_type_unavailable: 'selected event type',
        idempotency_key_conflict: 'different content',
        idempotency_request_in_progress: 'temporarily busy',
        antiforgery_failed: 'session changed',
      };
      expect(facade.error()).toContain(expected[code]);
      http.expectNone('/api/events');
    });
  it('blocks a stale-account cookie response until an explicit session check and reload', () => {
    load();
    facade.submit(draft).subscribe();
    csrf();
    http
      .expectOne('/api/events')
      .flush(
        { type: 'about:blank', title: 'Conflict', status: 409, code: 'event_session_changed' },
        { status: 409, statusText: 'Conflict' },
      );
    expect(facade.accessDenied()).toBe(true);
    facade.retry().subscribe((result) => expect(result).toBe(false));
    http.expectNone('/api/auth/csrf');
    facade.checkSession();
    http.expectOne('/api/auth/me').flush({ id: 'owner-b', userName: 'bob', roles: ['Editor'] });
    TestBed.tick();
    expect(facade.pending()).toBeNull();
    http.expectNone('/api/events');
  });
  it('treats a malformed success as ambiguous and retains its key', () => {
    load();
    facade.submit(draft).subscribe((result) => expect(result).toBe(false));
    csrf();
    http.expectOne('/api/events').flush({ id: 'not-an-event' });
    expect(facade.pending()?.payload).toEqual(draft);
    expect(facade.result()).toBeNull();
  });
  it('refuses to send if tab retry state cannot be saved', () => {
    load();
    const write = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('Storage disabled');
    });
    try {
      facade.submit(draft).subscribe((result) => expect(result).toBe(false));
      expect(facade.pending()).toBeNull();
      expect(facade.error()).toContain('session storage');
      http.expectNone('/api/auth/csrf');
      http.expectNone('/api/events');
    } finally {
      write.mockRestore();
    }
  });
  it('ignores malformed or wrong-account stored retry information', () => {
    for (const saved of [
      '{',
      JSON.stringify({ version: 1, ownerId: 'owner-b', key: 'forged', payload: draft }),
    ]) {
      sessionStorage.setItem('wea.event-create.pending.v1', saved);
      const recovered = TestBed.runInInjectionContext(() => new EventCreateFacade());
      expect(recovered.pending()).toBeNull();
      expect(sessionStorage.length).toBe(0);
    }
    http.expectNone('/api/events');
  });
  it('restores pending state when the facade is constructed before session restoration finishes', () => {
    load();
    facade.submit(draft).subscribe();
    csrf();
    http.expectOne('/api/events').error(new ProgressEvent('error'));
    const key = facade.pending()?.key;
    const auth = TestBed.inject(AuthFacade);
    auth.restore();
    const recovered = TestBed.runInInjectionContext(() => new EventCreateFacade());
    expect(recovered.pending()).toBeNull();
    http.expectOne('/api/auth/me').flush({ id: 'owner-a', userName: 'alice', roles: ['Editor'] });
    TestBed.tick();
    expect(recovered.pending()?.key).toBe(key);
    http.expectNone('/api/events');
  });
  it('cancels an in-flight write on account change without exposing the old pending payload', () => {
    load();
    facade.submit(draft).subscribe();
    csrf();
    const request = http.expectOne('/api/events');
    const auth = TestBed.inject(AuthFacade);
    auth.restore();
    http.expectOne('/api/auth/me').flush({ id: 'owner-b', userName: 'bob', roles: ['Editor'] });
    TestBed.tick();
    expect(request.cancelled).toBe(true);
    expect(facade.pending()).toBeNull();
    expect(facade.busy()).toBe(false);
    load();
    facade.submit(draft).subscribe();
    csrf();
    const next = http.expectOne('/api/events');
    facade.submit(draft).subscribe((result) => expect(result).toBe(false));
    next.flush(created);
    http.expectNone('/api/auth/csrf');
  });
  it('rejects malformed or duplicate selector options as a load failure', () => {
    for (const response of [
      null,
      [{ id: typeId, name: '' }],
      [
        { id: typeId, name: 'Weather' },
        { id: typeId, name: 'Duplicate' },
      ],
    ]) {
      facade.load();
      http.expectOne('/api/events/event-types').flush(response);
      expect(facade.typesLoaded()).toBe(false);
      expect(facade.error()).toBeTruthy();
    }
  });
  it('requires explicit discard to edit/new-key submit and preserves the failed content', () => {
    load();
    facade.submit(draft).subscribe();
    csrf();
    http.expectOne('/api/events').error(new ProgressEvent('error'));
    expect(facade.discardPending()).toEqual(draft);
    expect(facade.pending()).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });
});
