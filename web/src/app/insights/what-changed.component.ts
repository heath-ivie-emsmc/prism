import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { rxResource } from '@angular/core/rxjs-interop';
import { inject } from '@angular/core';
import { of } from 'rxjs';
import { EmsButtonDirective, EmsIconComponent } from '@ems-management-consultants/ui';
import { AuthService } from '../auth/auth.service';
import { AttentionCardView, attentionCard, reviewQuery } from '../attention/attention';
import { AttentionCountsDto, AttentionQuery, AttentionService, CLAIMS_PERMISSION } from '../attention/attention.service';
import { LoadProblemComponent } from '../shared/load-problem.component';
import { SkeletonComponent } from '../shared/skeleton.component';
import { InsightsService, WhatChangedDto } from './insights.service';
import { basisLine, toCardViews } from './what-changed';

interface Query {
  readonly providerId: string;
  readonly period: string;
  readonly line: 'air' | 'ground';
  readonly group: string;
}

/**
 * The written explanation of what changed in one group's net cash against the month before: a headline, a card for each thing that explains part of it,
 * and what is left over. Every sentence comes from the API's findings, so the words and the figures cannot disagree. It reads nothing but aggregates.
 * Expected effects, such as what a payer mix shift will do as claims are paid, are listed after the cards that explain this month and say so in their words.
 */
