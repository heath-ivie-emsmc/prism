import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { HomePageDto, OnboardingAnswerDto, OnboardingDto } from './personas.model';

/** The signed-in user's home page in a Provider. The user is the session's and the Provider is the route's: neither is sent in a body. */
@Injectable({ providedIn: 'root' })
export class HomeService {
  private readonly http = inject(HttpClient);

  get(providerId: string): Observable<HomePageDto> {
    return this.http.get<HomePageDto>(`/api/v1/providers/${encodeURIComponent(providerId)}/home`);
  }

  /** Copies the Persona's page into the user's own Saved Views. Returns the copy to open. */
  customize(providerId: string): Observable<{ readonly savedViewId: number }> {
    return this.http.post<{ readonly savedViewId: number }>(
      `/api/v1/providers/${encodeURIComponent(providerId)}/home/customize`,
      null,
    );
  }

  /** Deletes the user's copy, so the home page follows the Persona's page again. */
  reset(providerId: string): Observable<void> {
    return this.http.delete<void>(
      `/api/v1/providers/${encodeURIComponent(providerId)}/home/customize`,
    );
  }
}

/** The first-login questions. The answers are scored on the server and not kept. */
@Injectable({ providedIn: 'root' })
export class OnboardingService {
  private readonly http = inject(HttpClient);

  questions(): Observable<OnboardingDto> {
    return this.http.get<OnboardingDto>('/api/v1/me/onboarding');
  }

  submit(answers: readonly OnboardingAnswerDto[]): Observable<void> {
    return this.http.post<void>('/api/v1/me/onboarding', { answers });
  }
}
