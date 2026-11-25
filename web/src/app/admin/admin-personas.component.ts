import { LiveAnnouncer } from '@angular/cdk/a11y';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import {
  EmsBreadcrumb,
  EmsBreadcrumbsComponent,
  EmsButtonDirective,
  EmsChipComponent,
} from '@ems-management-consultants/ui';
import { Observable, forkJoin, map } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import {
  PersonaDto,
  QuestionDto,
  TIER_AUTHOR,
  TIER_PUBLISH,
  ViewTemplateSummaryDto,
} from './admin.model';
import { AdminPersonasService } from './admin-personas.service';
import { AdminTemplatesService } from './admin-templates.service';

const CRUMBS: EmsBreadcrumb[] = [
  { label: 'Admin', link: '/admin' },
  { label: 'Personas and questions' },
];
const MAX_OPTIONS = 8;

interface Loaded {
  readonly personas: readonly PersonaDto[];
  readonly questions: readonly QuestionDto[];
  readonly templates: readonly ViewTemplateSummaryDto[];
}

interface PersonaForm {
  readonly id: number | null;
  readonly name: string;
  readonly templateId: number | null;
  readonly sortOrder: string;
}

interface OptionForm {
  readonly label: string;
  /** Weight text by Persona id. Blank means no weight. */
  readonly weights: Readonly<Record<number, string>>;
}

interface QuestionForm {
  readonly id: number | null;
  readonly prompt: string;
  readonly sortOrder: string;
  readonly allowsMultiple: boolean;
  readonly options: readonly OptionForm[];
}

interface Cell {
  readonly personaId: number;
  readonly name: string;
  readonly value: string;
}

interface OptionView {
  readonly index: number;
  readonly label: string;
  readonly cells: readonly Cell[];
}

function problemText(error: unknown, fallback: string): string {
  const title =
    error instanceof HttpErrorResponse && error.status === 409
      ? (error.error as { title?: unknown } | null)?.title
      : undefined;
  return typeof title === 'string' && title.length > 0 ? title : fallback;
}

function emptyQuestion(): QuestionForm {
  return {
    id: null,
    prompt: '',
    sortOrder: '1',
    allowsMultiple: false,
    options: [
      { label: '', weights: {} },
      { label: '', weights: {} },
    ],
  };
}

/**
 * Personas and the first-login questions. A Persona points at a published View Template, which becomes the home page of the users it is chosen for.
 * Each answer carries a weight per Persona and the highest total wins. Nothing here shows a user or an answer: there are none to show.
 */
