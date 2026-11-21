import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { anonymousOnlyGuard, authGuard } from './auth.guard';
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
});
