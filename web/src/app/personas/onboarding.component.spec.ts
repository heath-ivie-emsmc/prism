import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { axeViolations } from '../axe';
import { bootstrap } from '../saved-views/saved-views.testing';
import { OnboardingComponent } from './onboarding.component';

const MULTIPLE = {
  questions: [
    {
      id: 1,
      prompt: 'What best describes your role?',
      allowsMultiple: false,
      options: [
        { id: 10, label: 'I run an air program' },
        { id: 11, label: 'I handle billing' },
      ],
    },
    {
      id: 2,
      prompt: 'Which lines of service do you support?',
      allowsMultiple: true,
      options: [
        { id: 20, label: 'Air' },
        { id: 21, label: 'Ground' },
        { id: 22, label: 'Interfacility' },
        { id: 23, label: 'Fire billing' },
      ],
    },
  ],
};

const QUESTIONS = {
  questions: [
    {
      id: 1,
      prompt: 'What best describes your role?',
      allowsMultiple: false,
      options: [
        { id: 10, label: 'I run an air program' },
        { id: 11, label: 'I handle billing' },
      ],
    },
    {
      id: 2,
      prompt: 'What do you check most?',
      allowsMultiple: false,
      options: [
        { id: 20, label: 'Cash' },
        { id: 21, label: 'Denials' },
      ],
    },
  ],
};

async function tick(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    fixture.detectChanges();
    TestBed.tick();
  }
}

