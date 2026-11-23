import { MetricRenderer } from '../metric-renderer';
import { loadNetCashProjection } from './net-cash-projection.loader';
import { fromProjection } from './net-cash-projection.widget';
import { NetCashProjectionDto } from './net-cash-projection.dto';

export const NET_CASH_PROJECTION_RENDERER: MetricRenderer<NetCashProjectionDto> = {
  loader: loadNetCashProjection,
  toWidgetData: fromProjection,
  presentations: ['column', 'line', 'table'],
};
