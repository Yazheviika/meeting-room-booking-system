import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * UX only: hides/redirects in the SPA so a logged-out user doesn't land on
 * a page that will just fail. The backend's [Authorize] on every real
 * endpoint is the actual access control — a guard here can be skipped
 * entirely (e.g. by calling the API directly), and the API must (and does)
 * reject that on its own.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isLoggedIn() ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Same caveat as {@link authGuard}: UX only, not the real access control (that's the backend's "AdminOnly" policy). */
export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isAdmin() ? true : router.createUrlTree(['/rooms']);
};
