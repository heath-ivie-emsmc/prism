import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { inject } from '@angular/core';
import { of } from 'rxjs';
import { EmsIconComponent } from '@ems-management-consultants/ui';
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
  imports: [EmsIconComponent, LoadProblemComponent, SkeletonComponent],
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
        @if (cards().length > 0) {
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
    h2 {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin: 0;
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
      font-weight: var(--ems-font-weight-heavy);
    }
    .insight__skeleton {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .insight__headline {
      margin: 0;
      overflow-wrap: anywhere;
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
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
      gap: 0.75rem 1rem;
      align-items: start;
      padding: 1rem;
      border-radius: var(--ems-corner-md);
      background: var(--ems-surface-neutral-subtle);
      box-shadow: inset 0 0 0 var(--ems-border-width-default) var(--ems-border-neutral-default);
    }
    .card--structural,
    .card--watch {
      box-shadow: inset 0 0 0 var(--ems-border-width-default) var(--ems-border-warning-strong);
    }
    .card__icon {
      color: var(--ems-text-muted);
    }
    .card__text {
      min-width: 0;
    }
    .card__title,
    .card__body {
      margin: 0;
      line-height: var(--ems-line-height-body);
      overflow-wrap: anywhere;
    }
    .card__title {
      font-weight: var(--ems-font-weight-bold);
    }
    .card__body {
      margin-top: 0.25rem;
      color: var(--ems-text-muted);
    }
    .card__side {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 0.25rem;
      text-align: right;
    }
    .card__label {
      padding: 0.125rem 0.625rem;
      border-radius: 999px;
      background: var(--ems-surface-neutral-default);
      color: var(--ems-text-default);
      box-shadow: inset 0 0 0 var(--ems-border-width-default) var(--ems-border-neutral-strong);
      font-size: var(--ems-font-size-small);
      font-weight: var(--ems-font-weight-bold);
      white-space: nowrap;
    }
    .card--structural .card__label,
    .card--watch .card__label {
      background: var(--ems-surface-warning-subtle);
      color: var(--ems-text-warning-default);
    }
    .card__amount {
      font-variant-numeric: tabular-nums;
      font-weight: var(--ems-font-weight-bold);
    }
    .card__note {
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
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
    .insight__basis {
      font-size: var(--ems-font-size-small);
    }
  `,
})
export class WhatChangedComponent {
  private readonly insights = inject(InsightsService);

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

  protected readonly dto = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly groupLower = computed(() => (this.dto()?.groupName ?? this.groupName()).toLowerCase());
  protected readonly cards = computed(() => toCardViews(this.dto()));
  protected readonly basis = computed(() => {
    const data = this.dto();
    return data ? basisLine(data) : '';
  });
}
