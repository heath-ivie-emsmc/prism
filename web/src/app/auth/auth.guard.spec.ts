import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { AuthService } from './auth.service';
import { anonymousOnlyGuard, authGuard, internalGuard, entryGuard, onboardingGuard, welcomeGuard } from './auth.guard';
import { MeDto } from './me.dto';

@Component({ template: 'page' })
class PageStub {}

const ME: MeDto = { userName: 'jdoe', displayName: 'Jane Doe', providers: [], permissions: [] };

describe('route guards', () => {
  let controller: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'login', component: PageStub, canActivate: [anonymousOnlyGuard] },
          { path: 'dashboard', component: PageStub, canActivate: [authGuard] },
          { path: 'admin', component: PageStub, canActivate: [internalGuard] },
          { path: 'area', canActivateChild: [onboardingGuard], children: [{ path: 'page', component: PageStub }] },
          { path: 'welcome', component: PageStub, canActivate: [welcomeGuard] },
          { path: '', pathMatch: 'full', canActivate: [entryGuard], children: [] },
        ]),
      ],
    });
    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => controller.verify());

  /** Waits for the guard's identity request, then answers it. */
  async function answerMe(response: MeDto | null): Promise<void> {
    let request!: ReturnType<HttpTestingController['expectOne']>;
    await vi.waitFor(() => {
      request = controller.expectOne('/api/v1/me');
    });
    if (response) request.flush(response);
    else request.flush('no', { status: 401, statusText: 'Unauthorized' });
  }

  it('sends an anonymous visitor to login and keeps the return target', async () => {
    const done = router.navigateByUrl('/dashboard');
    await answerMe(null);
    await done;

    expect(router.url).toBe('/login?returnUrl=%2Fdashboard&reason=auth');
  });

  it('lets a signed-in user through', async () => {
    const done = router.navigateByUrl('/dashboard');
    await answerMe(ME);
    await done;

    expect(router.url).toBe('/dashboard');
  });

  it('keeps a signed-in user off the login page', async () => {
    const done = router.navigateByUrl('/login');
    await answerMe(ME);
    await done;

    expect(router.url).toBe('/dashboard');
  });

  it('shows the login page to an anonymous visitor', async () => {
    const done = router.navigateByUrl('/login');
    await answerMe(null);
    await done;

    expect(router.url).toBe('/login');
  });

  it('lets an internal user into the admin area', async () => {
    const done = router.navigateByUrl('/admin');
    await answerMe({ ...ME, isInternal: true });
    await done;

    expect(router.url).toBe('/admin');
  });

  it('sends a customer user away from the admin area', async () => {
    const done = router.navigateByUrl('/admin');
    await answerMe({ ...ME, isInternal: false });
    await done;

    expect(router.url).toBe('/dashboard');
  });

  it('treats a missing internal flag as a customer', async () => {
    const done = router.navigateByUrl('/admin');
    await answerMe(ME);
    await done;

    expect(router.url).toBe('/dashboard');
  });

  it('sends an anonymous visitor away from the admin area, and on to login through the dashboard guard', async () => {
    const done = router.navigateByUrl('/admin');
    await answerMe(null);
    await done;

    expect(router.url).toBe('/login?returnUrl=%2Fdashboard&reason=auth');
  });

  it('sends a user who has not answered the first-login questions to them', async () => {
    const done = router.navigateByUrl('/area/page');
    await answerMe({ ...ME, onboardingRequired: true });
    await done;

    expect(router.url).toBe('/welcome');
  });

  it('lets a user who has answered, or has nothing to answer, through', async () => {
    const done = router.navigateByUrl('/area/page');
    await answerMe({ ...ME, onboardingRequired: false });
    await done;

    expect(router.url).toBe('/area/page');
  });

  it('lets a user through once the questionnaire has failed open', async () => {
    const done = router.navigateByUrl('/welcome');
    await answerMe({ ...ME, onboardingRequired: true });
    await done;
    TestBed.inject(AuthService).skipOnboarding();

    await router.navigateByUrl('/area/page');

    expect(router.url).toBe('/area/page');
  });

  it('shows the questionnaire page only to a user who needs it', async () => {
    const done = router.navigateByUrl('/welcome');
    await answerMe({ ...ME, onboardingRequired: false });
    await done;

    expect(router.url).toBe('/dashboard');
  });

  it('starts an Internal User with no customer Providers in the admin area', async () => {
    const done = router.navigateByUrl('/');
    await answerMe({ ...ME, isInternal: true, providers: [] });
    await done;

    expect(router.url).toBe('/admin');
  });

  it('starts everyone else on the dashboard', async () => {
    const done = router.navigateByUrl('/');
    await answerMe({ ...ME, isInternal: false });
    await done;

    expect(router.url).toBe('/dashboard');
  });
});
