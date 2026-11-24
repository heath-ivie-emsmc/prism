import { MetricHelp } from '../help';

export const NET_CASH_PROJECTION_HELP: MetricHelp = {
  summary:
    'The cash the data warehouse forecasts for the months after the month you chose. Pick one, two or three months. Each month shows its change from the month before (the first against the latest month with posted cash) and a running total. It is an estimate, not posted cash, and it changes as payments arrive.',
  glossary: [
    { term: 'Expected net cash and running total', text: 'The forecast total of cash expected to be posted in each coming month. The cumulative figure is the running total from the first month through that month. None of it is money collected yet.' },
    { term: 'Forecast range', text: 'The low and high ends of the estimate: the smaller and the larger of the expected total and its two parts. It is the range across the forecast components, not a statistical interval or a confidence interval, and it does not widen further out.' },
    { term: 'Expected collections on trips that already existed', text: 'The part of the forecast for trips that are already in the system. A trip that already existed may not be billed yet, so this is not the same as claims already billed.' },
    { term: 'Assumed run rate of trips that do not exist yet', text: 'The part of the forecast based on how many trips are assumed to be run in the coming months, at their usual run rate.' },
    { term: 'Share of the expected net cash', text: 'Each part divided by the expected total, shown as its own figure. The warehouse does not yet make the two parts add up to the total, so the shares can add to more than 100% and the parts may not add up to the expected total.' },
    { term: 'Change from the month before', text: 'The first month is compared with the latest month with posted net cash, and each later month with the month before it. A percent change is not shown when the month it is compared with was zero, and there is no change when no earlier month has cash.' },
    { term: 'Service line and {reportingDivision}', text: 'Choosing both service lines adds the air and ground forecasts together. The forecast is made for the whole Provider, so choosing a {reportingDivision} does not change it.' },
  ],
};
