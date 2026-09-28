import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

const STORAGE_KEY = 'auth.session';

function authResponse(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    accessToken: 'token-123',
    expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    userId: 'user-1',
    email: 'user@example.com',
    roles: ['User'],
    ...overrides,
  };
}

describe('AuthService', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
    localStorage.clear();
  });

  it('starts logged out with no stored session', () => {
    const service = TestBed.inject(AuthService);

    expect(service.isLoggedIn()).toBe(false);
    expect(service.currentUser()).toBeNull();
    expect(service.isAdmin()).toBe(false);
  });

  it('login persists the session and updates signals', () => {
    const service = TestBed.inject(AuthService);

    service.login('user@example.com', 'Passw0rd!').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/login`).flush(authResponse());

    expect(service.isLoggedIn()).toBe(true);
    expect(service.currentUser()).toEqual({ id: 'user-1', email: 'user@example.com', roles: ['User'] });
    expect(localStorage.getItem(STORAGE_KEY)).not.toBeNull();
  });

  it('isAdmin is true only when the session includes the Admin role', () => {
    const service = TestBed.inject(AuthService);

    service.login('admin@example.com', 'Passw0rd!').subscribe();
    httpMock
      .expectOne(`${environment.apiBaseUrl}/api/auth/login`)
      .flush(authResponse({ roles: ['User', 'Admin'] }));

    expect(service.isAdmin()).toBe(true);
  });

  it('login schedules auto-logout at the token expiry', () => {
    const service = TestBed.inject(AuthService);

    service.login('user@example.com', 'Passw0rd!').subscribe();
    httpMock
      .expectOne(`${environment.apiBaseUrl}/api/auth/login`)
      .flush(authResponse({ expiresAtUtc: new Date(Date.now() + 1000).toISOString() }));

    expect(service.isLoggedIn()).toBe(true);

    vi.advanceTimersByTime(1001);

    expect(service.isLoggedIn()).toBe(false);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('restores a valid session from localStorage on construction', () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        accessToken: 'stored-token',
        expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
        user: { id: 'user-2', email: 'stored@example.com', roles: ['User'] },
      }),
    );

    const service = TestBed.inject(AuthService);

    expect(service.isLoggedIn()).toBe(true);
    expect(service.currentUser()?.email).toBe('stored@example.com');
  });

  it('discards an expired session found in localStorage on construction', () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        accessToken: 'stale-token',
        expiresAtUtc: new Date(Date.now() - 1000).toISOString(),
        user: { id: 'user-2', email: 'stale@example.com', roles: ['User'] },
      }),
    );

    const service = TestBed.inject(AuthService);

    expect(service.isLoggedIn()).toBe(false);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('auto-logs-out a session restored from localStorage at its original expiry (not just a freshly-logged-in one)', () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        accessToken: 'stored-token',
        expiresAtUtc: new Date(Date.now() + 1000).toISOString(),
        user: { id: 'user-2', email: 'stored@example.com', roles: ['User'] },
      }),
    );

    const service = TestBed.inject(AuthService);
    expect(service.isLoggedIn()).toBe(true);

    vi.advanceTimersByTime(1001);

    expect(service.isLoggedIn()).toBe(false);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('logout clears the session and cancels the pending auto-logout timer', () => {
    const service = TestBed.inject(AuthService);

    service.login('user@example.com', 'Passw0rd!').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/login`).flush(authResponse());

    service.logout();

    expect(service.isLoggedIn()).toBe(false);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('picks up a session change made in another tab via the storage event', () => {
    const service = TestBed.inject(AuthService);
    expect(service.isLoggedIn()).toBe(false);

    const stored = {
      accessToken: 'other-tab-token',
      expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      user: { id: 'user-3', email: 'other-tab@example.com', roles: ['User'] },
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
    window.dispatchEvent(new StorageEvent('storage', { key: STORAGE_KEY, newValue: JSON.stringify(stored) }));

    expect(service.isLoggedIn()).toBe(true);
    expect(service.currentUser()?.email).toBe('other-tab@example.com');
  });

  it('clears the session when another tab logs out via the storage event', () => {
    const service = TestBed.inject(AuthService);

    service.login('user@example.com', 'Passw0rd!').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/login`).flush(authResponse());
    expect(service.isLoggedIn()).toBe(true);

    localStorage.removeItem(STORAGE_KEY);
    window.dispatchEvent(new StorageEvent('storage', { key: STORAGE_KEY, newValue: null }));

    expect(service.isLoggedIn()).toBe(false);
  });

  it('ignores storage events for unrelated keys', () => {
    const service = TestBed.inject(AuthService);

    service.login('user@example.com', 'Passw0rd!').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/login`).flush(authResponse());

    window.dispatchEvent(new StorageEvent('storage', { key: 'unrelated-key', newValue: 'x' }));

    expect(service.isLoggedIn()).toBe(true);
  });
});
