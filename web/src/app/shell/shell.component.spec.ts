import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { EmsThemeService, provideEmsUi } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { BROWSER_NAVIGATE } from '../auth/browser-navigation';
import { DashboardComponent } from '../dashboard/dashboard.component';
import { ShellComponent } from './shell.component';

describe('ShellComponent', () => {
  let navigate: ReturnType<typeof vi.fn>;
  let root: HTMLElement;

  beforeEach(async () => {
    navigate = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideEmsUi(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: '', component: ShellComponent, children: [{ path: 'dashboard', component: DashboardComponent }] },
        ]),
        { provide: BROWSER_NAVIGATE, useValue: navigate },
      ],
    });
    TestBed.inject(AuthService).refreshMe().subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({ userName: 'jdoe', displayName: 'Jane Doe', providers: [], permissions: [] });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/dashboard');
    harness.detectChanges();
    root = harness.fixture.nativeElement as HTMLElement;
  });

  it('shows the navigation, the signed-in user and the dashboard header', () => {
    expect(root.querySelector('ems-nav-bar')?.textContent).toContain('Dashboard');
    expect(root.querySelector('button[ems-profile]')?.getAttribute('aria-label')).toContain('Jane Doe');
    expect(root.querySelector('app-dashboard h1')?.textContent).toContain('Acme Air Care');
  });

  it('starts with a skip link that moves focus to the main region', () => {
    const link = root.querySelector<HTMLAnchorElement>('a.skip-link')!;
    expect(root.querySelector('header .ems-nav-bar, header ems-nav-bar')).not.toBeNull();
    expect(link.textContent).toContain('Skip to main content');
    expect(root.querySelector('a, button')).toBe(link);

    document.body.append(root);
    link.click();

    expect(document.activeElement?.tagName).toBe('MAIN');
    root.remove();
  });

  it('signs out through the API', () => {
    root.querySelector<HTMLButtonElement>('button[ems-profile]')!.click();
    const signOut = [...document.querySelectorAll<HTMLButtonElement>('button[mat-menu-item]')].find((b) => b.textContent?.includes('Sign out'))!;
    signOut.click();

    expect(navigate).toHaveBeenCalledWith('/api/auth/sso/logout');
  });

  it('cycles the theme through system, light and dark', () => {
    const theme = TestBed.inject(EmsThemeService);
    theme.setColorMode('system');
    const toggle = root.querySelector<HTMLButtonElement>('button[aria-label^="Theme"]')!;

    toggle.click();
    expect(theme.colorMode()).toBe('e2-light');
    toggle.click();
    expect(theme.colorMode()).toBe('e2-dark');
    toggle.click();
    expect(theme.colorMode()).toBe('system');
  });
});
