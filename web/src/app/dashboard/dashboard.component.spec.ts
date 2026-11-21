import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { MeDto } from '../auth/me.dto';
import { DashboardComponent } from './dashboard.component';

describe('DashboardComponent header', () => {
  async function create(me: Partial<MeDto> = {}): Promise<{ fixture: ComponentFixture<DashboardComponent>; root: HTMLElement }> {
    TestBed.configureTestingModule({ providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(AuthService).refreshMe().subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({ userName: 'jdoe', displayName: 'Jane Doe', providers: [], permissions: [], ...me });
    const fixture = TestBed.createComponent(DashboardComponent);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  const radios = (root: HTMLElement) => [...root.querySelectorAll<HTMLButtonElement>('[role="radio"]')];

  it('shows the Provider name, Data Through and when the data was updated', async () => {
    const { root } = await create();

    expect(root.querySelector('h1')?.textContent).toBe('Acme Air Care');
    expect(root.querySelector('.dashboard__subtitle')?.textContent).toContain('Data through 10/31/2026');
    expect(root.querySelector('.dashboard__subtitle')?.textContent).toContain('Updated 11/05/2026, 7:42 AM');
  });

  it('shows the Provider name exactly as the session carries it, even when it is only the id', async () => {
    const { root } = await create({ providers: [{ id: '100', name: '100', facilityCount: 0 }] });

    expect(root.querySelector('h1')?.textContent).toBe('100');
  });

  it('shows the first Provider name from the session', async () => {
    const { root } = await create({
      providers: [
        { id: '100', name: 'Mercy Air', facilityCount: 0 },
        { id: '200', name: 'Other', facilityCount: 0 },
      ],
    });

    expect(root.querySelector('h1')?.textContent).toBe('Mercy Air');
  });

  const cards = (root: HTMLElement) =>
    [...root.querySelectorAll('app-metric-card')].map((card) => card.textContent?.replace(/\s+/g, ' ').trim() ?? '');

  it('shows the three metric cards for air, with the wing split, nautical miles and a projection', async () => {
    const { root } = await create();

    const [cash, transports, miles] = cards(root);
    expect(cash).toContain('Net cash collected');
    expect(cash).toContain('$1,284,500');
    expect(cash).toContain('Projected for the period: $1,402,000');
    expect(transports).toContain('142');
    expect(transports).toContain('Rotor wing96');
    expect(transports).toContain('Fixed wing46');
    expect(miles).toContain('18,420');
    expect(miles).toContain('Nautical (air only)');
  });

  it('drops the air-only breakdowns for ground and splits air from ground for both', async () => {
    const { fixture, root } = await create();

    radios(root)[1].click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const ground = cards(root);
    expect(ground[1]).not.toContain('Rotor wing');
    expect(ground[2]).not.toContain('Nautical');

    radios(root)[2].click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(cards(root)[1]).toContain('Air142');
    expect(cards(root)[1]).toContain('Ground318');
  });

  it('starts on Air only with the air label for the Reporting Division filter', async () => {
    const { root } = await create();

    expect(radios(root).map((r) => [r.textContent?.trim(), r.getAttribute('aria-checked')])).toEqual([
      ['flight Air only', 'true'],
      ['Ground only', 'false'],
      ['Air + ground', 'false'],
    ]);
    expect(root.textContent).toContain('Base location');
    expect(root.textContent).toContain('Period');
  });

  it('renames the Reporting Division filter when the Service Line changes', async () => {
    const { fixture, root } = await create();

    radios(root)[1].click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(radios(root)[1].getAttribute('aria-checked')).toBe('true');
    expect(root.textContent).toContain('Reporting division');
    expect(root.textContent).not.toContain('Base location');
  });

  it('moves the Service Line with the arrow keys and keeps only the chosen option in the tab order', async () => {
    const { fixture, root } = await create();
    const press = (key: string) => radios(root).find((r) => r.tabIndex === 0)!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));

    press('ArrowRight');
    fixture.detectChanges();

    expect(radios(root).map((r) => r.getAttribute('aria-checked'))).toEqual(['false', 'true', 'false']);
    expect(radios(root).map((r) => r.tabIndex)).toEqual([-1, 0, -1]);

    press('ArrowLeft');
    fixture.detectChanges();
    press('ArrowLeft');
    fixture.detectChanges();
    expect(radios(root).map((r) => r.getAttribute('aria-checked'))).toEqual(['false', 'false', 'true']);
  });
});
