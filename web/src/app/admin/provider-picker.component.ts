import { HttpClient, HttpParams } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  EmsPickerOption,
  EmsPickerSearch,
  EmsRecordPickerComponent,
} from '@ems-management-consultants/ui';
import { catchError, map } from 'rxjs';

/** One Provider an Internal User chose: its number and name. */
export interface ProviderChoice {
  readonly id: string;
  readonly name: string;
}

interface ProviderSearchDto {
  readonly providers: readonly ProviderChoice[];
}

const MAX_QUERY = 100;

/**
 * Finds a Provider by name or number, so staff do not have to know ids. The list comes from the admin API, which only Internal Users may call.
 * It emits the chosen Provider, or null when the choice is cleared. Choosing only fills a field: the route that uses the number still checks it.
 */
@Component({
  selector: 'app-provider-picker',
  imports: [EmsRecordPickerComponent, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ems-record-picker
      [label]="label()"
      [hint]="hint()"
      [search]="search"
      [multiple]="false"
      [minimumSearchLength]="2"
      [ngModel]="selected()"
      (ngModelChange)="changed($event)"
    />
  `,
})
export class ProviderPickerComponent {
  private readonly http = inject(HttpClient);

  readonly label = input('Provider');
  readonly hint = input('Type part of the name or the number.');
  readonly chosen = output<ProviderChoice | null>();

  protected readonly selected = signal<EmsPickerOption[]>([]);

  /** The search the picker runs for the text typed. Public so it can be tested without driving the overlay. */
  readonly search: EmsPickerSearch = (text) =>
    this.http
      .get<ProviderSearchDto>('/api/v1/admin/providers', {
        params: new HttpParams().set('q', text.trim().slice(0, MAX_QUERY)),
      })
      .pipe(
        map((response) =>
          response.providers.map((p): EmsPickerOption => ({
            value: p.id,
            label: p.name,
            description: `Provider ${p.id}`,
          })),
        ),
        catchError(() => {
          throw new Error('The Provider list could not be loaded.');
        }),
      );

  protected changed(options: EmsPickerOption[] | null): void {
    this.selected.set(options ?? []);
    const first = options?.[0];
    this.chosen.emit(first ? { id: first.value, name: first.label } : null);
  }
}
