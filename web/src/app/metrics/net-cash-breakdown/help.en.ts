import { MetricHelp } from '../help';

export const NET_CASH_BREAKDOWN_HELP: MetricHelp = {
  summary:
    'Net cash for one month, split by aircraft type for air or level of service for ground, then by {reportingDivision} and vehicle, next to the month before. Cash is counted in the month it was posted. Payments loaded from the old billing system are left out.',
  glossary: [
    { term: 'Net cash', text: 'Payments received and posted in the month, less refunds. The groups add up to the net cash headline for the same month.' },
    { term: 'Transports counted by posting month', text: 'A transport is counted in the month cash was posted for it, however long ago it happened. These counts will not match transports by date of service on other cards.' },
    { term: 'Air and ground', text: 'Air is rotor wing and fixed wing. Anything else is ground. A transport with no trip record is shown as ground, Not recorded.' },
    { term: 'Patient transports only', text: 'Only patient transports are counted. For air, flight hours and non-patient flights stay with your aviation partner. For ground, standby and non-transport responses are not included.' },
    { term: 'Open month', text: 'While the accounting period is still open, cash is still being posted and the figures can change.' },
    { term: 'Scene and interfacility', text: 'For air, the share of transports picked up at a scene against those moved between facilities. For ground the shares are emergency and non-emergency.' },
  ],
};
