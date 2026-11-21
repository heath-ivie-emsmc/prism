import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { AuthService } from './auth/auth.service';
import { BROWSER_NAVIGATE } from './auth/browser-navigation';
import { DashboardComponent } from './dashboard/dashboard.component';
import { LoginComponent } from './login/login.component';
import { ShellComponent } from './shell/shell.component';
import { axeViolations } from './axe';

describe('accessibility (axe, WCAG 2.1 A and AA)', () => {
  it('actually reports a violation, so the clean results below mean something', async () => {
    const bad = document.createElement('div');
    bad.innerHTML = '<img src="x.png"><button></button>';

    const violations = await axeViolations(bad);

    expect(violations.some((line) => line.startsWith('image-alt'))).toBe(true);
    expect(violations.some((line) => line.startsWith('button-name'))).toBe(true);
  });

  function configure(): void {
    TestBed.configureTestingModule({
      providers: [
        provideEmsUi(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BROWSER_NAVIGATE, useValue: vi.fn() },
        provideRouter([
          { path: 'login', component: LoginComponent },
          { path: '', component: ShellComponent, children: [{ path: 'dashboard', component: DashboardComponent }] },
        ]),
      ],
    });
  }

  function signIn(): void {
    TestBed.inject(AuthService).refreshMe().subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({ userName: 'jdoe', displayName: 'Jane Doe', providers: [], permissions: [] });
  }

  it('has no violations on the login page', async () => {
    configure();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/login');
    harness.detectChanges();

    expect(await axeViolations(harness.fixture.nativeElement as HTMLElement)).toEqual([]);
  });

  it('has no violations on the login page when sign-in was refused', async () => {
    configure();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/login?error=access_denied');
    harness.detectChanges();

    expect(await axeViolations(harness.fixture.nativeElement as HTMLElement)).toEqual([]);
  });

  it('has no violations on the shell and dashboard header', async () => {
    configure();
    signIn();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/dashboard');
    harness.detectChanges();

    expect(await axeViolations(harness.fixture.nativeElement as HTMLElement)).toEqual([]);
  });
});
