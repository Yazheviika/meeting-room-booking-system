import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { Register } from './register';
import { AuthService } from '../auth.service';

describe('Register', () => {
  let authServiceStub: { register: ReturnType<typeof vi.fn> };
  let router: Router;

  beforeEach(async () => {
    authServiceStub = { register: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Register],
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

  it('does not call the API when the password fails the rule validator', () => {
    const fixture = TestBed.createComponent(Register);
    const component = fixture.componentInstance;
    component['form'].setValue({ email: 'user@example.com', password: 'weak' });

    component['onSubmit']();

    expect(authServiceStub.register).not.toHaveBeenCalled();
  });

  it('registers and navigates to /rooms on success', () => {
    authServiceStub.register.mockReturnValue(of(undefined));
    const fixture = TestBed.createComponent(Register);
    const component = fixture.componentInstance;
    component['form'].setValue({ email: 'user@example.com', password: 'Passw0rd!' });

    component['onSubmit']();

    expect(authServiceStub.register).toHaveBeenCalledWith('user@example.com', 'Passw0rd!');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/rooms');
  });

  it('shows flattened server-side validation errors on a 400', () => {
    authServiceStub.register.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { DuplicateUserName: ["Username 'user@example.com' is already taken."] } },
          }),
      ),
    );
    const fixture = TestBed.createComponent(Register);
    const component = fixture.componentInstance;
    component['form'].setValue({ email: 'user@example.com', password: 'Passw0rd!' });

    component['onSubmit']();

    expect(component['serverErrors']()).toEqual(["Username 'user@example.com' is already taken."]);
  });
});
