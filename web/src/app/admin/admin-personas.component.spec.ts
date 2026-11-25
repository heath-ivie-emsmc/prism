import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { bootstrap } from '../saved-views/saved-views.testing';
import { AdminPersonasComponent } from './admin-personas.component';

const ROOT = '/api/v1/admin';
const PERSONAS = {
  personas: [
    {
      id: 1,
      name: 'Air chief',
      viewTemplateId: 5,
      viewTemplateName: 'Chief start',
      sortOrder: 1,
      isPublished: true,
    },
    {
      id: 2,
      name: 'Finance',
      viewTemplateId: 6,
      viewTemplateName: 'Finance start',
      sortOrder: 2,
      isPublished: false,
    },
  ],
};
const QUESTIONS = {
  questions: [
    {
      id: 11,
      prompt: 'What is your role?',
      sortOrder: 1,
      isPublished: true,
      options: [
        { id: 1, label: 'Air', sortOrder: 0, weights: [{ personaId: 1, weight: 10 }] },
        { id: 2, label: 'Billing', sortOrder: 1, weights: [{ personaId: 2, weight: 10 }] },
      ],
    },
  ],
};
const TEMPLATES = {
  templates: [
    {
      id: 5,
      name: 'Chief start',
      description: null,
      isPublished: true,
      widgetCount: 2,
      updatedAt: '2026-11-20T10:00:00Z',
    },
    {
      id: 6,
      name: 'Finance start',
      description: null,
      isPublished: false,
      widgetCount: 1,
      updatedAt: '2026-11-20T10:00:00Z',
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

async function create(adminPermissions: string[]) {
  const http = bootstrap({ me: { isInternal: true, adminPermissions } });
  const fixture = TestBed.createComponent(AdminPersonasComponent);
  fixture.detectChanges();
  http.expectOne(`${ROOT}/personas`).flush(PERSONAS);
  http.expectOne(`${ROOT}/onboarding-questions`).flush(QUESTIONS);
  http.expectOne(`${ROOT}/view-templates`).flush(TEMPLATES);
  await tick(fixture);
  return { fixture, http, root: fixture.nativeElement as HTMLElement };
}

const button = (root: HTMLElement, label: string) =>
  root.querySelector<HTMLButtonElement>(`[aria-label="${label}"]`) ??
  [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;

function type(root: HTMLElement, selector: string, value: string): void {
  const input = root.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
  input.value = value;
  input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
}

describe('AdminPersonasComponent', () => {
  it('lists Personas and questions with their state', async () => {
    const { root } = await create(['prism.admin.templates.author']);

    expect(root.textContent).toContain('Air chief');
    expect(root.textContent).toContain('Home page: Chief start');
    expect(root.textContent).toContain('Draft');
    expect(root.textContent).toContain('What is your role?');
    expect(root.textContent).toContain('1 published');
  });

  it('shows authoring buttons to an author and publishing buttons to a publisher only', async () => {
    const author = await create(['prism.admin.templates.author']);
    expect(author.root.querySelector('[aria-label="Edit Air chief"]')).not.toBeNull();
    expect(author.root.querySelector('[aria-label="Publish Finance"]')).toBeNull();
  });

  it('shows publish buttons to a publisher', async () => {
    const { root } = await create(['prism.admin.templates.publish']);

    expect(root.querySelector('[aria-label="Publish Finance"]')).not.toBeNull();
    expect(root.querySelector('[aria-label="Unpublish Air chief"]')).not.toBeNull();
    expect(root.querySelector('[aria-label="Edit Air chief"]')).toBeNull();
  });

  it('refuses a Persona with no template without calling the API', async () => {
    const { fixture, http, root } = await create(['prism.admin.templates.author']);

    button(root, 'Add a Persona').click();
    await tick(fixture);
    type(root, '#persona-name', 'New persona');
    root.querySelector<HTMLButtonElement>('form button[type="submit"]')!.click();
    await tick(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('choose a template');
    http.expectNone({ method: 'POST', url: `${ROOT}/personas` });
  });

  it('creates a Persona from the form', async () => {
    const { fixture, http, root } = await create(['prism.admin.templates.author']);

    button(root, 'Add a Persona').click();
    await tick(fixture);
    type(root, '#persona-name', 'New persona');
    type(root, '#persona-template', '5');
    await tick(fixture);
    root.querySelector<HTMLButtonElement>('form button[type="submit"]')!.click();
    await tick(fixture);

    const post = http.expectOne({ method: 'POST', url: `${ROOT}/personas` });
    expect(post.request.body).toEqual({ name: 'New persona', viewTemplateId: 5, sortOrder: 1 });
    post.flush({});
  });

  it('builds a question with a weight per Persona and sends only the filled ones', async () => {
    const { fixture, http, root } = await create(['prism.admin.templates.author']);

    button(root, 'Add a question').click();
    await tick(fixture);
    type(root, '#question-prompt', 'Which page first?');
    type(root, '#option-label-0', 'Cash');
    type(root, '#weight-0-1', '30');
    type(root, '#option-label-1', 'Denials');
    type(root, '#weight-1-2', '40');
    await tick(fixture);
    root.querySelector<HTMLButtonElement>('form button[type="submit"]')!.click();
    await tick(fixture);

    const post = http.expectOne({ method: 'POST', url: `${ROOT}/onboarding-questions` });
    expect(post.request.body).toEqual({
      allowsMultiple: false,
      prompt: 'Which page first?',
      sortOrder: 1,
      options: [
        { label: 'Cash', sortOrder: 0, weights: [{ personaId: 1, weight: 30 }] },
        { label: 'Denials', sortOrder: 1, weights: [{ personaId: 2, weight: 40 }] },
      ],
    });
    post.flush({});
  });

  it('lets a question accept more than one answer and sends that choice', async () => {
    const { fixture, http, root } = await create(['prism.admin.templates.author']);

    button(root, 'Add a question').click();
    await tick(fixture);
    type(root, '#question-prompt', 'Which lines of service do you support?');
    type(root, '#option-label-0', 'Air');
    type(root, '#option-label-1', 'Ground');
    root.querySelector<HTMLInputElement>('#question-multiple')!.click();
    await tick(fixture);
    root.querySelector<HTMLButtonElement>('form button[type="submit"]')!.click();
    await tick(fixture);

    const post = http.expectOne({ method: 'POST', url: `${ROOT}/onboarding-questions` });
    expect((post.request.body as { allowsMultiple: boolean }).allowsMultiple).toBe(true);
    post.flush({});
  });

  it('refuses a weight above 100 without calling the API', async () => {
    const { fixture, http, root } = await create(['prism.admin.templates.author']);

    button(root, 'Add a question').click();
    await tick(fixture);
    type(root, '#question-prompt', 'Q');
    type(root, '#option-label-0', 'A');
    type(root, '#weight-0-1', '101');
    type(root, '#option-label-1', 'B');
    await tick(fixture);
    root.querySelector<HTMLButtonElement>('form button[type="submit"]')!.click();
    await tick(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('0 to 100');
    http.expectNone({ method: 'POST', url: `${ROOT}/onboarding-questions` });
  });

  it('shows the servers reason when publishing is refused', async () => {
    const { fixture, http, root } = await create(['prism.admin.templates.publish']);

    button(root, 'Publish Finance').click();
    await tick(fixture);
    http
      .expectOne({ method: 'POST', url: `${ROOT}/personas/2/publish` })
      .flush(
        { title: 'A published Persona needs a published View Template.' },
        { status: 409, statusText: 'Conflict' },
      );
    await tick(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'needs a published View Template',
    );
  });

  it('has no axe violations with both forms open', async () => {
    const { fixture, root } = await create([
      'prism.admin.templates.author',
      'prism.admin.templates.publish',
    ]);

    button(root, 'Add a Persona').click();
    button(root, 'Add a question').click();
    await tick(fixture);

    expect(await axeViolations(root)).toEqual([]);
  });

  it('leaves no request outstanding', async () => {
    const { http } = await create(['prism.admin.templates.author']);

    http.verify();
  });
});
