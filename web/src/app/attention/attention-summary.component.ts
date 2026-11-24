import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CATEGORY_OVERLAP_NOTE, FILING_ESTIMATE_NOTE, SummaryView, toSummary } from './attention';
import { AttentionCountsDto } from './attention.service';

/**
 * The summary above the claims list: the denial rate, the top denial reasons, the claim categories and the filing bands for the month.
 * Aggregates only. Denial reasons are warehouse text and are bound as text, never as HTML. Nothing here is stored by the browser.
 * The categories overlap (a claim can be denied and on hold), so they are shown as separate counts and never added up. The filing bands are estimates.
 */
@Component({
  selector: 'app-attention-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (summary(); as s) {
      <section class="app-panel summary" aria-labelledby="summary-title">
        <h2 id="summary-title">Summary, {{ periodLabel() }}</h2>

        <div class="summary__grid">
          <div class="summary__block summary__rate">
            <h3>Denial rate</h3>
            @if (s.deniedRate; as rate) {
              <p class="summary__figure">{{ rate }}</p>
              <p class="summary__note">Denied claims as a share of all claims in the period.</p>
            } @else {
              <p class="summary__figure">No claims</p>
              <p class="summary__note">There are no claims in this period, so there is no rate.</p>
            }
          </div>

          <div class="summary__block">
            <h3 id="reasons-title">Top denial reasons</h3>
            @if (s.reasons.length) {
              <table class="summary__table" aria-labelledby="reasons-title">
                <thead>
                  <tr>
                    <th scope="col">Reason</th>
                    <th scope="col" class="num">Claims</th>
                    <th scope="col" class="num">Balance due</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of s.reasons; track row.key) {
                    <tr>
                      <th scope="row">{{ row.name }}</th>
                      <td class="num">{{ row.count }}</td>
                      <td class="num">{{ row.balance }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            } @else {
              <p class="summary__note">No denied claims in this period.</p>
            }
          </div>

          <div class="summary__block">
            <h3 id="categories-title">Claim categories</h3>
            <table class="summary__table" aria-labelledby="categories-title">
              <thead>
                <tr>
                  <th scope="col">Category</th>
                  <th scope="col" class="num">Claims</th>
                  <th scope="col" class="num">Balance due</th>
                </tr>
              </thead>
              <tbody>
                @for (row of s.categories; track row.key) {
                  <tr>
                    <th scope="row">{{ row.name }}</th>
                    <td class="num">{{ row.count }}</td>
                    <td class="num">{{ row.balance }}</td>
                  </tr>
                }
              </tbody>
            </table>
            <p class="summary__note">{{ overlapNote }}</p>
          </div>

          <div class="summary__block">
            <h3 id="bands-title">Filing deadlines (estimated)</h3>
            <table class="summary__table" aria-labelledby="bands-title">
              <thead>
                <tr>
                  <th scope="col">Time left to file</th>
                  <th scope="col" class="num">Claims</th>
                  <th scope="col" class="num">Balance due</th>
                </tr>
              </thead>
              <tbody>
                @for (row of s.filingBands; track row.key) {
                  <tr>
                    <th scope="row">{{ row.name }}</th>
                    <td class="num">{{ row.count }}</td>
                    <td class="num">{{ row.balance }}</td>
                  </tr>
                }
              </tbody>
            </table>
            <p class="summary__note">{{ estimateNote }} Counts are of claims that are denied, on hold or in collections.</p>
          </div>
        </div>
      </section>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .summary {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    h2 {
      margin: 0;
      font-size: var(--ems-font-size-heading);
      line-height: var(--ems-line-height-heading);
      font-weight: var(--ems-font-weight-heavy);
      overflow-wrap: anywhere;
    }
    h3 {
      margin: 0 0 0.5rem;
      font-size: var(--ems-font-size-body);
      line-height: var(--ems-line-height-body);
      font-weight: var(--ems-font-weight-bold);
    }
    .summary__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 19rem), 1fr));
      gap: 1.25rem 1.5rem;
      align-items: start;
    }
    .summary__block {
      min-width: 0;
    }
    .summary__figure {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .summary__note {
      margin: 0.5rem 0 0;
      color: var(--ems-text-muted);
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      overflow-wrap: anywhere;
    }
    .summary__rate .summary__note {
      margin-top: 0.25rem;
    }
    .summary__table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--ems-font-size-small);
      line-height: var(--ems-line-height-small);
      font-variant-numeric: tabular-nums;
    }
    .summary__table th,
    .summary__table td {
      padding: 0.375rem 0.5rem 0.375rem 0;
      text-align: left;
      vertical-align: top;
      border-bottom: 1px solid var(--ems-border-neutral-subtle);
    }
    .summary__table thead th {
      font-weight: var(--ems-font-weight-bold);
    }
    /* The figure columns keep their heading on one line, so the text column gets the rest and its words wrap at spaces, not inside a word. */
    .summary__table thead th.num {
      white-space: nowrap;
    }
    .summary__table tbody th {
      font-weight: var(--ems-font-weight-regular, normal);
      overflow-wrap: anywhere;
    }
    .summary__table .num {
      text-align: right;
      padding-inline-end: 0;
      padding-inline-start: 0.5rem;
    }
    .summary__table td.num {
      white-space: nowrap;
    }
  `,
})
export class AttentionSummaryComponent {
  readonly counts = input<AttentionCountsDto | null>(null);
  readonly periodLabel = input('');

  protected readonly overlapNote = CATEGORY_OVERLAP_NOTE;
  protected readonly estimateNote = FILING_ESTIMATE_NOTE;
  protected readonly summary = computed<SummaryView | null>(() => toSummary(this.counts()));
}
