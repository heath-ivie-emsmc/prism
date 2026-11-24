import { MetricRenderer } from '../metric-renderer';
import { loadCashContributionByDivision } from './cash-contribution-by-division.loader';
import { fromCashContributionByDivision } from './cash-contribution-by-division.widget';
import { CashContributionByDivisionDto } from './cash-contribution-by-division.dto';

export const CASH_CONTRIBUTION_BY_DIVISION_RENDERER: MetricRenderer<CashContributionByDivisionDto> = {
  loader: loadCashContributionByDivision,
  toWidgetData: fromCashContributionByDivision,
  presentations: ['table', 'column'],
};
