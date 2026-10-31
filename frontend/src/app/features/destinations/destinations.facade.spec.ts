import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthFacade } from '../auth/auth.facade';
import { DestinationsFacade } from './destinations.facade';

const path = '/api/me/destinations';
const settings = { email: 'alice@example.com', slackLabel: '#weather' };
describe('DestinationsFacade critical behavior', () => {
  let facade: DestinationsFacade;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [DestinationsFacade, provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthFacade).restore();
    http.expectOne('/api/auth/me').flush({ id: 'owner-a', userName: 'alice', roles: [] });
    facade = TestBed.inject(DestinationsFacade);
    TestBed.tick();
  });
  afterEach(() => http.verify());
  function load() {
    facade.load();
    const request = http.expectOne(path);
    expect(request.request.headers.get('X-Expected-User-Id')).toBe('owner-a');
    request.flush(settings);
  }
  function csrf() {
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'fresh' });
  }

  it('blocks writes until a successful load and permits explicit recovery after load failure', () => {
    facade.load();
    http.expectOne(path).error(new ProgressEvent('error'));
    facade.save().subscribe((result) => expect(result).toBe(false));
    expect(facade.editable()).toBe(false);
    http.expectNone('/api/auth/csrf');
    load();
    expect(facade.editable()).toBe(true);
  });
  it('uses fresh CSRF and the captured account, normalizes payload and blocks overlapping writes', () => {
    load();
    facade.setEmail('  new@example.com  ');
    facade.setSlackLabel('  ');
    facade.save().subscribe((result) => expect(result).toBe(true));
    facade.save().subscribe((result) => expect(result).toBe(false));
    facade.load();
    csrf();
    const request = http.expectOne(path);
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('fresh');
    expect(request.request.headers.get('X-Expected-User-Id')).toBe('owner-a');
    expect(request.request.body).toEqual({ email: 'new@example.com', slackLabel: null });
    request.flush({ email: 'new@example.com', slackLabel: null });
    expect(facade.saved()).toBe(true);
    expect(facade.busy()).toBe(false);
    expect(facade.draft()).toEqual({ email: 'new@example.com', slackLabel: '' });
    http.expectNone(path);
  });
  for (const failure of [401, 403, 'destination_session_changed'] as const) {
    it(`clears stale personal state and blocks writes after ${failure}, explicit reload restores the actual account`, () => {
      load();
      facade.save().subscribe();
      csrf();
      const status = typeof failure === 'number' ? failure : 409;
      http.expectOne(path).flush(
        {
          type: 'about:blank',
          title: 'Denied',
          status,
          code: typeof failure === 'string' ? failure : 'authentication_required',
        },
        { status, statusText: 'Denied' },
      );
      expect(facade.draft()).toEqual({ email: '', slackLabel: '' });
      expect(facade.accessDenied()).toBe(true);
      expect(facade.saved()).toBe(false);
      facade.save().subscribe((result) => expect(result).toBe(false));
      facade.load();
      http.expectNone('/api/auth/csrf');
      http.expectNone(path);
      facade.reload();
      http.expectOne('/api/auth/me').flush({ id: 'owner-b', userName: 'bob', roles: [] });
      TestBed.tick();
      const reloaded = http.expectOne(path);
      expect(reloaded.request.headers.get('X-Expected-User-Id')).toBe('owner-b');
      reloaded.flush({ email: null, slackLabel: '#bob' });
      expect(facade.draft()).toEqual({ email: '', slackLabel: '#bob' });
      expect(facade.editable()).toBe(true);
    });
  }
  it('does not expose or submit an old draft after a locally observed account change', () => {
    load();
    facade.save().subscribe();
    const token = http.expectOne('/api/auth/csrf');
    TestBed.inject(AuthFacade).restore();
    http.expectOne('/api/auth/me').flush({ id: 'owner-b', userName: 'bob', roles: [] });
    expect(facade.draft()).toEqual({ email: '', slackLabel: '' });
    TestBed.tick();
    expect(token.cancelled).toBe(true);
    expect(facade.busy()).toBe(false);
    facade.save().subscribe((result) => expect(result).toBe(false));
    http.expectNone(path);
  });
  it('retains edits on response loss without retry or false success; an explicit reload checks persisted settings', () => {
    load();
    facade.setEmail('edited@example.com');
    facade.save().subscribe((result) => expect(result).toBe(false));
    csrf();
    http.expectOne(path).error(new ProgressEvent('error'));
    expect(facade.draft().email).toBe('edited@example.com');
    expect(facade.error()).toContain('may already be saved');
    expect(facade.saved()).toBe(false);
    http.expectNone('/api/auth/csrf');
    http.expectNone(path);
    facade.reload();
    http.expectOne('/api/auth/me').flush({ id: 'owner-a', userName: 'alice', roles: [] });
    TestBed.tick();
    http.expectOne(path).flush({ email: 'edited@example.com', slackLabel: '#weather' });
    expect(facade.draft().email).toBe('edited@example.com');
    expect(facade.saved()).toBe(false);
  });
  it('maps expected errors safely and keeps failed input; malformed success is not confirmed', () => {
    load();
    for (const code of [
      'destination_in_use',
      'destination_validation_failed',
      'destination_save_in_progress',
      'antiforgery_failed',
    ]) {
      facade.save().subscribe((result) => expect(result).toBe(false));
      csrf();
      http
        .expectOne(path)
        .flush(
          { type: 'about:blank', status: 400, code, detail: 'SECRET', title: 'SECRET' },
          { status: 400, statusText: 'Failure' },
        );
      expect(facade.error()).not.toContain('SECRET');
      if (code === 'destination_in_use') expect(facade.error()).toContain('subscription uses');
      expect(facade.draft()).toEqual(settings);
      expect(facade.saved()).toBe(false);
      http.expectNone('/api/auth/csrf');
    }
    facade.save().subscribe((result) => expect(result).toBe(false));
    csrf();
    http.expectOne(path).flush({ email: 'wrong@example.com', slackLabel: '#wrong' });
    expect(facade.saved()).toBe(false);
    expect(facade.error()).toContain('Unable to confirm');
  });
});
