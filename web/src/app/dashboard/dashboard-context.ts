import { ServiceLineSelection } from './service-line';

export interface ReportingDivision {
  readonly id: string;
  readonly name: string;
  readonly serviceLine: 'air' | 'ground';
}

/** What the dashboard header shows. Mocked until the API serves it (Provider settings, Data Through, Reporting Divisions). */
export interface DashboardContext {
  readonly providerName: string;
  /** Last day the Provider's data is complete, as yyyy-MM-dd. */
  readonly dataThrough: string;
  /** When the data was last loaded, as an ISO instant. */
  readonly updatedAt: string;
  /** The Provider's own time zone, for showing instants. */
  readonly timeZone: string;
  readonly enabledServiceLines: readonly ('air' | 'ground')[];
  readonly reportingDivisions: readonly ReportingDivision[];
}

export const MOCK_CONTEXT: DashboardContext = {
  providerName: 'Acme Air Care',
  dataThrough: '2026-10-31',
  updatedAt: '2026-11-05T13:42:00Z',
  timeZone: 'America/Chicago',
  enabledServiceLines: ['air', 'ground'],
  reportingDivisions: [
    { id: 'a1', name: 'Riverside Base', serviceLine: 'air' },
    { id: 'a2', name: 'Summit Base', serviceLine: 'air' },
    { id: 'a3', name: 'Lakeshore Base', serviceLine: 'air' },
    { id: 'g1', name: 'Station 12', serviceLine: 'ground' },
    { id: 'g2', name: 'Station 31', serviceLine: 'ground' },
  ],
};

/** Reporting Divisions that match the chosen Service Line. */
export function divisionsFor(
  divisions: readonly ReportingDivision[],
  selection: ServiceLineSelection,
): readonly ReportingDivision[] {
  return selection === 'both' ? divisions : divisions.filter((division) => division.serviceLine === selection);
}
