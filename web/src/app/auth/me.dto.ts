/** A Provider the signed-in user may see, with the Facilities they are limited to (empty means all). */
export interface ProviderScopeDto {
  readonly id: string;
  readonly name: string;
  readonly facilityCount: number;
  /** What the user may do in this Provider, from their Customer Role. The server checks every request again. */
  readonly permissions?: readonly string[];
}

/** Response of GET /api/v1/me. */
export interface MeDto {
  readonly userName: string;
  readonly displayName: string;
  readonly providers: readonly ProviderScopeDto[];
  readonly permissions: readonly string[];
  /** True when the user has no Persona yet and a questionnaire is published. Set by the server only. */
  readonly onboardingRequired?: boolean;
  /** True for a member of the internal portal group. Only decides whether the admin area is shown; the server decides access. */
  readonly isInternal?: boolean;
  /** The admin tier permissions, for an Internal User only. */
  readonly adminPermissions?: readonly string[];
}
