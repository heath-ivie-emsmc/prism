/** A Provider the signed-in user may see, with the Facilities they are limited to (empty means all). */
export interface ProviderScopeDto {
  readonly id: string;
  readonly name: string;
  readonly facilityCount: number;
}

/** Response of GET /api/v1/me. */
export interface MeDto {
  readonly userName: string;
  readonly displayName: string;
  readonly providers: readonly ProviderScopeDto[];
  readonly permissions: readonly string[];
}
