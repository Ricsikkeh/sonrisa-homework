import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router } from '@angular/router';
import { filter, map, take } from 'rxjs';
import { AuthFacade } from './auth.facade';

export const eventCreatorGuard: CanActivateFn = () => {
  const auth = inject(AuthFacade);
  const router = inject(Router);
  // Direct initial navigation may run before the root component's ngOnInit.
  auth.ensureRestored();
  return toObservable(auth.status).pipe(
    filter((status) => status !== 'loading'),
    take(1),
    map((status) => {
      const roles = auth.user()?.roles;
      return status === 'authenticated' &&
        (roles?.includes('Editor') || roles?.includes('Administrator'))
        ? true
        : router.createUrlTree(['/']);
    }),
  );
};
