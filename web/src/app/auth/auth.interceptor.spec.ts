import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { BROWSER_NAVIGATE } from './browser-navigation';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', children: [] }, { path: '**', children: [] }]),
        { provide: BROWSER_NAVIGATE, useValue: vi.fn() },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  afterEach(() => controller.verify());

  it('blocks protocol-relative and absolute URLs', () => {
    for (const url of ['//evil.com/x', 'https://evil.com/x', 'http://localhost/x', 'javascript:alert(1)']) {
      let error: unknown;
      http.get(url).subscribe({ error: (e: unknown) => (error = e) });
      expect(error, url).toBeInstanceOf(Error);
    }
    controller.expectNone(() => true);
  });

  it('passes same-origin relative requests through', () => {
    http.get('/api/v1/ping').subscribe();
    controller.expectOne('/api/v1/ping').flush({});
  });

  it('on 401 ends the local session and sends the user to sign in with a return target', async () => {
    await router.navigateByUrl('/dashboard');
    const clear = vi.spyOn(auth, 'clearSession');

    http.get('/api/v1/metrics').subscribe({ error: () => undefined });
    controller.expectOne('/api/v1/metrics').flush('no', { status: 401, statusText: 'Unauthorized' });

    expect(clear).toHaveBeenCalled();
    await vi.waitFor(() => expect(router.url).toBe('/login?returnUrl=%2Fdashboard&reason=auth'));
  });

  it('does not redirect when the identity probe itself returns 401', () => {
    const clear = vi.spyOn(auth, 'clearSession');

    http.get('/api/v1/me').subscribe({ error: () => undefined });
    controller.expectOne('/api/v1/me').flush('no', { status: 401, statusText: 'Unauthorized' });

    expect(clear).not.toHaveBeenCalled();
  });

  it('treats the identity probe with a query string as the probe, but a route that merely starts with the same letters as an ordinary call', () => {
    const clearSession = vi.spyOn(auth, 'clearSession');
    http.get('/api/v1/me?refresh=1').subscribe({ error: () => undefined });
    controller.expectOne('/api/v1/me?refresh=1').flush('no', { status: 401, statusText: 'Unauthorized' });
    expect(clearSession).not.toHaveBeenCalled();

    http.get('/api/v1/metrics').subscribe({ error: () => undefined });
    controller.expectOne('/api/v1/metrics').flush('no', { status: 401, statusText: 'Unauthorized' });
    expect(clearSession).toHaveBeenCalledTimes(1);
  });
});
