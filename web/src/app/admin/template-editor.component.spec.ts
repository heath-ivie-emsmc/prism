import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../axe';
import { DashboardFilterContext } from '../dashboard/dashboard-filter-context';
import { bootstrap } from '../saved-views/saved-views.testing';
import { ProviderPickerComponent } from './provider-picker.component';
import { TemplateEditorComponent } from './template-editor.component';

function create() {
  bootstrap({
    me: { isInternal: true, adminPermissions: ['prism.admin.templates.author'], providers: [] },
  });
  const fixture: ComponentFixture<TemplateEditorComponent> =
    TestBed.createComponent(TemplateEditorComponent);
  fixture.detectChanges();
  return {
    fixture,
    root: fixture.nativeElement as HTMLElement,
    filters: TestBed.inject(DashboardFilterContext),
  };
}

function pick(
  fixture: ComponentFixture<TemplateEditorComponent>,
  choice: { id: string; name: string } | null,
): void {
  fixture.debugElement
    .query(By.directive(ProviderPickerComponent))
    .componentInstance.chosen.emit(choice);
  fixture.detectChanges();
}

describe('TemplateEditorComponent', () => {
  it('asks for a Provider before it shows anything, and does not set one yet', () => {
    const { root, filters } = create();

    expect(root.querySelector('h1')?.textContent).toBe('Choose a Provider to preview');
    expect(filters.previewProviderId()).toBeNull();
  });

  it('previews the Provider that was picked and says which one in the banner', () => {
    const { fixture, root, filters } = create();

    pick(fixture, { id: '526', name: 'Seminole County Fire Department' });

    expect(filters.previewProviderId()).toBe('526');
    expect(root.querySelector('[role="status"]')?.textContent).toContain(
      'Seminole County Fire Department (Provider 526)',
    );
  });

  it('keeps the chooser open when the choice is cleared', () => {
    const { fixture, filters } = create();

    pick(fixture, null);

    expect(filters.previewProviderId()).toBeNull();
  });

  it('puts the dashboard back on the users own Provider when the editor goes away', () => {
    const { fixture, filters } = create();
    pick(fixture, { id: '526', name: 'Seminole County Fire Department' });
    expect(filters.previewProviderId()).toBe('526');

    fixture.destroy();

    expect(filters.previewProviderId()).toBeNull();
  });

  it('has no axe violations on the chooser', async () => {
    const { root } = create();

    expect(await axeViolations(root)).toEqual([]);
  });

  it('answers the leave guard: nothing to lose on the chooser', () => {
    const { fixture } = create();

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });
});
