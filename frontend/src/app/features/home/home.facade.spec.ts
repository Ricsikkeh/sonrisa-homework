import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HomeFacade } from './home.facade';

describe('HomeFacade', () => {
  let facade: HomeFacade;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [HomeFacade, provideHttpClient(), provideHttpClientTesting()],
    });
    facade = TestBed.inject(HomeFacade);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads backend liveness through the same-origin API proxy', () => {
    facade.checkAvailability();
    expect(facade.status()).toBe('loading');
    const request = http.expectOne('/api/health');
    expect(request.request.method).toBe('GET');
    request.flush({ status: 'ok' });
    expect(facade.status()).toBe('available');
  });

  it('reports unavailable without exposing server diagnostics', () => {
    facade.checkAvailability();
    http.expectOne('/api/health').flush('internal diagnostic', {
      status: 503,
      statusText: 'Service Unavailable',
    });
    expect(facade.status()).toBe('unavailable');
  });

  it.each([{ status: 'unexpected' }, null])(
    'does not treat an invalid payload as available: %s',
    (payload) => {
      facade.checkAvailability();
      http.expectOne('/api/health').flush(payload);
      expect(facade.status()).toBe('unavailable');
    },
  );

  it('can retry after a failed check', () => {
    facade.checkAvailability();
    http.expectOne('/api/health').error(new ProgressEvent('error'));
    expect(facade.status()).toBe('unavailable');

    facade.checkAvailability();
    expect(facade.status()).toBe('loading');
    http.expectOne('/api/health').flush({ status: 'ok' });
    expect(facade.status()).toBe('available');
  });
});
