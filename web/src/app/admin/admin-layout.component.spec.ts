import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { axeViolations } from '../axe';
import { AdminLayoutComponent } from './admin-layout.component';

function create(adminPermissions: string[]) {
  TestBed.configureTestingModule({
    providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  TestBed.inject(AuthService).refreshMe().subscribe();
  TestBed.inject(HttpTestingController).expectOne('/api/v1/me').flush({
    userName: 'jdoe',
    displayName: 'Jane Doe',
    providers: [],
    permissions: [],
    isInternal: true,
    adminPermissions,
  });
  const fixture = TestBed.createComponent(AdminLayoutComponent);
  fixture.detectChanges();
  return fixture;
}

const open = (root: HTMLElement) =>
  [...root.querySelectorAll('nav a')].map((a) => a.textContent?.trim());
const closed = (root: HTMLElement) =>
  [...root.querySelectorAll('nav [aria-disabled="true"]')].map((n) =>
    n.textContent?.replace(/\s+/g, ' ').trim(),
  );

describe('AdminLayoutComponent', () => {
  it('opens every area to someone holding every tier', () => {
    const fixture = create(['prism.admin.templates.author', 'prism.admin.manage']);

    expect(open(fixture.nativeElement)).toEqual([
      'Overview',
      'View Templates',
      'Personas and questions',
      'Customer users',
    ]);
    expect(closed(fixture.nativeElement)).toEqual([]);
  });

  it('shows the template areas to an admin manager, marked as needing the author role', () => {
    const fixture = create(['prism.admin.manage']);

    expect(open(fixture.nativeElement)).toEqual(['Overview', 'Customer users']);
    expect(closed(fixture.nativeElement)).toEqual([
      'View Templates Needs the Template author role',
      'Personas and questions Needs the Template author role',
    ]);
  });

  it('shows customer users to a template author, marked as needing the admin manager role', () => {
    const fixture = create(['prism.admin.templates.author']);

    expect(open(fixture.nativeElement)).toEqual([
      'Overview',
      'View Templates',
      'Personas and questions',
    ]);
    expect(closed(fixture.nativeElement)).toEqual(['Customer users Needs the Admin manager role']);
  });

  it('opens only the overview when there is no tier, and ignores a tier it does not know', () => {
    const fixture = create(['prism.admin.unknown']);

    expect(open(fixture.nativeElement)).toEqual(['Overview']);
    expect(closed(fixture.nativeElement)).toHaveLength(3);
  });

  it('is a labelled navigation region with no axe violations', async () => {
    const fixture = create(['prism.admin.manage']);

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('nav')?.getAttribute('aria-label'),
    ).toBe('Admin');
    expect(await axeViolations(fixture.nativeElement)).toEqual([]);
  });

  it('has a button that hides and shows the menu and says whether it is open', () => {
    const fixture = create(['prism.admin.manage']);
    const root = fixture.nativeElement as HTMLElement;
    const button = () => root.querySelector<HTMLButtonElement>('.adminshell__toggle')!;
    expect(button().getAttribute('aria-expanded')).toBe('true');

    button().click();
    fixture.detectChanges();

    expect(button().getAttribute('aria-expanded')).toBe('false');
    expect(button().textContent).toContain('Admin menu');
    expect(root.querySelectorAll('nav a')).toHaveLength(0);

    button().click();
    fixture.detectChanges();

    expect(root.querySelectorAll('nav a').length).toBeGreaterThan(0);
  });

  it('starts closed on a template editor, which needs the width, and opens on request', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideEmsUi(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
      ],
    });
    await TestBed.inject(Router).navigateByUrl('/admin/templates/new');
    TestBed.inject(AuthService).refreshMe().subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({
        userName: 'jdoe',
        displayName: 'Jane Doe',
        providers: [],
        permissions: [],
        isInternal: true,
        adminPermissions: ['prism.admin.templates.author'],
      });
    const fixture = TestBed.createComponent(AdminLayoutComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelectorAll('nav a')).toHaveLength(0);

    root.querySelector<HTMLButtonElement>('.adminshell__toggle')!.click();
    fixture.detectChanges();

    expect(root.querySelectorAll('nav a').length).toBeGreaterThan(0);
  });

  it('closes a floating menu with Escape', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideEmsUi(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
      ],
    });
    await TestBed.inject(Router).navigateByUrl('/admin/templates/7');
    TestBed.inject(AuthService).refreshMe().subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({
        userName: 'jdoe',
        displayName: 'Jane Doe',
        providers: [],
        permissions: [],
        isInternal: true,
        adminPermissions: ['prism.admin.templates.author'],
      });
    const fixture = TestBed.createComponent(AdminLayoutComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('.adminshell__toggle')!.click();
    fixture.detectChanges();
    expect(root.querySelector('.adminshell__nav--floating')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();

    expect(root.querySelector('nav')).toBeNull();
  });

  it('closes a floating menu when the page outside it is clicked, but not when the menu or its button is', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideEmsUi(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
      ],
    });
    await TestBed.inject(Router).navigateByUrl('/admin/templates/7');
    TestBed.inject(AuthService).refreshMe().subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({
        userName: 'jdoe',
        displayName: 'Jane Doe',
        providers: [],
        permissions: [],
        isInternal: true,
        adminPermissions: ['prism.admin.templates.author'],
      });
    const fixture = TestBed.createComponent(AdminLayoutComponent);
    document.body.append(fixture.nativeElement);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('.adminshell__toggle')!.click();
    fixture.detectChanges();

    root.querySelector<HTMLElement>('.adminshell__title')!.click();
    fixture.detectChanges();
    expect(root.querySelector('nav')).not.toBeNull();

    document.body.click();
    fixture.detectChanges();
    expect(root.querySelector('nav')).toBeNull();
    fixture.nativeElement.remove();
  });

  it('leaves a menu that sits beside the page open when the page is clicked', () => {
    const fixture = create(['prism.admin.manage']);
    document.body.append(fixture.nativeElement);

    document.body.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('nav')).not.toBeNull();
    fixture.nativeElement.remove();
  });
});
