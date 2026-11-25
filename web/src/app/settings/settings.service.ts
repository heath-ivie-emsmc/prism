import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SavedViewListDto } from '../saved-views/layout.model';

/** The signed-in user's own settings in a Provider. The user is the session's and the Provider is the route's: neither is sent in a body. */
@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly http = inject(HttpClient);

  pushedViews(providerId: string): Observable<SavedViewListDto> {
    return this.http.get<SavedViewListDto>(`/api/v1/providers/${encodeURIComponent(providerId)}/settings/pushed-views/`);
  }

  hide(providerId: string, viewId: number): Observable<void> {
    return this.http.post<void>(`/api/v1/providers/${encodeURIComponent(providerId)}/settings/pushed-views/${viewId}/hide`, null);
  }

  restore(providerId: string, viewId: number): Observable<void> {
    return this.http.post<void>(`/api/v1/providers/${encodeURIComponent(providerId)}/settings/pushed-views/${viewId}/restore`, null);
  }
}
