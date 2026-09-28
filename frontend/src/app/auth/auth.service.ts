import { Injectable, OnDestroy, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface CurrentUser {
  id: string;
  email: string;
  roles: string[];
}

interface AuthResponse {
  accessToken: string;
  expiresAtUtc: string;
  userId: string;
  email: string;
  roles: string[];
}

interface StoredSession {
  accessToken: string;
  expiresAtUtc: string;
  user: CurrentUser;
}

const STORAGE_KEY = 'auth.session';

/**
 * Holds the current session and exposes it as signals. The session is
 * persisted to `localStorage` so it survives a page reload — but anything
 * in `localStorage` is readable by any JS running on the page, which is an
 * XSS risk an httpOnly cookie wouldn't have. Accepted here: the backend is
 * a stateless JWT API with no cookie/CSRF infrastructure, and this is a
 * task/demo app, not a production deployment carrying real user data.
 */
@Injectable({ providedIn: 'root' })
export class AuthService implements OnDestroy {
  private readonly http = inject(HttpClient);

  private readonly session = signal<StoredSession | null>(this.readStoredSession());
  private logoutTimer: ReturnType<typeof setTimeout> | undefined;
  private readonly onStorageEvent = (event: StorageEvent): void => {
    if (event.key === STORAGE_KEY) {
      this.applyStoredSession(this.readStoredSession());
    }
  };

  readonly currentUser = computed<CurrentUser | null>(() => this.session()?.user ?? null);
  readonly isLoggedIn = computed(() => this.session() !== null);
  readonly isAdmin = computed(() => this.session()?.user.roles.includes('Admin') ?? false);

  constructor() {
    const restored = this.session();
    if (restored) {
      this.scheduleAutoLogout(restored.expiresAtUtc);
    }

    // Fires in *other* tabs when this tab's localStorage write happens, so
    // logging in/out in one tab updates the session (and its auto-logout
    // timer) everywhere else the app is open.
    window.addEventListener('storage', this.onStorageEvent);
  }

  ngOnDestroy(): void {
    window.removeEventListener('storage', this.onStorageEvent);
    if (this.logoutTimer !== undefined) {
      clearTimeout(this.logoutTimer);
    }
  }

  get accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  login(email: string, password: string): Observable<void> {
    return this.http
      .post<AuthResponse>(`${environment.apiBaseUrl}/api/auth/login`, { email, password })
      .pipe(
        tap((response) => this.persistSession(response)),
        map(() => undefined),
      );
  }

  register(email: string, password: string): Observable<void> {
    return this.http
      .post<AuthResponse>(`${environment.apiBaseUrl}/api/auth/register`, { email, password })
      .pipe(
        tap((response) => this.persistSession(response)),
        map(() => undefined),
      );
  }

  logout(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.applyStoredSession(null);
  }

  private persistSession(response: AuthResponse): void {
    const stored: StoredSession = {
      accessToken: response.accessToken,
      expiresAtUtc: response.expiresAtUtc,
      user: { id: response.userId, email: response.email, roles: response.roles },
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
    this.applyStoredSession(stored);
  }

  private applyStoredSession(stored: StoredSession | null): void {
    this.session.set(stored);

    if (this.logoutTimer !== undefined) {
      clearTimeout(this.logoutTimer);
      this.logoutTimer = undefined;
    }

    if (stored) {
      this.scheduleAutoLogout(stored.expiresAtUtc);
    }
  }

  /** Schedules `logout()` for the token's exact expiry — fires immediately if it's already past. */
  private scheduleAutoLogout(expiresAtUtc: string): void {
    const delayMs = new Date(expiresAtUtc).getTime() - Date.now();
    this.logoutTimer = setTimeout(() => this.logout(), Math.max(delayMs, 0));
  }

  private readStoredSession(): StoredSession | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }

    let parsed: StoredSession;
    try {
      parsed = JSON.parse(raw) as StoredSession;
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }

    if (new Date(parsed.expiresAtUtc).getTime() <= Date.now()) {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }

    return parsed;
  }
}
