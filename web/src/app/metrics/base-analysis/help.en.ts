import { MetricHelp } from '../help';

export const BASE_ANALYSIS_HELP: MetricHelp = {
  summary:
    'Transports, loaded miles and net cash for each {reportingDivision}, next to the month before, with the aircraft that flew from each one. Transports and miles are counted by date of service. Net cash is the cash posted in the month, so net cash per transport is that cash divided by transports, not an expected payment.',
  glossary: [
    { term: '{ReportingDivision}', text: 'The operational unit a transport rolls up to. Transports with none are listed on their own row, and the smallest are added together in Other.' },
    { term: 'Net cash per transport', text: 'Cash posted in the month divided by the transports of the month. It is not what a transport is expected to be paid.' },
    { term: 'Loaded miles', text: 'Statute miles driven or flown with the patient on board.' },
    { term: 'Aircraft that flew from it', text: 'The table view lists the tail numbers used from each {reportingDivision}, with their transports. Ground transports have no tail number.' },
    { term: 'Change %', text: 'The change from the month before as a percent. It is not shown when the month before was zero.' },
  ],
};
