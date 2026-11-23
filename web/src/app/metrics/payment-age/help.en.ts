import { MetricHelp } from '../help';

export const PAYMENT_AGE_HELP: MetricHelp = {
  summary:
    'How long payers take to pay. Net cash by payer group and posting month, split by the months between the transport and the payment, for the month you chose and the twelve months before it. Cash for a transport with no date of service is left out.',
  glossary: [
    { term: 'Payment age', text: 'The posting month minus the month of the transport. Age 0 is cash posted in the same month as the transport. Anything older than 24 months is counted as 24.' },
    { term: 'Posting month', text: 'The month the payment was recorded, not the month of the transport.' },
    { term: 'Payer group', text: 'The primary payer’s group: Commercial, Medicare, Medicaid, Other government, VA, Facility or Patient.' },
    { term: 'Net cash', text: 'Payments posted, less refunds, without payments loaded from the old billing system.' },
    { term: 'Totals', text: 'The cells can add up to slightly less than the net cash breakdown, because cash with no date of service is not shown.' },
  ],
};
