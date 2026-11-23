import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { describe, expect, it } from 'vitest';
import { FilterBarComponent } from './filter-bar.component';

@Component({
  imports: [FilterBarComponent],
  template: `<app-filter-bar [divisionUnavailable]="unavailable()" [divisionIgnoredBy]="ignored()" />`,
})
class HostComponent {
  readonly unavailable = signal(false);
  readonly ignored = signal<readonly string[]>([]);
}

describe('FilterBarComponent Reporting Division note', () => {
  function create() {
    TestBed.configureTestingModule({ providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('says nothing when every metric honors the Division', () => {
    const { root } = create();
    expect(root.querySelector('#division-reason')).toBeNull();
  });

  it('names the metrics that ignore the Division, from the catalog', () => {
    const { fixture, root } = create();
    fixture.componentInstance.ignored.set(['Net cash collected', 'Clean claim rate']);
    fixture.detectChanges();

    const note = root.querySelector('#division-reason')!.textContent!;
    expect(note).toContain('Net cash collected, Clean claim rate');
    expect(note).toContain('They show');
    expect([...root.querySelectorAll('ems-select')].at(-1)!.getAttribute('aria-describedby')).toBe('division-reason');
  });

  it('uses the singular for one metric', () => {
    const { fixture, root } = create();
    fixture.componentInstance.ignored.set(['Clean claim rate']);
    fixture.detectChanges();
    expect(root.querySelector('#division-reason')!.textContent).toContain('It shows');
  });

  it('turns the control off with the reason when no metric can use it', () => {
    const { fixture, root } = create();
    fixture.componentInstance.unavailable.set(true);
    fixture.detectChanges();
    expect(root.querySelector('#division-reason')!.textContent).toContain("can't be split by");
  });

  it('names the metrics that cannot be split when the control is off', () => {
    const { fixture, root } = create();
    fixture.componentInstance.unavailable.set(true);
    fixture.componentInstance.ignored.set(['Clean claim rate']);
    fixture.detectChanges();
    expect(root.querySelector('#division-reason')!.textContent).toContain("Clean claim rate can't be split by Base location");
  });
});
