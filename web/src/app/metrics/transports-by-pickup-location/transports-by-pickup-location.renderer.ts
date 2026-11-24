import { MetricRenderer } from '../metric-renderer';
import { loadTransportsByPickupLocation } from './transports-by-pickup-location.loader';
import { fromTransportsByPickupLocation } from './transports-by-pickup-location.widget';
import { TransportsByPickupLocationDto } from './transports-by-pickup-location.dto';

export const TRANSPORTS_BY_PICKUP_LOCATION_RENDERER: MetricRenderer<TransportsByPickupLocationDto> = {
  loader: loadTransportsByPickupLocation,
  toWidgetData: fromTransportsByPickupLocation,
  presentations: ['column', 'donut', 'table'],
};
