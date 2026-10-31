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
import { eventCreatorGuard } from './event-creator.guard';

describe('Event creator guard', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  function run(): Promise<boolean | UrlTree> {
    const result = TestBed.runInInjectionContext(() =>
      eventCreatorGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
    return firstValueFrom(
      (isObservable(result) ? result : of(result)) as Observable<boolean | UrlTree>,
    );
  }
  for (const roles of [[], ['Editor'], ['Administrator'], ['Editor', 'Administrator']])
    it(`restores a direct-link session and gates roles ${roles}`, async () => {
      const result = run();
      const http = TestBed.inject(HttpTestingController);
      let settled = false;
      void result.then(() => {
        settled = true;
      });
      await Promise.resolve();
      expect(settled).toBe(false);
      http.expectOne('/api/auth/me').flush({ id: 'a', userName: 'alice', roles });
      TestBed.tick();
      expect((await result) === true).toBe(
        roles.includes('Editor') || roles.includes('Administrator'),
      );
      http.verify();
    });
  for (const status of [401, 503])
    it(`fails closed on session status ${status}`, async () => {
      const result = run();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/auth/me').flush(null, { status, statusText: 'Unavailable' });
      TestBed.tick();
      expect(await result).not.toBe(true);
      http.verify();
    });
});
