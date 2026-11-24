import { MetricHelp } from '../help';

export const TRANSPORTS_BY_PICKUP_LOCATION_HELP: MetricHelp = {
  summary:
    'Where transports picked the patient up: the scene, a residence, a hospital, a nursing home and so on, next to the month before. The grouping follows the origin modifier on the claim. Transports and miles are counted by date of service. Net cash is the cash posted in the month, a separate figure.',
  glossary: [
    { term: 'Pickup type', text: 'The kind of place a transport began, taken from the origin modifier on the claim. A code the grouping does not recognise is counted under Other.' },
    { term: 'Facility', text: 'The table view also names the facilities. Scene, residence, custodial facility and Other never have a name. The busiest facilities are listed and the rest are added together in Other facilities.' },
    { term: 'Share', text: 'A pickup type’s transports as a part of all transports in the month. The change in share is in percentage points.' },
    { term: 'Net cash posted in the month', text: 'Payments posted in the month, less refunds. It follows the month cash was posted, so it does not match the counts month for month.' },
    { term: 'Payer mix', text: 'The share of each payer group within a pickup type, in the table view.' },
  ],
};
