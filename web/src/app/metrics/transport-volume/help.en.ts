import { MetricHelp } from '../help';

export const TRANSPORT_VOLUME_HELP: MetricHelp = {
  summary:
    'The number of transports and the loaded miles they covered, by the month the transport happened. Every transport is counted once, with no cancelled status to remove. A transport with no mileage adds no miles. The newest month can still rise.',
  glossary: [
    { term: 'Transport', text: 'One patient movement, counted under one service line and one {reportingDivision}.' },
    { term: 'Loaded miles', text: 'Miles driven or flown with the patient on board. They are treated as statute miles, and the nautical switch converts them for air.' },
    { term: 'Month of the transport', text: 'Counted by the date of service, so these counts differ from the net cash breakdown, which counts transports by the month cash was posted.' },
    { term: 'Open month', text: 'While the accounting period is open, new transports can still arrive, so the count may rise.' },
  ],
};
