import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { axeViolations } from '../axe';
import { AdminHomeComponent } from './admin-home.component';

function create(me: object) {
  TestBed.configureTestingModule({
    providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  TestBed.inject(AuthService).refreshMe().subscribe();
  TestBed.inject(HttpTestingController)
    .expectOne('/api/v1/me')
    .flush({ userName: 'jdoe', displayName: 'Jane Doe', providers: [], permissions: [], ...me });
  const fixture = TestBed.createComponent(AdminHomeComponent);
  fixture.detectChanges();
  return fixture;
}

describe('AdminHomeComponent', () => {
  it('lists only the tiers the user holds, in plain words', () => {
    const fixture = create({
      isInternal: true,
      adminPermissions: ['prism.admin.templates.publish', 'prism.admin.manage'],
    });

    const items = [...(fixture.nativeElement as HTMLElement).querySelectorAll('li strong')].map(
      (n) => n.textContent,
    );

    expect(items).toEqual(['Template publisher', 'Admin manager']);
  });

  it('ignores a permission it does not know', () => {
    const fixture = create({ isInternal: true, adminPermissions: ['prism.admin.unknown'] });

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No admin tools yet');
  });

  it('says so when an internal user holds no tier', () => {
    const fixture = create({ isInternal: true, adminPermissions: [] });

    expect((fixture.nativeElement as HTMLElement).querySelector('h2')?.textContent).toBe(
      'No admin tools yet',
    );
  });

  it('has no axe violations with tiers', async () => {
    const fixture = create({
      isInternal: true,
      adminPermissions: ['prism.admin.templates.author', 'prism.admin.customers.view'],
    });

    expect(await axeViolations(fixture.nativeElement as HTMLElement)).toEqual([]);
  });

  it('has no axe violations without tiers', async () => {
    const fixture = create({ isInternal: true });

    expect(await axeViolations(fixture.nativeElement as HTMLElement)).toEqual([]);
  });
});