async function create(reply: object | 'error' = QUESTIONS, retake = false) {
  const http = bootstrap({
    me: { onboardingRequired: !retake },
    providers: retake
      ? [{ provide: ActivatedRoute, useValue: { snapshot: { data: { retake: true } } } }]
      : [],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  const fixture = TestBed.createComponent(OnboardingComponent);
  fixture.detectChanges();
  const request = http.expectOne('/api/v1/me/onboarding');
  if (reply === 'error') request.flush('no', { status: 500, statusText: 'Server Error' });
  else request.flush(reply);
  await tick(fixture);
  return { fixture, http, navigate, root: fixture.nativeElement as HTMLElement };
}

const radio = (root: HTMLElement, name: string, index: number) =>
  root.querySelectorAll<HTMLInputElement>(`input[name="${name}"]`)[index];

describe('OnboardingComponent', () => {
  it('asks every question with its options', async () => {
    const { root } = await create();

    expect([...root.querySelectorAll('legend')].map((l) => l.textContent?.trim())).toEqual([
      'What best describes your role?',
      'What do you check most?',
    ]);
    expect(root.querySelectorAll('input[type="radio"]').length).toBe(4);
  });

  it('will not continue until every question is answered', async () => {
    const { fixture, http, root } = await create();

    radio(root, 'q1', 0).click();
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await tick(fixture);

    expect(root.querySelector('.error')?.textContent).toContain('answer every question');
    http.expectNone({ method: 'POST', url: '/api/v1/me/onboarding' });
  });

  it('sends one answer per question, refreshes the profile and goes to the dashboard', async () => {
    const { fixture, http, navigate, root } = await create();

    radio(root, 'q1', 1).click();
    radio(root, 'q2', 0).click();
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await tick(fixture);

    const post = http.expectOne({ method: 'POST', url: '/api/v1/me/onboarding' });
    expect(post.request.body).toEqual({
      answers: [
        { questionId: 1, optionId: 11 },
        { questionId: 2, optionId: 20 },
      ],
    });
    post.flush(null, { status: 204, statusText: 'No Content' });
    await tick(fixture);
    http.expectOne('/api/v1/me').flush({
      userName: 'jdoe',
      displayName: 'Jane',
      providers: [],
      permissions: [],
      onboardingRequired: false,
    });
    await tick(fixture);

    expect(navigate).toHaveBeenCalledWith('/dashboard');
  });

  it('says so when saving fails and lets the user try again', async () => {
    const { fixture, http, root } = await create();

    radio(root, 'q1', 0).click();
    radio(root, 'q2', 0).click();
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await tick(fixture);
    http
      .expectOne({ method: 'POST', url: '/api/v1/me/onboarding' })
      .flush('no', { status: 500, statusText: 'Server Error' });
    await tick(fixture);

    expect(root.querySelector('.error')?.textContent).toContain("couldn't save");
    expect(root.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('fails open when there is nothing to ask', async () => {
    const { navigate } = await create({ questions: [] });

    expect(TestBed.inject(AuthService).onboardingRequired()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/dashboard');
  });

  it('fails open when the questions cannot be loaded', async () => {
    const { navigate } = await create('error');

    expect(TestBed.inject(AuthService).onboardingRequired()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/dashboard');
  });

  it('when answering again, says so, returns to settings and does not count as the first login', async () => {
    const { fixture, http, navigate, root } = await create(QUESTIONS, true);

    expect(root.querySelector('h1')?.textContent).toBe('Your questions');
    radio(root, 'q1', 0).click();
    radio(root, 'q2', 0).click();
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await tick(fixture);
    http
      .expectOne({ method: 'POST', url: '/api/v1/me/onboarding' })
      .flush(null, { status: 204, statusText: 'No Content' });
    await tick(fixture);
    http
      .expectOne('/api/v1/me')
      .flush({ userName: 'jdoe', displayName: 'Jane', providers: [], permissions: [] });
    await tick(fixture);

    expect(navigate).toHaveBeenCalledWith('/settings');
  });

  it('when answering again and nothing is published, goes back to settings without skipping onboarding', async () => {
    const { navigate } = await create({ questions: [] }, true);

    expect(navigate).toHaveBeenCalledWith('/settings');
  });

  it('has no axe violations', async () => {
    const { root } = await create();

    expect(await axeViolations(root)).toEqual([]);
  });

  it('leaves no request outstanding', async () => {
    const { http } = await create();

    http.verify();
  });

  it('shows a choose-all-that-apply question as checkboxes with a hint, and a single-choice one as radios', async () => {
    const { root } = await create(MULTIPLE);

    expect(root.querySelectorAll('input[type="radio"]')).toHaveLength(2);
    expect(root.querySelectorAll('input[type="checkbox"]')).toHaveLength(4);
    expect(root.querySelectorAll('legend')[1]?.textContent).toContain('Choose all that apply');
    expect(root.querySelectorAll('legend')[0]?.textContent).not.toContain('Choose all that apply');
  });

  it('will not continue while a choose-all-that-apply question has nothing chosen, and lets a choice be taken back', async () => {
    const { fixture, http, root } = await create(MULTIPLE);
    radio(root, 'q1', 0).click();
    radio(root, 'q2', 1).click();
    radio(root, 'q2', 1).click();
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await tick(fixture);

    expect(root.querySelector('.error')?.textContent).toContain('answer every question');
    http.expectNone({ method: 'POST', url: '/api/v1/me/onboarding' });
  });

  it('sends one answer for each option chosen on a multiple-choice question', async () => {
    const { fixture, http, root } = await create(MULTIPLE);
    radio(root, 'q1', 1).click();
    radio(root, 'q2', 0).click();
    radio(root, 'q2', 2).click();
    radio(root, 'q2', 3).click();

    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await tick(fixture);

    const post = http.expectOne({ method: 'POST', url: '/api/v1/me/onboarding' });
    expect(post.request.body).toEqual({
      answers: [
        { questionId: 1, optionId: 11 },
        { questionId: 2, optionId: 20 },
        { questionId: 2, optionId: 22 },
        { questionId: 2, optionId: 23 },
      ],
    });
    post.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('has no axe violations with a multiple-choice question', async () => {
    const { root } = await create(MULTIPLE);

    expect(await axeViolations(root)).toEqual([]);
  });
});
