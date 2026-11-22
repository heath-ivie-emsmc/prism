import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { SavedViewsListComponent } from './saved-views-list.component';
import { BASE, bootstrap, settle } from './saved-views.testing';

const SUMMARY = { id: 5, name: 'Cash overview', description: 'Monthly look', ownerName: 'jdoe', isOwner: true, widgetCount: 1, updatedAt: '2026-11-22T10:00:00Z' };
const THEIRS = { ...SUMMARY, id: 6, name: 'Shared cash', description: null, ownerName: 'asmith', isOwner: false, widgetCount: 3 };

async function create(list: object, me: object = {}) {
  const http = bootstrap({ me });
  const fixture: ComponentFixture<SavedViewsListComponent> = TestBed.createComponent(SavedViewsListComponent);
  await settle(fixture, http, { [`${BASE}/saved-views`]: list });
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

describe('SavedViewsListComponent', () => {
  it('shows my views and the ones shared with me, each as a link', async () => {
    const { root } = await create({ mine: [SUMMARY], shared: [THEIRS] });

    expect([...root.querySelectorAll('h2')].map((h) => h.textContent)).toEqual(['My views', 'Shared with me']);
    expect([...root.querySelectorAll('li a')].map((a) => a.getAttribute('href'))).toEqual(['/saved-views/5', '/saved-views/6']);
    expect(root.textContent).toContain('1 metric');
    expect(root.textContent).toContain('3 metrics');
    expect(root.textContent).toContain('Shared by asmith');
    expect(root.textContent).toContain('Monthly look');
  });

  it('invites a new user to create a first view', async () => {
    const { root } = await create({ mine: [], shared: [] });

    expect(root.textContent).toContain('No saved views yet');
    expect(root.querySelectorAll('a[href="/saved-views/new"]').length).toBe(2);
  });

  it('says when only one of the two lists is empty', async () => {
    const { root } = await create({ mine: [], shared: [THEIRS] });

    expect(root.textContent).toContain("You haven't created a view yet.");
    TestBed.resetTestingModule();
    const onlyMine = await create({ mine: [SUMMARY], shared: [] });
    expect(onlyMine.root.textContent).toContain('Nothing has been shared with you yet.');
  });

  it('hides Create a view from someone who may only read', async () => {
    const { root } = await create({ mine: [], shared: [THEIRS] }, { permissions: ['prism.app', 'prism.dashboard.view'] });

    expect(root.querySelector('a[href="/saved-views/new"]')).toBeNull();
  });

  it('says calmly when the list cannot load, and offers to try again', async () => {
    const http = bootstrap();
    const fixture = TestBed.createComponent(SavedViewsListComponent);
    fixture.detectChanges();
    for (const request of http.match((r) => r.url.endsWith('/periods'))) request.flush({ providerId: '526', periods: [] });
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne(`${BASE}/saved-views`).flush({ title: 'x' }, { status: 503, statusText: 'Service Unavailable' });
    await Promise.resolve();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain("We can't load your saved views right now");
    expect(root.textContent).not.toContain('503');
  });

  it('passes axe', async () => {
    const { root } = await create({ mine: [SUMMARY], shared: [THEIRS] });

    expect(await axeViolations(root)).toEqual([]);
  });
});
