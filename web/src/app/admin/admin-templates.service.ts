import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SavedViewRequest } from '../saved-views/layout.model';
import {
  AssignmentDto,
  AssignmentListDto,
  AssignmentPreviewDto,
  AssignmentScopeDto,
  ViewTemplateDto,
  ViewTemplateListDto,
} from './admin.model';

/** A template with the version the API sent in the `ETag` header, to send back as `If-Match` when saving. */
export interface LoadedTemplate {
  readonly template: ViewTemplateDto;
  readonly etag: string;
}

const ROOT = '/api/v1/admin';

/** View Templates and their pushes. Every call is checked on the server for the group and the tier; nothing here carries a user or a Provider in a body. */
@Injectable({ providedIn: 'root' })
export class AdminTemplatesService {
  private readonly http = inject(HttpClient);

  list(): Observable<ViewTemplateListDto> {
    return this.http.get<ViewTemplateListDto>(`${ROOT}/view-templates`);
  }

  get(id: number): Observable<LoadedTemplate> {
    return this.http
      .get<ViewTemplateDto>(`${ROOT}/view-templates/${id}`, { observe: 'response' })
      .pipe(map(this.loaded));
  }

  create(body: SavedViewRequest): Observable<LoadedTemplate> {
    return this.http
      .post<ViewTemplateDto>(`${ROOT}/view-templates`, body, { observe: 'response' })
      .pipe(map(this.loaded));
  }

  update(id: number, body: SavedViewRequest, etag: string): Observable<LoadedTemplate> {
    return this.http
      .put<ViewTemplateDto>(`${ROOT}/view-templates/${id}`, body, {
        observe: 'response',
        headers: new HttpHeaders({ 'If-Match': etag }),
      })
      .pipe(map(this.loaded));
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${ROOT}/view-templates/${id}`);
  }

  publish(id: number): Observable<ViewTemplateDto> {
    return this.http.post<ViewTemplateDto>(`${ROOT}/view-templates/${id}/publish`, null);
  }

  unpublish(id: number): Observable<ViewTemplateDto> {
    return this.http.post<ViewTemplateDto>(`${ROOT}/view-templates/${id}/unpublish`, null);
  }

  preview(id: number, scope: AssignmentScopeDto): Observable<AssignmentPreviewDto> {
    return this.http.post<AssignmentPreviewDto>(
      `${ROOT}/view-templates/${id}/assignments/preview`,
      scope,
    );
  }

  assignments(id: number): Observable<AssignmentListDto> {
    return this.http.get<AssignmentListDto>(`${ROOT}/view-templates/${id}/assignments`);
  }

  push(
    id: number,
    scope: AssignmentScopeDto,
    confirmedWidgetCount: number,
  ): Observable<AssignmentDto> {
    return this.http.post<AssignmentDto>(`${ROOT}/view-templates/${id}/assignments`, {
      scope,
      confirmedWidgetCount,
    });
  }

  revoke(assignmentId: number): Observable<AssignmentDto> {
    return this.http.delete<AssignmentDto>(`${ROOT}/template-assignments/${assignmentId}`);
  }

  private loaded = (response: {
    body: ViewTemplateDto | null;
    headers: { get(name: string): string | null };
  }): LoadedTemplate => ({
    template: response.body!,
    etag: response.headers.get('ETag') ?? `"${response.body!.version}"`,
  });
}
