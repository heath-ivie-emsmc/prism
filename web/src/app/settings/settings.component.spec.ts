import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { BASE, bootstrap } from '../saved-views/saved-views.testing';
import { SettingsComponent } from './settings.component';

const HOME = `${BASE}/home`;
const PUSHED = `${BASE}/settings/pushed-views/`;
const QUESTIONS = { questions: [{ id: 1, prompt: 'Q', options: [{ id: 1, label: 'A' }, { id: 2, label: 'B' }] }] };
const summary = (id: number, name: string, isHidden = false) => ({ id, name, description: null, ownerName: 'Prism team', isOwner: true, widgetCount: 2, updatedAt: '2026-11-24T10:00:00Z', fromTemplate: true, isHidden });

async function tick(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
  }
}

async function create(source: string, questions: object = QUESTIONS, pushed = [summary(5, 'Finance start'), summary(6, 'Chief start', true)]) {
  const http = bootstrap();
  const fixture = TestBed.createComponent(SettingsComponent);
  fixture.detectChanges();
  http.expectOne(HOME).flush({ source, layout: null, savedViewId: source === 'custom' ? 7 : null });
  http.expectOne('/api/v1/me/onboarding').flush(questions);
  http.expectOne(PUSHED).flush({ mine: pushed, shared: [] });
  await tick(fixture);
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

const button = (root: HTMLElement, label: string) => root.querySelector<HTMLButtonElement>(`[aria-label="${label}"]`) ?? [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;

describe('SettingsComponent', () => {
  it('lists pushed views with the From EMS marker and their hidden state', async () => {
    const { root } = await create('persona');

    expect(root.textContent).toContain('Finance start');
    expect(root.textContent).toContain('From EMS');
    expect(root.querySelector('[aria-label="Hide Finance start"]')).not.toBeNull();
    expect(root.querySelector('[aria-label="Restore Chief start"]')).not.toBeNull();
  });

  it('hides a view and swaps its button in place, moving focus to it', async () => {
    const { fixture, http, root } = await create('persona');
    document.body.append(root);

    button(root, 'Hide Finance start').click();
    await tick(fixture);
    http.expectOne({ method: 'POST', url: `${PUSHED}5/hide` }).flush(null, { status: 204, statusText: 'No Content' });
    await tick(fixture);

    expect(root.querySelector('[aria-label="Restore Finance start"]')).not.toBeNull();
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Restore Finance start');
    root.remove();
  });

  it('restores a hidden view', async () => {
    const { fixture, http, root } = await create('persona');

    button(root, 'Restore Chief start').click();
    await tick(fixture);
    http.expectOne({ method: 'POST', url: `${PUSHED}6/restore` }).flush(null, { status: 204, statusText: 'No Content' });
    await tick(fixture);

    expect(root.querySelector('[aria-label="Hide Chief start"]')).not.toBeNull();
  });

  it('says so when hiding fails', async () => {
    const { fixture, http, root } = await create('persona');

    button(root, 'Hide Finance start').click();
    await tick(fixture);
    http.expectOne({ method: 'POST', url: `${PUSHED}5/hide` }).flush('no', { status: 500, statusText: 'Server Error' });
    await tick(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain("couldn't hide");
    expect(root.querySelector('[aria-label="Hide Finance start"]')).not.toBeNull();
  });

  it('asks before resetting a customized home page, then resets it', async () => {
    const { fixture, http, root } = await create('custom');

    button(root, 'Reset to the recommended page').click();
    await tick(fixture);
    expect(root.textContent).toContain('This deletes your copy');
    http.expectNone({ method: 'DELETE', url: `${HOME}/customize` });

    button(root, 'Confirm reset').click();
    await tick(fixture);
    http.expectOne({ method: 'DELETE', url: `${HOME}/customize` }).flush(null, { status: 204, statusText: 'No Content' });
    await tick(fixture);
    http.expectOne(HOME).flush({ source: 'persona', layout: null, savedViewId: null });
  });

  it('offers no reset when the home page is not customized, and never names the Persona', async () => {
    const { root } = await create('persona');

    expect([...root.querySelectorAll('button')].map((b) => b.textContent?.trim())).not.toContain('Reset to the recommended page');
    expect(root.textContent?.toLowerCase()).not.toContain('persona');
  });

  it('offers to answer the questions again only when a questionnaire is published', async () => {
    const withQuestions = await create('persona');
    expect(withQuestions.root.querySelector('a[href="/settings/questionnaire"]')).not.toBeNull();
    TestBed.resetTestingModule();

    const without = await create('persona', { questions: [] });
    expect(without.root.querySelector('a[href="/settings/questionnaire"]')).toBeNull();
  });

  it('says when nothing has been pushed', async () => {
    const { root } = await create('default', QUESTIONS, []);

    expect(root.textContent).toContain('No views have been pushed to you.');
  });

  it('has no axe violations', async () => {
    const { root } = await create('custom');

    expect(await axeViolations(root)).toEqual([]);
  });

  it('leaves no request outstanding', async () => {
    const { http } = await create('persona');

    http.verify();
  });
});
