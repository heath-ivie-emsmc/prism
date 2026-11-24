import { HttpTestingController } from '@angular/common/http/testing';

/** The Provider's Reporting Divisions as the API returns them. The last name is hostile on purpose: it must only ever be shown as text. */
export const DIVISIONS = {
  providerId: '526',
  divisions: [
    { key: 'riverside', name: 'Riverside Base' },
    { key: 'summit', name: 'Summit Base' },
    { key: 'x-img', name: '<img src=x onerror=alert(1)> Base' },
  ],
};

/** Answers every pending request for the Provider's Reporting Divisions. A page that shows the division control asks for them, and a test cannot settle until they are answered. */
export function flushDivisions(http: HttpTestingController, body: object = DIVISIONS): void {
  for (const request of http.match((r) => r.url.endsWith('/metrics/net-collections/divisions'))) if (!request.cancelled) request.flush(body);
}
