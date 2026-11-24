import { MetricRenderer } from '../metric-renderer';
import { loadCashContributionByAircraft } from './cash-contribution-by-aircraft.loader';
import { fromCashContributionByAircraft } from './cash-contribution-by-aircraft.widget';
import { CashContributionByAircraftDto } from './cash-contribution-by-aircraft.dto';

export const CASH_CONTRIBUTION_BY_AIRCRAFT_RENDERER: MetricRenderer<CashContributionByAircraftDto> = {
  loader: loadCashContributionByAircraft,
  toWidgetData: fromCashContributionByAircraft,
  presentations: ['table', 'column'],
};
