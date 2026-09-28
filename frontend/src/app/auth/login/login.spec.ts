import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { Login } from './login';
import { AuthService } from '../auth.service';

describe('Login', () => {
  let authServiceStub: { login: ReturnType<typeof vi.fn> };
  let router: Router;

  beforeEach(async () => {
    authServiceStub = { login: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceStub },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({}) } },
        },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  it('does not call the API when the form is invalid', () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();

    fixture.componentInstance['onSubmit']();

    expect(authServiceStub.login).not.toHaveBeenCalled();
  });

  it('logs in and navigates to /rooms on success when there is no returnUrl', () => {
    authServiceStub.login.mockReturnValue(of(undefined));
    const fixture = TestBed.createComponent(Login);
    const component = fixture.componentInstance;
    component['form'].setValue({ email: 'user@example.com', password: 'Passw0rd!' });

    component['onSubmit']();

    expect(authServiceStub.login).toHaveBeenCalledWith('user@example.com', 'Passw0rd!');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/rooms');
  });

  it('shows "Invalid email or password." on a 401', () => {
    authServiceStub.login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    const fixture = TestBed.createComponent(Login);
    const component = fixture.componentInstance;
    component['form'].setValue({ email: 'user@example.com', password: 'wrong' });

    component['onSubmit']();

    expect(component['errorMessage']()).toBe('Invalid email or password.');
  });
});
