import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthFacade } from '../auth/auth.facade';
import { SubscriptionCreateFacade } from './subscription-create.facade';

const typeId = '11111111-1111-4111-8111-111111111111';
const resultId = '22222222-2222-4222-8222-222222222222';
const types = [{ id: typeId, name: 'weather.alert' }];
const destinations = { email: 'alice@example.com', slackLabel: '#weather' };
describe('SubscriptionCreateFacade critical behavior', () => {
  let facade: SubscriptionCreateFacade;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SubscriptionCreateFacade, provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthFacade).restore();
    http.expectOne('/api/auth/me').flush({ id: 'owner-a', userName: 'alice', roles: [] });
    facade = TestBed.inject(SubscriptionCreateFacade);
    TestBed.tick();
  });
  afterEach(() => http.verify());
  function load(owner = 'owner-a') {
    facade.load();
    const eventTypes = http.expectOne('/api/subscriptions/event-types');
    const destinationRequest = http.expectOne('/api/me/destinations');
    expect(eventTypes.request.headers.get('X-Expected-User-Id')).toBe(owner);
    expect(destinationRequest.request.headers.get('X-Expected-User-Id')).toBe(owner);
    eventTypes.flush(types);
    destinationRequest.flush(destinations);
  }
  function select() {
    facade.setType(typeId);
    facade.setChannel('email', true);
    facade.setChannel('slack', true);
  }
  function csrf() {
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'freshCSRF' });
  }
  function createPost() {
    const request = http.expectOne('/api/subscriptions');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('freshCSRF');
    expect(request.request.headers.get('X-Expected-User-Id')).toBe('owner-a');
    expect(request.request.body).toEqual({ eventTypeId: typeId, channels: ['email', 'slack'] });
    return request;
  }
  function created(channels = ['email', 'slack']) {
    return {
      id: resultId,
      eventTypeId: typeId,
      eventTypeName: 'weather.alert',
      channels,
      createdAt: '2025-01-01T00:00:00Z',
    };
  }
  it('posts selected channels once with fresh CSRF and clears selectors only after confirmed success', () => {
    load();
    select();
    facade.create().subscribe((ok) => expect(ok).toBe(true));
    facade.create().subscribe((ok) => expect(ok).toBe(false));
    expect(facade.busy()).toBe(true);
    csrf();
    createPost().flush(created());
    expect(facade.result()).toEqual(created());
    expect(facade.selectedType()).toBe('');
    expect(facade.channelOptions().every((option) => !option.selected)).toBe(true);
    expect(facade.busy()).toBe(false);
    http.expectNone('/api/auth/csrf');
  });
  it('deduplicates generic selections and posts in catalog order regardless of toggle order', () => {
    load();
    facade.setType(typeId);
    facade.setChannel('slack', true);
    facade.setChannel('email', true);
    facade.setChannel('email', true);
    facade.setChannel('slack', false);
    expect(facade.channelOptions().find((option) => option.channel === 'slack')!.selected).toBe(
      false,
    );
    facade.setChannel('slack', true);
    facade.create().subscribe((ok) => expect(ok).toBe(true));
    csrf();
    createPost().flush(created());
  });
  it('blocks incomplete or unavailable selections and failed loads without writes', () => {
    facade.create().subscribe((ok) => expect(ok).toBe(false));
    http.expectNone('/api/auth/csrf');
    facade.load();
    http.expectOne('/api/subscriptions/event-types').flush(types);
    http.expectOne('/api/me/destinations').error(new ProgressEvent('error'));
    expect(facade.canCreate()).toBe(false);
    facade.create().subscribe((ok) => expect(ok).toBe(false));
    http.expectNone('/api/auth/csrf');
    load();
    facade.setType(typeId);
    facade.setChannel('email', true);
    expect(facade.canCreate()).toBe(true);
  });
  it('disables missing-destination channels and does not reuse a previously successful load after refresh failure', () => {
    facade.load();
    http.expectOne('/api/subscriptions/event-types').flush(types);
    http.expectOne('/api/me/destinations').flush({ email: 'alice@example.com', slackLabel: null });
    facade.setType(typeId);
    facade.setChannel('slack', true);
    expect(facade.channelOptions().find((option) => option.channel === 'slack')!.selected).toBe(
      false,
    );
    expect(facade.canCreate()).toBe(false);
    facade.setChannel('email', true);
    expect(facade.canCreate()).toBe(true);
    facade.load();
    http.expectOne('/api/subscriptions/event-types').flush(types);
    http.expectOne('/api/me/destinations').error(new ProgressEvent('error'));
    expect(facade.canCreate()).toBe(false);
    facade.create().subscribe((ok) => expect(ok).toBe(false));
    http.expectNone('/api/auth/csrf');
  });
  it('rejects a stale-account settings response during load without exposing destinations or enabling creation', () => {
    facade.load();
    http.expectOne('/api/subscriptions/event-types').flush(types);
    http.expectOne('/api/me/destinations').flush(
      {
        type: 'about:blank',
        title: 'Conflict',
        status: 409,
        code: 'destination_session_changed',
      },
      { status: 409, statusText: 'Conflict' },
    );
    expect(facade.denied()).toBe(true);
    expect(facade.channelOptions().every((option) => !option.configured)).toBe(true);
    expect(facade.eventTypes()).toEqual([]);
    facade.create().subscribe((ok) => expect(ok).toBe(false));
    http.expectNone('/api/auth/csrf');
  });
  it('retains selection on response loss, never retries, and duplicate conflict is not success', () => {
    load();
    select();
    facade.create().subscribe((ok) => expect(ok).toBe(false));
    csrf();
    createPost().error(new ProgressEvent('error'));
    expect(facade.selectedType()).toBe(typeId);
    expect(facade.channelOptions().every((option) => option.selected)).toBe(true);
    expect(facade.result()).toBeNull();
    expect(facade.error()).toContain('Unable to confirm');
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/subscriptions');
    facade.create().subscribe((ok) => expect(ok).toBe(false));
    csrf();
    createPost().flush(
      { type: 'about:blank', title: 'Conflict', status: 409, code: 'subscription_already_exists' },
      { status: 409, statusText: 'Conflict' },
    );
    expect(facade.result()).toBeNull();
    expect(facade.error()).toContain('not been confirmed or changed');
    expect(facade.selectedType()).toBe(typeId);
  });
  for (const failure of [
    401,
    403,
    'subscription_session_changed',
    'destination_session_changed',
  ] as const) {
    it(`clears private state and requires explicit restore after ${failure}`, () => {
      load();
      select();
      facade.create().subscribe((ok) => expect(ok).toBe(false));
      csrf();
      const status = typeof failure === 'number' ? failure : 409;
      createPost().flush(
        {
          type: 'about:blank',
          title: 'Denied',
          status,
          code: typeof failure === 'string' ? failure : 'authentication_required',
        },
        { status, statusText: 'Denied' },
      );
      expect(facade.selectedType()).toBe('');
      expect(facade.result()).toBeNull();
      expect(facade.denied()).toBe(true);
      facade.create().subscribe((ok) => expect(ok).toBe(false));
      http.expectNone('/api/auth/csrf');
      facade.reload();
      http.expectOne('/api/auth/me').flush({ id: 'owner-b', userName: 'bob', roles: [] });
      TestBed.tick();
      const eventTypes = http.expectOne('/api/subscriptions/event-types');
      const destinationRequest = http.expectOne('/api/me/destinations');
      expect(eventTypes.request.headers.get('X-Expected-User-Id')).toBe('owner-b');
      expect(destinationRequest.request.headers.get('X-Expected-User-Id')).toBe('owner-b');
      eventTypes.flush(types);
      destinationRequest.flush({ email: null, slackLabel: '#bob' });
      expect(facade.denied()).toBe(false);
      expect(facade.canCreate()).toBe(false);
    });
  }
  it('invalidates old selection, cancels pending CSRF and hides old result after local account change', () => {
    load();
    select();
    facade.create().subscribe();
    const token = http.expectOne('/api/auth/csrf');
    TestBed.inject(AuthFacade).restore();
    http.expectOne('/api/auth/me').flush({ id: 'owner-b', userName: 'bob', roles: [] });
    TestBed.tick();
    expect(token.cancelled).toBe(true);
    expect(facade.selectedType()).toBe('');
    expect(facade.result()).toBeNull();
    expect(facade.busy()).toBe(false);
    http.expectNone('/api/subscriptions');
  });
  it('blocks stale event type or destination errors until explicit reload', () => {
    load();
    select();
    for (const code of ['event_type_unavailable', 'destination_required']) {
      facade.create().subscribe((ok) => expect(ok).toBe(false));
      csrf();
      createPost().flush(
        { type: 'about:blank', title: 'Invalid', status: 409, code },
        { status: 409, statusText: 'Invalid' },
      );
      expect(facade.canCreate()).toBe(false);
      facade.create().subscribe((ok) => expect(ok).toBe(false));
      http.expectNone('/api/auth/csrf');
      facade.reload();
      http.expectOne('/api/auth/me').flush({ id: 'owner-a', userName: 'alice', roles: [] });
      TestBed.tick();
      const eventTypes = http.expectOne('/api/subscriptions/event-types');
      const destinationRequest = http.expectOne('/api/me/destinations');
      eventTypes.flush(types);
      destinationRequest.flush(destinations);
      select();
    }
  });
  it('does not show success for malformed successful responses', () => {
    load();
    select();
    for (const body of [created(['email']), { ...created(), eventTypeId: resultId }]) {
      facade.create().subscribe((ok) => expect(ok).toBe(false));
      csrf();
      createPost().flush(body);
      expect(facade.result()).toBeNull();
      expect(facade.selectedType()).toBe(typeId);
      expect(facade.error()).toContain('Unable to confirm');
    }
  });
  it('unsubscribing cancels an in-flight create and releases busy state', () => {
    load();
    select();
    const subscription = facade.create().subscribe();
    const token = http.expectOne('/api/auth/csrf');
    token.flush({ requestToken: 'freshCSRF' });
    const request = http.expectOne('/api/subscriptions');
    subscription.unsubscribe();
    expect(request.cancelled).toBe(true);
    expect(facade.busy()).toBe(false);
    expect(facade.selectedType()).toBe(typeId);
  });
});
