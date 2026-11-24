import { MetricHelp } from '../help';

export const TRANSPORT_MIX_BY_CALL_TYPE_HELP: MetricHelp = {
  summary:
    'How many transports were scene, interfacility, emergency or non-emergency calls, next to the month before. Transports and miles are counted by the date of service. Net cash is the cash posted in the month, a separate figure, not the cash for these transports. Expected payment is not available here.',
  glossary: [
    { term: 'Call type', text: 'The kind of call a transport answered: scene, interfacility, emergency or non-emergency. A transport with no call type is Not recorded.' },
    { term: 'Share', text: 'A call type’s transports as a part of all transports in the month. The change in share is in percentage points, and is not shown when the month before had no transports.' },
    { term: 'Loaded miles', text: 'Statute miles driven or flown with the patient on board, counted in the month of the transport.' },
    { term: 'Net cash posted in the month', text: 'Payments posted in the month, less refunds, for transports of that call type. It follows the month cash was posted, so it does not match the counts month for month.' },
    { term: 'Payer mix', text: 'The share of each payer group within a call type, in the table view.' },
    { term: 'Months shown', text: 'The trend in the line and stacked views covers 6 or 12 months ending with the month you chose.' },
  ],
};
