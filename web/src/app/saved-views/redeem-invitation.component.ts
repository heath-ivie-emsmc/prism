import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { EmsButtonDirective } from '@ems-management-consultants/ui';
import { InvitationTokenShape } from './invitation-token';
import { SavedViewsService } from './saved-views.service';

/**
 * Where an emailed link lands. The token is read once and taken out of the address bar at once, so it is not left in history or a screenshot, then used.
 * Every failure reads the same, because the server answers every failure the same.
 */
@Component({
  selector: 'app-redeem-invitation',
  imports: [EmsButtonDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="redeem" id="main-content">
      @if (failed()) {
        <h1 tabindex="-1">This invitation is no longer valid</h1>
        <p>Ask the sender for a new one.</p>
        <button emsButton="filled" type="button" (click)="toViews()">Go to saved views</button>
      } @else {
        <h1 tabindex="-1">Opening the shared view</h1>
        <p role="status">One moment…</p>
      }
    </main>
  `,
  styles: `
    .redeem {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 1rem;
      padding: 2rem 1.5rem;
    }
    h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    p {
      margin: 0;
      color: var(--ems-text-muted);
    }
  `,
})
export class RedeemInvitationComponent {
  private readonly router = inject(Router);
  private readonly api = inject(SavedViewsService);
  protected readonly failed = signal(false);

  constructor() {
    const token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');
    // Out of the address bar before anything else happens.
    history.replaceState(history.state, '', location.pathname);
    if (!InvitationTokenShape.isWellFormed(token)) {
      this.failed.set(true);
      return;
    }
    this.api.redeem(token).subscribe({
      next: (result) => void this.router.navigate(['/saved-views', result.viewId], { replaceUrl: true }),
      error: () => this.failed.set(true),
    });
  }

  protected toViews(): void {
    void this.router.navigateByUrl('/saved-views');
  }
}