@Component({
  selector: 'app-what-changed',
  imports: [EmsButtonDirective, EmsIconComponent, LoadProblemComponent, RouterLink, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="app-panel insight" aria-labelledby="insight-title">
      <header class="insight__header">
        <h2 id="insight-title"><ems-icon name="auto_awesome" />What changed in {{ groupLower() }}</h2>
      </header>

      @if (resource.error() !== undefined) {
        <app-load-problem [error]="resource.error()" subject="what changed" (retry)="resource.reload()" />
      } @else if (resource.isLoading()) {
        <p class="cdk-visually-hidden" role="status">Loading what changed</p>
        <div class="insight__skeleton" aria-hidden="true">
          <app-skeleton width="80%" height="1.25rem" />
          <app-skeleton height="5rem" />
          <app-skeleton height="5rem" />
        </div>
      } @else if (dto(); as data) {
        <p class="insight__headline">{{ data.headline }}</p>
        @if (cards().length > 0 || attentionView()) {
          <ul class="insight__cards" aria-label="What explains the change">
            @for (card of cards(); track card.key) {
              <li class="card" [class]="'card card--' + card.label">
                <ems-icon class="card__icon" [name]="card.icon" aria-hidden="true" />
                <div class="card__text">
                  <p class="card__title">{{ card.title }}</p>
                  <p class="card__body">{{ card.body }}</p>
                </div>
                <div class="card__side">
                  <span class="card__label">{{ card.labelText }}</span>
                  <span class="card__amount"><span class="cdk-visually-hidden">{{ card.amountSpoken }}. </span><span aria-hidden="true">{{ card.amount }}</span></span>
                  <span class="card__note" aria-hidden="true">{{ card.amountNote }}</span>
                </div>
              </li>
            }
            @if (attentionView(); as attention) {
              <li class="card card--structural card--attention">
                <ems-icon class="card__icon" name="error_outline" aria-hidden="true" />
                <div class="card__text">
                  <p class="card__title">{{ attention.title }}</p>
                  <p class="card__body">{{ attention.body }}</p>
                  <a emsButton="flat" class="card__action" [routerLink]="reviewPath" [queryParams]="reviewParams()"><ems-icon name="receipt_long" aria-hidden="true" />{{ attention.button }}</a>
                </div>
                <div class="card__side">
                  <span class="card__label">Needs action</span>
                  <span class="card__amount"><span class="cdk-visually-hidden">{{ attention.amountSpoken }}. </span><span aria-hidden="true">{{ attention.amount }}</span></span>
                  <span class="card__note" aria-hidden="true">{{ attention.amountNote }}</span>
                </div>
              </li>
            }
          </ul>
        }
        <p class="insight__remainder">{{ data.remainder.text }}</p>
        <p class="insight__basis">{{ basis() }}</p>
      } @else {
        <p class="insight__empty">There is no net cash for {{ groupLower() }} in this month or the month before it to explain.</p>
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    /* The AI surface: the panel's own ring is replaced by a soft blue-to-purple edge, as in the mockup. Forced colors falls back to a plain border. */
    .insight {
      position: relative;
      box-shadow: var(--ems-fx-neutral-default-frosted-raised-glowing);
    }
    .insight::before {
      content: '';
      position: absolute;
      inset: 0;
      padding: 1px;
      border-radius: inherit;
      background: linear-gradient(135deg, var(--ems-border-ai-blue), var(--ems-border-ai-purple));
      -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
      -webkit-mask-composite: xor;
      mask-composite: exclude;
      pointer-events: none;
    }
    @media (forced-colors: active) {
      .insight::before {
        background: CanvasText;
      }
    }
    h2 {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin: 0;
      font-size: 1rem;
      line-height: 1.5;
      font-weight: var(--ems-font-weight-heavy);
    }
    h2 ems-icon {
      color: var(--ems-surface-accent-plum);
    }
    .insight__skeleton {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .insight__headline {
      margin: 0;
      overflow-wrap: anywhere;
      font-size: 0.9375rem;
      line-height: 1.4;
      font-weight: var(--ems-font-weight-bold);
    }
    .insight__cards {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .card {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) auto;
      gap: 0 0.75rem;
      align-items: start;
      padding: 0.875rem 0.75rem 0.875rem 0.875rem;
      font-size: 0.8125rem;
      border: 1px solid var(--ems-border-neutral-subtle);
      border-radius: var(--ems-corner-md);
      background: var(--ems-surface-neutral-default);
    }
    /* Only a finding that needs action gets a coloured edge, a soft red one. Watch and temporary stay neutral. */
    .card--structural {
      border-color: var(--ems-border-error-subtle);
    }
    .card__icon {
      color: var(--ems-text-muted);
      font-size: 1.25rem;
      width: 1.25rem;
      height: 1.25rem;
    }
    .card--structural .card__icon {
      color: var(--ems-text-error-default);
    }
    .card__text {
      min-width: 0;
    }
    .card__title,
    .card__body {
      margin: 0;
      line-height: 1.45;
      overflow-wrap: anywhere;
    }
    .card__title {
      font-weight: var(--ems-font-weight-bold);
    }
    .card__body {
      margin-top: 0.25rem;
      color: var(--ems-text-default);
    }
    .card__side {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 0.25rem;
      line-height: 1.3;
      text-align: right;
    }
    .card__label {
      padding: 0.3125rem 0.875rem;
      border-radius: 0.375rem;
      background: color-mix(in srgb, var(--ems-text-default) 8%, transparent);
      color: var(--ems-text-default);
      font-size: 0.8125rem;
      font-weight: 500;
      line-height: 1.2;
      white-space: nowrap;
    }
    .card--watch .card__label {
      background: var(--ems-surface-warning-subtle);
      color: var(--ems-text-warning-default);
    }
    .card--structural .card__label {
      background: var(--ems-surface-error-subtle);
      color: var(--ems-text-error-default);
    }
    @media (forced-colors: active) {
      .card,
      .card__label {
        border: 1px solid CanvasText;
      }
    }
    .card__action {
      margin-top: 0.5rem;
      max-width: 100%;
      height: auto;
      min-height: 2rem;
      white-space: normal;
      text-align: start;
      overflow-wrap: anywhere;
    }
    .card__amount {
      color: var(--ems-text-muted);
      font-variant-numeric: tabular-nums;
    }
    .card__note {
      color: var(--ems-text-muted);
      font-size: 0.75rem;
    }
    @media (max-width: 599.98px) {
      .card {
        grid-template-columns: auto minmax(0, 1fr);
      }
      .card__side {
        grid-column: 1 / -1;
        flex-direction: row;
        flex-wrap: wrap;
        align-items: center;
        justify-content: flex-start;
        text-align: left;
      }
    }
    .insight__remainder,
    .insight__basis,
    .insight__empty {
      margin: 0;
      color: var(--ems-text-muted);
      line-height: var(--ems-line-height-body);
      overflow-wrap: anywhere;
    }
    .insight__remainder,
    .insight__basis {
      font-size: 0.75rem;
    }
  `,
})
export class WhatChangedComponent {
  private readonly insights = inject(InsightsService);
  private readonly auth = inject(AuthService);
  private readonly attention = inject(AttentionService);

  readonly providerId = input.required<string | null>();
  readonly period = input.required<string>();
  readonly line = input.required<'air' | 'ground'>();
  readonly group = input.required<string>();
  /** The group's name for the heading while the insight loads, and when there is none. */
  readonly groupName = input<string>('');

  protected readonly resource = rxResource<WhatChangedDto | null, Query | null>({
    params: () => {
      const providerId = this.providerId();
      const period = this.period();
      const group = this.group();
      return providerId && /^\d{6}$/.test(period) && group ? { providerId, period, line: this.line(), group } : null;
    },
    stream: ({ params }) => (params ? this.insights.whatChanged(params.providerId, params.period, params.line, params.group) : of(null)),
  });

  /** The denied claims count, asked for only when the user may see claim data. A count is an aggregate, but it sits behind the same permission. */
  private readonly attentionResource = rxResource<AttentionCountsDto | null, AttentionQuery | null>({
    params: () => {
      const providerId = this.providerId();
      const period = this.period();
      const allowed = this.auth.me()?.permissions.includes(CLAIMS_PERMISSION) ?? false;
      return allowed && providerId && /^\d{6}$/.test(period) ? { providerId, period, line: this.line(), group: this.group() || null } : null;
    },
    stream: ({ params }) => (params ? this.attention.counts(params) : of(null)),
  });

  protected readonly attentionView = computed<AttentionCardView | null>(() => {
    // A failed count simply shows no card: the card is a pointer, and the explanation above it is complete without it.
    const counts = this.attentionResource.error() === undefined && this.attentionResource.hasValue() ? this.attentionResource.value() : null;
    return counts ? attentionCard(counts.counts.find((c) => c.key === 'denied')?.count ?? 0, counts.deniedBalanceDue) : null;
  });
  protected readonly reviewPath = '/dashboard/needs-attention';
  protected readonly reviewParams = computed(() => reviewQuery('denied', this.period(), this.line(), this.group() || null));

  protected readonly dto = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly groupLower = computed(() => (this.dto()?.groupName ?? this.groupName()).toLowerCase());
  protected readonly cards = computed(() => toCardViews(this.dto()));
  protected readonly basis = computed(() => {
    const data = this.dto();
    return data ? basisLine(data) : '';
  });
}
