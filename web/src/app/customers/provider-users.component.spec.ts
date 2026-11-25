import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { BASE, PROVIDER, bootstrap } from '../saved-views/saved-views.testing';
import { ProviderUsersComponent } from './provider-users.component';

const USERS = `${BASE}/users`;
const ROLES = [
  { key: 'viewer', name: 'Viewer', description: 'See the dashboards.', restricted: false, assignable: true },
  { key: 'analyst', name: 'Analyst', description: 'Viewer and saved views.', restricted: false, assignable: true },
  { key: 'claims_reviewer', name: 'Claims reviewer', description: 'Claims.', restricted: true, assignable: false },
  { key: 'provider_admin', name: 'Provider admin', description: 'Manage users.', restricted: false, assignable: true },
];
const PAGE = {
  users: [
    { portalUserId: 1, displayName: 'Jane Doe', roleKey: 'provider_admin', lastSignIn: '2026-11-24T10:00:00Z', isYou: true },
    { portalUserId: 2, displayName: 'Sam Smith', roleKey: 'viewer', lastSignIn: '2026-11-23T10:00:00Z', isYou: false },
    { portalUserId: 3, displayName: 'Ana Ruiz', roleKey: 'claims_reviewer', lastSignIn: '2026-11-22T10:00:00Z', isYou: false },
  ],
  roles: ROLES,
  hasMore: false,
};

async function tick(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
  }
}

async function create(permissions: string[] = ['prism.dashboard.view', 'prism.users.manage']) {
  const http = bootstrap({ me: { providers: [{ ...PROVIDER, permissions }] } });
  const fixture = TestBed.createComponent(ProviderUsersComponent);
  fixture.detectChanges();
  await tick(fixture);
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

async function createLoaded() {
  const made = await create();
  made.http.expectOne((r) => r.url === USERS && r.params.get('page') === '0').flush(PAGE);
  await tick(made.fixture);
  return made;
}

const selects = (root: HTMLElement) => [...root.querySelectorAll<HTMLSelectElement>('select')];

describe('ProviderUsersComponent', () => {
  it('lists the users with a role each, and the signed-in user cannot change their own', async () => {
    const { root } = await createLoaded();

    expect(root.textContent).toContain('Sam Smith');
    expect(root.textContent).toContain('You');
    expect(selects(root).map((s) => s.disabled)).toEqual([true, false, false]);
  });

  it('shows a restricted role as it is but will not offer it to be given', async () => {
    const { root } = await createLoaded();

    const options = [...selects(root)[1].querySelectorAll('option')];
    expect(options.find((o) => o.value === 'claims_reviewer')?.disabled).toBe(true);
    expect(options.find((o) => o.value === 'analyst')?.disabled).toBe(false);
    expect([...selects(root)[2].querySelectorAll('option')].find((o) => o.value === 'claims_reviewer')?.disabled).toBe(false);
  });

  it('changes a role and reloads', async () => {
    const { fixture, http, root } = await createLoaded();

    const select = selects(root)[1];
    select.value = 'analyst';
    select.dispatchEvent(new Event('change'));
    await tick(fixture);

    const put = http.expectOne({ method: 'PUT', url: `${USERS}/2/role` });
    expect(put.request.body).toEqual({ roleKey: 'analyst' });
    put.flush({ portalUserId: 2, displayName: 'Sam Smith', roleKey: 'analyst', lastSignIn: '2026-11-23T10:00:00Z', isYou: false });
    await tick(fixture);
    http.expectOne((r) => r.url === USERS).flush(PAGE);
  });

  it('shows the servers reason when a change is refused, and keeps the shown role', async () => {
    const { fixture, http, root } = await createLoaded();

    const select = selects(root)[1];
    select.value = 'analyst';
    select.dispatchEvent(new Event('change'));
    await tick(fixture);
    http.expectOne({ method: 'PUT', url: `${USERS}/2/role` }).flush({ title: 'Only EMS staff can give that role for this Provider.' }, { status: 409, statusText: 'Conflict' });
    await tick(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Only EMS staff');
    expect(selects(root)[1].value).toBe('viewer');
  });

  it('asks nothing of the server and explains when the user cannot manage users', async () => {
    const { http, root } = await create(['prism.dashboard.view']);

    expect(root.textContent).toContain("You can't manage users here");
    http.expectNone((r) => r.url === USERS);
  });

  it('has no axe violations', async () => {
    const { root } = await createLoaded();

    expect(await axeViolations(root)).toEqual([]);
  });

  it('leaves no request outstanding', async () => {
    const { http } = await createLoaded();

    http.verify();
  });
});
