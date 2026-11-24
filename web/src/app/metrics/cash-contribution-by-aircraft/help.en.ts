import { MetricHelp } from '../help';

export const CASH_CONTRIBUTION_BY_AIRCRAFT_HELP: MetricHelp = {
  summary:
    'The change in net cash for each aircraft, by tail number, from the month before to the month you chose. The changes add up to the total change in net cash. It covers air only. Net cash is the cash posted in the month.',
  glossary: [
    { term: 'Change in net cash', text: 'An aircraft’s net cash this month minus its net cash the month before. A negative change means it brought in less cash.' },
    { term: 'Share of change', text: 'An aircraft’s change divided by the total change. It can be above 100% or below 0% when some aircraft rose and others fell.' },
    { term: 'Tail number', text: 'The registration of the aircraft. The 25 that moved cash the most are listed and every other aircraft is added together in Other.' },
    { term: 'Adds up to the total', text: 'The rows, Other included, always add up to the total change shown in the last row. Nothing is left unexplained.' },
    { term: 'Air only', text: 'Ground transports have no tail number, so this Metric cannot be shown for Ground only.' },
  ],
};
