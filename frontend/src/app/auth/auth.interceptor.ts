import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

/**
 * Attaches the bearer token to requests going to the API, and treats a 401
 * on one of *those* requests as "the session expired" — logging out and
 * redirecting to /login.
 *
 * The "was a token actually attached" check (rather than just "was this an
 * API URL") is what keeps a failed login's 401 from also triggering this:
 * the user isn't logged in yet, so no token was attached, so a bad-password
 * 401 is left for the login page's own error handling instead of being
 * hijacked into a logout+redirect. This needs no hardcoded list of "public"
 * endpoints to stay correct as more endpoints are added.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isApiRequest = req.url.startsWith(environment.apiBaseUrl);
  const token = auth.accessToken;
  const attachedToken = isApiRequest && token !== null;

  const outgoing = attachedToken ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      if (attachedToken && error instanceof HttpErrorResponse && error.status === 401) {
        auth.logout();
        router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }

      return throwError(() => error);
    }),
  );
};
