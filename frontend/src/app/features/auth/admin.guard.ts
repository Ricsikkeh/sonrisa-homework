import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router } from '@angular/router';
import { filter, map, take } from 'rxjs';
import { AuthFacade } from './auth.facade';

export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthFacade);
  const router = inject(Router);
  // Direct initial navigation may run before the root component's ngOnInit.
  auth.ensureRestored();
  return toObservable(auth.status).pipe(
    filter((status) => status !== 'loading'),
    take(1),
    map((status) =>
      status === 'authenticated' && auth.user()?.roles.includes('Administrator')
        ? true
        : router.createUrlTree(['/']),
    ),
  );
};