@Component({
  selector: 'app-admin-personas',
  imports: [
    EmsBreadcrumbsComponent,
    EmsButtonDirective,
    EmsChipComponent,
    LoadProblemComponent,
    SkeletonComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="personas" id="main-content">
      <ems-breadcrumbs [items]="crumbs" />
      <h1 tabindex="-1">Personas and questions</h1>
      <p class="personas__lead">
        At first sign-in every customer answers two or three questions. The answers pick a Persona,
        and the Persona picks the home page. Users never see the Persona name.
      </p>

      @if (resource.isLoading() && !resource.hasValue()) {
        <app-skeleton height="6rem" />
        <span class="cdk-visually-hidden" role="status">Loading</span>
      } @else if (resource.error() !== undefined) {
        <app-load-problem
          [error]="resource.error()"
          subject="Personas and questions"
          (retry)="resource.reload()"
        />
      } @else {
        @if (problem()) {
          <p class="personas__problem" role="alert">{{ problem() }}</p>
        }

        <section aria-labelledby="personas-heading">
          <h2 id="personas-heading">Personas</h2>
          @if (personaRows().length === 0) {
            <p>No Personas yet. Without a published Persona, users are not asked the questions.</p>
          }
          <ul class="personas__list">
            @for (row of personaRows(); track row.id) {
              <li>
                <strong>{{ row.name }}</strong>
                @if (row.published) {
                  <ems-chip state="success" size="sm" icon="check">Published</ems-chip>
                } @else {
                  <ems-chip state="neutral" size="sm" icon="edit">Draft</ems-chip>
                }
                <span class="personas__meta">{{ row.detail }}</span>
                <span class="personas__actions">
                  @if (canAuthor()) {
                    <button
                      emsButton="flat"
                      type="button"
                      [attr.aria-label]="'Edit ' + row.name"
                      (click)="editPersona(row.persona)"
                    >
                      Edit
                    </button>
                    <button
                      emsButton="flat"
                      type="button"
                      [attr.aria-label]="'Delete ' + row.name"
                      (click)="deletePersona(row.persona)"
                    >
                      Delete
                    </button>
                  }
                  @if (canPublish()) {
                    @if (row.published) {
                      <button
                        emsButton="flat"
                        type="button"
                        [attr.aria-label]="'Unpublish ' + row.name"
                        (click)="togglePersona(row.persona)"
                      >
                        Unpublish
                      </button>
                    } @else {
                      <button
                        emsButton="raised"
                        type="button"
                        [attr.aria-label]="'Publish ' + row.name"
                        (click)="togglePersona(row.persona)"
                      >
                        Publish
                      </button>
                    }
                  }
                </span>
              </li>
            }
          </ul>

          @if (canAuthor()) {
            @if (personaForm(); as form) {
              <form
                class="personas__form"
                (submit)="$event.preventDefault(); savePersona()"
                aria-labelledby="persona-form-heading"
              >
                <h3 id="persona-form-heading">
                  {{ form.id === null ? 'Add a Persona' : 'Edit the Persona' }}
                </h3>
                <label for="persona-name">Name</label>
                <input
                  id="persona-name"
                  type="text"
                  autocomplete="off"
                  maxlength="80"
                  [value]="form.name"
                  (input)="patchPersona({ name: $any($event.target).value })"
                />
                <label for="persona-template">Home page template</label>
                <select
                  id="persona-template"
                  [value]="form.templateId ?? ''"
                  (change)="
                    patchPersona({
                      templateId: $any($event.target).value ? +$any($event.target).value : null,
                    })
                  "
                >
                  <option value="">Choose a View Template</option>
                  @for (template of templateOptions(); track template.id) {
                    <option [value]="template.id" [selected]="template.id === form.templateId">
                      {{ template.label }}
                    </option>
                  }
                </select>
                <label for="persona-order">Tie-break order</label>
                <input
                  id="persona-order"
                  type="text"
                  inputmode="numeric"
                  autocomplete="off"
                  maxlength="4"
                  aria-describedby="persona-order-hint"
                  [value]="form.sortOrder"
                  (input)="patchPersona({ sortOrder: $any($event.target).value })"
                />
                <p id="persona-order-hint" class="personas__meta">
                  When two Personas score the same, the lower number wins. Use 0 to 1000.
                </p>
                <div class="personas__actions">
                  <button
                    emsButton="filled"
                    type="submit"
                    [attr.aria-disabled]="busy() ? 'true' : null"
                  >
                    Save Persona
                  </button>
                  <button emsButton="flat" type="button" (click)="personaForm.set(null)">
                    Cancel
                  </button>
                </div>
              </form>
            } @else {
              <button emsButton="raised" type="button" (click)="newPersona()">Add a Persona</button>
            }
          }
        </section>

        <section aria-labelledby="questions-heading">
          <h2 id="questions-heading">First-login questions</h2>
          <p class="personas__meta">{{ questionSummary() }}</p>
          <ul class="personas__list">
            @for (row of questionRows(); track row.id) {
              <li>
                <strong>{{ row.prompt }}</strong>
                @if (row.published) {
                  <ems-chip state="success" size="sm" icon="check">Published</ems-chip>
                } @else {
                  <ems-chip state="neutral" size="sm" icon="edit">Draft</ems-chip>
                }
                <span class="personas__meta">{{ row.detail }}</span>
                <span class="personas__actions">
                  @if (canAuthor()) {
                    <button
                      emsButton="flat"
                      type="button"
                      [attr.aria-label]="'Edit ' + row.prompt"
                      (click)="editQuestion(row.question)"
                    >
                      Edit
                    </button>
                    <button
                      emsButton="flat"
                      type="button"
                      [attr.aria-label]="'Delete ' + row.prompt"
                      (click)="deleteQuestion(row.question)"
                    >
                      Delete
                    </button>
                  }
                  @if (canPublish()) {
                    @if (row.published) {
                      <button
                        emsButton="flat"
                        type="button"
                        [attr.aria-label]="'Unpublish ' + row.prompt"
                        (click)="toggleQuestion(row.question)"
                      >
                        Unpublish
                      </button>
                    } @else {
                      <button
                        emsButton="raised"
                        type="button"
                        [attr.aria-label]="'Publish ' + row.prompt"
                        (click)="toggleQuestion(row.question)"
                      >
                        Publish
                      </button>
                    }
                  }
                </span>
              </li>
            }
          </ul>

          @if (canAuthor()) {
            @if (questionForm(); as form) {
              <form
                class="personas__form"
                (submit)="$event.preventDefault(); saveQuestion()"
                aria-labelledby="question-form-heading"
              >
                <h3 id="question-form-heading">
                  {{ form.id === null ? 'Add a question' : 'Edit the question' }}
                </h3>
                <label for="question-prompt">Question</label>
                <input
                  id="question-prompt"
                  type="text"
                  autocomplete="off"
                  maxlength="200"
                  [value]="form.prompt"
                  (input)="patchQuestion({ prompt: $any($event.target).value })"
                />
                <label for="question-order">Order</label>
                <input
                  id="question-order"
                  type="text"
                  inputmode="numeric"
                  autocomplete="off"
                  maxlength="4"
                  [value]="form.sortOrder"
                  (input)="patchQuestion({ sortOrder: $any($event.target).value })"
                />
                <label class="personas__check">
                  <input
                    id="question-multiple"
                    type="checkbox"
                    [checked]="form.allowsMultiple"
                    (change)="patchQuestion({ allowsMultiple: $any($event.target).checked })"
                  />
                  Let people choose more than one answer
                </label>
                @for (option of optionViews(); track option.index) {
                  <fieldset>
                    <legend>Answer {{ option.index + 1 }}</legend>
                    <label [for]="'option-label-' + option.index">Answer text</label>
                    <input
                      [id]="'option-label-' + option.index"
                      type="text"
                      autocomplete="off"
                      maxlength="120"
                      [value]="option.label"
                      (input)="patchOption(option.index, $any($event.target).value)"
                    />
                    @for (cell of option.cells; track cell.personaId) {
                      <label [for]="'weight-' + option.index + '-' + cell.personaId"
                        >Points toward {{ cell.name }} (0 to 100)</label
                      >
                      <input
                        [id]="'weight-' + option.index + '-' + cell.personaId"
                        type="text"
                        inputmode="numeric"
                        autocomplete="off"
                        maxlength="3"
                        [value]="cell.value"
                        (input)="
                          patchWeight(option.index, cell.personaId, $any($event.target).value)
                        "
                      />
                    }
                    @if (form.options.length > 2) {
                      <button
                        emsButton="flat"
                        type="button"
                        [attr.aria-label]="'Remove answer ' + (option.index + 1)"
                        (click)="removeOption(option.index)"
                      >
                        Remove answer
                      </button>
                    }
                  </fieldset>
                }
                @if (form.options.length < maxOptions) {
                  <button emsButton="raised" type="button" (click)="addOption()">
                    Add an answer
                  </button>
                }
                <div class="personas__actions">
                  <button
                    emsButton="filled"
                    type="submit"
                    [attr.aria-disabled]="busy() ? 'true' : null"
                  >
                    Save question
                  </button>
                  <button emsButton="flat" type="button" (click)="questionForm.set(null)">
                    Cancel
                  </button>
                </div>
              </form>
            } @else {
              <button emsButton="raised" type="button" (click)="newQuestion()">
                Add a question
              </button>
            }
          }
        </section>
      }
    </main>
  `,
  styles: `
    .personas {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
      max-width: 60rem;
    }
    h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    h2 {
      margin: 0 0 0.5rem;
      font-size: var(--ems-font-size-subheading);
    }
    h3 {
      margin: 0;
      font-size: var(--ems-font-size-body);
    }
    .personas__lead,
    .personas__meta {
      margin: 0;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    .personas__problem {
      margin: 0;
      color: var(--ems-text-error);
    }
    .personas__list {
      list-style: none;
      margin: 0 0 0.75rem;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    .personas__list li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.25rem 0.75rem;
      overflow-wrap: anywhere;
    }
    .personas__actions {
      display: inline-flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .personas__form {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: 0.5rem;
      max-width: 32rem;
    }
    label {
      font-weight: 600;
    }
    .personas__check {
      display: flex;
      align-items: center;
      justify-content: flex-start;
      gap: 0.5rem;
      font-weight: normal;
    }
    /* A checkbox is not a text field: it keeps its own size next to its words. */
    .personas__check input {
      flex: none;
      inline-size: 1.25rem;
      min-block-size: 0;
      block-size: 1.25rem;
      padding: 0;
      margin: 0;
    }
    fieldset {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: 0.5rem;
      min-inline-size: 0;
      margin: 0;
      padding: 0.75rem;
      border: 1px solid var(--ems-border-default);
    }
    legend {
      padding-inline: 0.25rem;
      font-weight: 600;
    }
    input,
    select {
      box-sizing: border-box;
      inline-size: 100%;
      max-inline-size: 100%;
      min-block-size: 2.5rem;
      padding: 0.5rem;
      font: inherit;
    }
  `,
})
export class AdminPersonasComponent {
  private readonly auth = inject(AuthService);
  private readonly api = inject(AdminPersonasService);
  private readonly templates = inject(AdminTemplatesService);
  private readonly announcer = inject(LiveAnnouncer);

  protected readonly crumbs = CRUMBS;
  protected readonly maxOptions = MAX_OPTIONS;
  protected readonly canAuthor = computed(() => this.auth.adminPermissions().includes(TIER_AUTHOR));
  protected readonly canPublish = computed(() =>
    this.auth.adminPermissions().includes(TIER_PUBLISH),
  );

  protected readonly resource = rxResource<Loaded, void>({
    stream: () =>
      forkJoin({
        personas: this.api.personas(),
        questions: this.api.questions(),
        templates: this.templates.list(),
      }).pipe(
        map((all) => ({
          personas: all.personas.personas,
          questions: all.questions.questions,
          templates: all.templates.templates,
        })),
      ),
  });

  protected readonly busy = signal(false);
  protected readonly problem = signal('');
  protected readonly personaForm = signal<PersonaForm | null>(null);
  protected readonly questionForm = signal<QuestionForm | null>(null);

  private readonly loaded = computed<Loaded>(() =>
    this.resource.hasValue()
      ? this.resource.value()
      : { personas: [], questions: [], templates: [] },
  );
  protected readonly personaRows = computed(() =>
    this.loaded().personas.map((p) => ({
      id: p.id,
      name: p.name,
      published: p.isPublished,
      detail: `Home page: ${p.viewTemplateName ?? 'a template that no longer exists'} · order ${p.sortOrder}`,
      persona: p,
    })),
  );
  protected readonly questionRows = computed(() =>
    this.loaded().questions.map((q) => ({
      id: q.id,
      prompt: q.prompt,
      published: q.isPublished,
      detail: `${q.options.length} answers${q.allowsMultiple ? ' (choose all that apply)' : ''} · order ${q.sortOrder}`,
      question: q,
    })),
  );
  protected readonly questionSummary = computed(() => {
    const published = this.loaded().questions.filter((q) => q.isPublished).length;
    return published >= 2 && published <= 3
      ? `${published} questions are published. Users are asked them when at least one published Persona has a published template.`
      : `${published} published. Users are asked only when two or three questions are published.`;
  });
  protected readonly templateOptions = computed(() =>
    this.loaded().templates.map((t) => ({
      id: t.id,
      label: t.isPublished ? t.name : `${t.name} (draft: publish it before the Persona)`,
    })),
  );
  protected readonly optionViews = computed<readonly OptionView[]>(() => {
    const form = this.questionForm();
    if (!form) return [];
    const personas = this.loaded().personas;
    return form.options.map((option, index) => ({
      index,
      label: option.label,
      cells: personas.map((p) => ({
        personaId: p.id,
        name: p.name,
        value: option.weights[p.id] ?? '',
      })),
    }));
  });

  protected newPersona(): void {
    this.personaForm.set({ id: null, name: '', templateId: null, sortOrder: '1' });
  }

  protected editPersona(persona: PersonaDto): void {
    this.personaForm.set({
      id: persona.id,
      name: persona.name,
      templateId: persona.viewTemplateId,
      sortOrder: String(persona.sortOrder),
    });
  }

  protected patchPersona(change: Partial<PersonaForm>): void {
    this.personaForm.update((form) => (form ? { ...form, ...change } : form));
  }

  protected savePersona(): void {
    const form = this.personaForm();
    if (!form || this.busy()) return;
    const sortOrder = Number(form.sortOrder);
    if (
      form.name.trim() === '' ||
      form.templateId === null ||
      !Number.isInteger(sortOrder) ||
      sortOrder < 0 ||
      sortOrder > 1000
    ) {
      this.problem.set(
        'Give the Persona a name, choose a template, and use a whole number from 0 to 1000 for the order.',
      );
      return;
    }

    const body = { name: form.name.trim(), viewTemplateId: form.templateId, sortOrder };
    this.run(
      form.id === null ? this.api.createPersona(body) : this.api.updatePersona(form.id, body),
      'The Persona is saved.',
      "We couldn't save the Persona.",
      () => this.personaForm.set(null),
    );
  }

  protected togglePersona(persona: PersonaDto): void {
    this.run(
      persona.isPublished
        ? this.api.unpublishPersona(persona.id)
        : this.api.publishPersona(persona.id),
      persona.isPublished ? `${persona.name} is a draft again.` : `${persona.name} is published.`,
      "We couldn't change that Persona.",
    );
  }

  protected deletePersona(persona: PersonaDto): void {
    this.run(
      this.api.deletePersona(persona.id),
      `${persona.name} is deleted.`,
      "We couldn't delete that Persona.",
    );
  }

  protected newQuestion(): void {
    this.questionForm.set(emptyQuestion());
  }

  protected editQuestion(question: QuestionDto): void {
    this.questionForm.set({
      id: question.id,
      prompt: question.prompt,
      sortOrder: String(question.sortOrder),
      allowsMultiple: question.allowsMultiple,
      options: question.options.map((o) => ({
        label: o.label,
        weights: Object.fromEntries(o.weights.map((w) => [w.personaId, String(w.weight)])),
      })),
    });
  }

  protected patchQuestion(change: Partial<QuestionForm>): void {
    this.questionForm.update((form) => (form ? { ...form, ...change } : form));
  }

  protected patchOption(index: number, label: string): void {
    this.questionForm.update((form) =>
      form
        ? { ...form, options: form.options.map((o, i) => (i === index ? { ...o, label } : o)) }
        : form,
    );
  }

  protected patchWeight(index: number, personaId: number, value: string): void {
    this.questionForm.update((form) =>
      form
        ? {
            ...form,
            options: form.options.map((o, i) =>
              i === index ? { ...o, weights: { ...o.weights, [personaId]: value } } : o,
            ),
          }
        : form,
    );
  }

  protected addOption(): void {
    this.questionForm.update((form) =>
      form && form.options.length < MAX_OPTIONS
        ? { ...form, options: [...form.options, { label: '', weights: {} }] }
        : form,
    );
  }

  protected removeOption(index: number): void {
    this.questionForm.update((form) =>
      form && form.options.length > 2
        ? { ...form, options: form.options.filter((_, i) => i !== index) }
        : form,
    );
  }

  protected saveQuestion(): void {
    const form = this.questionForm();
    if (!form || this.busy()) return;
    const sortOrder = Number(form.sortOrder);
    const options = form.options.map((option, index) => ({
      label: option.label.trim(),
      sortOrder: index,
      weights: Object.entries(option.weights)
        .filter(([, value]) => value.trim() !== '')
        .map(([personaId, value]) => ({ personaId: Number(personaId), weight: Number(value) })),
    }));
    const weightsOk = options.every((o) =>
      o.weights.every((w) => Number.isInteger(w.weight) && w.weight >= 0 && w.weight <= 100),
    );
    if (
      form.prompt.trim() === '' ||
      !Number.isInteger(sortOrder) ||
      sortOrder < 0 ||
      sortOrder > 1000 ||
      options.some((o) => o.label === '') ||
      !weightsOk
    ) {
      this.problem.set(
        'Give the question and every answer some text, and use whole numbers from 0 to 100 for the points.',
      );
      return;
    }

    const body = {
      prompt: form.prompt.trim(),
      sortOrder,
      allowsMultiple: form.allowsMultiple,
      options,
    };
    this.run(
      form.id === null ? this.api.createQuestion(body) : this.api.updateQuestion(form.id, body),
      'The question is saved.',
      "We couldn't save the question.",
      () => this.questionForm.set(null),
    );
  }

  protected toggleQuestion(question: QuestionDto): void {
    this.run(
      question.isPublished
        ? this.api.unpublishQuestion(question.id)
        : this.api.publishQuestion(question.id),
      question.isPublished ? 'The question is a draft again.' : 'The question is published.',
      "We couldn't change that question.",
    );
  }

  protected deleteQuestion(question: QuestionDto): void {
    this.run(
      this.api.deleteQuestion(question.id),
      'The question is deleted.',
      "We couldn't delete that question.",
    );
  }

  private run(call: Observable<unknown>, done: string, failed: string, after?: () => void): void {
    this.busy.set(true);
    this.problem.set('');
    call.subscribe({
      next: () => {
        this.busy.set(false);
        after?.();
        this.announcer.announce(done, 'polite');
        this.resource.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.problem.set(problemText(error, failed));
      },
    });
  }
}
