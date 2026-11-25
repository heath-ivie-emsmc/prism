import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideEmsUi } from '@ems-management-consultants/ui';
import { firstValueFrom } from 'rxjs';
import { axeViolations } from '../axe';
import { ProviderPickerComponent } from './provider-picker.component';

function create() {
  TestBed.configureTestingModule({
    providers: [provideEmsUi(), provideHttpClient(), provideHttpClientTesting()],
  });
  const fixture = TestBed.createComponent(ProviderPickerComponent);
  fixture.detectChanges();
  return fixture;
}

describe('ProviderPickerComponent', () => {
  it('searches the admin API with the typed text, cut to 100 characters, and shows the name with its number', async () => {
    const fixture = create();
    const result = firstValueFrom(fixture.componentInstance.search(` ${'a'.repeat(150)} `));
    const request = TestBed.inject(HttpTestingController).expectOne(
      (r) => r.url === '/api/v1/admin/providers',
    );
    request.flush({ providers: [{ id: '9001', name: 'Abbeville County Emergency Services' }] });

    expect(request.request.params.get('q')).toBe('a'.repeat(100));
    expect(await result).toEqual([
      { value: '9001', label: 'Abbeville County Emergency Services', description: 'Provider 9001' },
    ]);
  });

  it('has a labelled field and no axe violations', async () => {
    const fixture = create();

    expect(await axeViolations(fixture.nativeElement)).toEqual([]);
  });
});
