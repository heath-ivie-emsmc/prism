import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../axe';
import { BASE, bootstrap, settle } from '../saved-views/saved-views.testing';
import { HomeComponent } from './home.component';

const LAYOUT = {
  version: 1,
  density: 'comfortable',
  widgets: [
    {
      id: 'w1',
      metricKey: 'net_collections',
      presentation: 'line',
      options: { months: '6' },
      x: 0,
      y: 0,
      w: 6,
      h: 3,
    },
  ],
};
const PERSONA = { source: 'persona', layout: LAYOUT, savedViewId: null };
const CUSTOM = { source: 'custom', layout: null, savedViewId: 7 };
const SAVED = {
  id: 7,
  name: 'Home',
  description: null,
  ownerName: 'Jane Doe',
  isOwner: true,
  layout: LAYOUT,
  version: '3',
  updatedAt: '2026-11-24T10:00:00Z',
};
const HOME = `${BASE}/home`;

async function create(home: object | 'error') {
  const http = bootstrap();
  const fixture = TestBed.createComponent(HomeComponent);
  fixture.detectChanges();
  const request = http.expectOne(HOME);
  if (home === 'error') request.flush('no', { status: 500, statusText: 'Server Error' });
  else request.flush(home);
  await settle(fixture, http, { [`${BASE}/saved-views/7`]: SAVED }, false);
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

async function tick(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
  }
}

describe('HomeComponent', () => {
  it('shows the standard dashboard when the user has no Persona page', async () => {
    const { root } = await create({ source: 'default', layout: null, savedViewId: null });

    expect(root.querySelector('app-dashboard')).not.toBeNull();
    expect(root.querySelector('app-grid-canvas')).toBeNull();
  });

  it('shows the standard dashboard instead of an error when the home page cannot be resolved', async () => {
    const { root } = await create('error');

    expect(root.querySelector('app-dashboard')).not.toBeNull();
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it("draws the Persona's template read-only with a way to customize it, and never names the Persona", async () => {
    const { root } = await create(PERSONA);

    expect(root.querySelector('app-dashboard')).toBeNull();
    expect(root.querySelector('app-grid-canvas')).not.toBeNull();
    expect(root.querySelector('h1')?.textContent).toContain('Seminole County Fire Department');
    expect([...root.querySelectorAll('button')].map((b) => b.textContent?.trim())).toContain(
      'Customize this page',
    );
    expect(root.textContent?.toLowerCase()).not.toContain('persona');
  });

  it('customizing makes the copy and opens it', async () => {
    const { fixture, http, root } = await create(PERSONA);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    [...root.querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === 'Customize this page')!
      .click();
    await tick(fixture);
    http.expectOne({ method: 'POST', url: `${HOME}/customize` }).flush({ savedViewId: 7 });
    await tick(fixture);

    expect(navigate).toHaveBeenCalledWith(['/saved-views', 7]);
  });

  it('draws the users own copy with Edit and a confirmed reset', async () => {
    const { fixture, http, root } = await create(CUSTOM);

    expect(root.querySelector('app-grid-canvas')).not.toBeNull();
    expect(root.querySelector('a')?.textContent).toContain('Edit this page');
    [...root.querySelectorAll('button')]
      .find((b) => b.textContent?.includes('Reset to the recommended page'))!
      .click();
    await tick(fixture);
    expect(root.textContent).toContain('This deletes your copy');
    http.expectNone({ method: 'DELETE', url: `${HOME}/customize` });

    [...root.querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === 'Confirm reset')!
      .click();
    await tick(fixture);
    http
      .expectOne({ method: 'DELETE', url: `${HOME}/customize` })
      .flush(null, { status: 204, statusText: 'No Content' });
    await tick(fixture);
    http.expectOne(HOME).flush(PERSONA);
  });

  it('falls back to the standard dashboard when the users copy cannot be opened', async () => {
    const http = bootstrap();
    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges();
    http.expectOne(HOME).flush(CUSTOM);
    await tick(fixture);
    http.expectOne(`${BASE}/saved-views/7`).flush('no', { status: 404, statusText: 'Not Found' });
    await tick(fixture);

    expect((fixture.nativeElement as HTMLElement).querySelector('app-dashboard')).not.toBeNull();
  });

  it('has no axe violations on a Persona page', async () => {
    const { root } = await create(PERSONA);

    expect(await axeViolations(root)).toEqual([]);
  });

  it('asks for the catalog of its own Provider only', async () => {
    const { http } = await create(PERSONA);

    http.expectNone((r) => r.url.includes('/providers/') && !r.url.startsWith(BASE));
  });
});
