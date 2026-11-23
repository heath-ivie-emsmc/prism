import { MetricRenderer } from '../metric-renderer';
import { loadNetCashBreakdown } from './net-cash-breakdown.loader';
import { fromBreakdown } from './net-cash-breakdown.widget';
import { NetCashBreakdownDto } from './net-cash-breakdown.dto';

export const NET_CASH_BREAKDOWN_RENDERER: MetricRenderer<NetCashBreakdownDto> = {
  loader: loadNetCashBreakdown,
  toWidgetData: fromBreakdown,
  presentations: ['table', 'column'],
};
