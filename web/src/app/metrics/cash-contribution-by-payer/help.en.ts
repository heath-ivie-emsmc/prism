import { MetricHelp } from '../help';

export const CASH_CONTRIBUTION_BY_PAYER_HELP: MetricHelp = {
  summary:
    'The change in net cash for each payer group, from the month before to the month you chose. The changes add up to the total change in net cash. Net cash is the cash posted in the month. Open a payer group to see its breakdown.',
  glossary: [
    { term: 'Change in net cash', text: 'A payer group’s net cash this month minus its net cash the month before. A negative change means it brought in less cash.' },
    { term: 'Share of change', text: 'A payer group’s change divided by the total change. It can be above 100% or below 0% when some rose and others fell.' },
    { term: 'Payer group', text: 'The primary payer’s group: Commercial, Medicare, Medicaid, Other government, VA, Facility or Patient. A transport with no payer is Not recorded.' },
    { term: 'Adds up to the total', text: 'The rows, Other included, always add up to the total change shown in the last row. Nothing is left unexplained.' },
    { term: 'Open a breakdown', text: 'A payer group with detail has a link to its payer mix breakdown for the same month, Service Line and {reportingDivision}.' },
  ],
};
