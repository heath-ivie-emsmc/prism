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

  const radios = (root: HTMLElement) => [...root.querySelectorAll<HTMLButtonElement>('app-service-line-toggle [role="radio"]')];

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

  it('lays out the net cash panel, four metric tiles and the projection panel', async () => {
    const { root } = await create();

    expect(root.querySelector('app-net-cash-panel h2')?.textContent).toContain('Air net cash collected');
    const tiles = [...root.querySelectorAll('app-kpi-card')].map((tile) => tile.textContent?.replace(/\s+/g, ' ').trim() ?? '');
    expect(tiles).toHaveLength(4);
    expect(tiles[0]).toContain('Transports');
    expect(tiles[1]).toContain('Loaded miles');
    expect(tiles[2]).toContain('Clean claim rate');
    expect(tiles[3]).toContain('Open negotiation');
    expect(root.querySelector('app-projection-panel h2')?.textContent).toContain('projection');
  });

  it('switches the net cash panel between a chart and a table with the chart type control', async () => {
    const { fixture, root } = await create();
    const radio = (name: string) => root.querySelector<HTMLButtonElement>(`app-net-cash-panel [role="radio"][aria-label="${name}"]`)!;
    expect(root.querySelector('app-net-cash-panel app-chart')).not.toBeNull();
    expect(root.querySelector('app-net-cash-panel table')).toBeNull();

    radio('Table').click();
    fixture.detectChanges();

    expect(root.querySelector('app-net-cash-panel app-chart')).toBeNull();
    const rows = [...root.querySelectorAll('app-net-cash-panel tbody tr')].map((r) => r.textContent?.replace(/\s+/g, ' ').trim());
    expect(rows).toHaveLength(9);
    expect(rows[5]).toContain('Oct 2026');
    expect(rows[5]).toContain('$1,100,240.00');
    expect(rows[5]).toContain('12.0%');
    expect(rows[6]).toContain('Projected');
    expect(rows[6]).toContain('$1.15M to $1.28M');

    radio('Bar chart').click();
    fixture.detectChanges();
    expect(root.querySelector('app-net-cash-panel table')).toBeNull();
  });

  it('hides the expected 3-month figure when the projection switch is off', async () => {
    const { fixture, root } = await create();
    const toggle = root.querySelector<HTMLInputElement>('app-net-cash-panel input[role="switch"]')!;
    expect(root.querySelector('app-net-cash-panel')?.textContent).toContain('Expected, next 3 months');

    toggle.click();
    fixture.detectChanges();

    expect(root.querySelector('app-net-cash-panel')?.textContent).not.toContain('Expected, next 3 months');
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
