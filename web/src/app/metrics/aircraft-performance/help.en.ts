import { MetricHelp } from '../help';

export const AIRCRAFT_PERFORMANCE_HELP: MetricHelp = {
  summary:
    'Transports, loaded miles and net cash for each aircraft, by tail number, next to the month before. It covers air only. Transports and miles are counted by date of service. Net cash is the cash posted in the month, so net cash per transport is that cash divided by transports, not an expected payment.',
  glossary: [
    { term: 'Tail number', text: 'The registration of the aircraft that flew the transport. The 100 busiest are listed and every other aircraft is added together in Other.' },
    { term: 'Net cash per transport', text: 'Cash posted in the month divided by the transports of the month. It is not what a transport is expected to be paid, and it moves with the month cash was posted.' },
    { term: 'Loaded miles', text: 'Statute miles flown with the patient on board.' },
    { term: 'Change %', text: 'The change from the month before as a percent. It is not shown when the month before was zero.' },
    { term: 'Air only', text: 'Ground transports have no tail number, so this Metric cannot be shown for Ground only.' },
  ],
};
