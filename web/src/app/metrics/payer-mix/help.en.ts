import { MetricHelp } from '../help';

export const PAYER_MIX_HELP: MetricHelp = {
  summary:
    'The share of transports billed to each primary payer group, for the month you chose and the month before. Each transport is counted once, in the month it happened, under its primary payer. Only counts are shown, never payer names or patient details. Recent months can still move.',
  glossary: [
    { term: 'Share of transports', text: 'The part of the month’s transports billed to a payer group, as a percentage of all transports in that month.' },
    { term: 'Primary payer', text: 'The payer first responsible for the bill. Each transport is counted once, under its primary payer.' },
    { term: 'Commercial', text: 'Insurance and third-party liability.' },
    { term: 'Medicare', text: 'Includes Medicare Advantage.' },
    { term: 'Medicaid', text: 'Includes Medicaid managed care.' },
    { term: 'VA', text: 'Veterans Affairs. A payer whose name has the word VA, CHAMPVA or TRICARE, whatever its financial class.' },
    { term: 'Other government', text: 'Other government payers. Facility and Patient are their own groups, and a transport with no payer is Not recorded.' },
  ],
};
