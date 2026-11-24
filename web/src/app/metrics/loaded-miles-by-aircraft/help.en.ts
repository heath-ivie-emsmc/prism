import { MetricHelp } from '../help';

export const LOADED_MILES_BY_AIRCRAFT_HELP: MetricHelp = {
  summary:
    'Loaded miles for each aircraft, by tail number, with the average miles per transport and a month-by-month trend. It covers air only. Miles are statute miles with the patient on board, counted in the month of the transport. An aircraft with no transports in a month has no average for it.',
  glossary: [
    { term: 'Loaded miles', text: 'Statute miles flown with the patient on board. The dashboard’s nautical switch does not change this Metric: it always shows statute miles.' },
    { term: 'Average loaded miles per transport', text: 'Loaded miles divided by transports. It is not shown when there were no transports.' },
    { term: 'Tail number', text: 'The registration of the aircraft. The busiest are listed and every other aircraft is added together in Other.' },
    { term: 'Months shown', text: 'The trend covers 6 or 12 months ending with the month you chose. The chart draws the aircraft with the most miles, and the table lists them all.' },
    { term: 'Air only', text: 'Ground transports have no tail number, so this Metric cannot be shown for Ground only.' },
  ],
};
