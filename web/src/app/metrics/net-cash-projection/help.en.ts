import { MetricHelp } from '../help';

export const NET_CASH_PROJECTION_HELP: MetricHelp = {
  summary:
    'The cash the data warehouse forecasts for the next three months after the month you chose, from the latest forecast. It is an estimate, not posted cash, and it changes as payments arrive. If the forecast covers fewer than three months, there is no projection.',
  glossary: [
    { term: 'Projected net cash', text: 'The forecast total of cash expected to be posted in each coming month. It is not money collected yet.' },
    { term: 'Likely range', text: 'The low and high ends of the estimate. The low end is the smaller, and the high end the larger, of three figures: the total forecast, the forecast from claims already billed, and the forecast from the budget.' },
    { term: 'Not a confidence interval', text: 'The warehouse does not give a statistical margin of error. The range only shows how far apart its own forecasts are.' },
    { term: 'Air and ground', text: 'Choosing both service lines adds the air and ground forecasts together.' },
    { term: 'Not filtered by {reportingDivision}', text: 'The forecast is made for the whole Provider, so choosing a {reportingDivision} does not change it.' },
  ],
};
