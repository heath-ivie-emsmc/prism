import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import {
  EmsButtonDirective,
  EmsCardComponent,
  EmsLogoComponent,
  EmsMessageComponent,
} from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import {
  LOGIN_QUERY_ERROR,
  LOGIN_QUERY_REASON,
  LOGIN_QUERY_RETURN_URL,
  LOGIN_REASON_AUTH,
  normalizeReturnUrl,
} from '../auth/return-url.util';

/** Messages for the short error codes the API puts on `/login`. Unknown codes get the generic message. */
const ERROR_MESSAGES: Readonly<Record<string, string>> = {
  access_denied: 'Your account does not have access to Prism. Contact your administrator.',
  sso_expired: 'The sign-in took too long. Please try again.',
};

const GENERIC_ERROR = 'We could not sign you in. Please try again.';

@Component({
  selector: 'app-login',
  imports: [EmsButtonDirective, EmsCardComponent, EmsLogoComponent, EmsMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="login">
      <ems-card class="login__card">
        <ems-logo variant="emsmc" />
        <h1 class="login__title">Sign in to Prism</h1>
        <p class="login__copy">Use your EMSolutions Account Portal sign-in to continue.</p>

        @if (notice(); as notice) {
          <ems-message [state]="notice.state" role="status">{{ notice.text }}</ems-message>
        }

        <button emsButton type="button" variant="filled" color="primary" block (click)="signIn()">
          Continue to sign in
        </button>
      </ems-card>
    </main>
  `,
  styles: `
    .login {
      display: grid;
      place-items: center;
      min-height: 100dvh;
      padding: 1.5rem;
    }
    .login__card {
      width: min(26rem, 100%);
    }
    .login__title {
      margin: 1rem 0 0.25rem;
    }
    .login__copy {
      margin: 0 0 1rem;
    }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap.pipe(map((params) => ({
    reason: params.get(LOGIN_QUERY_REASON),
    error: params.get(LOGIN_QUERY_ERROR),
    returnUrl: normalizeReturnUrl(params.get(LOGIN_QUERY_RETURN_URL)),
  }))), { requireSync: true });

  protected readonly notice = computed<{ state: 'error' | 'neutral'; text: string } | null>(() => {
    const { error, reason } = this.query();
    if (error) return { state: 'error', text: ERROR_MESSAGES[error] ?? GENERIC_ERROR };
    if (reason === LOGIN_REASON_AUTH) return { state: 'neutral', text: 'Sign in to continue.' };
    return null;
  });

  protected signIn(): void {
    this.auth.beginSsoLogin({ returnUrl: this.query().returnUrl });
  }
}
