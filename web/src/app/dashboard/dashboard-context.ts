/** What the dashboard header shows. Mocked until the API serves it (Provider settings, Data Through). */
export interface DashboardContext {
  readonly providerName: string;
  /** Last day the Provider's data is complete, as yyyy-MM-dd. */
  readonly dataThrough: string;
  /** When the data was last loaded, as an ISO instant. */
  readonly updatedAt: string;
  /** The Provider's own time zone, for showing instants. */
  readonly timeZone: string;
  readonly enabledServiceLines: readonly ('air' | 'ground')[];
}

export const MOCK_CONTEXT: DashboardContext = {
  providerName: 'Acme Air Care',
  dataThrough: '2026-10-31',
  updatedAt: '2026-11-05T13:42:00Z',
  timeZone: 'America/Chicago',
  enabledServiceLines: ['air', 'ground'],
};
