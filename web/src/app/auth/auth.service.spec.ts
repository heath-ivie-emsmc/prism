import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BROWSER_NAVIGATE } from './browser-navigation';
import { AuthService } from './auth.service';
import { MeDto } from './me.dto';

const ME: MeDto = {
  userName: 'jdoe',
  displayName: 'Jane Doe',
  providers: [{ id: 'p1', name: 'Acme Air Care', facilityCount: 0 }],
  permissions: ['dashboard.view'],
};

describe('AuthService', () => {
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.fn>;
  let auth: AuthService;

  beforeEach(() => {
    // Other specs in the same worker (the shell's theme service, for one) may have written to storage. This spec only asserts that
    // AuthService adds nothing, so it starts from empty storage.
    localStorage.clear();
    sessionStorage.clear();
    navigate = vi.fn();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: BROWSER_NAVIGATE, useValue: navigate }],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  it('loads the profile once and caches it', () => {
    let first: MeDto | null | undefined;
    auth.ensureMeLoaded().subscribe((me) => (first = me));
    http.expectOne('/api/v1/me').flush(ME);

    expect(first).toEqual(ME);
    expect(auth.isAuthenticated()).toBe(true);

    let second: MeDto | null | undefined;
    auth.ensureMeLoaded().subscribe((me) => (second = me));
    http.expectNone('/api/v1/me');
    expect(second).toEqual(ME);
  });

  it('shares one request between concurrent callers', () => {
    auth.ensureMeLoaded().subscribe();
    auth.ensureMeLoaded().subscribe();

    http.expectOne('/api/v1/me').flush(ME);
  });

  it('treats an error response as signed out', () => {
    let result: MeDto | null | undefined;
    auth.ensureMeLoaded().subscribe((me) => (result = me));
    http.expectOne('/api/v1/me').flush('nope', { status: 401, statusText: 'Unauthorized' });

    expect(result).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('does not ask again after the API says there is no session', () => {
    auth.ensureMeLoaded().subscribe();
    http.expectOne('/api/v1/me').flush('no', { status: 401, statusText: 'Unauthorized' });

    let again: MeDto | null | undefined;
    auth.ensureMeLoaded().subscribe((me) => (again = me));

    http.expectNone('/api/v1/me');
    expect(again).toBeNull();
  });

  it('asks again after a server error, which says nothing about the session', () => {
    auth.ensureMeLoaded().subscribe();
    http.expectOne('/api/v1/me').flush('boom', { status: 500, statusText: 'Server Error' });

    auth.ensureMeLoaded().subscribe();

    http.expectOne('/api/v1/me').flush(ME);
    expect(auth.me()).toEqual(ME);
  });

  it('asks again when refreshMe is called explicitly', () => {
    auth.ensureMeLoaded().subscribe();
    http.expectOne('/api/v1/me').flush('no', { status: 401, statusText: 'Unauthorized' });

    auth.refreshMe().subscribe();

    http.expectOne('/api/v1/me').flush(ME);
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('keeps the newest response when an older one arrives later', () => {
    auth.refreshMe().subscribe();
    auth.refreshMe().subscribe();
    const [older, newer] = http.match('/api/v1/me');

    newer.flush(ME);
    older.flush('late', { status: 401, statusText: 'Unauthorized' });

    expect(auth.me()).toEqual(ME);
  });

  it('starts SSO with a safe return URL', () => {
    auth.beginSsoLogin({ returnUrl: '/dashboard?period=2026-10' });

    expect(navigate).toHaveBeenCalledWith('/api/auth/sso/start?returnUrl=%2Fdashboard%3Fperiod%3D2026-10');
  });

  it('drops an unsafe return URL instead of forwarding it', () => {
    for (const bad of ['//evil.com', 'https://evil.com', '/\\evil', '/login']) {
      navigate.mockClear();
      auth.beginSsoLogin({ returnUrl: bad });
      expect(navigate).toHaveBeenCalledWith('/api/auth/sso/start');
    }
  });

  it('logout clears the identity and navigates to the API sign-out', () => {
    auth.refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush(ME);

    auth.logout();

    expect(auth.me()).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/api/auth/sso/logout');
  });

  it('never writes tokens to browser storage', () => {
    auth.refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush(ME);

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});
