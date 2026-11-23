import { MetricHelp } from '../help';

export const NET_CASH_BY_GROUP_HELP: MetricHelp = {
  summary:
    'Net cash for each aircraft type (air) or level of service (ground) by the month it was posted, for the month you chose and the twelve months before it. Payments loaded from the old billing system are left out. Months with no cash are left out, not shown as zero.',
  glossary: [
    { term: 'Net cash', text: 'Payments received and posted in the month, less refunds.' },
    { term: 'Group', text: 'An aircraft type for air or a level of service for ground. Each payment belongs to exactly one group, the one on its transport.' },
    { term: 'Posting month', text: 'The month the payment was recorded. A group’s figure for a month matches the same group in the net cash breakdown.' },
    { term: 'Not included', text: 'Cash for a transport with no trip record has no group, so it is not shown here.' },
    { term: 'Filtering by {reportingDivision}', text: 'Choosing a {reportingDivision} keeps only cash for transports in that {reportingDivision}.' },
  ],
};
