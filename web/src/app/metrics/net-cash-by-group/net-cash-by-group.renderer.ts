import { MetricRenderer } from '../metric-renderer';
import { loadNetCashByGroup } from './net-cash-by-group.loader';
import { fromByGroup } from './net-cash-by-group.widget';
import { NetCashByGroupDto } from './net-cash-by-group.dto';

export const NET_CASH_BY_GROUP_RENDERER: MetricRenderer<NetCashByGroupDto> = {
  loader: loadNetCashByGroup,
  toWidgetData: fromByGroup,
  presentations: ['stacked', 'column', 'table'],
};
