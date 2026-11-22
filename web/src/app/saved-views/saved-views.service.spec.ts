import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { EMPTY_LAYOUT } from './layout.model';
import { SavedViewsService } from './saved-views.service';

const BASE = '/api/v1/providers/100/saved-views';
const VIEW = { id: 5, name: 'Cash', description: null, ownerName: 'jdoe', isOwner: true, layout: EMPTY_LAYOUT, version: '77', updatedAt: '2026-11-22T00:00:00Z' };

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { service: TestBed.inject(SavedViewsService), http: TestBed.inject(HttpTestingController) };
}

describe('SavedViewsService', () => {
  it('reads the version of a view from the ETag header', () => {
    const { service, http } = setup();
    let etag = '';

    service.get('100', 5).subscribe((loaded) => (etag = loaded.etag));
    http.expectOne(`${BASE}/5`).flush(VIEW, { headers: { ETag: '"77"' } });

    expect(etag).toBe('"77"');
  });

  it('falls back to the version in the body when no ETag arrives', () => {
    const { service, http } = setup();
    let etag = '';

    service.get('100', 5).subscribe((loaded) => (etag = loaded.etag));
    http.expectOne(`${BASE}/5`).flush(VIEW);

    expect(etag).toBe('"77"');
  });

  it('sends the version as If-Match when saving, and only the name, description and layout in the body', () => {
    const { service, http } = setup();

    service.update('100', 5, { name: 'Cash', description: null, layout: EMPTY_LAYOUT }, '"77"').subscribe();
    const request = http.expectOne(`${BASE}/5`);
    request.flush(VIEW);

    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"77"');
    expect(Object.keys(request.request.body as object).sort()).toEqual(['description', 'layout', 'name']);
  });

  it('puts the Provider in the path, escaped, and never in a body', () => {
    const { service, http } = setup();

    service.list('a/b c').subscribe();
    http.expectOne('/api/v1/providers/a%2Fb%20c/saved-views').flush({ mine: [], shared: [] });
    service.create('100', { name: 'n', description: null, layout: EMPTY_LAYOUT }).subscribe();
    const create = http.expectOne(BASE);
    create.flush(VIEW);

    expect(JSON.stringify(create.request.body)).not.toContain('providerId');
    expect(JSON.stringify(create.request.body)).not.toContain('ownerId');
  });

  it('sends only the emails when inviting and only the token when redeeming', () => {
    const { service, http } = setup();

    service.invite('100', 5, ['a@example.org']).subscribe();
    const invite = http.expectOne(`${BASE}/5/invitations`);
    invite.flush({ sent: [], failed: [] });
    service.redeem('t'.repeat(43)).subscribe();
    const redeem = http.expectOne('/api/v1/saved-view-invitations/redeem');
    redeem.flush({ providerId: '100', viewId: 5 });

    expect(invite.request.body).toEqual({ emails: ['a@example.org'] });
    expect(redeem.request.body).toEqual({ token: 't'.repeat(43) });
  });

  it('reaches the sharing, revoke and catalog routes', () => {
    const { service, http } = setup();

    service.sharing('100', 5).subscribe();
    http.expectOne(`${BASE}/5/shares`).flush({ recipients: [], pending: [] });
    service.revokeShare('100', 5, 9).subscribe();
    expect(http.expectOne(`${BASE}/5/shares/9`).request.method).toBe('DELETE');
    service.revokeInvitation('100', 5, 4).subscribe();
    expect(http.expectOne(`${BASE}/5/invitations/4`).request.method).toBe('DELETE');
    service.catalog('100').subscribe();
    http.expectOne('/api/v1/providers/100/metrics/catalog').flush({ categories: [], metrics: [] });
    service.delete('100', 5).subscribe();
    expect(http.expectOne(`${BASE}/5`).request.method).toBe('DELETE');
  });
});
