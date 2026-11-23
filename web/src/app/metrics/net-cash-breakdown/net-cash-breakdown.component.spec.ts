import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { describe, expect, it } from 'vitest';
import { NetCashBreakdownComponent } from './net-cash-breakdown.component';
import { defaultLabels } from '../../dashboard/service-line';

describe('NetCashBreakdownComponent location heading', () => {
  it('takes the location heading from the Labels, not from typed text', () => {
    TestBed.configureTestingModule({ providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(NetCashBreakdownComponent);
    const component = fixture.componentInstance as unknown as { locationLabel: () => string };

    expect(component.locationLabel()).toBe(defaultLabels('air').reportingDivision);
  });
});
