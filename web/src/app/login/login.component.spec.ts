import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { BROWSER_NAVIGATE } from '../auth/browser-navigation';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let navigate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    navigate = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideEmsUi(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', component: LoginComponent }]),
        { provide: BROWSER_NAVIGATE, useValue: navigate },
      ],
    });
  });

  async function open(query = ''): Promise<HTMLElement> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/login${query}`);
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  }

  it('offers one way to sign in and no password form', async () => {
    const page = await open();

    expect(page.querySelector('button')?.textContent).toContain('Continue to sign in');
    expect(page.querySelector('input[type="password"]')).toBeNull();
  });

  it('starts SSO and carries a safe return URL', async () => {
    const page = await open('?returnUrl=%2Fdashboard&reason=auth');

    page.querySelector('button')!.click();

    expect(navigate).toHaveBeenCalledWith('/api/auth/sso/start?returnUrl=%2Fdashboard');
  });

  it('ignores an unsafe return URL', async () => {
    const page = await open('?returnUrl=%2F%2Fevil.com');

    page.querySelector('button')!.click();

    expect(navigate).toHaveBeenCalledWith('/api/auth/sso/start');
  });

  it('explains a guarded visit', async () => {
    const page = await open('?returnUrl=%2Fdashboard&reason=auth');

    expect(page.textContent).toContain('Sign in to continue.');
  });

  it('shows a known error code as its message', async () => {
    const page = await open('?error=access_denied');

    expect(page.textContent).toContain('does not have access to Prism');
  });

  it('never echoes an unknown error code', async () => {
    const page = await open('?error=%3Cimg%20src%3Dx%3E');

    expect(page.textContent).toContain('We could not sign you in');
    expect(page.textContent).not.toContain('<img');
    expect(page.querySelector('img')).toBeNull();
  });
});
