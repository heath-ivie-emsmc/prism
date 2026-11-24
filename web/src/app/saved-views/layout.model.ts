/** The shapes the Saved Views API speaks. A layout holds Metric keys and positions only: never a value, a Label or a filter. */

export const GRID_COLUMNS = 12;
export const MAX_ROWS = 200;
export const MAX_WIDGETS = 24;
export const MAX_VIEW_NAME = 80;
export const MAX_DESCRIPTION = 300;
export const MAX_EMAILS_PER_INVITE = 10;
export const LAYOUT_VERSION = 1;

export type Density = 'comfortable' | 'compact';
export type SizePreset = 'S' | 'M' | 'L';

export interface LayoutWidget {
  readonly id: string;
  readonly metricKey: string;
  readonly presentation: string;
  readonly options?: Readonly<Record<string, string>>;
  readonly x: number;
  readonly y: number;
  readonly w: number;
  readonly h: number;
}

export interface SavedViewLayout {
  readonly version: number;
  readonly density: Density;
  readonly widgets: readonly LayoutWidget[];
}

export const EMPTY_LAYOUT: SavedViewLayout = { version: LAYOUT_VERSION, density: 'comfortable', widgets: [] };

/** The grid cells a preset covers. The catalog narrows which presets suit a presentation. */
export const SIZE_PRESETS: Readonly<Record<SizePreset, { readonly w: number; readonly h: number }>> = {
  S: { w: 3, h: 2 },
  M: { w: 6, h: 3 },
  L: { w: 12, h: 4 },
};

export interface CatalogCategoryDto {
  readonly key: string;
  readonly name: string;
}

export interface CatalogPresentationDto {
  readonly key: string;
  readonly name: string;
  readonly icon: string;
  readonly sizes: readonly string[];
  readonly minWidth: number;
  readonly minHeight: number;
  readonly maxWidth: number;
  readonly maxHeight: number;
  readonly isDefault: boolean;
}

export interface CatalogOptionDto {
  readonly key: string;
  readonly name: string;
  readonly type: 'choice' | 'toggle';
  readonly choices: readonly string[];
  readonly default: string;
  readonly appliesTo: readonly string[];
}

export interface CatalogMetricDto {
  readonly key: string;
  readonly name: string;
  readonly description: string;
  readonly unit: string;
  readonly category: string;
  readonly status: 'available' | 'planned';
  readonly placeable: boolean;
  readonly claimLevel: boolean;
  readonly serviceLines: readonly string[];
  readonly dimensions: readonly string[];
  readonly presentations: readonly CatalogPresentationDto[];
  readonly options: readonly CatalogOptionDto[];
  /** What the Metric applies of the page scope. Older responses omit it. */
  readonly context?: { readonly divisionSupport?: 'supported' | 'none'; readonly appliesTo?: 'air' | 'ground' | 'both' };
}

export interface CatalogDto {
  readonly categories: readonly CatalogCategoryDto[];
  readonly metrics: readonly CatalogMetricDto[];
}

export interface SavedViewSummaryDto {
  readonly id: number;
  readonly name: string;
  readonly description: string | null;
  readonly ownerName: string | null;
  readonly isOwner: boolean;
  readonly widgetCount: number;
  readonly updatedAt: string;
}

export interface SavedViewListDto {
  readonly mine: readonly SavedViewSummaryDto[];
  readonly shared: readonly SavedViewSummaryDto[];
}

export interface SavedViewDto {
  readonly id: number;
  readonly name: string;
  readonly description: string | null;
  readonly ownerName: string | null;
  readonly isOwner: boolean;
  readonly layout: SavedViewLayout;
  readonly version: string;
  readonly updatedAt: string;
}

export interface SavedViewRequest {
  readonly name: string;
  readonly description: string | null;
  readonly layout: SavedViewLayout;
}

export interface PendingInvitationDto {
  readonly invitationId: number;
  readonly email: string;
  readonly expiresAt: string;
  readonly isExpired: boolean;
}

export interface RecipientDto {
  readonly shareId: number;
  readonly email: string | null;
  readonly sharedAt: string;
}

export interface SharingDto {
  readonly recipients: readonly RecipientDto[];
  readonly pending: readonly PendingInvitationDto[];
}

export interface InviteResultDto {
  readonly sent: readonly PendingInvitationDto[];
  readonly failed: readonly string[];
}

export interface RedeemDto {
  readonly providerId: string;
  readonly viewId: number;
}
