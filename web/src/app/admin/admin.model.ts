import { SavedViewLayout } from '../saved-views/layout.model';

/** The shapes the admin View Template API speaks. A layout holds Metric keys and positions only. */

export interface ViewTemplateSummaryDto {
  readonly id: number;
  readonly name: string;
  readonly description: string | null;
  readonly isPublished: boolean;
  readonly widgetCount: number;
  readonly updatedAt: string;
}

export interface ViewTemplateListDto {
  readonly templates: readonly ViewTemplateSummaryDto[];
}

export interface ViewTemplateDto {
  readonly id: number;
  readonly name: string;
  readonly description: string | null;
  readonly isPublished: boolean;
  readonly layout: SavedViewLayout;
  readonly version: string;
  readonly updatedAt: string;
}

export type AssignmentKind = 'All' | 'Provider';

export interface AssignmentScopeDto {
  readonly kind: AssignmentKind;
  readonly providerId: number | null;
}

export interface AssignmentPreviewDto {
  readonly viewTemplateId: number;
  readonly templateName: string;
  readonly widgetCount: number;
  readonly scope: AssignmentScopeDto;
  /** Null for everyone: Prism has no list of every Provider. */
  readonly providerCount: number | null;
}

export interface AssignmentDto {
  readonly id: number;
  readonly viewTemplateId: number;
  readonly scope: AssignmentScopeDto;
  readonly name: string;
  readonly widgetCount: number;
  readonly deliveredCount: number;
  readonly createdAt: string;
  readonly revokedAt: string | null;
}

export interface AssignmentListDto {
  readonly assignments: readonly AssignmentDto[];
}

/** The tier permissions the server checks. The SPA only uses them to decide what to show. */
export const TIER_AUTHOR = 'prism.admin.templates.author';
export const TIER_PUBLISH = 'prism.admin.templates.publish';

/** Personas and the first-login questionnaire, as the admin API speaks them. */
export interface PersonaDto {
  readonly id: number;
  readonly name: string;
  readonly viewTemplateId: number;
  readonly viewTemplateName: string | null;
  readonly sortOrder: number;
  readonly isPublished: boolean;
}

export interface PersonaListDto {
  readonly personas: readonly PersonaDto[];
}

export interface PersonaRequest {
  readonly name: string;
  readonly viewTemplateId: number;
  readonly sortOrder: number;
}

export interface WeightDto {
  readonly personaId: number;
  readonly weight: number;
}

export interface QuestionOptionDto {
  readonly id: number;
  readonly label: string;
  readonly sortOrder: number;
  readonly weights: readonly WeightDto[];
}

export interface QuestionDto {
  readonly id: number;
  readonly prompt: string;
  readonly sortOrder: number;
  readonly isPublished: boolean;
  /** True when a user may choose more than one answer (choose all that apply). */
  readonly allowsMultiple: boolean;
  readonly options: readonly QuestionOptionDto[];
}

export interface QuestionListDto {
  readonly questions: readonly QuestionDto[];
}

export interface QuestionRequest {
  readonly prompt: string;
  readonly sortOrder: number;
  readonly allowsMultiple: boolean;
  readonly options: readonly {
    readonly label: string;
    readonly sortOrder: number;
    readonly weights: readonly WeightDto[];
  }[];
}
