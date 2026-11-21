/** What the user is looking at: one Service Line, or both together. */
export type ServiceLineSelection = 'air' | 'ground' | 'both';

export interface ServiceLineOption {
  readonly value: ServiceLineSelection;
  readonly label: string;
  /** Material symbol shown while this option is selected. */
  readonly icon: string;
}

export const SERVICE_LINE_OPTIONS: readonly ServiceLineOption[] = [
  { value: 'air', label: 'Air only', icon: 'flight' },
  { value: 'ground', label: 'Ground only', icon: 'local_shipping' },
  { value: 'both', label: 'Air + ground', icon: 'swap_horiz' },
];

/**
 * Display names for the terms a Provider can rename. These are the defaults by Service Line. A Provider's own Labels will
 * override them. They never change an API key.
 */
export interface Labels {
  /** The Reporting Division filter. */
  readonly reportingDivision: string;
  /** The "everything" choice in that filter. */
  readonly allReportingDivisions: string;
}

export function defaultLabels(selection: ServiceLineSelection): Labels {
  return selection === 'air'
    ? { reportingDivision: 'Base location', allReportingDivisions: 'All bases' }
    : { reportingDivision: 'Reporting division', allReportingDivisions: 'All reporting divisions' };
}
