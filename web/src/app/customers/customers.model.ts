/** The shapes the customer role APIs speak. */

export interface ProviderUserDto {
  readonly portalUserId: number;
  readonly displayName: string;
  readonly roleKey: string;
  readonly lastSignIn: string;
  readonly isYou: boolean;
}

export interface RoleDto {
  readonly key: string;
  readonly name: string;
  readonly description: string;
  /** Carries claim or PHI access. Only staff give it, unless they allowed Provider admins to. */
  readonly restricted: boolean;
  /** Whether the signed-in user may give this role. */
  readonly assignable: boolean;
}

export interface ProviderUsersDto {
  readonly users: readonly ProviderUserDto[];
  readonly roles: readonly RoleDto[];
  readonly hasMore: boolean;
}

export interface ProviderSettingsDto {
  readonly defaultRoleKey: string;
  readonly allowAdminRestrictedGrants: boolean;
}

export const USERS_MANAGE = 'prism.users.manage';
