import { MetricRenderer } from '../metric-renderer';
import { loadCashPerTransport } from './cash-per-transport-by-payer.loader';
import { fromCashPerTransport } from './cash-per-transport-by-payer.widget';
import { CashPerTransportDto } from './cash-per-transport-by-payer.dto';

export const CASH_PER_TRANSPORT_BY_PAYER_RENDERER: MetricRenderer<CashPerTransportDto> = {
  loader: loadCashPerTransport,
  toWidgetData: fromCashPerTransport,
  presentations: ['column', 'table'],
};
