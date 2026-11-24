import { MetricRenderer } from '../metric-renderer';
import { loadCashContributionByPayer } from './cash-contribution-by-payer.loader';
import { fromCashContributionByPayer } from './cash-contribution-by-payer.widget';
import { CashContributionByPayerDto } from './cash-contribution-by-payer.dto';

export const CASH_CONTRIBUTION_BY_PAYER_RENDERER: MetricRenderer<CashContributionByPayerDto> = {
  loader: loadCashContributionByPayer,
  toWidgetData: fromCashContributionByPayer,
  presentations: ['table', 'column'],
};
