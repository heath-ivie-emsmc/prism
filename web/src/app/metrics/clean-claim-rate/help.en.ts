import { MetricHelp } from '../help';

export const CLEAN_CLAIM_RATE_HELP: MetricHelp = {
  summary:
    'The share of claims accepted the first time they were sent, with no fixes needed, by month of service. It is clean claims divided by all claims. The card shows the newest month the warehouse has, which can be behind. It is not split by air or ground or by {reportingDivision}.',
  glossary: [
    { term: 'Clean claim', text: 'A claim the payer accepted on the first submission, without being corrected or resent.' },
    { term: 'Rejected', text: 'A claim the payer sent back on the first submission.' },
    { term: 'Rate', text: 'Clean claims divided by all claims. With no claims in a month there is no rate, not zero.' },
    { term: 'Latest in the data', text: 'The newest month the warehouse has. The card says which month, and never shows an older month as the current one.' },
    { term: 'Goal mark', text: 'The 95% line is a placeholder for now. It will become a Provider setting.' },
  ],
};
