import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { Observable, firstValueFrom, isObservable, of } from 'rxjs';
import { authenticatedGuard } from './authenticated.guard';

describe('Authenticated-user guard', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  function run(): Promise<boolean | UrlTree> {
    const result = TestBed.runInInjectionContext(() =>
      authenticatedGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
    return firstValueFrom(
      (isObservable(result) ? result : of(result)) as Observable<boolean | UrlTree>,
    );
  }
  for (const roles of [[], ['Editor'], ['Administrator']]) {
    it(`waits for restoration and allows authenticated roles ${roles}`, async () => {
      const result = run();
      const http = TestBed.inject(HttpTestingController);
      let settled = false;
      void result.then(() => {
        settled = true;
      });
      await Promise.resolve();
      expect(settled).toBe(false);
      http.expectOne('/api/auth/me').flush({ id: 'owner-a', userName: 'alice', roles });
      TestBed.tick();
      expect(await result).toBe(true);
      http.verify();
    });
  }
  for (const status of [401, 503]) {
    it(`fails closed on ${status}`, async () => {
      const result = run();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/auth/me').flush(null, { status, statusText: 'Unavailable' });
      TestBed.tick();
      expect(await result).not.toBe(true);
      http.verify();
    });
  }
});
