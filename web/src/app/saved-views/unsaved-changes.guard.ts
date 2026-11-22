import { CanDeactivateFn } from '@angular/router';
import { of } from 'rxjs';
import type { ViewBuilderComponent } from './view-builder.component';

/** Asks before leaving a view with unsaved changes. A clean view, or any other page, goes straight through. */
export const unsavedChangesGuard: CanDeactivateFn<ViewBuilderComponent> = (component) => (component.hasUnsavedChanges() ? component.confirmLeave() : of(true));
