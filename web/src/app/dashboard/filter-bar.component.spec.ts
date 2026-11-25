import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { describe, expect, it } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { flushDivisions } from '../shared/division.testing';
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
  async function create() {
    TestBed.configureTestingModule({ providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).refreshMe().subscribe();
    http.expectOne('/api/v1/me').flush({ userName: 'j', displayName: 'J', providers: [{ id: '526', name: 'P', facilityCount: 0 }], permissions: [] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    TestBed.tick();
    for (const r of http.match((q) => q.url.endsWith('/periods'))) r.flush({ providerId: '526', periods: [] });
    flushDivisions(http);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('says nothing when every metric honors the Division', async () => {
    const { root } = await create();
    expect(root.querySelector('#division-reason')).toBeNull();
  });

  it('says nothing about metrics that ignore the Division', async () => {
    const { fixture, root } = await create();
    fixture.componentInstance.ignored.set(['Net cash collected', 'Clean claim rate']);
    fixture.detectChanges();

    expect(root.querySelector('#division-reason')).toBeNull();
  });

  it('turns the control off with the reason when no metric can use it', async () => {
    const { fixture, root } = await create();
    fixture.componentInstance.unavailable.set(true);
    fixture.detectChanges();
    expect(root.querySelector('#division-reason')!.textContent).toContain("can't be split by");
  });

  it('names the metrics that cannot be split when the control is off', async () => {
    const { fixture, root } = await create();
    fixture.componentInstance.unavailable.set(true);
    fixture.componentInstance.ignored.set(['Clean claim rate']);
    fixture.detectChanges();
    expect(root.querySelector('#division-reason')!.textContent).toContain("Clean claim rate can't be split by Base location");
  });
});
