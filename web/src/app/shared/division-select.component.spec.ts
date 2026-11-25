import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { describe, expect, it } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { axeViolations } from '../axe';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { CHOSEN_ON_DASHBOARD, DivisionSelectComponent, FOLLOWS_DASHBOARD } from './division-select.component';
import { flushDivisions } from './division.testing';

@Component({
  imports: [DivisionSelectComponent],
  template: `<app-division-select [mode]="mode()" [selectedKey]="key()" [line]="line()" [unavailable]="unavailable()" [ignoredBy]="ignored()" />`,
})
class HostComponent {
  readonly mode = signal<'edit' | 'follow'>('edit');
  readonly key = signal<string | undefined>(undefined);
  readonly line = signal<'air' | 'ground' | undefined>(undefined);
  readonly unavailable = signal(false);
  readonly ignored = signal<readonly string[]>([]);
}

describe('DivisionSelectComponent', () => {
  function create(answer: 'ok' | 'error' | 'empty' | 'hold' = 'ok'): { fixture: ComponentFixture<HostComponent>; root: HTMLElement; http: HttpTestingController } {
    TestBed.configureTestingModule({ providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush({ userName: 'j', displayName: 'J', providers: [{ id: '526', name: 'P', facilityCount: 0 }], permissions: [] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    TestBed.tick();
    for (const r of http.match((q) => q.url.endsWith('/periods'))) r.flush({ providerId: '526', periods: [] });
    if (answer === 'ok') flushDivisions(http);
    if (answer === 'empty') flushDivisions(http, { providerId: '526', divisions: [] });
    if (answer === 'error') for (const r of http.match((q) => q.url.endsWith('/divisions'))) r.flush({ title: 'x' }, { status: 503, statusText: 'Service Unavailable' });
    return { fixture, root: fixture.nativeElement as HTMLElement, http };
  }

  async function show(fixture: ComponentFixture<HostComponent>): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  /** The text the select shows for its value: the library puts it in the input, so it can only ever be text. */
  const shown = (root: HTMLElement): string => root.querySelector<HTMLInputElement>('ems-select input')?.value ?? '';
  const note = (root: HTMLElement): string => root.querySelector('#division-reason')?.textContent?.trim() ?? '';

  it('is a live select with no note when the list is here and nothing ignores it', async () => {
    const { fixture, root } = create();
    await show(fixture);
    expect(root.querySelectorAll('ems-select')).toHaveLength(1);
    expect(note(root)).toBe('');
  });

  it('says it is loading while the list is on its way', async () => {
    const { fixture, root } = create('hold');
    fixture.detectChanges();
    expect(note(root)).toContain('Loading');
    expect(root.querySelector('ems-select')!.getAttribute('aria-describedby')).toBe('division-reason');
  });

  it('says the list could not be loaded and offers to try again', async () => {
    const { fixture, root, http } = create('error');
    await show(fixture);
    expect(note(root)).toContain('could not be loaded');
    expect(note(root)).not.toContain('503');
    root.querySelector<HTMLButtonElement>('#division-reason button')!.click();
    TestBed.tick();
    expect(http.match((r) => r.url.endsWith('/divisions'))).toHaveLength(1);
  });

  it('says so when the Provider has no divisions', async () => {
    const { fixture, root } = create('empty');
    await show(fixture);
    expect(note(root)).toContain('No Base location values were found');
  });

  it('says nothing about metrics that ignore the choice', async () => {
    const { fixture, root } = create();
    fixture.componentInstance.ignored.set(['Net cash collected', 'Clean claim rate']);
    await show(fixture);
    expect(note(root)).toBe('');
  });

  it('turns off with the reason when nothing on the page can use it', async () => {
    const { fixture, root } = create();
    fixture.componentInstance.unavailable.set(true);
    fixture.componentInstance.ignored.set(['Clean claim rate']);
    await show(fixture);
    expect(note(root)).toBe("Not applied yet: Clean claim rate can't be split by Base location.");
  });

  it('follows the key from the address: off, with the division name and the reason', async () => {
    const { fixture, root } = create();
    fixture.componentInstance.mode.set('follow');
    fixture.componentInstance.key.set('summit');
    fixture.componentInstance.line.set('ground');
    await show(fixture);
    expect(note(root)).toBe(FOLLOWS_DASHBOARD);
    expect(shown(root)).toBe('Summit Base');
    expect(root.textContent).toContain('Reporting division');
  });

  it('follows a key the list does not hold as "Chosen on the dashboard"', async () => {
    const { fixture, root } = create();
    fixture.componentInstance.mode.set('follow');
    fixture.componentInstance.key.set('gone');
    await show(fixture);
    expect(shown(root)).toBe(CHOSEN_ON_DASHBOARD);
  });

  it('follows no key as all divisions', async () => {
    const { fixture, root } = create();
    fixture.componentInstance.mode.set('follow');
    await show(fixture);
    expect(shown(root)).toBe('All bases');
  });

  it('shows a hostile division name as text, never as markup', async () => {
    const { fixture, root } = create();
    fixture.componentInstance.mode.set('follow');
    fixture.componentInstance.key.set('x-img');
    await show(fixture);
    expect(shown(root)).toBe('<img src=x onerror=alert(1)> Base');
    expect(root.querySelector('img')).toBeNull();
  });

  it('keeps a division the new Service Line has no transports for, says so in a note and a live region, and clears it when all is chosen', async () => {
    const { fixture, root, http } = create();
    await show(fixture);
    const filters = TestBed.inject(DashboardFilterContext);
    filters.division.setValue('summit');
    await show(fixture);
    expect(note(root)).toBe('');

    filters.serviceLine.set('ground');
    TestBed.tick();
    for (const r of http.match((q) => q.url.endsWith('/divisions'))) if (!r.cancelled) r.flush({ providerId: '526', divisions: [{ key: 'riverside', name: 'Riverside Base' }] });
    await show(fixture);

    expect(filters.division.value).toBe('summit');
    expect(shown(root)).toBe('Summit Base (no ground transports)');
    const message = 'Summit Base has no ground transports, so its figures are empty. Choose all reporting divisions to see ground.';
    expect(note(root)).toBe(message);
    expect(root.querySelector('[role="status"]')?.textContent).toBe(message);
    expect(await axeViolations(root)).toEqual([]);

    filters.division.setValue('all');
    await show(fixture);
    expect(note(root)).toBe('');
    expect(root.querySelector('[role="status"]')?.textContent).toBe('');
  });

  it('passes axe in the live, loading, error and followed states', async () => {
    const { fixture, root } = create('error');
    await show(fixture);
    expect(await axeViolations(root)).toEqual([]);
    fixture.componentInstance.mode.set('follow');
    await show(fixture);
    expect(await axeViolations(root)).toEqual([]);
  });
});
