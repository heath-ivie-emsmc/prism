import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Observable, of } from 'rxjs';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { SavedViewsService } from '../saved-views/saved-views.service';
import { VIEW_STORE } from '../saved-views/view-store';
import { ViewBuilderComponent } from '../saved-views/view-builder.component';
import { AdminTemplatesService } from './admin-templates.service';
import { ProviderChoice, ProviderPickerComponent } from './provider-picker.component';
import { templateViewStore } from './template-view-store';

/**
 * Lays out a View Template with the Saved View builder. A template has no Provider, so the author picks one by name or number to see real figures while they arrange
 * widgets. The server only allows that for the customer data viewer tier, only on aggregate figures, and audits it. The Provider is never saved in the template.
 */
@Component({
  selector: 'app-template-editor',
  imports: [ProviderPickerComponent, ViewBuilderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: VIEW_STORE,
      useFactory: () => templateViewStore(inject(AdminTemplatesService), inject(SavedViewsService)),
    },
  ],
  template: `
    @if (filters.previewProviderId() === null) {
      <main class="chooser" id="main-content">
        <h1 tabindex="-1">Choose a Provider to preview</h1>
        <p class="hint">
          Pick a Provider to see its figures while you arrange widgets. Your access is recorded. The
          Provider is not saved in the template.
        </p>
        <app-provider-picker label="Provider" (chosen)="choose($event)" />
      </main>
    } @else {
      <div class="banner">
        <span role="status"
          >Previewing {{ previewName() }}. Figures are for layout only and your access is
          recorded.</span
        >
        <app-provider-picker
          label="Change Provider"
          hint="Type part of the name or the number."
          (chosen)="choose($event)"
        />
      </div>
      <app-view-builder />
    }
  `,
  styles: `
    .chooser {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      padding: 1.5rem;
      max-width: 32rem;
    }
    h1 {
      margin: 0;
      font-size: var(--ems-font-size-title);
      line-height: var(--ems-line-height-title);
      font-weight: var(--ems-font-weight-heavy);
    }
    .hint {
      margin: 0.25rem 0;
      font-size: var(--ems-font-size-small);
      color: var(--ems-text-muted);
    }
    .banner {
      margin: 0;
      padding: 0.5rem 1.5rem;
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
      background: var(--ems-surface-neutral-elevated);
      overflow-wrap: anywhere;
    }
  `,
})
export class TemplateEditorComponent {
  protected readonly filters = inject(DashboardFilterContext);

  private readonly builder = viewChild(ViewBuilderComponent);

  /** The name of the Provider being previewed, for the banner. Falls back to the number if the name is not known. */
  private readonly previewLabel = signal<string | null>(null);
  protected readonly previewName = computed(
    () => this.previewLabel() ?? `Provider ${this.filters.previewProviderId()}`,
  );

  constructor() {
    // The preview Provider belongs to this page only. Leaving it puts the dashboard back on the user's own Provider.
    inject(DestroyRef).onDestroy(() => this.filters.previewProviderId.set(null));
  }

  /** Asked by the route's leave guard, which expects a builder: the unsaved work is the builder's. */
  hasUnsavedChanges(): boolean {
    return this.builder()?.hasUnsavedChanges() ?? false;
  }

  confirmLeave(): Observable<boolean> {
    return this.builder()?.confirmLeave() ?? of(true);
  }

  protected choose(provider: ProviderChoice | null): void {
    if (provider === null) {
      return;
    }

    this.previewLabel.set(`${provider.name} (Provider ${provider.id})`);
    this.filters.previewProviderId.set(provider.id);
  }
}
