import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthFacade } from './auth.facade';

describe('AuthFacade', () => {
  let facade: AuthFacade;
  let http: HttpTestingController;
  const user = { id: 'u1', userName: 'alice', roles: [] };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AuthFacade, provideHttpClient(), provideHttpClientTesting()],
    });
    facade = TestBed.inject(AuthFacade);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('deduplicates guard/root restoration while keeping explicit retries available', () => {
    facade.ensureRestored();
    facade.ensureRestored();
    http.expectOne('/api/auth/me').flush(user);
    facade.ensureRestored();
    http.expectNone('/api/auth/me');
    facade.restore();
    http.expectOne('/api/auth/me').flush(user);
  });

  it('restores the current user and handles anonymous/network states', () => {
    expect(facade.status()).toBe('loading');
    facade.restore();
    http.expectOne('/api/auth/me').flush(user);
    expect(facade.user()).toEqual(user);
    expect(facade.status()).toBe('authenticated');

    facade.restore();
    http.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(facade.status()).toBe('anonymous');
    expect(facade.user()).toBeNull();
    facade.restore();
    http.expectOne('/api/auth/me').error(new ProgressEvent('error'));
    expect(facade.status()).toBe('unavailable');
  });

  it('uses fresh csrf tokens and only posts the specified credentials', () => {
    facade.register('alice', 'secret').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token-1' });
    const register = http.expectOne('/api/auth/register');
    expect(register.request.method).toBe('POST');
    expect(register.request.headers.get('X-XSRF-TOKEN')).toBe('token-1');
    expect(register.request.body).toEqual({ userName: 'alice', password: 'secret' });
    register.flush(null, { status: 201, statusText: 'Created' });
    expect(facade.status()).toBe('loading');

    facade.login('alice', 'secret').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token-2' });
    const login = http.expectOne('/api/auth/login');
    expect(login.request.headers.get('X-XSRF-TOKEN')).toBe('token-2');
    expect(login.request.body).toEqual({ userName: 'alice', password: 'secret' });
    login.flush(user);
    expect(facade.user()).toEqual(user);
    expect(facade.status()).toBe('authenticated');
  });

  it('suppresses concurrent submits and releases busy after csrf errors', () => {
    facade.login('a', 'b').subscribe();
    facade.login('c', 'd').subscribe((result) => expect(result).toBe(false));
    expect(facade.busy()).toBe(true);
    http.expectOne('/api/auth/csrf').error(new ProgressEvent('error'));
    expect(facade.busy()).toBe(false);
  });

  it('does not let a delayed startup restore overwrite a successful login', () => {
    facade.restore();
    const restore = http.expectOne('/api/auth/me');
    facade.login('alice', 'secret').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token' });
    http.expectOne('/api/auth/login').flush(user);
    expect(restore.cancelled).toBe(true);
    expect(facade.status()).toBe('authenticated');
  });

  it('releases busy on cancellation and clears previous errors for a deliberate retry', () => {
    facade.login('alice', 'secret').subscribe();
    http.expectOne('/api/auth/csrf').error(new ProgressEvent('error'));
    expect(facade.error()).toContain('Unable to connect');
    const submission = facade.login('alice', 'secret').subscribe();
    expect(facade.error()).toBeNull();
    const csrf = http.expectOne('/api/auth/csrf');
    submission.unsubscribe();
    expect(csrf.cancelled).toBe(true);
    expect(facade.busy()).toBe(false);
    http.expectNone('/api/auth/login');
  });

  it('maps known credential errors safely', () => {
    facade.register('a', 'b').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'x' });
    http
      .expectOne('/api/auth/register')
      .flush('server prose', { status: 409, statusText: 'Conflict' });
    expect(facade.error()).toContain('That username is unavailable');

    facade.login('a', 'b').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'y' });
    http
      .expectOne('/api/auth/login')
      .flush('server prose', { status: 401, statusText: 'Unauthorized' });
    expect(facade.error()).toContain('Invalid username or password');
  });
});
