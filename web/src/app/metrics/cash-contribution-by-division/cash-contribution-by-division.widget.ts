import { WidgetData } from '../../saved-views/widget-data';
import { divisionLabel } from '../base-analysis/base-analysis.widget';
import { fromContribution } from '../shared/contribution.widget';
import { CashContributionByDivisionDto } from './cash-contribution-by-division.dto';

export function fromCashContributionByDivision(dto: CashContributionByDivisionDto): WidgetData {
  const label = divisionLabel(dto);
  return fromContribution(dto, { contributor: label, plural: `${label.toLowerCase()} rows`, totalLabel: `All ${label.toLowerCase()} rows` });
}
