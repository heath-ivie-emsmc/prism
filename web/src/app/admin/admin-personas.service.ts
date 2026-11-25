import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  PersonaDto,
  PersonaListDto,
  PersonaRequest,
  QuestionDto,
  QuestionListDto,
  QuestionRequest,
} from './admin.model';

const ROOT = '/api/v1/admin';

/** Personas and the first-login questions. Every call is checked on the server for the group and the tier. */
@Injectable({ providedIn: 'root' })
export class AdminPersonasService {
  private readonly http = inject(HttpClient);

  personas(): Observable<PersonaListDto> {
    return this.http.get<PersonaListDto>(`${ROOT}/personas`);
  }

  createPersona(body: PersonaRequest): Observable<PersonaDto> {
    return this.http.post<PersonaDto>(`${ROOT}/personas`, body);
  }

  updatePersona(id: number, body: PersonaRequest): Observable<PersonaDto> {
    return this.http.put<PersonaDto>(`${ROOT}/personas/${id}`, body);
  }

  publishPersona(id: number): Observable<PersonaDto> {
    return this.http.post<PersonaDto>(`${ROOT}/personas/${id}/publish`, null);
  }

  unpublishPersona(id: number): Observable<PersonaDto> {
    return this.http.post<PersonaDto>(`${ROOT}/personas/${id}/unpublish`, null);
  }

  deletePersona(id: number): Observable<void> {
    return this.http.delete<void>(`${ROOT}/personas/${id}`);
  }

  questions(): Observable<QuestionListDto> {
    return this.http.get<QuestionListDto>(`${ROOT}/onboarding-questions`);
  }

  createQuestion(body: QuestionRequest): Observable<QuestionDto> {
    return this.http.post<QuestionDto>(`${ROOT}/onboarding-questions`, body);
  }

  updateQuestion(id: number, body: QuestionRequest): Observable<QuestionDto> {
    return this.http.put<QuestionDto>(`${ROOT}/onboarding-questions/${id}`, body);
  }

  publishQuestion(id: number): Observable<QuestionDto> {
    return this.http.post<QuestionDto>(`${ROOT}/onboarding-questions/${id}/publish`, null);
  }

  unpublishQuestion(id: number): Observable<QuestionDto> {
    return this.http.post<QuestionDto>(`${ROOT}/onboarding-questions/${id}/unpublish`, null);
  }

  deleteQuestion(id: number): Observable<void> {
    return this.http.delete<void>(`${ROOT}/onboarding-questions/${id}`);
  }
}
