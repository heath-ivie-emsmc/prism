import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../axe';
import { AuthService } from '../auth/auth.service';
import { DataFreshnessService } from './data-freshness.service';
import { DataUpdatedNoticeComponent } from './data-updated-notice.component';

describe('DataUpdatedNoticeComponent', () => {
  function setup() {
    const announce = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: LiveAnnouncer, useValue: { announce } }],
    });
    TestBed.inject(AuthService).refreshMe().subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({ userName: 'jdoe', displayName: 'Jane', providers: [{ id: '526', name: 'P', facilityCount: 0 }], permissions: [] });
    const freshness = TestBed.inject(DataFreshnessService);
    const fixture = TestBed.createComponent(DataUpdatedNoticeComponent);
    fixture.detectChanges();
    return { freshness, fixture, root: fixture.nativeElement as HTMLElement, announce };
  }

  /** Makes the service believe the warehouse has a newer date than the one on screen. */
  function makeNewer(freshness: DataFreshnessService): void {
    freshness.recordShown('526', '2026-11-22');
    freshness['recordLatest']('526', '2026-11-23');
  }

  it('keeps an empty polite live region in the page while there is nothing to say', () => {
    const { root } = setup();
    const region = root.querySelector('[role="status"]')!;
    expect(region.getAttribute('aria-live')).toBe('polite');
    expect(region.textContent?.trim()).toBe('');
  });

  it('says the data is newer, offers a button, and never moves focus', async () => {
    const { freshness, fixture, root } = setup();
    const before = document.activeElement;
    makeNewer(freshness);
    fixture.detectChanges();

    expect(root.querySelector('[role="status"]')!.textContent).toContain('Data updated through');
    expect(root.querySelector('button')!.textContent).toContain('Update view');
    expect(document.activeElement).toBe(before);
    expect(await axeViolations(root)).toEqual([]);
  });

  it('applies the update, announces the new date and removes the notice when the button is used', () => {
    const { freshness, fixture, root, announce } = setup();
    makeNewer(freshness);
    fixture.detectChanges();

    root.querySelector('button')!.click();
    fixture.detectChanges();

    expect(freshness.revision()).toBe(1);
    expect(announce).toHaveBeenCalledWith(expect.stringMatching(/^Updated to .*23/));
    expect(root.querySelector('button')).toBeNull();
  });
});
