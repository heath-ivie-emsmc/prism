import { MetricHelp } from '../help';

export const NET_COLLECTIONS_HELP: MetricHelp = {
  summary:
    'Net cash collected each month: payments posted in the month, less refunds. Each month is the month the payment was posted, not the month of the transport. Payments loaded from the old billing system (the Old AR Import, where AR is accounts receivable) are left out. A month with no figure yet is skipped, not shown as zero.',
  glossary: [
    { term: 'Net cash', text: 'Payments received and posted, less refunds. It is money actually collected, not money billed or still owed.' },
    { term: 'Posted', text: 'Recorded against a claim in the billing system. A payment counts in the accounting period it was posted in, which can be later than the day it arrived.' },
    { term: 'Accounting period', text: 'The billing month a payment is posted into. A late correction posted into an adjustment period counts in the month it adjusts.' },
    { term: 'Old AR Import', text: 'Payments (AR is short for accounts receivable) copied over from a previous billing system. They are not new collections, so they are not counted.' },
    { term: 'Air or ground only', text: 'The warehouse total has no air or ground split. When you choose one, the figure is added up from the individual payments by the transport’s level of service. Air is rotor wing and fixed wing, and everything else is ground.' },
  ],
};
