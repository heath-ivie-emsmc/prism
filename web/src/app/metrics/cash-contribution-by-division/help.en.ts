import { MetricHelp } from '../help';

export const CASH_CONTRIBUTION_BY_DIVISION_HELP: MetricHelp = {
  summary:
    'The change in net cash for each {reportingDivision}, from the month before to the month you chose. The changes add up to the total change in net cash. Net cash is the cash posted in the month.',
  glossary: [
    { term: 'Change in net cash', text: 'A {reportingDivision}’s net cash this month minus its net cash the month before. A negative change means it brought in less cash.' },
    { term: 'Share of change', text: 'A {reportingDivision}’s change divided by the total change. It can be above 100% or below 0% when some rose and others fell.' },
    { term: '{ReportingDivision}', text: 'The operational unit a transport rolls up to. The 25 that moved cash the most are listed and the rest are added together in Other. Transports with none show as Not recorded.' },
    { term: 'Adds up to the total', text: 'The rows, Other included, always add up to the total change shown in the last row. Nothing is left unexplained.' },
  ],
};
