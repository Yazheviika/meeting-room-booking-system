import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, type ActivatedRouteSnapshot, type RouterStateSnapshot } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { authGuard, adminGuard } from './guards';
import { AuthService } from './auth.service';

describe('authGuard / adminGuard', () => {
  let router: Router;
  let authServiceStub: { isLoggedIn: () => boolean; isAdmin: () => boolean };

  const routeSnapshot = {} as ActivatedRouteSnapshot;
  const stateSnapshot = { url: '/rooms/5' } as RouterStateSnapshot;

  beforeEach(() => {
    authServiceStub = { isLoggedIn: () => false, isAdmin: () => false };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: authServiceStub },
      ],
    });

    router = TestBed.inject(Router);
  });

  describe('authGuard', () => {
    it('allows navigation when logged in', () => {
      authServiceStub.isLoggedIn = () => true;

      const result = TestBed.runInInjectionContext(() => authGuard(routeSnapshot, stateSnapshot));

      expect(result).toBe(true);
    });

    it('redirects to /login with a returnUrl when logged out', () => {
      authServiceStub.isLoggedIn = () => false;

      const result = TestBed.runInInjectionContext(() => authGuard(routeSnapshot, stateSnapshot));

      expect(router.serializeUrl(result as ReturnType<Router['createUrlTree']>)).toBe(
        '/login?returnUrl=%2Frooms%2F5',
      );
    });
  });

  describe('adminGuard', () => {
    it('allows navigation when the user is an Admin', () => {
      authServiceStub.isAdmin = () => true;

      const result = TestBed.runInInjectionContext(() => adminGuard(routeSnapshot, stateSnapshot));

      expect(result).toBe(true);
    });

    it('redirects to /rooms when the user is not an Admin', () => {
      authServiceStub.isAdmin = () => false;

      const result = TestBed.runInInjectionContext(() => adminGuard(routeSnapshot, stateSnapshot));

      expect(router.serializeUrl(result as ReturnType<Router['createUrlTree']>)).toBe('/rooms');
    });
  });
});
