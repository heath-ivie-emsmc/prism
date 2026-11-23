import { MetricHelp } from '../help';

export const CASH_PER_TRANSPORT_BY_PAYER_HELP: MetricHelp = {
  summary:
    'Net cash per transport for each payer group, using transports from the twelve months that ended at least six months before the month you chose. Every transport counts, paid or not, and cash is everything posted up to the chosen month. Recent months are left out so unpaid claims do not look like a low rate.',
  glossary: [
    { term: 'Cash per transport', text: 'Net cash posted for a payer group divided by its number of transports. A group with no transports shows no figure, not zero.' },
    { term: 'Matured', text: 'Only transports at least six months old are counted, giving payers time to pay.' },
    { term: 'Payer group', text: 'The primary payer’s group: Commercial, Medicare, Medicaid, Other government, VA, Facility or Patient. A transport with no payer is Not recorded.' },
    { term: 'Net cash', text: 'Payments posted, less refunds, without payments loaded from the old billing system.' },
    { term: 'Filtering by {reportingDivision}', text: 'Choosing a {reportingDivision} keeps only transports and cash in that {reportingDivision}.' },
  ],
};
