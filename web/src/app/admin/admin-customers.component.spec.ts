import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { bootstrap } from '../saved-views/saved-views.testing';
import { AdminCustomersComponent } from './admin-customers.component';

const ROOT = '/api/v1/admin/customers/526';
const PAGE = {
  users: [
    {
      portalUserId: 2,
      displayName: 'Sam Smith',
      roleKey: 'viewer',
      lastSignIn: '2026-11-23T10:00:00Z',
      isYou: false,
    },
  ],
  roles: [
    { key: 'viewer', name: 'Viewer', description: 'See.', restricted: false, assignable: true },
    { key: 'analyst', name: 'Analyst', description: 'Views.', restricted: false, assignable: true },
    {
      key: 'provider_admin',
      name: 'Provider admin',
      description: 'Manage users.',
      restricted: false,
      assignable: true,
    },
  ],
  hasMore: false,
};

async function tick(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
  }
}

async function create() {
  const http = bootstrap({
    me: { isInternal: true, adminPermissions: ['prism.admin.manage'], providers: [] },
  });
  const fixture = TestBed.createComponent(AdminCustomersComponent);
  fixture.detectChanges();
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

async function open(fixture: ComponentFixture<unknown>, root: HTMLElement, text: string) {
  const input = root.querySelector<HTMLInputElement>('#customer-provider')!;
  input.value = text;
  input.dispatchEvent(new Event('input'));
  root.querySelector<HTMLButtonElement>('form button[type="submit"]')!.click();
  await tick(fixture);
}

async function createOpen() {
  const made = await create();
  await open(made.fixture, made.root, '526');
  made.http.expectOne((r) => r.url === `${ROOT}/users`).flush(PAGE);
  made.http
    .expectOne(`${ROOT}/settings`)
    .flush({ defaultRoleKey: 'analyst', allowAdminRestrictedGrants: true });
  await tick(made.fixture);
  return made;
}

describe('AdminCustomersComponent', () => {
  it('refuses a Provider number that is not digits without calling the API', async () => {
    const { fixture, http, root } = await create();

    await open(fixture, root, '52a');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('digits only');
    http.expectNone((r) => r.url.includes('/admin/customers'));
  });

  it('opens a Provider with its settings and users', async () => {
    const { root } = await createOpen();

    expect(root.textContent).toContain('Provider 526 settings');
    expect(root.textContent).toContain('Sam Smith');
    expect(root.querySelector<HTMLSelectElement>('#default-role')!.value).toBe('analyst');
    expect(root.querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(true);
  });

  it('makes a user the first Provider admin', async () => {
    const { fixture, http, root } = await createOpen();

    const select = root.querySelector<HTMLSelectElement>('app-user-roles-table select')!;
    select.value = 'provider_admin';
    select.dispatchEvent(new Event('change'));
    await tick(fixture);

    const put = http.expectOne({ method: 'PUT', url: `${ROOT}/users/2/role` });
    expect(put.request.body).toEqual({ roleKey: 'provider_admin' });
    put.flush({});
  });

  it('saves the default role and the restricted-grant switch', async () => {
    const { fixture, http, root } = await createOpen();

    root.querySelector<HTMLSelectElement>('#default-role')!.value = 'viewer';
    root.querySelector<HTMLSelectElement>('#default-role')!.dispatchEvent(new Event('change'));
    root.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    await tick(fixture);
    root.querySelector<HTMLButtonElement>('.customers__settings button[type="submit"]')!.click();
    await tick(fixture);

    const put = http.expectOne({ method: 'PUT', url: `${ROOT}/settings` });
    expect(put.request.body).toEqual({
      defaultRoleKey: 'viewer',
      allowAdminRestrictedGrants: false,
    });
    put.flush({});
  });

  it('has no axe violations', async () => {
    const { root } = await createOpen();

    expect(await axeViolations(root)).toEqual([]);
  });
});
