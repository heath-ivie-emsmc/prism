import { WidgetData } from '../../saved-views/widget-data';
import { fromContribution } from '../shared/contribution.widget';
import { CashContributionByPayerDto } from './cash-contribution-by-payer.dto';

/** Rows carry server-issued links to the payer breakdown. The strict drill-link parser reads them, so a link to anything else is ignored. */
export function fromCashContributionByPayer(dto: CashContributionByPayerDto): WidgetData {
  return fromContribution(dto, { contributor: 'Payer group', plural: 'payer groups', totalLabel: 'All payer groups', drills: true, keyed: true });
}
