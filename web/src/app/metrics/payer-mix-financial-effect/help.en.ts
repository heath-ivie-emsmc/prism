import { MetricHelp } from '../help';

export const PAYER_MIX_FINANCIAL_EFFECT_HELP: MetricHelp = {
  summary:
    'What a change in payer mix is expected to do to cash once those claims are paid. It multiplies each payer group’s change in share by what its transports have earned. This is an expected effect, not posted cash. If the shift lasts, the same amount repeats each month.',
  glossary: [
    { term: 'Expected effect', text: 'An estimate of the change in cash from the shift in payer mix. No cash has moved yet, so it is not part of posted net cash and does not add to it.' },
    { term: 'Recurring monthly effect', text: 'The same amount again each month, if the shift persists. It is the same number as the expected effect, not an extra amount.' },
    { term: 'Cash per transport', text: 'Net cash posted for a payer group divided by its transports, using transports old enough to have been paid. A group with none shows no figure.' },
    { term: 'Change in share', text: 'A payer group’s share of transports minus its share the month before, in percentage points.' },
    { term: 'Service months and cash cut-off', text: 'The widget says which months of transports are used and the month cash is counted through, for example transports from Jan 2025 to Dec 2025 with cash posted through Nov 2026.' },
  ],
};
