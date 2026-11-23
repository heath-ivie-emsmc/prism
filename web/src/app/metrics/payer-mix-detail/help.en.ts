import { MetricHelp } from '../help';

export const PAYER_MIX_DETAIL_HELP: MetricHelp = {
  summary:
    'The payers inside one payer group, as a share of that group’s transports for the month you chose and the month before. The ten largest are listed and all the rest are added together as Other. Patient, Facility and Not recorded have no detail.',
  glossary: [
    { term: 'Share of transports', text: 'The part of the group’s transports billed to a payer, as a percentage of all the group’s transports in that month.' },
    { term: 'Top ten', text: 'The ten payers with the most transports across the two months. The ranking uses both months together, so the same payers show for each.' },
    { term: 'Other', text: 'All the payers outside the ten largest, added together. A real payer named Other is shown as Other payer.' },
    { term: 'Unknown payer', text: 'Transports whose payer name is empty or missing.' },
    { term: 'Primary payer', text: 'The payer first responsible for the bill. Each transport is counted once, under its primary payer.' },
  ],
};
