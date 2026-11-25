import { SavedViewLayout } from '../saved-views/layout.model';

/** The shapes the home page and first-login questionnaire APIs speak. */

export type HomeSource = 'default' | 'persona' | 'custom';

export interface HomePageDto {
  readonly source: HomeSource;
  /** Set for `persona`: the Persona's current published View Template. */
  readonly layout: SavedViewLayout | null;
  /** Set for `custom`: the Saved View that is the user's own copy. */
  readonly savedViewId: number | null;
}

export interface OnboardingOptionDto {
  readonly id: number;
  readonly label: string;
}

export interface OnboardingQuestionDto {
  readonly id: number;
  readonly prompt: string;
  /** True for a choose-all-that-apply question (at least one answer). False means exactly one. */
  readonly allowsMultiple: boolean;
  readonly options: readonly OnboardingOptionDto[];
}

export interface OnboardingDto {
  readonly questions: readonly OnboardingQuestionDto[];
}

export interface OnboardingAnswerDto {
  readonly questionId: number;
  readonly optionId: number;
}
