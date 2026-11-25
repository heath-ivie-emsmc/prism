import { LiveAnnouncer } from '@angular/cdk/a11y';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { EmsButtonDirective } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { SkeletonComponent } from '../shared/skeleton.component';
import { OnboardingDto, OnboardingQuestionDto } from './personas.model';
import { OnboardingService } from './personas.service';

/**
 * The first-login questions: two or three, all answered before the home page. It fails open: if there is nothing to ask, or the questions cannot be
 * loaded, the user goes on to the dashboard instead of being stopped. The answers are sent once and the server keeps only the outcome.
 */
@Component({
  selector: 'app-onboarding',
  imports: [EmsButtonDirective, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="welcome" id="main-content">
      <h1 tabindex="-1">{{ retake ? 'Your questions' : 'Welcome to Prism' }}</h1>
      <p class="welcome__lead">
        {{
          retake
            ? 'Answer again to get a recommended home page that fits your work now. A home page you already customized stays as it is.'
            : 'A few quick questions help us start you on a page that fits your work. You can change it later.'
        }}
      </p>

      @if (resource.isLoading() && !resource.hasValue()) {
        <app-skeleton height="8rem" />
        <span class="cdk-visually-hidden" role="status">Loading the questions</span>
      } @else if (questions().length > 0) {
        <form (submit)="$event.preventDefault(); submit()">
          @for (question of questions(); track question.id) {
            <fieldset>
              <legend>
                {{ question.prompt }}
                @if (question.allowsMultiple) {
                  <span class="hint">Choose all that apply.</span>
                }
              </legend>
              @for (option of question.options; track option.id) {
                <label>
                  <input
                    [type]="question.allowsMultiple ? 'checkbox' : 'radio'"
                    [name]="'q' + question.id"
                    [value]="option.id"
                    [checked]="isChosen(question.id, option.id)"
                    (change)="pick(question, option.id)"
                  />
                  {{ option.label }}
                </label>
              }
            </fieldset>
          }
          <p class="error" [attr.role]="problem() ? 'alert' : null">{{ problem() }}</p>
          <button emsButton="filled" type="submit" [attr.aria-disabled]="busy() ? 'true' : null">
            Continue
          </button>
        </form>
      }
    </main>
  `,
  styles: `
    .welcome {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1.5rem;
      max-width: 40rem;
    }
    h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .welcome__lead {
      margin: 0;
    }
    form {
      display: grid;
      gap: 1rem;
    }
    fieldset {
      margin: 0;
      padding: 0;
      border: 0;
      display: grid;
      gap: 0.5rem;
    }
    legend {
      padding: 0;
      margin-block-end: 0.25rem;
      font-weight: 600;
    }
    .hint {
      display: block;
      font-weight: normal;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    label {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      min-block-size: 2rem;
      overflow-wrap: anywhere;
    }
    .error {
      margin: 0;
      color: var(--ems-text-error);
    }
    .error:empty {
      display: none;
    }
  `,
})
export class OnboardingComponent {
  private readonly api = inject(OnboardingService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly announcer = inject(LiveAnnouncer);
  /** Answering again from settings. It leaves the user where they started instead of at the dashboard, and never counts as the first login. */
  protected readonly retake = inject(ActivatedRoute).snapshot.data['retake'] === true;
  private readonly after = this.retake ? '/settings' : '/dashboard';

  protected readonly resource = rxResource<OnboardingDto, undefined>({
    stream: () => this.api.questions(),
  });
  protected readonly questions = computed<readonly OnboardingQuestionDto[]>(() =>
    this.resource.hasValue() ? this.resource.value().questions : [],
  );
  /** The options chosen for each question: one for a single-choice question, any number for a multiple-choice one. */
  protected readonly answers = signal<Readonly<Record<number, readonly number[]>>>({});
  protected readonly busy = signal(false);
  protected readonly problem = signal('');

  constructor() {
    // Nothing to ask, or it could not be loaded: let the user through rather than stop them.
    effect(() => {
      const failed = this.resource.error() !== undefined;
      const empty = this.resource.hasValue() && this.questions().length === 0;
      if (failed || empty) {
        untracked(() => {
          if (!this.retake) this.auth.skipOnboarding();
          void this.router.navigateByUrl(this.after);
        });
      }
    });
  }

  protected isChosen(questionId: number, optionId: number): boolean {
    return this.answers()[questionId]?.includes(optionId) ?? false;
  }

  protected pick(question: OnboardingQuestionDto, optionId: number): void {
    if (question.allowsMultiple) this.toggle(question.id, optionId);
    else this.choose(question.id, optionId);
  }

  /** A single-choice question keeps one option. */
  protected choose(questionId: number, optionId: number): void {
    this.answers.update((current) => ({ ...current, [questionId]: [optionId] }));
    this.problem.set('');
  }

  /** A multiple-choice question adds or removes the option. */
  protected toggle(questionId: number, optionId: number): void {
    this.answers.update((current) => {
      const held = current[questionId] ?? [];
      return {
        ...current,
        [questionId]: held.includes(optionId)
          ? held.filter((id) => id !== optionId)
          : [...held, optionId],
      };
    });
    this.problem.set('');
  }

  protected submit(): void {
    if (this.busy()) return;
    const answers = this.answers();
    const missing = this.questions().some((q) => (answers[q.id]?.length ?? 0) === 0);
    if (missing) {
      this.problem.set('Please answer every question.');
      return;
    }

    this.busy.set(true);
    this.api
      .submit(
        this.questions().flatMap((q) =>
          (answers[q.id] ?? []).map((optionId) => ({ questionId: q.id, optionId })),
        ),
      )
      .subscribe({
        next: () =>
          this.auth.refreshMe().subscribe(() => {
            this.announcer.announce(
              this.retake
                ? 'Thanks. Your recommended home page is updated.'
                : 'Thanks. Taking you to your dashboard.',
              'polite',
            );
            void this.router.navigateByUrl(this.after);
          }),
        error: () => {
          this.busy.set(false);
          this.problem.set("We couldn't save your answers. Please try again.");
        },
      });
  }
}
