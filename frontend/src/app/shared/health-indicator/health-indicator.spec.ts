import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { HealthIndicator } from './health-indicator';

describe('HealthIndicator', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HealthIndicator],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows "checking" before the health call resolves', () => {
    const fixture = TestBed.createComponent(HealthIndicator);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.status--checking')).toBeTruthy();

    httpMock.expectOne(`${environment.apiBaseUrl}/health`).flush({ status: 'healthy' });
  });

  it('shows "healthy" when the health call succeeds', () => {
    const fixture = TestBed.createComponent(HealthIndicator);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/health`).flush({ status: 'healthy' });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.status--healthy')).toBeTruthy();
  });

  it('shows "unreachable" when the health call fails', () => {
    const fixture = TestBed.createComponent(HealthIndicator);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/health`).error(new ProgressEvent('error'));
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.status--unreachable')).toBeTruthy();
  });
});
