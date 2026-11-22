import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  CatalogDto,
  InviteResultDto,
  PendingInvitationDto,
  RedeemDto,
  SavedViewDto,
  SavedViewListDto,
  SavedViewRequest,
  SharingDto,
} from './layout.model';

/** A view with the version the API sent in the `ETag` header, to send back as `If-Match` when saving. */
export interface LoadedView {
  readonly view: SavedViewDto;
  readonly etag: string;
}

/** Saved Views for a Provider. The Provider is in the path and checked by the API on every call. The browser never sends an owner or a Provider in a body. */
@Injectable({ providedIn: 'root' })
export class SavedViewsService {
  private readonly http = inject(HttpClient);

  catalog(providerId: string): Observable<CatalogDto> {
    return this.http.get<CatalogDto>(`${this.provider(providerId)}/metrics/catalog`);
  }

  list(providerId: string): Observable<SavedViewListDto> {
    return this.http.get<SavedViewListDto>(this.views(providerId));
  }

  get(providerId: string, id: number): Observable<LoadedView> {
    return this.http
      .get<SavedViewDto>(`${this.views(providerId)}/${id}`, { observe: 'response' })
      .pipe(map((response) => ({ view: response.body!, etag: response.headers.get('ETag') ?? `"${response.body!.version}"` })));
  }

  create(providerId: string, body: SavedViewRequest): Observable<LoadedView> {
    return this.http
      .post<SavedViewDto>(this.views(providerId), body, { observe: 'response' })
      .pipe(map((response) => ({ view: response.body!, etag: response.headers.get('ETag') ?? `"${response.body!.version}"` })));
  }

  update(providerId: string, id: number, body: SavedViewRequest, etag: string): Observable<LoadedView> {
    return this.http
      .put<SavedViewDto>(`${this.views(providerId)}/${id}`, body, { observe: 'response', headers: new HttpHeaders({ 'If-Match': etag }) })
      .pipe(map((response) => ({ view: response.body!, etag: response.headers.get('ETag') ?? `"${response.body!.version}"` })));
  }

  delete(providerId: string, id: number): Observable<void> {
    return this.http.delete<void>(`${this.views(providerId)}/${id}`);
  }

  invite(providerId: string, id: number, emails: readonly string[]): Observable<InviteResultDto> {
    return this.http.post<InviteResultDto>(`${this.views(providerId)}/${id}/invitations`, { emails });
  }

  sharing(providerId: string, id: number): Observable<SharingDto> {
    return this.http.get<SharingDto>(`${this.views(providerId)}/${id}/shares`);
  }

  revokeShare(providerId: string, id: number, shareId: number): Observable<void> {
    return this.http.delete<void>(`${this.views(providerId)}/${id}/shares/${shareId}`);
  }

  revokeInvitation(providerId: string, id: number, invitationId: number): Observable<void> {
    return this.http.delete<void>(`${this.views(providerId)}/${id}/invitations/${invitationId}`);
  }

  redeem(token: string): Observable<RedeemDto> {
    return this.http.post<RedeemDto>('/api/v1/saved-view-invitations/redeem', { token });
  }

  private provider(providerId: string): string {
    return `/api/v1/providers/${encodeURIComponent(providerId)}`;
  }

  private views(providerId: string): string {
    return `${this.provider(providerId)}/saved-views`;
  }
}

export type { PendingInvitationDto };
