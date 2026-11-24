import { MetricRenderer } from '../metric-renderer';
import { loadTransportMixByCallType } from './transport-mix-by-call-type.loader';
import { fromTransportMixByCallType } from './transport-mix-by-call-type.widget';
import { TransportMixByCallTypeDto } from './transport-mix-by-call-type.dto';

export const TRANSPORT_MIX_BY_CALL_TYPE_RENDERER: MetricRenderer<TransportMixByCallTypeDto> = {
  loader: loadTransportMixByCallType,
  toWidgetData: fromTransportMixByCallType,
  presentations: ['column', 'stacked', 'donut', 'line', 'table'],
};
