import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../environments/environment';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let router: Router;
  let authServiceStub: { accessToken: string | null; logout: () => void };

  beforeEach(() => {
    authServiceStub = { accessToken: null, logout: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: authServiceStub },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('attaches the Authorization header for API requests when a session exists', () => {
    authServiceStub.accessToken = 'token-123';

    http.get(`${environment.apiBaseUrl}/api/rooms`).subscribe();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/rooms`);
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-123');
    request.flush({});
  });

  it('does not attach the header when there is no session', () => {
    authServiceStub.accessToken = null;

    http.get(`${environment.apiBaseUrl}/api/rooms`).subscribe();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/rooms`);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('does not attach the header for requests outside the API base URL', () => {
    authServiceStub.accessToken = 'token-123';

    http.get('https://fonts.googleapis.com/css2').subscribe();

    const request = httpMock.expectOne('https://fonts.googleapis.com/css2');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('logs out and redirects to /login on a 401 from a request that carried the token', () => {
    authServiceStub.accessToken = 'token-123';

    http.get(`${environment.apiBaseUrl}/api/rooms`).subscribe({ error: () => undefined });

    httpMock.expectOne(`${environment.apiBaseUrl}/api/rooms`).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(authServiceStub.logout).toHaveBeenCalledOnce();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: router.url } });
  });

  it('does not log out on a 401 from a request that carried no token (e.g. a failed login)', () => {
    authServiceStub.accessToken = null;

    http.post(`${environment.apiBaseUrl}/api/auth/login`, {}).subscribe({ error: () => undefined });

    httpMock
      .expectOne(`${environment.apiBaseUrl}/api/auth/login`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(authServiceStub.logout).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
