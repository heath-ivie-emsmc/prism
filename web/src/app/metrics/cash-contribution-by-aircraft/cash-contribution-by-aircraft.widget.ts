import { WidgetData } from '../../saved-views/widget-data';
import { fromContribution } from '../shared/contribution.widget';
import { CashContributionByAircraftDto } from './cash-contribution-by-aircraft.dto';

export function fromCashContributionByAircraft(dto: CashContributionByAircraftDto): WidgetData {
  return fromContribution(dto, { contributor: 'Tail number', plural: 'aircraft', totalLabel: 'All aircraft' });
}
