import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ProviderSettingsDto, ProviderUserDto, ProviderUsersDto } from './customers.model';

/**
 * Customer roles: a Provider admin managing their own Provider's users, and staff managing any Provider's. The user changing a role is always the session's,
 * and the Provider is in the route: neither is sent in a body.
 */
@Injectable({ providedIn: 'root' })
export class CustomerRolesService {
  private readonly http = inject(HttpClient);

  users(providerId: string, page: number): Observable<ProviderUsersDto> {
    return this.http.get<ProviderUsersDto>(`/api/v1/providers/${encodeURIComponent(providerId)}/users`, { params: { page } });
  }

  setRole(providerId: string, userId: number, roleKey: string): Observable<ProviderUserDto> {
    return this.http.put<ProviderUserDto>(`/api/v1/providers/${encodeURIComponent(providerId)}/users/${userId}/role`, { roleKey });
  }

  staffUsers(providerId: number, page: number): Observable<ProviderUsersDto> {
    return this.http.get<ProviderUsersDto>(`/api/v1/admin/customers/${providerId}/users`, { params: { page } });
  }

  staffSetRole(providerId: number, userId: number, roleKey: string): Observable<ProviderUserDto> {
    return this.http.put<ProviderUserDto>(`/api/v1/admin/customers/${providerId}/users/${userId}/role`, { roleKey });
  }

  staffSettings(providerId: number): Observable<ProviderSettingsDto> {
    return this.http.get<ProviderSettingsDto>(`/api/v1/admin/customers/${providerId}/settings`);
  }

  staffSetSettings(providerId: number, settings: ProviderSettingsDto): Observable<ProviderSettingsDto> {
    return this.http.put<ProviderSettingsDto>(`/api/v1/admin/customers/${providerId}/settings`, settings);
  }
}
