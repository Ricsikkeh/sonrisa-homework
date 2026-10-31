import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EventTypesFacade } from './event-types.facade';

describe('EventTypesFacade', () => {
  let facade: EventTypesFacade;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [EventTypesFacade, provideHttpClient(), provideHttpClientTesting()],
    });
    facade = TestBed.inject(EventTypesFacade);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function load() {
    facade.load();
    http.expectOne('/api/event-types').flush([{ id: 'a', name: 'Weather' }]);
  }
  function csrf() {
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'fresh-token' });
  }
  it('loads active types with loading and safe failure/retry states', () => {
    facade.load();
    expect(facade.loading()).toBe(true);
    http.expectOne('/api/event-types').error(new ProgressEvent('error'));
    expect(facade.loading()).toBe(false);
    expect(facade.error()).toContain('Unable to connect');
    load();
    expect(facade.error()).toBeNull();
    expect(facade.items()).toEqual([{ id: 'a', name: 'Weather' }]);
  });
  for (const action of ['create', 'rename', 'remove'] as const) {
    it(`uses fresh CSRF for ${action} and reloads only after success`, () => {
      load();
      const result: boolean[] = [];
      (action === 'create'
        ? facade.create('Storms')
        : action === 'rename'
          ? facade.rename('a', 'Storms')
          : facade.remove('a')
      ).subscribe((x) => result.push(x));
      expect(facade.busy()).toBe(true);
      csrf();
      const request = http.expectOne(
        action === 'create' ? '/api/event-types' : '/api/event-types/a',
      );
      expect(request.request.method).toBe(
        action === 'create' ? 'POST' : action === 'rename' ? 'PUT' : 'DELETE',
      );
      expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('fresh-token');
      if (action !== 'remove') expect(request.request.body).toEqual({ name: 'Storms' });
      request.flush(action === 'remove' ? null : { id: 'a', name: 'Storms' });
      expect(result).toEqual([true]);
      http
        .expectOne('/api/event-types')
        .flush(action === 'remove' ? [] : [{ id: 'a', name: 'Storms' }]);
      expect(facade.busy()).toBe(false);
    });
  }
  it('preserves data on conflict, safely maps errors, and never retries a mutation', () => {
    load();
    facade.rename('a', 'Taken').subscribe((x) => expect(x).toBe(false));
    csrf();
    http.expectOne('/api/event-types/a').flush(
      {
        type: 'about:blank',
        title: 'Conflict',
        status: 409,
        code: 'event_type_name_conflict',
        detail: 'SECRET',
      },
      { status: 409, statusText: 'Conflict' },
    );
    expect(facade.error()).toContain('already exists');
    expect(facade.error()).not.toContain('SECRET');
    expect(facade.items()[0]?.name).toBe('Weather');
    expect(facade.busy()).toBe(false);
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/event-types/a');
  });
  it('suppresses double submits and list refreshes during a mutation', () => {
    load();
    facade.create('Storms').subscribe();
    facade.create('Other').subscribe((result) => expect(result).toBe(false));
    facade.load();
    csrf();
    http.expectOne('/api/event-types').flush(null, { status: 500, statusText: 'Error' });
    expect(facade.busy()).toBe(false);
    http.expectNone('/api/event-types');
  });
  for (const status of [401, 403])
    it(`clears private data and blocks mutations after ${status} until a successful explicit reload`, () => {
      load();
      facade.remove('a').subscribe();
      csrf();
      http.expectOne('/api/event-types/a').flush(null, { status, statusText: 'Denied' });
      expect(facade.items()).toEqual([]);
      expect(facade.accessDenied()).toBe(true);
      facade.create('Blocked').subscribe((result) => expect(result).toBe(false));
      http.expectNone('/api/auth/csrf');
      load();
      expect(facade.accessDenied()).toBe(false);
    });
  for (const { status, code, expected } of [
    { status: 400, code: 'event_type_name_invalid', expected: '1–100' },
    { status: 404, code: 'event_type_not_found', expected: 'no longer exists' },
    { status: 409, code: 'event_type_in_use', expected: 'has subscriptions' },
    { status: 409, code: 'event_type_change_in_progress', expected: 'another request' },
    { status: 400, code: 'antiforgery_failed', expected: 'session changed' },
  ])
    it(`maps ${code} to a local safe message without a retry`, () => {
      load();
      facade.rename('a', 'Storms').subscribe();
      csrf();
      http
        .expectOne('/api/event-types/a')
        .flush(
          { type: 'about:blank', title: 'SECRET', detail: 'SECRET', status, code },
          { status, statusText: 'Failure' },
        );
      expect(facade.error()).toContain(expected);
      expect(facade.error()).not.toContain('SECRET');
      http.expectNone('/api/auth/csrf');
    });
  it('keeps malformed successful responses out of presentation and permits a deliberate retry', () => {
    facade.load();
    http.expectOne('/api/event-types').flush(null);
    expect(facade.items()).toEqual([]);
    expect(facade.error()).not.toBeNull();
    expect(facade.loading()).toBe(false);
    load();
    expect(facade.error()).toBeNull();
  });
  it('releases busy on cancellation or CSRF failure and sends no mutation', () => {
    load();
    const subscription = facade.create('Storms').subscribe();
    const request = http.expectOne('/api/auth/csrf');
    subscription.unsubscribe();
    expect(request.cancelled).toBe(true);
    expect(facade.busy()).toBe(false);
    facade.create('Storms').subscribe();
    http.expectOne('/api/auth/csrf').error(new ProgressEvent('error'));
    expect(facade.busy()).toBe(false);
    http.expectNone('/api/event-types');
  });
});
