import { MetricHelp } from '../help';

export const PAYMENT_AGE_HELP: MetricHelp = {
  summary:
    'How long payers take to pay. Net cash by payer group and posting month, split by the months between the transport and the payment, for the month you chose and the twelve months before it. Age is counted in whole months only, never days. Cash for a transport with no date of service is left out.',
  glossary: [
    { term: 'Payment age', text: 'The posting month minus the month of the transport. Age 0 is cash posted in the same month as the transport. Anything older than 24 months is counted as 24.' },
    { term: 'Typical months to payment', text: 'For each payer group, the age by which half of its cash had been paid, in whole months. A payer group with no cash shows no figure.' },
    { term: 'Age bands', text: 'The share of a payer group’s cash paid at 0 to 1, 2 to 3, 4 to 6, 7 to 12, and 13 or more months old. The shares add up to 100% for each payer group, and refunds are left out of them.' },
    { term: 'Posting month', text: 'The month the payment was recorded, not the month of the transport.' },
    { term: 'Payer group', text: 'The primary payer’s group: Commercial, Medicare, Medicaid, Other government, VA, Facility or Patient.' },
    { term: 'Net cash', text: 'Payments posted, less refunds, without payments loaded from the old billing system.' },
    { term: 'Totals', text: 'The cells can add up to slightly less than the net cash breakdown, because cash with no date of service is not shown.' },
  ],
};
